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
    private async Task OpenProjectAsync(ProjectRow row)
    {
        try
        {
            SetStatus($"Opening {row.Name}…");

            var handled = await NativePresentationReflection.ExecuteCommandAsync(
                row.Source,
                null,
                "OpenCommand",
                "OpenProjectCommand",
                "SelectCommand");

            if (!handled)
            {
                handled = await NativePresentationReflection.ExecuteCommandAsync(
                    _source,
                    row.Source,
                    "OpenProjectCommand",
                    "SelectProjectCommand",
                    "OpenCommand",
                    "SelectCommand");
            }

            if (!handled)
            {
                await _openProjectFallback(row.Source);
            }

            await _stateStore.MarkReadAsync(row.Id, DateTimeOffset.UtcNow, CancellationToken.None);
            ProjectOpened?.Invoke(this, row.Source);
            SetStatus(string.Empty);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"{row.Name} could not be opened: {ex.Message}", isError: true);
        }
    }

    private async Task ArchiveProjectAsync(ProjectRow row)
    {
        try
        {
            SetStatus($"Archiving {row.Name}…");

            var handled = await NativePresentationReflection.ExecuteCommandAsync(
                row.Source,
                null,
                "ArchiveCommand",
                "ArchiveProjectCommand");

            if (!handled)
            {
                handled = await NativePresentationReflection.ExecuteCommandAsync(
                    _source,
                    row.Source,
                    "ArchiveProjectCommand",
                    "ArchiveCommand");
            }

            if (!handled)
            {
                await _archiveProjectFallback(row.Source);
            }

            await RefreshProjectsAsync(refreshSource: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"{row.Name} could not be archived: {ex.Message}", isError: true);
        }
    }

    private async Task SetPinnedAsync(ProjectRow row, bool isPinned)
    {
        try
        {
            await _stateStore.SetPinnedAsync(row.Id, isPinned, _lifetime.Token);
            await RefreshProjectsAsync(refreshSource: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"The pinned state could not be saved: {ex.Message}", isError: true);
        }
    }

    private async Task SetReadStateAsync(ProjectRow row, bool markUnread)
    {
        try
        {
            if (markUnread)
            {
                await _stateStore.MarkUnreadAsync(row.Id, _lifetime.Token);
            }
            else
            {
                await _stateStore.MarkReadAsync(row.Id, DateTimeOffset.UtcNow, _lifetime.Token);
            }

            await RefreshProjectsAsync(refreshSource: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"The read state could not be saved: {ex.Message}", isError: true);
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        _status.Text = message;
        _status.IsVisible = !string.IsNullOrWhiteSpace(message);
        _status.Foreground = isError ? Brush("#B42318") : MutedBrush;
    }

    private static Guid CreateStableIdentifier(object source, string name, string rootPath)
    {
        var input = $"{source.GetType().FullName}|{rootPath}|{name}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string FormatActivity(DateTimeOffset updatedAt)
    {
        if (updatedAt == DateTimeOffset.MinValue)
        {
            return "Activity unknown";
        }

        var elapsed = DateTimeOffset.UtcNow - updatedAt;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => "Just now",
            { TotalHours: < 1 } => $"{Math.Max(1, (int)elapsed.TotalMinutes)}m ago",
            { TotalDays: < 1 } => $"{Math.Max(1, (int)elapsed.TotalHours)}h ago",
            { TotalDays: < 7 } => $"{Math.Max(1, (int)elapsed.TotalDays)}d ago",
            _ => updatedAt.LocalDateTime.ToString("d")
        };
    }

    private sealed record ProjectRow(
        object Source,
        Guid Id,
        string Name,
        string RootPath,
        string Summary,
        string Branch,
        string State,
        string RecommendedAction,
        DateTimeOffset UpdatedAt,
        bool IsPinned,
        bool IsUnread);
}
