using System.Runtime.CompilerServices;

namespace Haven.Application;

public static class BrowserAutomationRegistry
{
    private static readonly ConditionalWeakTable<IBrowserToolService, Holder> Registrations = new();
    private static readonly object Gate = new();

    public static void Register(IBrowserToolService browser, IBrowserAutomationService automation)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(automation);
        lock (Gate)
        {
            Registrations.Remove(browser);
            Registrations.Add(browser, new Holder(automation));
        }
    }

    public static IBrowserAutomationService Resolve(IBrowserToolService browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        return Registrations.TryGetValue(browser, out var holder)
            ? holder.Automation
            : throw new InvalidOperationException("Browser automation has not been attached to this browser session.");
    }

    private sealed record Holder(IBrowserAutomationService Automation);
}
