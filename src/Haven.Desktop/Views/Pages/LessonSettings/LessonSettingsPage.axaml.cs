using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.LessonSettings;

/// <summary>
/// Product adapter for the Haven.UI Lesson settings scene. Existing services,
/// persistence, event contracts and the saved callback remain outside the UI
/// framework.
/// </summary>
public sealed partial class LessonSettingsPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly Lesson _lesson;
    private readonly IContainerRepository _containers;
    private readonly Func<Task> _saved;
    private readonly LessonSettingsHavenScene _route;
    private readonly List<(HavenElement Element, EventHandler Handler)> _stateSubscriptions = [];
    private bool _disposed;

    public LessonSettingsPage(HavenEventBus bus, Lesson lesson, IContainerRepository containers, Func<Task> saved)
    {
        _bus = bus;
        _lesson = lesson;
        _containers = containers;
        _saved = saved;

        InitializeComponent();
        _route = new LessonSettingsHavenScene();
        Scene.Root = _route.Root;
        _route.LoadLesson(_lesson);
        WireEvents();
    }

    internal LessonSettingsHavenScene Route => _route;
    internal HavenSceneControl SceneHost => Scene;
    internal HavenElement SceneRoot => _route.Root;

    private void WireEvents()
    {
        _bus.RegisterElement("LessonSettings.Actions.Save", Scene);
        var named = new (string Name, HavenElement Element)[]
        {
            ("LessonSettings.Actions.Save", _route.SaveButton),
            ("LessonSettings.Fields.Name", _route.NameInput),
            ("LessonSettings.Fields.TopicGroup", _route.TopicGroupInput),
            ("LessonSettings.Fields.StructureJson", _route.StructureJsonInput)
        };
        foreach (var entry in named)
        {
            var captured = entry.Element;
            var name = entry.Name;
            var previous = captured.State;
            EventHandler handler = (_, _) =>
            {
                var next = captured.State;
                if (previous.HasFlag(HavenElementState.Hover) != next.HasFlag(HavenElementState.Hover))
                    _bus.Fire(name + (next.HasFlag(HavenElementState.Hover) ? ".Hover" : ".Leave"));
                if (previous.HasFlag(HavenElementState.Pressed) != next.HasFlag(HavenElementState.Pressed))
                    _bus.Fire(name + (next.HasFlag(HavenElementState.Pressed) ? ".Press" : ".Release"));
                if (previous.HasFlag(HavenElementState.Focused) != next.HasFlag(HavenElementState.Focused))
                    _bus.Fire(name + (next.HasFlag(HavenElementState.Focused) ? ".Focus" : ".Blur"));
                previous = next;
            };
            captured.Invalidated += handler;
            _stateSubscriptions.Add((captured, handler));
        }

        _route.SaveRequested += (_, _) =>
        {
            _bus.Fire("LessonSettings.Actions.Save");
            _ = SaveAsync();
        };
    }

    private async Task SaveAsync()
    {
        if (_disposed) return;
        var name = _route.NameInput.Text.Trim();
        var topicGroup = _route.TopicGroupInput.Text.Trim();
        var structureJson = string.IsNullOrWhiteSpace(_route.StructureJsonInput.Text) ? "{}" : _route.StructureJsonInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            _route.SetStatus("Lesson name is required.");
            return;
        }

        _route.EnableSave(false);
        try
        {
            var updated = _lesson with
            {
                Name = name,
                TopicGroup = topicGroup,
                StructureJson = structureJson,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _containers.UpsertLessonAsync(updated, CancellationToken.None);
            _route.SetStatus("Lesson saved.");
            await _saved();
        }
        catch (Exception ex)
        {
            _route.SetStatus($"Save failed: {ex.Message}");
        }
        finally
        {
            _route.EnableSave(true);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var (element, handler) in _stateSubscriptions) element.Invalidated -= handler;
        _stateSubscriptions.Clear();
        _route.Dispose();
    }
}
