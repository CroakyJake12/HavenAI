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
using Haven.Desktop.Controls;

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
        var unread = filtered
            .Where(row => !row.IsPinned && row.IsUnread)
            .ToArray();
        var remaining = filtered
            .Where(row => !row.IsPinned && !row.IsUnread)
            .ToArray();

        _pinnedPanel.Children.Clear();
        foreach (var row in pinned)
        {
            _pinnedPanel.Children.Add(BuildProjectTile(row, ProjectTileKind.Pinned));
        }

        _unreadPanel.Children.Clear();
        foreach (var row in unread)
        {
            _unreadPanel.Children.Add(BuildProjectTile(row, ProjectTileKind.Unread));
        }

        _projectPanel.Children.Clear();
        foreach (var row in remaining)
        {
            _projectPanel.Children.Add(BuildProjectTile(row, ProjectTileKind.Standard));
        }

        _pinnedHeading.IsVisible = pinned.Length > 0;
        _pinnedPanel.IsVisible = pinned.Length > 0;
        _unreadHeading.IsVisible = unread.Length > 0;
        _unreadPanel.IsVisible = unread.Length > 0;
        _projectHeading.IsVisible = remaining.Length > 0;
        _projectPanel.IsVisible = remaining.Length > 0;
        _emptyState.IsVisible = filtered.Length == 0;

        SetStatus(string.Empty);
    }

    private Control BuildProjectTile(ProjectRow row, ProjectTileKind kind)
    {
        var icon = new HavenIcon
        {
            IconKey = ProjectIcon(row),
            Width = 56,
            Height = 56,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var title = new TextBlock
        {
            Text = row.Name,
            FontSize = 17,
            FontWeight = FontWeight.Bold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 205,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var activity = new TextBlock
        {
            Text = kind == ProjectTileKind.Unread ? "Updated " + FormatActivity(row.UpdatedAt) : row.State,
            FontSize = 11,
            Foreground = MutedBrush,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 205,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var content = new StackPanel
        {
            Spacing = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { icon, title, activity }
        };
        var tile = new Button
        {
            Width = 240,
            Height = 176,
            Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(18),
            BorderBrush = kind == ProjectTileKind.Unread ? Brush("#F3F58E") : BorderBrush,
            BorderThickness = new Thickness(kind == ProjectTileKind.Unread ? 2 : 1),
            CornerRadius = new CornerRadius(22),
            Background = kind switch
            {
                ProjectTileKind.Pinned => Brush("#DDF9FB"),
                ProjectTileKind.Unread => Brush("#FEFFA6"),
                _ => CardBrush
            },
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = content
        };
        ToolTip.SetTip(tile, BuildProjectTooltip(row));
        tile.Click += async (_, _) => await OpenProjectAsync(row);
        tile.ContextMenu = BuildProjectContextMenu(row);
        AutomationProperties.SetName(
            tile,
            $"Open {row.Name}{(row.IsUnread ? ", unread changes" : string.Empty)}");
        return tile;
    }

    private ContextMenu BuildProjectContextMenu(ProjectRow row)
    {
        var open = new MenuItem { Header = "Open" };
        open.Click += async (_, _) => await OpenProjectAsync(row);

        var pin = new MenuItem { Header = row.IsPinned ? "Unpin" : "Pin" };
        pin.Click += async (_, _) => await SetPinnedAsync(row, !row.IsPinned);

        var readState = new MenuItem { Header = row.IsUnread ? "Mark read" : "Mark unread" };
        readState.Click += async (_, _) => await SetReadStateAsync(row, markUnread: !row.IsUnread);

        var archive = new MenuItem { Header = "Archive" };
        archive.Click += async (_, _) => await ArchiveProjectAsync(row);

        return new ContextMenu { ItemsSource = new object[] { open, pin, readState, archive } };
    }

    private static Control BuildProjectTooltip(ProjectRow row) => new StackPanel
    {
        MaxWidth = 380,
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = row.Name, FontWeight = FontWeight.Bold },
            new TextBlock { Text = row.RootPath, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = row.Summary, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = $"{row.Branch} · {row.State}", Foreground = MutedBrush }
        }
    };

    private static string ProjectIcon(ProjectRow row)
    {
        var combined = (row.Name + " " + row.Summary).ToLowerInvariant();
        if (combined.Contains("photo") || combined.Contains("film") || combined.Contains("camera")) return "image";
        if (combined.Contains("code") || combined.Contains("app") || combined.Contains("software")) return "code";
        return "folder";
    }

    private enum ProjectTileKind
    {
        Standard,
        Pinned,
        Unread
    }
}
