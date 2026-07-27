/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Browser/BrowserNativeRequestPolicy.cs, in the Browser layer, which isolates browser state, safety policy, transport, and automation.
 * What: This file owns BrowserTopLevelAssessment, BrowserPopupDisposition, BrowserPopupAssessment, BrowserNativeRequestPolicy. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Browser capabilities are isolated behind explicit policy boundaries because navigation and automation process untrusted external content.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Browser;

/// <summary>
/// Represents browser top level assessment and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserTopLevelAssessment(bool IsAllowed, string Reason)
{
    /// <summary>
    /// Performs the allow step owned by this component.
    /// </summary>
    public static BrowserTopLevelAssessment Allow(string reason) => new(true, reason);
    /// <summary>
    /// Performs the deny step owned by this component.
    /// </summary>
    public static BrowserTopLevelAssessment Deny(string reason) => new(false, reason);
}

/// <summary>
/// Lists the supported browser popup disposition values used to make state explicit and type-safe.
/// </summary>
public enum BrowserPopupDisposition
{
    OpenInCurrentTab,
    BlockAsk,
    BlockDenied,
    BlockUnsafe
}

/// <summary>
/// Represents browser popup assessment and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserPopupAssessment(BrowserPopupDisposition Disposition, string Reason)
{
    /// <summary>
    /// Reports whether allowed applies to the current state.
    /// </summary>
    public bool IsAllowed => Disposition == BrowserPopupDisposition.OpenInCurrentTab;
}

/// <summary>
/// Represents browser native request policy and keeps its related state and behavior together.
/// </summary>
public static class BrowserNativeRequestPolicy
{
    /// <summary>
    /// Performs the assess top level step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the assess popup step owned by this component.
    /// </summary>
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
