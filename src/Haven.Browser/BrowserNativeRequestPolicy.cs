namespace Haven.Browser;

public sealed record BrowserTopLevelAssessment(bool IsAllowed, string Reason)
{
    public static BrowserTopLevelAssessment Allow(string reason) => new(true, reason);
    public static BrowserTopLevelAssessment Deny(string reason) => new(false, reason);
}

public enum BrowserPopupDisposition
{
    OpenInCurrentTab,
    BlockAsk,
    BlockDenied,
    BlockUnsafe
}

public sealed record BrowserPopupAssessment(BrowserPopupDisposition Disposition, string Reason)
{
    public bool IsAllowed => Disposition == BrowserPopupDisposition.OpenInCurrentTab;
}

public static class BrowserNativeRequestPolicy
{
    public static BrowserTopLevelAssessment AssessTopLevel(Uri? address)
    {
        if (address is null) return BrowserTopLevelAssessment.Deny("The requested address is missing.");
        if (address.IsAbsoluteUri
            && address.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase)
            && address.AbsoluteUri.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            return BrowserTopLevelAssessment.Allow("The empty native document is allowed during browser initialization.");
        if (!address.IsAbsoluteUri || address.Scheme is not ("http" or "https"))
            return BrowserTopLevelAssessment.Deny("Only HTTP and HTTPS top-level pages are supported.");
        if (!string.IsNullOrEmpty(address.UserInfo))
            return BrowserTopLevelAssessment.Deny("Addresses containing embedded credentials are not allowed.");
        return BrowserTopLevelAssessment.Allow("The top-level address uses an allowed web scheme without embedded credentials.");
    }

    public static BrowserPopupAssessment AssessPopup(
        Uri? requestingAddress,
        Uri? requestedAddress,
        BrowserSitePermissionDecision windowManagementDecision)
    {
        var target = AssessTopLevel(requestedAddress);
        if (!target.IsAllowed || requestedAddress?.Scheme is not ("http" or "https"))
            return new BrowserPopupAssessment(BrowserPopupDisposition.BlockUnsafe, "Popup blocked: " + target.Reason);

        var requester = AssessTopLevel(requestingAddress);
        if (!requester.IsAllowed || requestingAddress?.Scheme is not ("http" or "https"))
            return new BrowserPopupAssessment(BrowserPopupDisposition.BlockUnsafe, "Popup blocked because its requesting origin is unavailable or unsafe.");

        return windowManagementDecision switch
        {
            BrowserSitePermissionDecision.Allow => new BrowserPopupAssessment(
                BrowserPopupDisposition.OpenInCurrentTab,
                "The requesting origin is allowed to open the target inside Haven's managed browser tab."),
            BrowserSitePermissionDecision.Deny => new BrowserPopupAssessment(
                BrowserPopupDisposition.BlockDenied,
                "Popup blocked by the saved WindowManagement decision for this origin."),
            _ => new BrowserPopupAssessment(
                BrowserPopupDisposition.BlockAsk,
                "Popup blocked. Set WindowManagement to Allow in Browser Safety to open it in the current tab.")
        };
    }
}
