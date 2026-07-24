using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.DesignRequests.Claude;

/// <summary>
/// Runs a stateless, tool-free Claude Code process. The long-lived OAuth token
/// is supplied only through the child process environment and is never placed
/// on the command line or written to disk.
/// </summary>
public sealed class ClaudeCliRunner : IClaudeCliRunner
{
    private readonly IOptionsMonitor<ClaudeCliOptions> _options;

    public ClaudeCliRunner(IOptionsMonitor<ClaudeCliOptions> options) => _options = options;

    public async Task<string> RunAsync(
        string systemPrompt,
        string userPrompt,
        string oauthToken,
        CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var startInfo = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--print");
        startInfo.ArgumentList.Add("--no-session-persistence");
        startInfo.ArgumentList.Add("--disable-slash-commands");
        startInfo.ArgumentList.Add("--setting-sources");
        startInfo.ArgumentList.Add(string.Empty);
        startInfo.ArgumentList.Add("--tools");
        startInfo.ArgumentList.Add(string.Empty);
        startInfo.ArgumentList.Add("--output-format");
        startInfo.ArgumentList.Add("text");
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(options.Model);
        startInfo.ArgumentList.Add("--system-prompt");
        startInfo.ArgumentList.Add(systemPrompt);

        // A DB/admin update takes effect on the next invocation. Keeping the
        // token out of ArgumentList also prevents it appearing in `ps`.
        startInfo.Environment["CLAUDE_CODE_OAUTH_TOKEN"] = oauthToken;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Claude CLI process did not start.");

        try
        {
            await process.StandardInput.WriteAsync(userPrompt.AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
            process.StandardInput.Close();
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds)));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"Claude CLI prompt refinement timed out after {options.TimeoutSeconds} seconds.");
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Claude CLI exited with code {process.ExitCode}: {Truncate(stderr.Trim(), 800)}");
        }

        return stdout.Trim();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort. The process object is disposed by the caller.
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
