using System.Collections.Specialized;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Haven.Desktop.Views.Shell.NativePresentation;

internal sealed partial class NativeProjectsPage
{
    private async Task<IReadOnlyList<ProjectRow>> ReadRowsAsync(CancellationToken cancellationToken)
    {
        var state = await _stateStore.GetAllAsync(cancellationToken);
        var projects = FindProjectCollection().ToArray();

        if (projects.Length == 0)
        {
            projects = _fallbackProjects()
                .Where(item => item is not null)
                .ToArray();
        }

        var rows = new List<ProjectRow>(projects.Length);
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (NativePresentationReflection.Boolean(project, false, "IsArchived", "Archived"))
            {
                continue;
            }

            var name = NativePresentationReflection.Text(
                project,
                "Untitled project",
                "Name",
                "Title",
                "ProjectName");

            var rootPath = NativePresentationReflection.Text(
                project,
                "No folder connected",
                "RootPath",
                "Path",
                "FolderPath",
                "WorkspacePath");

            var id = NativePresentationReflection.Identifier(
                         project,
                         "Id",
                         "ProjectId",
                         "WorkspaceId",
                         "ContainerId")
                     ?? CreateStableIdentifier(project, name, rootPath);

            var updatedAt = NativePresentationReflection.Timestamp(
                                project,
                                "UpdatedAt",
                                "LastActivityAt",
                                "LastUpdatedAt",
                                "ModifiedAt",
                                "CreatedAt")
                            ?? DateTimeOffset.MinValue;

            state.TryGetValue(id, out var uiState);
            uiState ??= ProjectUiState.Empty;

            var sourcePinned = NativePresentationReflection.Boolean(
                project,
                false,
                "IsPinned",
                "Pinned");

            var sourceUnread = NativePresentationReflection.Boolean(
                project,
                false,
                "IsUnread",
                "Unread",
                "HasUnreadActivity");

            rows.Add(
                new ProjectRow(
                    project,
                    id,
                    name,
                    rootPath,
                    NativePresentationReflection.Text(
                        project,
                        "No meaningful task recorded yet",
                        "LastMeaningfulTask",
                        "RecentTask",
                        "LastTask",
                        "Summary",
                        "Description"),
                    NativePresentationReflection.Text(
                        project,
                        "No Git branch",
                        "Branch",
                        "BranchName",
                        "GitBranch"),
                    NativePresentationReflection.Text(
                        project,
                        "Not inspected",
                        "WorkState",
                        "WorkingTreeState",
                        "State",
                        "Status"),
                    NativePresentationReflection.Text(
                        project,
                        "Open the project to inspect its next useful action.",
                        "RecommendedAction",
                        "NextAction",
                        "AdaptiveHelp"),
                    updatedAt,
                    sourcePinned || uiState.IsPinned,
                    sourceUnread || uiState.IsUnread(updatedAt)));
        }

        return rows;
    }

    private IEnumerable<object> FindProjectCollection()
    {
        var projects = NativePresentationReflection.ReadCollection(
            _source,
            "Projects",
            "ProjectItems",
            "ProjectCards",
            "Workspaces",
            "Containers",
            "Items");

        return projects;
    }

    private void RenderRows(IReadOnlyList<ProjectRow> rows)
    {
        if (_disposed)
        {
            return;
        }

        var query = _searchBox.Text?.Trim();
        var filtered = rows
            .Where(row =>
                string.IsNullOrWhiteSpace(query) ||
                row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.RootPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Summary.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.UpdatedAt)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var pinned = filtered
            .Where(row => row.IsPinned)
            .ToArray();

        _pinnedPanel.Children.Clear();
        foreach (var row in pinned)
        {
            _pinnedPanel.Children.Add(BuildPinnedRow(row));
        }

        _projectPanel.Children.Clear();
        foreach (var row in filtered)
        {
            _projectPanel.Children.Add(BuildProjectCard(row));
        }

        _pinnedHeading.IsVisible = pinned.Length > 0;
        _pinnedPanel.IsVisible = pinned.Length > 0;
        _projectHeading.Text = filtered.Length == 1 ? "1 project" : $"{filtered.Length} projects";
        _emptyState.IsVisible = filtered.Length == 0;

        SetStatus(string.Empty);
    }

    private Control BuildProjectCard(ProjectRow row)
    {
        var title = new TextBlock
        {
            Text = row.Name,
            FontSize = 19,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var unread = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = CyanBrush,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = row.IsUnread
        };
        AutomationProperties.SetName(unread, row.IsUnread ? "Unread project activity" : "No unread project activity");

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children = { title, unread }
        };

        var path = new TextBlock
        {
            Text = row.RootPath,
            Foreground = MutedBrush,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 340
        };

        var summary = new TextBlock
        {
            Text = row.Summary,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 48,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var metrics = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Thickness(0, 14, 0, 0)
        };
        var branch = BuildMetric("BRANCH", row.Branch);
        var state = BuildMetric("STATE", row.State);
        Grid.SetColumn(state, 1);
        metrics.Children.Add(branch);
        metrics.Children.Add(state);

        var recommendation = new TextBlock
        {
            Text = row.RecommendedAction,
            Foreground = MutedBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var open = Button("Open", true);
        open.Click += async (_, _) => await OpenProjectAsync(row);
        AutomationProperties.SetName(open, $"Open {row.Name}");

        var pin = LinkButton(row.IsPinned ? "Unpin" : "Pin");
        pin.Click += async (_, _) => await SetPinnedAsync(row, !row.IsPinned);
        AutomationProperties.SetName(pin, row.IsPinned ? $"Unpin {row.Name}" : $"Pin {row.Name}");

        var readState = LinkButton(row.IsUnread ? "Mark read" : "Mark unread");
        readState.Click += async (_, _) => await SetReadStateAsync(row, markUnread: !row.IsUnread);
        AutomationProperties.SetName(
            readState,
            row.IsUnread ? $"Mark {row.Name} as read" : $"Mark {row.Name} as unread");

        var archive = LinkButton("Archive");
        archive.Click += async (_, _) => await ArchiveProjectAsync(row);
        AutomationProperties.SetName(archive, $"Archive {row.Name}");

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { open, pin, readState, archive }
        };

        var content = new StackPanel
        {
            Children =
            {
                titleRow,
                path,
                summary,
                metrics,
                recommendation,
                actions
            }
        };

        return new Border
        {
            Width = 380,
            MinHeight = 250,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(18),
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Background = row.IsUnread ? UnreadBrush : CardBrush,
            Child = content
        };
    }
}
