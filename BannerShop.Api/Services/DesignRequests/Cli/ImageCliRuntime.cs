using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.DesignRequests.Cli;

public sealed class ImageCliOptions
{
    public string CodexExecutable { get; set; } = "codex";
    public string GrokExecutable { get; set; } = "grok";
    public int TimeoutSeconds { get; set; } = 360;
}

public sealed record ImageCliLoginStatus(bool IsConfigured, bool Pending, string? AuthorizationUrl,
    string? UserCode, string? Error);

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
            ? new(configured, !flow.Finished, flow.Url, flow.Code, flow.Error)
            : new(configured, false, null, null, null);
    }

    public void StartLogin(string provider)
    {
        ValidateProvider(provider);
        var flow = new LoginFlow();
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
            start.ArgumentList.Add("--device-auth");
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
            flow.Error = "Kunne ikke logge inn. Kontroller at CLI er installert og at kontoen tillater enhetsinnlogging.";
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
        public volatile bool Finished;
        public volatile string? Url;
        public volatile string? Code;
        public volatile string? Error;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task? Completion;
    }
}
