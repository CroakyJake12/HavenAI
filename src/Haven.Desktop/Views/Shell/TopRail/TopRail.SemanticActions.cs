namespace Haven.Desktop.Views.Shell.TopRail;

public sealed partial class TopRail
{
    private void InvokeHomeAction()
    {
        Fire("TopRail.Logo.Click");
        HomeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InvokeNewTabAction()
    {
        Fire("TopRail.Tabs.AddTab");
        NewTabRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InvokeTabOverviewAction()
    {
        Fire("TopRail.Tabs.Overview");
        TabOverviewRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InvokeBackAction()
    {
        Fire("TopRail.Actions.Back.Click");
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InvokeForwardAction()
    {
        Fire("TopRail.Actions.Forward.Click");
        ForwardRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InvokeAppsAction()
    {
        Fire("TopRail.Actions.Apps.Click");
        AppsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InvokeActionsAction()
    {
        Fire("TopRail.Actions.Open");
        ActionsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InvokeModelAction()
    {
        Fire("TopRail.Actions.Model.Click");
        ModelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InvokeSearchAction()
    {
        Fire("TopRail.Actions.Search.Click");
        SearchRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InvokeNotificationsAction()
    {
        Fire("TopRail.Actions.Notifications.Click");
        ShowNotifications();
    }

    private void InvokeTabSelection(string key)
    {
        Fire("TopRail.Tabs.TabClicked");
        TabSelected?.Invoke(this, key);
    }
}
