using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class PowerShellTerminalSessionTests
{
    [Fact]
    public async Task SessionPersistsWorkingDirectoryEnvironmentAndShellState()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        var child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
        await using var session = new PowerShellTerminalSession(root, "A");
        var processId = session.ProcessId;
        var output = new List<TerminalSessionOutput>();
        session.OutputReceived += (_, item) => output.Add(item);

        await session.ExecuteAsync(Guid.NewGuid(), "Set-Location -LiteralPath 'child'; $env:HAVEN_PERSIST='abc'; $HavenVariable='xyz'; function Get-HavenValue { $env:HAVEN_PERSIST + ':' + $HavenVariable }", CancellationToken.None);
        var second = await session.ExecuteAsync(Guid.NewGuid(), "Write-Output (Get-Location).Path; Write-Output (Get-HavenValue)", CancellationToken.None);

        Assert.Equal(processId, session.ProcessId);
        Assert.Equal(child, second.CurrentWorkingDirectory, ignoreCase: true);
        Assert.Contains(output, item => item.Text.Contains("abc:xyz", StringComparison.Ordinal));
        Assert.DoesNotContain(output, item => item.Stream == TerminalOutputStream.StandardError);
    }

    [Fact]
    public async Task SessionsAreIndependent()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        var child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
        await using var first = new PowerShellTerminalSession(root, "A");
        await using var second = new PowerShellTerminalSession(root, "B");
        var secondOutput = new List<TerminalSessionOutput>();
        second.OutputReceived += (_, item) => secondOutput.Add(item);

        await first.ExecuteAsync(Guid.NewGuid(), "Set-Location -LiteralPath 'child'; $env:HAVEN_ONLY_A='yes'", CancellationToken.None);
        await second.ExecuteAsync(Guid.NewGuid(), "Write-Output (Get-Location).Path; if ($null -eq $env:HAVEN_ONLY_A) { Write-Output '<missing>' } else { Write-Output $env:HAVEN_ONLY_A }", CancellationToken.None);

        Assert.NotEqual(first.ProcessId, second.ProcessId);
        Assert.Equal(child, first.Metadata.CurrentWorkingDirectory, ignoreCase: true);
        Assert.Equal(root, second.Metadata.CurrentWorkingDirectory, ignoreCase: true);
        Assert.Contains(secondOutput, item => item.Text.Contains("<missing>", StringComparison.Ordinal));
        Assert.DoesNotContain(secondOutput, item => item.Text.Equals("yes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OutputStreamsBeforeCompletionAndExitCodeIsFramed()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        await using var session = new PowerShellTerminalSession(root, "stream");
        var firstOutput = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.OutputReceived += (_, item) =>
        {
            if (item.Stream == TerminalOutputStream.StandardOutput && item.Text.Contains("first", StringComparison.Ordinal)) firstOutput.TrySetResult(true);
        };

        var running = session.ExecuteAsync(Guid.NewGuid(), "Write-Output 'first'; Start-Sleep -Milliseconds 700; cmd /c exit 7", CancellationToken.None);
        await firstOutput.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(running.IsCompleted);
        var result = await running;
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task GenuinePowerShellErrorsStreamOnStandardErrorAndFailCommand()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        await using var session = new PowerShellTerminalSession(root, "errors");
        var stderr = new List<TerminalSessionOutput>();
        session.OutputReceived += (_, item) =>
        {
            if (item.Stream == TerminalOutputStream.StandardError) stderr.Add(item);
        };

        var result = await session.ExecuteAsync(Guid.NewGuid(), "Write-Error 'expected-error'", CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(stderr, item => item.Text.Contains("expected-error", StringComparison.Ordinal));
        Assert.DoesNotContain(stderr, item => item.Text.Contains("CLIXML", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InterruptTargetsOnlyOneSessionAndLeavesItUsableWithTruthfulReset()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        await using var first = new PowerShellTerminalSession(root, "A");
        await using var second = new PowerShellTerminalSession(root, "B");
        var firstPid = first.ProcessId;
        var secondPid = second.ProcessId;
        using var cts = new CancellationTokenSource();
        var running = first.ExecuteAsync(Guid.NewGuid(), "$env:WILL_RESET='yes'; Start-Sleep -Seconds 30", cts.Token);
        await Task.Delay(250);
        cts.Cancel();
        var cancelled = await running.WaitAsync(TimeSpan.FromSeconds(8));

        Assert.True(cancelled.Cancelled);
        Assert.True(cancelled.StateWasReset);
        Assert.NotEqual(firstPid, first.ProcessId);
        Assert.Equal(secondPid, second.ProcessId);
        var check = new List<TerminalSessionOutput>();
        first.OutputReceived += (_, item) => check.Add(item);
        await first.ExecuteAsync(Guid.NewGuid(), "if ($null -eq $env:WILL_RESET) { Write-Output '<missing>' } else { Write-Output $env:WILL_RESET }; Write-Output 'usable'", CancellationToken.None);
        Assert.Contains(check, item => item.Text.Contains("usable", StringComparison.Ordinal));
        Assert.Contains(check, item => item.Text.Contains("<missing>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisposeTerminatesShellAndChildProcessTree()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        var session = new PowerShellTerminalSession(root, "cleanup");
        var shellPid = session.ProcessId ?? throw new InvalidOperationException("Shell process did not start.");
        var childPidSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.OutputReceived += (_, item) =>
        {
            const string prefix = "childpid=";
            if (item.Text.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(item.Text[prefix.Length..], out var pid)) childPidSource.TrySetResult(pid);
        };
        await session.ExecuteAsync(Guid.NewGuid(), "$p=Start-Process powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 60' -PassThru; Write-Output ('childpid=' + $p.Id)", CancellationToken.None);
        var childPid = await childPidSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

        session.Dispose();
        await WaitUntilAsync(() => !ProcessExists(shellPid) && !ProcessExists(childPid), TimeSpan.FromSeconds(5));

        Assert.False(ProcessExists(shellPid));
        Assert.False(ProcessExists(childPid));
    }

    private static string CreateRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "haven-terminal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool ProcessExists(int pid)
    {
        try { using var process = System.Diagnostics.Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(50);
    }
}
