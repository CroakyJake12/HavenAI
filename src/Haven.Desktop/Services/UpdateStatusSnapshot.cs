/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/UpdateStatusSnapshot.cs, in Haven.Desktop's shared Services folder.
 * What: This file owns UpdateStatusSnapshot. Read the member comments below as a map of each responsibility.
 * How: A thread-safe static store of the most recent IUpdateService status report; App.axaml.cs records every StatusChanged report here and any surface (Settings, About) reads LastReport without owning the update pipeline.
 * Why: Update state must stay discoverable after startup checks run before UI exists, without duplicating orchestrator state.
 * Maintenance: Presentation-only cache; never move business logic or persistence into this type.
 */

using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Holds the most recent update status report so shell surfaces can display honest update state even when it was produced before they existed.
/// </summary>
public static class UpdateStatusSnapshot
{
    private static readonly object Gate = new();
    private static UpdateStatusReport? _lastReport;

    /// <summary>Gets the latest recorded report, or <c>null</c> when no check or status change has been observed yet.</summary>
    public static UpdateStatusReport? LastReport
    {
        get { lock (Gate) return _lastReport; }
    }

    /// <summary>
    /// Records a new status snapshot, replacing any previous one. Thread-safe; raised from background threads by design.
    /// </summary>
    /// <param name="report">The report to record; never <c>null</c>.</param>
    public static void Record(UpdateStatusReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (Gate)
        {
            _lastReport = report;
        }
    }
}
