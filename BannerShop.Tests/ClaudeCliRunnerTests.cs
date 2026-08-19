using System.ComponentModel;
using System.Diagnostics;
using BannerShop.Api.Services.DesignRequests.Claude;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Exercises the real subprocess-spawning <see cref="ClaudeCliRunner"/> against a
/// fake "claude" executable (a small bash script) rather than mocking Process —
/// the class's entire value is in how it shells out (argument list contents,
/// stdin/env wiring, exit-code and timeout handling), which a mock can't verify.
/// </summary>
public sealed class ClaudeCliRunnerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("claude-cli-runner-tests").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string WriteScript(IEnumerable<string> lines)
    {
        var path = Path.Combine(_dir, $"fake_claude_{Guid.NewGuid():N}.sh");
        File.WriteAllText(path, string.Join('\n', lines) + "\n");

        using var chmod = Process.Start(new ProcessStartInfo("chmod", ["+x", path]) { UseShellExecute = false })!;
        chmod.WaitForExit();
        return path;
    }

    private static ClaudeCliRunner MakeRunner(string executablePath, int timeoutSeconds = 5, string model = "sonnet")
    {
        var options = new ClaudeCliOptions { ExecutablePath = executablePath, Model = model, TimeoutSeconds = timeoutSeconds };
        return new ClaudeCliRunner(new SimpleOptionsMonitor<ClaudeCliOptions>(options));
    }

    [Fact]
    public async Task RunAsync_returns_trimmed_stdout_on_success()
    {
        var script = WriteScript([
            "#!/bin/bash",
            "cat > /dev/null",
            "echo '  A vivid prompt.  '",
            "exit 0"
        ]);
        var runner = MakeRunner(script);

        var result = await runner.RunAsync("system prompt", "user prompt", "tok", CancellationToken.None);

        result.Should().Be("A vivid prompt.");
    }

    [Fact]
    public async Task RunAsync_passes_flags_and_system_prompt_as_arguments_user_prompt_via_stdin_and_token_via_env_only()
    {
        var outFile = Path.Combine(_dir, "out.txt");
        var script = WriteScript([
            "#!/bin/bash",
            "{",
            "  printf 'ARGC:%s\\n' \"$#\"",
            "  for a in \"$@\"; do printf 'ARG:%s\\n' \"$a\"; done",
            "  printf 'STDIN:%s\\n' \"$(cat)\"",
            "  printf 'TOKEN:%s\\n' \"$CLAUDE_CODE_OAUTH_TOKEN\"",
            $"}} > \"{outFile}\"",
            "echo done",
            "exit 0"
        ]);
        var runner = MakeRunner(script, model: "opus-test");

        await runner.RunAsync("SYS PROMPT HERE", "USER PROMPT HERE", "secret-token-123", CancellationToken.None);

        var captured = await File.ReadAllTextAsync(outFile);
        captured.Should().Contain("ARG:--print");
        captured.Should().Contain("ARG:--no-session-persistence");
        captured.Should().Contain("ARG:--disable-slash-commands");
        captured.Should().Contain("ARG:--setting-sources");
        captured.Should().Contain("ARG:--tools");
        captured.Should().Contain("ARG:--output-format");
        captured.Should().Contain("ARG:text");
        captured.Should().Contain("ARG:--model");
        captured.Should().Contain("ARG:opus-test");
        captured.Should().Contain("ARG:--system-prompt");
        captured.Should().Contain("ARG:SYS PROMPT HERE");
        captured.Should().Contain("STDIN:USER PROMPT HERE");
        captured.Should().Contain("TOKEN:secret-token-123");
        // The OAuth token must never appear as a bare argument (would leak via `ps`).
        captured.Should().NotContain("ARG:secret-token-123");
    }

    [Fact]
    public async Task RunAsync_throws_with_exit_code_and_stderr_when_process_fails()
    {
        var script = WriteScript([
            "#!/bin/bash",
            "cat > /dev/null",
            "echo 'bad stuff happened' 1>&2",
            "exit 7"
        ]);
        var runner = MakeRunner(script);

        var act = () => runner.RunAsync("s", "u", "t", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("exited with code 7");
        ex.Which.Message.Should().Contain("bad stuff happened");
    }

    [Fact]
    public async Task RunAsync_throws_TimeoutException_and_kills_process_when_it_runs_too_long()
    {
        // TimeoutSeconds is floored at 5 by ClaudeCliRunner (Math.Max(5, options.TimeoutSeconds)),
        // so a 1-second config still waits up to 5s here — the process itself sleeps far longer
        // and must be killed rather than run to completion.
        var script = WriteScript([
            "#!/bin/bash",
            "cat > /dev/null",
            "sleep 30",
            "echo too-late",
            "exit 0"
        ]);
        var runner = MakeRunner(script, timeoutSeconds: 1);

        var act = () => runner.RunAsync("s", "u", "t", CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task RunAsync_throws_OperationCanceled_and_kills_process_when_caller_token_is_cancelled()
    {
        var script = WriteScript([
            "#!/bin/bash",
            "cat > /dev/null",
            "sleep 30",
            "exit 0"
        ]);
        var runner = MakeRunner(script, timeoutSeconds: 30);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        var act = () => runner.RunAsync("s", "u", "t", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_throws_when_the_executable_does_not_exist()
    {
        var runner = MakeRunner(Path.Combine(_dir, "does-not-exist-binary"));

        var act = () => runner.RunAsync("s", "u", "t", CancellationToken.None);

        await act.Should().ThrowAsync<Win32Exception>();
    }

    private sealed class SimpleOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
