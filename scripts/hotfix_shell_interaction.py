from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

main_path = ROOT / "src/Haven.Desktop/Views/Shell/MainView.axaml"
main = main_path.read_text(encoding="utf-8")

old_context = """                    <Grid x:Name="ShellContextBar"
                          ColumnDefinitions="Auto,*,Auto"
                          Margin="18,16,18,0"
                          IsVisible="False"
                          VerticalAlignment="Top">"""
new_context = """                    <Grid x:Name="ShellContextBar"
                          ColumnDefinitions="Auto,*,Auto"
                          Width="0"
                          Height="0"
                          Margin="0"
                          Opacity="0"
                          IsHitTestVisible="False"
                          IsVisible="False"
                          VerticalAlignment="Top">"""
if old_context in main:
    main = main.replace(old_context, new_context, 1)
elif new_context not in main:
    raise RuntimeError("ShellContextBar anchor was not found.")

old_overlay = """                <Border x:Name="OverlayHost"
                        Background="{DynamicResource HavenBackgroundBrush}"
                        CornerRadius="24"
                        ClipToBounds="True"
                        IsVisible="False"/>"""
new_overlay = """                <Grid x:Name="OverlayHost"
                      IsVisible="False"/>"""
if old_overlay in main:
    main = main.replace(old_overlay, new_overlay, 1)
elif new_overlay not in main:
    raise RuntimeError("OverlayHost anchor was not found.")

main_path.write_text(main, encoding="utf-8", newline="\n")

beta_path = ROOT / "src/Haven.Desktop/Views/Shell/MainView.BetaOverlays.cs"
beta_path.write_text(
"""using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell.Overlays;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private bool _betaOverlayLifecycleWired;
    private bool _betaOverlaysAttached;
    private ChatExecutionStatusControl? _globalExecutionStatus;
    private InChatCallWidgetViewModel? _globalCallViewModel;

    private void AttachBetaOverlays()
    {
        if (!_betaOverlayLifecycleWired)
        {
            _betaOverlayLifecycleWired = true;
            AttachedToVisualTree += OnBetaOverlayAttached;
            DetachedFromVisualTree += OnBetaOverlayDetached;
        }

        if (_betaOverlaysAttached)
        {
            return;
        }

        ChatExecutionStatusControl? executionStatus = null;
        InChatCallWidgetViewModel? callViewModel = null;

        try
        {
            executionStatus = new ChatExecutionStatusControl
            {
                Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                MaxWidth = 720
            };

            callViewModel = new InChatCallWidgetViewModel(
                _callCoordinator,
                _conversations);
            callViewModel.Open();

            var callWidget = new GlobalCallWidget(callViewModel)
            {
                Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            OverlayHost.Children.Clear();
            OverlayHost.Children.Add(executionStatus);
            OverlayHost.Children.Add(callWidget);

            _globalExecutionStatus = executionStatus;
            _globalCallViewModel = callViewModel;
            _sessions.ExecutionChanged += OnExecutionChanged;
            executionStatus.Snapshot = _sessions.CurrentExecution;

            _betaOverlaysAttached = true;
            OverlayHost.IsVisible = true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to attach Haven beta overlays: {exception}");

            executionStatus?.Dispose();
            callViewModel?.Dispose();
            OverlayHost.Children.Clear();
            OverlayHost.IsVisible = false;
            _globalExecutionStatus = null;
            _globalCallViewModel = null;
            _betaOverlaysAttached = false;
        }
    }

    private void DetachBetaOverlays()
    {
        if (!_betaOverlaysAttached)
        {
            OverlayHost.IsVisible = false;
            OverlayHost.Children.Clear();
            return;
        }

        _betaOverlaysAttached = false;
        _sessions.ExecutionChanged -= OnExecutionChanged;
        _globalExecutionStatus?.Dispose();
        _globalCallViewModel?.Dispose();
        _globalExecutionStatus = null;
        _globalCallViewModel = null;
        OverlayHost.Children.Clear();
        OverlayHost.IsVisible = false;
    }

    private void OnExecutionChanged(ChatExecutionSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_globalExecutionStatus is not null)
            {
                _globalExecutionStatus.Snapshot = snapshot;
            }
        });
    }

    private void OnBetaOverlayAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachBetaOverlays();
    }

    private void OnBetaOverlayDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachBetaOverlays();
    }
}
""",
    encoding="utf-8",
    newline="\n",
)

call_path = ROOT / "src/Haven.Desktop/Views/Pages/Call/CallPage.axaml"
call = call_path.read_text(encoding="utf-8")
call = call.replace("Haven Voice", "Haven Call")
call = call.replace("Start local call", "Start call")
call_path.write_text(call, encoding="utf-8", newline="\n")

print("Applied the shell interaction, overlay, widget, and page-chrome hotfix.")
