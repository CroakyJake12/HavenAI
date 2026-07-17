using Haven.Browser;

namespace Haven.Infrastructure.Tests;

public sealed class BrowserNativePolicyAndRecoveryTests
{
    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("http://example.com:8080/path")]
    [InlineData("about:blank")]
    public void TopLevelPolicyAllowsSupportedSafeAddresses(string value)
    {
        var result = BrowserNativeRequestPolicy.AssessTopLevel(new Uri(value));
        Assert.True(result.IsAllowed, result.Reason);
    }

    [Theory]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://user:password@example.com")]
    public void TopLevelPolicyBlocksUnsupportedOrCredentialBearingAddresses(string value)
    {
        var result = BrowserNativeRequestPolicy.AssessTopLevel(new Uri(value));
        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void PopupPolicyRequiresSafeRequesterTargetAndExplicitAllow()
    {
        var requester = new Uri("https://example.com/page");
        var target = new Uri("https://accounts.example.org/login");

        var ask = BrowserNativeRequestPolicy.AssessPopup(requester, target, BrowserSitePermissionDecision.Ask);
        var deny = BrowserNativeRequestPolicy.AssessPopup(requester, target, BrowserSitePermissionDecision.Deny);
        var allow = BrowserNativeRequestPolicy.AssessPopup(requester, target, BrowserSitePermissionDecision.Allow);
        var unsafeTarget = BrowserNativeRequestPolicy.AssessPopup(requester, new Uri("file:///C:/secret.txt"), BrowserSitePermissionDecision.Allow);
        var unsafeRequester = BrowserNativeRequestPolicy.AssessPopup(new Uri("about:blank"), target, BrowserSitePermissionDecision.Allow);

        Assert.Equal(BrowserPopupDisposition.BlockAsk, ask.Disposition);
        Assert.Equal(BrowserPopupDisposition.BlockDenied, deny.Disposition);
        Assert.Equal(BrowserPopupDisposition.OpenInCurrentTab, allow.Disposition);
        Assert.True(allow.IsAllowed);
        Assert.Equal(BrowserPopupDisposition.BlockUnsafe, unsafeTarget.Disposition);
        Assert.Equal(BrowserPopupDisposition.BlockUnsafe, unsafeRequester.Disposition);
    }

    [Fact]
    public void RecoveryLimiterBoundsAttemptsAndReopensAfterTheWindow()
    {
        var limiter = new BrowserRecoveryLimiter(2, TimeSpan.FromMinutes(1));
        var start = new DateTimeOffset(2026, 7, 17, 4, 0, 0, TimeSpan.Zero);

        Assert.True(limiter.TryAcquire(start));
        Assert.True(limiter.TryAcquire(start.AddSeconds(10)));
        Assert.False(limiter.TryAcquire(start.AddSeconds(20)));
        Assert.Equal(2, limiter.ActiveAttempts(start.AddSeconds(20)));
        Assert.True(limiter.TryAcquire(start.AddMinutes(1)));
        Assert.Equal(2, limiter.ActiveAttempts(start.AddMinutes(1)));
    }

    [Fact]
    public void RecoveryLimiterRejectsInvalidConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserRecoveryLimiter(0, TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserRecoveryLimiter(1, TimeSpan.Zero));
    }
}
