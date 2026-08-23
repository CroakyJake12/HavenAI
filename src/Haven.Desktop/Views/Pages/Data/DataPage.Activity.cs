using Haven.Application;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage
{
    private IGenUiLiveActivitySurface BeginDataActivity(string title, string status)
    {
        var activity = new DefaultGenUiLiveActivitySurface(
            Guid.NewGuid(),
            Workbook?.Id ?? Guid.NewGuid(),
            "data",
            title,
            _genUiInstances);
        _activities.Track(activity);
        activity.Update(new GenUiLiveActivityUpdate(
            GenUiLiveActivityPhase.Operating,
            status,
            null,
            null,
            null,
            DateTimeOffset.UtcNow));
        return activity;
    }

    private static void CompleteDataActivity(IGenUiLiveActivitySurface activity, string status)
    {
        activity.Update(new GenUiLiveActivityUpdate(
            GenUiLiveActivityPhase.Completed,
            status,
            100,
            null,
            null,
            DateTimeOffset.UtcNow));
    }
}
