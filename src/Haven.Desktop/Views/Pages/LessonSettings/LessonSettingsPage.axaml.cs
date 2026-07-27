using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.LessonSettings;

public sealed partial class LessonSettingsPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly Lesson _lesson;
    private readonly IContainerRepository _containers;
    private readonly Func<Task> _saved;

    public LessonSettingsPage(HavenEventBus bus, Lesson lesson, IContainerRepository containers, Func<Task> saved)
    {
        _bus = bus;
        _lesson = lesson;
        _containers = containers;
        _saved = saved;

        InitializeComponent();
        LoadLesson();
        WireEvents();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) { }

    private void LoadLesson()
    {
        NameHeading.Text = _lesson.Name;
        NameBox.Text = _lesson.Name;
        TopicGroupBox.Text = _lesson.TopicGroup;
        StructureJsonBox.Text = _lesson.StructureJson;
    }

    private void WireEvents()
    {
        _bus.RegisterElement("LessonSettings.Actions.Save", SaveButton);
        _bus.WirePointerEvents("LessonSettings.Actions.Save", SaveButton);
        SaveButton.Click += async (_, _) =>
        {
            _bus.Fire("LessonSettings.Actions.Save");
            await SaveAsync();
        };
    }

    private async Task SaveAsync()
    {
        var name = NameBox.Text?.Trim() ?? "";
        var topicGroup = TopicGroupBox.Text?.Trim() ?? "";
        var structureJson = StructureJsonBox.Text?.Trim() ?? "{}";

        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText.Text = "Lesson name is required.";
            return;
        }

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
            StatusText.Text = "Lesson saved.";
            await _saved();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }
}
