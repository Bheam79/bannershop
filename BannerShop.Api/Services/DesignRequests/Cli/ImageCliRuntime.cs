using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;

namespace BannerShop.Api.Services.DesignRequests.Cli;

public sealed class ImageCliOptions
{
    public string CodexExecutable { get; set; } = "codex";
    public string GrokExecutable { get; set; } = "grok";
    public int TimeoutSeconds { get; set; } = 360;
}

public sealed record ImageCliLoginStatus(bool IsConfigured, bool Pending, string? AuthorizationUrl,
    string? UserCode, string? Error, string LoginMethod = "device");

/// <summary>
/// Native CLI device login and token refresh. Credentials are kept in masked DB rows;
/// each invocation receives a private temporary CLI home, never the web server's home.
/// Separate provider locks prevent refresh-token rotation races within this host.
/// </summary>
public sealed class ImageCliRuntime(
    IServiceScopeFactory scopes,
    Microsoft.Extensions.Options.IOptionsMonitor<ImageCliOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<ImageCliRuntime> log)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, LoginFlow> _flows = new();
    public static bool IsProvider(string provider) => provider is "codex" or "grok";
    public static string CredentialKey(string provider) => $"{provider}_image_cli_credentials";
    public int TimeoutSeconds => Math.Clamp(options.CurrentValue.TimeoutSeconds, 10, 900);

    public async Task<ImageCliLoginStatus> StatusAsync(string provider, CancellationToken ct)
    {
        ValidateProvider(provider);
        var configured = !string.IsNullOrWhiteSpace(await ReadCredentialAsync(provider, ct));
        return _flows.TryGetValue(provider, out var flow)
            ? new(configured, !flow.Finished, flow.Url, flow.Code, flow.Error, flow.BrowserOAuth ? "oauth" : "device")
            : new(configured, false, null, null, null);
    }

    public void StartLogin(string provider, bool browserOAuth = false)
    {
        ValidateProvider(provider);
        if (browserOAuth && provider != "grok") throw new ArgumentException("Browser OAuth is only available for Grok.");
        var flow = new LoginFlow { BrowserOAuth = browserOAuth };
        // At most one pending login per provider. Repeated clicks are idempotent.
        _flows.AddOrUpdate(provider, _ => flow, (_, previous) => previous.Finished ? flow : previous);
        if (ReferenceEquals(_flows[provider], flow))
            flow.Completion = LoginAsync(provider, flow);
    }

    public void CancelLogin(string provider)
    {
        ValidateProvider(provider);
        if (_flows.TryGetValue(provider, out var flow)) flow.Cancellation.Cancel();
    }

    // The browser runs on the administrator's computer, so its loopback redirect
    // cannot reach the server's CLI. Relay only to the callback emitted by that CLI.
    // Grok ignores piped stdin for OAuth; it only reads pasted codes from a TTY.
    public async Task CompleteBrowserLoginAsync(string provider, string? input, CancellationToken ct)
    {
        ValidateProvider(provider);
        if (!_flows.TryGetValue(provider, out var flow) || !flow.BrowserOAuth
            || flow.Finished || flow.Cancellation.IsCancellationRequested)
            throw new ArgumentException("Ingen aktiv OAuth-innlogging. Start tilkoblingen på nytt.");

        var authorizationUrl = flow.Url;
        if (authorizationUrl is null)
            throw new ArgumentException("Venter på innloggingslenken. Prøv igjen om et øyeblikk.");
        var authorization = QueryHelpers.ParseQuery(new Uri(authorizationUrl).Query);
        if (!Uri.TryCreate(authorization["redirect_uri"].ToString(), UriKind.Absolute, out var callback)
            || callback.Scheme != "http" || callback.Host != "127.0.0.1"
            || callback.Port <= 0 || callback.AbsolutePath != "/callback"
            || callback.UserInfo.Length != 0 || callback.Query.Length != 0 || callback.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(authorization["state"]))
            throw new ArgumentException("Ugyldig OAuth-innlogging. Start tilkoblingen på nytt.");

        var state = authorization["state"].ToString();
        var code = input?.Trim() ?? "";
        if (code.Length == 0 || code.Length > 8192 || code.Any(char.IsControl))
            throw new ArgumentException("Lim inn koden eller hele returadressen fra Grok.");
        if (Uri.TryCreate(code, UriKind.Absolute, out var pasted))
        {
            var query = QueryHelpers.ParseQuery(pasted.Query);
            if (pasted.GetLeftPart(UriPartial.Path) != callback.GetLeftPart(UriPartial.Path)
                || pasted.UserInfo.Length != 0 || pasted.Fragment.Length != 0
                || query["state"].Count != 1 || query["state"].ToString() != state)
                throw new ArgumentException("Returadressen tilhører ikke denne innloggingen. Bruk den nyeste lenken.");
            if (query.ContainsKey("error"))
                throw new ArgumentException("Grok godkjente ikke innloggingen. Prøv igjen eller avbryt.");
            code = query["code"].Count == 1 ? query["code"].ToString() : "";
        }
        if (string.IsNullOrWhiteSpace(code) || code.Any(char.IsWhiteSpace))
            throw new ArgumentException("Lim inn koden eller hele returadressen fra Grok.");
        if (Interlocked.CompareExchange(ref flow.Submitted, 1, 0) != 0)
            throw new ArgumentException("Koden er allerede sendt. Vent på tilkoblingsstatus eller start på nytt.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct,
            lifetime.ApplicationStopping, flow.Cancellation.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        // No redirects or proxies: never forward an authorization code outside the
        // active local CLI, even if the pasted URL or local response is malicious.
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler);
        var target = QueryHelpers.AddQueryString(callback.AbsoluteUri,
            new Dictionary<string, string?> { ["code"] = code, ["state"] = state });
        try
        {
            using var response = await client.GetAsync(target, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Delivery may have succeeded. Cancel before allowing another attempt.
            flow.Cancellation.Cancel();
            throw new ArgumentException("Kunne ikke fullføre innloggingen. Start tilkoblingen på nytt.");
        }
    }

    private async Task LoginAsync(string provider, LoginFlow flow)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            lifetime.ApplicationStopping, flow.Cancellation.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var gate = _locks.GetOrAdd(provider, _ => new(1, 1));
        string? root = null;
        var acquired = false;
        try
        {
            await gate.WaitAsync(timeout.Token);
            acquired = true;
            root = NewPrivateDirectory();
            var start = StartInfo(provider, root);
            start.ArgumentList.Add("login");
            start.ArgumentList.Add(flow.BrowserOAuth ? "--oauth" : "--device-auth");
            if (provider == "codex")
            {
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add("cli_auth_credentials_store=\"file\"");
            }
            await ExecuteAsync(start, null, line => ReadLoginLine(provider, flow, line), timeout.Token);
            var path = Path.Combine(root, "home", "auth.json");
            if (!File.Exists(path)) throw new InvalidOperationException("Login returned no credentials.");
            await StoreCredentialAsync(provider, await File.ReadAllTextAsync(path, timeout.Token), timeout.Token);
        }
        catch (OperationCanceledException)
        {
            flow.Error = "Tilkoblingen ble avbrutt eller utløp. Start på nytt.";
        }
        catch (Exception ex)
        {
            log.LogWarning("{Provider} login failed ({ErrorType}).", provider, ex.GetType().Name);
            flow.Error = "Kunne ikke logge inn. Kontroller at CLI er installert og at kontoen tillater valgt innloggingsmetode.";
        }
        finally
        {
            flow.Url = null;
            flow.Code = null;
            flow.Finished = true;
            if (root is not null) DeleteDirectory(root);
            if (acquired) gate.Release();
        }
    }

    public async Task<T> WithCredentialsAsync<T>(string provider,
        Func<string, ProcessStartInfo, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        ValidateProvider(provider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.ApplicationStopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        var gate = _locks.GetOrAdd(provider, _ => new(1, 1));
        await gate.WaitAsync(timeout.Token);
        string? root = null;
        try
        {
            var credential = await ReadCredentialAsync(provider, timeout.Token);
            if (string.IsNullOrWhiteSpace(credential))
                throw new InvalidOperationException($"{provider}_image_cli_not_connected");
            root = NewPrivateDirectory();
            var start = StartInfo(provider, root);
            var path = Path.Combine(root, "home", "auth.json");
            await File.WriteAllTextAsync(path, credential, timeout.Token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            try
            {
                return await action(root, start, timeout.Token);
            }
            finally
            {
                // Even a failed image request may have rotated its access/refresh token.
                if (File.Exists(path))
                {
                    var refreshed = await File.ReadAllTextAsync(path, CancellationToken.None);
                    if (refreshed != credential)
                        await StoreCredentialAsync(provider, refreshed, lifetime.ApplicationStopping);
                }
            }
        }
        finally
        {
            if (root is not null) DeleteDirectory(root);
            gate.Release();
        }
    }

    private ProcessStartInfo StartInfo(string provider, string root)
    {
        var start = new ProcessStartInfo
        {
            FileName = provider == "codex" ? options.CurrentValue.CodexExecutable : options.CurrentValue.GrokExecutable,
            WorkingDirectory = Path.Combine(root, "work"),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        // Do not inherit API keys, service credentials, agent overrides or plugins.
        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
        start.Environment.Clear();
        start.Environment["PATH"] = path;
        start.Environment["HOME"] = root;
        start.Environment["TMPDIR"] = Path.Combine(root, "tmp");
        start.Environment["CODEX_HOME"] = Path.Combine(root, "home");
        start.Environment["GROK_HOME"] = Path.Combine(root, "home");
        start.Environment["NO_COLOR"] = "1";
        start.Environment["BROWSER"] = "/bin/true";
        return start;
    }

    public static async Task ExecuteAsync(ProcessStartInfo start, string? input,
        Action<string>? onLine, CancellationToken ct)
    {
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Image CLI did not start.");
        // Drain both streams concurrently; never expose raw CLI diagnostics (may contain tokens).
        var stdout = DrainAsync(process.StandardOutput, onLine);
        var stderr = DrainAsync(process.StandardError, onLine);
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), ct);
            process.StandardInput.Close();
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0) throw new InvalidOperationException($"Image CLI exited with code {process.ExitCode}.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
        }
    }

    private static async Task DrainAsync(StreamReader stream, Action<string>? onLine)
    {
        while (await stream.ReadLineAsync() is { } line) onLine?.Invoke(line);
    }

    private static void ReadLoginLine(string provider, LoginFlow flow, string line)
    {
        var clean = Regex.Replace(line, @"\x1B\[[0-?]*[ -/]*[@-~]", "").Trim();
        foreach (Match match in Regex.Matches(clean, @"https://[^\s<>""']+"))
        {
            if (Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)
                && (provider == "codex" ? uri.Host is "auth.openai.com" or "chatgpt.com" : uri.Host is "auth.x.ai" or "accounts.x.ai" or "grok.com"))
                flow.Url = uri.AbsoluteUri;
        }
        if (Regex.IsMatch(clean, @"^[A-Z0-9]{4,6}-[A-Z0-9]{4,6}$")) flow.Code = clean;
    }

    private async Task<string?> ReadCredentialAsync(string provider, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>()
            .GetValueAsync(CredentialKey(provider), ct);
    }

    private async Task StoreCredentialAsync(string provider, string value, CancellationToken ct)
    {
        using var parsed = JsonDocument.Parse(value);
        var json = parsed.RootElement;
        if (json.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Invalid CLI credentials.");
        // Grok stores a map of auth scopes to credentials; Codex stores tokens at the root.
        var hasToken = provider == "grok"
            ? json.EnumerateObject().Any(entry => entry.Value.ValueKind == JsonValueKind.Object
                && entry.Value.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(key.GetString()))
            : json.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object
                && tokens.TryGetProperty("access_token", out var access) && access.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(access.GetString());
        if (!hasToken) throw new InvalidOperationException("Invalid CLI credentials.");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BannerShopDbContext>();
        var key = CredentialKey(provider);
        var row = await db.SystemSettings.SingleOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            row = new SystemSetting { Key = key, Value = "" };
            db.SystemSettings.Add(row);
        }
        row.IsSensitive = true;
        row.Label = $"{provider} image CLI OAuth (managed)";
        await db.SaveChangesAsync(ct);
        await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>().SetValueAsync(key, value, ct);
    }

    private static string NewPrivateDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bannershop-image-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var dir in new[] { "home", "work", "tmp" }) Directory.CreateDirectory(Path.Combine(root, dir));
        return root;
    }

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { }
    }

    private static void ValidateProvider(string provider)
    {
        if (!IsProvider(provider)) throw new ArgumentException("Unknown image provider.");
    }

    private sealed class LoginFlow
    {
        public bool BrowserOAuth;
        public int Submitted;
        public volatile bool Finished;
        public volatile string? Url;
        public volatile string? Code;
        public volatile string? Error;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task? Completion;
    }
}
