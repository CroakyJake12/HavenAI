/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/BrowserNativePolicyAndRecoveryTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserNativePolicyAndRecoveryTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Browser;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents browser native policy and recovery tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserNativePolicyAndRecoveryTests
{
    /// <summary>
    /// Performs the top level policy allows supported safe addresses step owned by this component.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("http://example.com:8080/path")]
    [InlineData("about:blank")]
    public void TopLevelPolicyAllowsSupportedSafeAddresses(string value)
    {
        var result = BrowserNativeRequestPolicy.AssessTopLevel(new Uri(value));
        Assert.True(result.IsAllowed, result.Reason);
    }

    /// <summary>
    /// Performs the top level policy blocks unsupported or credential bearing addresses step owned by this component.
    /// </summary>
    [Theory]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://user:password@example.com")]
    public void TopLevelPolicyBlocksUnsupportedOrCredentialBearingAddresses(string value)
    {
        var result = BrowserNativeRequestPolicy.AssessTopLevel(new Uri(value));
        Assert.False(result.IsAllowed);
    }

    /// <summary>
    /// Performs the popup policy requires safe requester target and explicit allow step owned by this component.
    /// </summary>
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
        Assert.Equal(BrowserPopupDisposition.OpenInNewTab, allow.Disposition);
        Assert.True(allow.IsAllowed);
        Assert.Equal(BrowserPopupDisposition.BlockUnsafe, unsafeTarget.Disposition);
        Assert.Equal(BrowserPopupDisposition.BlockUnsafe, unsafeRequester.Disposition);
    }

    /// <summary>
    /// Ensures untrusted document metadata is bounded before Haven-owned browser chrome consumes it.
    /// </summary>
    [Fact]
    public void PageMetadataNormalizationBoundsTitleAndRejectsUnsafeFavicons()
    {
        var address = new Uri("https://example.com/page");
        var longTitle = new string('x', 700);
        var safe = BrowserNativeRequestPolicy.NormalizePageMetadata(address, "  Example title  ", "https://cdn.example.net/favicon.ico");
        var unsafeIcon = BrowserNativeRequestPolicy.NormalizePageMetadata(address, longTitle, "data:image/svg+xml,<svg/>");
        var credentialIcon = BrowserNativeRequestPolicy.NormalizePageMetadata(address, null, "https://user:secret@example.com/icon.png");
        Assert.Equal("Example title", safe.Title);
        Assert.Equal("https://cdn.example.net/favicon.ico", safe.Favicon);
        Assert.Equal(512, unsafeIcon.Title.Length);
        Assert.Null(unsafeIcon.Favicon);
        Assert.Equal("example.com", credentialIcon.Title);
        Assert.Null(credentialIcon.Favicon);
    }

    /// <summary>
    /// Performs the recovery limiter bounds attempts and reopens after the window step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the recovery limiter rejects invalid configuration step owned by this component.
    /// </summary>
    [Fact]
    public void RecoveryLimiterRejectsInvalidConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserRecoveryLimiter(0, TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserRecoveryLimiter(1, TimeSpan.Zero));
    }
    [Fact]
    public void ShortcutPolicyMapsStandardBrowserAccelerators()
    {
        Assert.Equal(BrowserShortcutAction.FocusAddress, BrowserShortcutPolicy.Resolve(BrowserShortcutKey.L, true, false, false));
        Assert.Equal(BrowserShortcutAction.NewTab, BrowserShortcutPolicy.Resolve(BrowserShortcutKey.T, true, false, false));
        Assert.Equal(BrowserShortcutAction.NewPrivateTab, BrowserShortcutPolicy.Resolve(BrowserShortcutKey.N, true, true, false));
        Assert.Equal(BrowserShortcutAction.CloseTab, BrowserShortcutPolicy.Resolve(BrowserShortcutKey.W, true, false, false));
        Assert.Equal(BrowserShortcutAction.Reload, BrowserShortcutPolicy.Resolve(BrowserShortcutKey.F5, false, false, false));
        Assert.Equal(BrowserShortcutAction.Back, BrowserShortcutPolicy.Resolve(BrowserShortcutKey.Left, false, false, true));
        Assert.Equal(BrowserShortcutAction.ToggleBookmark, BrowserShortcutPolicy.Resolve(BrowserShortcutKey.D, true, false, false));
        Assert.Equal(BrowserShortcutAction.None, BrowserShortcutPolicy.Resolve(BrowserShortcutKey.T, true, true, false));
    }

}
