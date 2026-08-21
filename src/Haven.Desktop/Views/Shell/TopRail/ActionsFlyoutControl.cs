using Avalonia.Controls;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>Thin platform popup host for the Haven-owned Actions catalogue.</summary>
public sealed class ActionsFlyoutControl : UserControl
{
    private readonly ActionsFlyoutFinalScene _scene;
    private readonly HavenSceneControl _host;
    private Action? _editActions;

    public ActionsFlyoutControl()
    {
        _scene = new ActionsFlyoutFinalScene();
        _host = new HavenSceneControl { Root = _scene.Root };
        Content = _host;
        _scene.ActionRequested += action =>
        {
            action.OnExecute();
            ActionInvoked?.Invoke(this, EventArgs.Empty);
        };
        _scene.EditRequested += () =>
        {
            _editActions?.Invoke();
            ActionInvoked?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? ActionInvoked;

    public void SetActions(IReadOnlyList<DynamicActionToolbar.ToolbarAction> actions) => _scene.SetActions(actions);

    public void SetEditActionsHandler(Action onExecute) => _editActions = onExecute;

    public bool FocusSearch() => _host.FocusElement(_scene.Search);

    internal ActionsFlyoutFinalScene HavenScene => _scene;
    internal HavenSceneControl SceneHost => _host;
}
