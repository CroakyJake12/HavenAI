using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Shell.NativePresentation;

internal sealed record StudyAssignmentSidebarEntry(
    Guid PlanTaskId,
    string Title,
    string Subtitle,
    bool Completed,
    bool Overdue);

internal enum StudyAssignmentSidebarAction
{
    OpenPlan,
    EditDeadline,
    Complete
}

internal sealed record StudyAssignmentSidebarRequest(Guid PlanTaskId, StudyAssignmentSidebarAction Action);

/// <summary>
/// Product-owned Haven.UI section that projects canonical Study/Plan assignments into the shared native sidebar.
/// </summary>
internal sealed class StudyAssignmentsHavenSection : IDisposable
{
    private readonly DynamicUI _dynamicUi;
    private readonly Dictionary<string, DynamicUIItem> _items = new(StringComparer.Ordinal);
    private bool _disposed;

    public StudyAssignmentsHavenSection(Page root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Section = new HavenContainer { Name = "StudyAssignmentsSection", Layout = HavenLayout.Vertical };
        Section.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Section.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        Section.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        Heading = new HavenText("Assignments · select a subject") { Name = "StudyAssignmentsHeading", Level = TextLevel.H3 };
        Rows = new DynamicUIRuntime { Name = "StudyAssignmentRows" };
        Rows.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Empty = new HavenText("Select a subject to see its assignments and homework.") { Name = "StudyAssignmentsEmpty" };
        Empty.SetValue(HavenProperties.FontSize, 11d);
        Empty.SetValue(HavenProperties.Foreground, "TextSecondary");
        Section.Add(Heading);
        Section.Add(Rows);
        Section.Add(Empty);

        var scrollHost = root.DescendantsAndSelf().OfType<HavenContainer>().Single(item => item.Name == "ScrollHost");
        scrollHost.Add(Section);

        var templates = new HavenDynamicUITemplateCatalog();
        templates.Register("""
            <DynamicUI Name="StudyAssignmentSidebarRow">
              <Container Name="RowRoot" Layout="Vertical" Width="100%" Gap="4px" Padding="8px" Background="{{BACKGROUND}}" BorderColor="Border" BorderWidth="1px" Radius="12px">
                <Button Name="Open" Variant="Navigation" IconKey="calendar" Content="{{TITLE}}" Width="100%" MinHeight="34px" />
                <Text Name="Subtitle" Content="{{SUBTITLE}}" Foreground="TextSecondary" FontSize="11" />
                <Container Name="Actions" Layout="Grid" Columns="1fr 1fr" Width="100%" Gap="6px">
                  <Button Name="Edit" Variant="Tertiary" Content="Edit deadline" Column="0" MinHeight="32px" />
                  <Button Name="Complete" Variant="Tertiary" Content="{{COMPLETE_LABEL}}" Column="1" MinHeight="32px" />
                </Container>
              </Container>
            </DynamicUI>
            """, "StudyAssignmentsSidebar.hui");
        _dynamicUi = new DynamicUI(root, templates);
    }

    public HavenContainer Section { get; }
    public HavenText Heading { get; }
    public DynamicUIRuntime Rows { get; }
    public HavenText Empty { get; }

    public event EventHandler<StudyAssignmentSidebarRequest>? ActionRequested;

    public void SetContext(bool isStudyMode, bool subjectSelected, IReadOnlyList<StudyAssignmentSidebarEntry> entries)
    {
        if (_disposed) return;
        Section.SetValue(HavenProperties.Visibility, isStudyMode ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        Heading.Content = subjectSelected ? "Assignments" : "Assignments · select a subject";
        Empty.Content = subjectSelected
            ? "No Plan-linked assignments for this subject."
            : "Select a subject to see its assignments and homework.";
        Empty.SetValue(HavenProperties.Visibility, isStudyMode && (!subjectSelected || entries.Count == 0) ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        Rows.SetValue(HavenProperties.Visibility, isStudyMode && subjectSelected && entries.Count > 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        SyncRows(subjectSelected ? entries : []);
    }

    private void SyncRows(IReadOnlyList<StudyAssignmentSidebarEntry> entries)
    {
        var expected = entries.Select(entry => $"assignment-{entry.PlanTaskId:N}").ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _items.Keys.Where(id => !expected.Contains(id)).ToArray())
        {
            _dynamicUi.DeleteItem("StudyAssignmentRows", stale);
            _items.Remove(stale);
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var id = $"assignment-{entry.PlanTaskId:N}";
            var values = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["TITLE"] = entry.Title,
                ["SUBTITLE"] = entry.Subtitle,
                ["BACKGROUND"] = entry.Completed || entry.Overdue ? "SurfaceRaised" : "Transparent",
                ["COMPLETE_LABEL"] = entry.Completed ? "Completed" : "Complete"
            };

            if (!_items.TryGetValue(id, out var item))
            {
                item = _dynamicUi.CreateItem("StudyAssignmentSidebarRow", "StudyAssignmentRows", id, values, index);
                _items[id] = item;
                Wire(item, "Open", () => ActionRequested?.Invoke(this, new(entry.PlanTaskId, StudyAssignmentSidebarAction.OpenPlan)));
                Wire(item, "Edit", () => ActionRequested?.Invoke(this, new(entry.PlanTaskId, StudyAssignmentSidebarAction.EditDeadline)));
                Wire(item, "Complete", () => ActionRequested?.Invoke(this, new(entry.PlanTaskId, StudyAssignmentSidebarAction.Complete)));
            }
            else
            {
                item.SetVariables(values);
                var currentIndex = Rows.Items.ToList().IndexOf(item);
                if (currentIndex != index) _dynamicUi.MoveItem("StudyAssignmentRows", id, index);
            }

            item.GetComponent<HavenButton>("Open").Accessibility.AccessibleName = $"Open {entry.Title} in Plan";
            item.GetComponent<HavenButton>("Edit").Accessibility.AccessibleName = $"Edit deadline for {entry.Title}";
            var complete = item.GetComponent<HavenButton>("Complete");
            complete.Accessibility.AccessibleName = entry.Completed ? $"{entry.Title} completed" : $"Complete {entry.Title}";
            complete.SetValue(HavenProperties.Enabled, !entry.Completed);
        }
    }

    private static void Wire(DynamicUIItem item, string componentName, Action action)
    {
        var button = item.GetComponent<HavenButton>(componentName);
        button.Invoked += (_, _) => action();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ActionRequested = null;
        _items.Clear();
    }
}
