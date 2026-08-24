using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Tasks;

internal sealed record TasksSpaceRecentItem(Guid Id, string Title, string Subtitle);

/// <summary>
/// Haven-owned Tasks Space composition for one-off delegated work. Reusable and scheduled work belongs to Automations.
/// </summary>
internal sealed class TasksSpaceHavenScene : IDisposable
{
    private bool _disposed;

    public TasksSpaceHavenScene()
    {
        Root = BuildRoot();
        Instruction = Get<Input>("Instruction");
        DelegateTask = Get<HavenButton>("DelegateTask");
        NewBlankTask = Get<HavenButton>("NewBlankTask");
        RecentRows = Get<Container>("RecentRows");
        Status = Get<HavenText>("Status");
        DelegateTask.Invoked += OnDelegateTaskInvoked;
        NewBlankTask.Invoked += OnNewBlankTaskInvoked;
    }

    public Page Root { get; }
    public Input Instruction { get; }
    public HavenButton DelegateTask { get; }
    public HavenButton NewBlankTask { get; }
    public Container RecentRows { get; }
    public HavenText Status { get; }

    public event EventHandler<string>? DelegateRequested;
    public event EventHandler? NewBlankTaskRequested;
    public event EventHandler<Guid>? RecentTaskRequested;

    public void SetRecent(IReadOnlyList<TasksSpaceRecentItem> items)
    {
        foreach (var child in RecentRows.Children.ToArray()) RecentRows.Remove(child);
        if (items.Count == 0)
        {
            var empty = new HavenText { Content = "No delegated tasks yet. Start a one-off task above." };
            empty.SetValue(HavenProperties.Foreground, "TextSecondary");
            empty.SetValue(HavenProperties.FontSize, 12d);
            RecentRows.Add(empty);
            return;
        }

        foreach (var item in items)
        {
            var card = new Container { Layout = HavenLayout.Vertical };
            card.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(10)));
            card.SetValue(HavenProperties.Gap, HavenLength.Px(2));
            card.SetValue(HavenProperties.Background, "SurfaceRaised");
            card.SetValue(HavenProperties.BorderColor, "Border");
            card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));

            var open = new HavenButton { Content = item.Title, Variant = ButtonVariant.Navigation };
            open.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            open.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
            open.Accessibility.AccessibleName = $"Open delegated task {item.Title}";
            var id = item.Id;
            open.Invoked += (_, _) => RecentTaskRequested?.Invoke(this, id);
            card.Add(open);

            var subtitle = new HavenText { Content = item.Subtitle };
            subtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
            subtitle.SetValue(HavenProperties.FontSize, 11d);
            card.Add(subtitle);
            RecentRows.Add(card);
        }
    }

    public void SetBusy(bool busy)
    {
        Instruction.SetValue(HavenProperties.Enabled, !busy);
        DelegateTask.SetValue(HavenProperties.Enabled, !busy);
        NewBlankTask.SetValue(HavenProperties.Enabled, !busy);
        DelegateTask.Content = busy ? "Starting task…" : "Delegate task";
    }

    public void SetStatus(string? value)
    {
        Status.Content = value ?? string.Empty;
        Status.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(value) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private void OnDelegateTaskInvoked(object? sender, EventArgs e)
    {
        var instruction = Instruction.Text.Trim();
        if (instruction.Length == 0)
        {
            SetStatus("Describe the one-off task you want Haven to carry out.");
            return;
        }
        DelegateRequested?.Invoke(this, instruction);
    }

    private void OnNewBlankTaskInvoked(object? sender, EventArgs e) => NewBlankTaskRequested?.Invoke(this, EventArgs.Empty);

    private T Get<T>(string name) where T : HavenElement =>
        (T)Root.DescendantsAndSelf().Single(element => element.Name == name);

    private static Page BuildRoot()
    {
        const string markup = """
            <Page Name="TasksSpaceRoot" Layout="Grid" Width="100%" Height="100%" Rows="Auto Auto Auto Auto 1fr Auto" Gap="12px" Padding="24px" Background="Surface">
              <Text Row="0" Content="Tasks" Level="H1" />
              <Text Row="1" Content="Delegate one-off work to Haven. Research, compare, organise, build, or investigate something once; reusable and scheduled workflows live in Automations." Foreground="TextSecondary" FontSize="13" />
              <Container Row="2" Layout="Vertical" Width="100%" Gap="8px" Padding="14px" Background="SurfaceRaised" BorderColor="Border" BorderWidth="1px" Radius="16px">
                <Input Name="Instruction" Width="100%" MinHeight="52px" Placeholder="What should Haven do?" />
                <Container Layout="Grid" Columns="1fr 1fr" Width="100%" Gap="8px">
                  <Button Name="DelegateTask" Column="0" Variant="Primary" IconKey="arrow-up" Content="Delegate task" MinHeight="40px" />
                  <Button Name="NewBlankTask" Column="1" Variant="Tertiary" IconKey="plus" Content="Open blank task" MinHeight="40px" />
                </Container>
              </Container>
              <Text Row="3" Content="Recent delegated work" Level="H2" />
              <Container Name="RecentRows" Row="4" Layout="Vertical" Width="100%" Overflow="Scroll" Clip="true" Gap="8px" />
              <Text Name="Status" Row="5" Content="" Foreground="TextSecondary" FontSize="11" Visibility="Collapsed" />
            </Page>
            """;
        return (Page)new HavenMarkupParser().Parse(markup, "TasksSpace.hui");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DelegateTask.Invoked -= OnDelegateTaskInvoked;
        NewBlankTask.Invoked -= OnNewBlankTaskInvoked;
    }
}
