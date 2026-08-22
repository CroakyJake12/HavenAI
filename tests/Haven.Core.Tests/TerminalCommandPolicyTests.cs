using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;
public sealed class TerminalCommandPolicyTests
{
    [Theory]
    [InlineData(PermissionMode.Ask, TerminalPermissionDecision.RequiresApproval)]
    [InlineData(PermissionMode.AutoSafe, TerminalPermissionDecision.RequiresApproval)]
    [InlineData(PermissionMode.FullAccess, TerminalPermissionDecision.Allowed)]
    public void ArbitraryCommandsRespectGlobalPermissionMode(PermissionMode permission, TerminalPermissionDecision expected)
    {
        RuntimeSafetyState.DisableSafeMode();
        Assert.Equal(expected, TerminalCommandPolicy.Evaluate(permission).Decision);
    }
    [Theory]
    [InlineData(PermissionMode.Ask)]
    [InlineData(PermissionMode.AutoSafe)]
    public void OneShotApprovalDoesNotRequireChangingPreference(PermissionMode permission)
    {
        RuntimeSafetyState.DisableSafeMode();
        Assert.Equal(TerminalPermissionDecision.Allowed, TerminalCommandPolicy.Evaluate(permission, approvedOnce: true).Decision);
    }
    [Fact]
    public void SafeModeDeniesEvenFullAccess()
    {
        RuntimeSafetyState.EnableSafeMode("test");
        try
        {
            var result = TerminalCommandPolicy.Evaluate(PermissionMode.FullAccess, approvedOnce: true);
            Assert.Equal(TerminalPermissionDecision.Denied, result.Decision);
            Assert.Contains("Safe Mode", result.Reason, StringComparison.Ordinal);
        }
        finally { RuntimeSafetyState.DisableSafeMode(); }
    }
    [Fact]
    public void RedactorRemovesCommonSecretsAndSignedUrlData()
    {
        const string input = "Bearer abc.def api_key=super-secret password:hunter2 {\"apiKey\":\"json-secret\"} https://example.test/path?token=signed#fragment";
        var output = SensitiveTextRedactor.Redact(input);
        Assert.DoesNotContain("abc.def", output, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", output, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", output, StringComparison.Ordinal);
        Assert.DoesNotContain("json-secret", output, StringComparison.Ordinal);
        Assert.Contains("{\"apiKey\":\"<redacted>\"}", output, StringComparison.Ordinal);
        Assert.DoesNotContain("signed", output, StringComparison.Ordinal);
        Assert.DoesNotContain("?token=", output, StringComparison.Ordinal);
        Assert.Contains("<redacted>", output, StringComparison.Ordinal);
        Assert.Contains("https://example.test/path", output, StringComparison.Ordinal);
    }
    [Fact]
    public void ActivityHubPublishesOnlyRedactedCommandAndOutput()
    {
        var hub = new TerminalCommandActivityHub();
        TerminalCommandActivity? observed = null;
        hub.ActivityPublished += (_, activity) => observed = activity;
        hub.Publish(new TerminalCommandActivity(Guid.NewGuid(), TerminalCommandOrigin.Agent, TerminalExecutionState.Succeeded, "curl -H \"Authorization: Bearer abc123\" https://example.test/x?token=q", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), new ProcessResult(0, "token=output-secret", "password=error-secret", TimeSpan.Zero, false), null, DateTimeOffset.UtcNow));
        Assert.NotNull(observed);
        Assert.DoesNotContain("abc123", observed!.Command, StringComparison.Ordinal);
        Assert.DoesNotContain("?token=", observed.Command, StringComparison.Ordinal);
        Assert.DoesNotContain("output-secret", observed.Result!.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("error-secret", observed.Result.StandardError, StringComparison.Ordinal);
    }
}
