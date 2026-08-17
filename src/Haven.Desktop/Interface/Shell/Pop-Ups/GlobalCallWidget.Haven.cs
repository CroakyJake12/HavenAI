using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Desktop.ViewModels;
using Haven.UI;
using HavenInput = Haven.UI.Components.Input;

namespace Haven.Desktop.Views.Shell.Overlays;

/// <summary>
/// Thin desktop host for the Haven.UI Voice scene. Product state and actions remain
/// in InChatCallWidgetViewModel/ICallCoordinator; this adapter owns only Avalonia host
/// integration that cannot live in Haven.UI (window placement and the OS file picker).
/// </summary>
public sealed partial class GlobalCallWidget : UserControl, IDisposable
{
    private readonly GlobalCallHavenScene _scene;
    private readonly DispatcherTimer _durationTimer;
    private bool _disposed;

    public GlobalCallWidget()
        : this(null)
    {
    }

    public GlobalCallWidget(InChatCallWidgetViewModel? viewModel)
    {
        var applicationViewModel = viewModel ?? throw new InvalidOperationException(
            "GlobalCallWidget must be created with the application call view-model.");

        InitializeComponent();
        AutomationProperties.SetAutomationId(this, "VoiceFloatingSurface");
        AutomationProperties.SetName(this, "Voice floating surface");

        _scene = new GlobalCallHavenScene(applicationViewModel, OnSceneDragDelta);
        Scene.Root = _scene.Root;
        Scene.InputSubmitted += OnSceneInputSubmitted;
        _scene.AddFilesRequested += OnAddFilesRequested;

        _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _durationTimer.Tick += OnDurationTimerTick;
        _durationTimer.Start();
    }

    public event EventHandler<Vector>? DragDelta;

    private void OnSceneDragDelta(HavenPoint delta) =>
        DragDelta?.Invoke(this, new Vector(delta.X, delta.Y));

    private void OnSceneInputSubmitted(HavenInput input) => _scene.SubmitFocusedInput(input);

    private void OnDurationTimerTick(object? sender, EventArgs e) => _scene.Tick(DateTimeOffset.Now);

    private async void OnAddFilesRequested(object? sender, EventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add a file to this voice session",
            AllowMultiple = true
        });

        _scene.AddContextFiles(files.Select(file => file.Name));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _durationTimer.Stop();
        _durationTimer.Tick -= OnDurationTimerTick;
        _scene.AddFilesRequested -= OnAddFilesRequested;
        Scene.InputSubmitted -= OnSceneInputSubmitted;
        Scene.Root = null;
        _scene.Dispose();
    }
}
