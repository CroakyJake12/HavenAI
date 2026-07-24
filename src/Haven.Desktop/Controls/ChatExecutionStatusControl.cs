using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Haven.Core;

namespace Haven.Desktop.Controls;

public sealed class ChatExecutionStatusControl : UserControl, IDisposable
{
    public static readonly StyledProperty<ChatExecutionSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<ChatExecutionStatusControl, ChatExecutionSnapshot?>(nameof(Snapshot));

    private readonly TextBlock _status = new()
    {
        FontSize = 13,
        FontWeight = Avalonia.Media.FontWeight.SemiBold,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };

    private readonly TextBlock _details = new()
    {
        FontSize = 11,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Opacity = 0.72
    };

    private readonly DispatcherTimer _pulse = new()
    {
        Interval = TimeSpan.FromMilliseconds(650)
    };

    private bool _dimmed;

    public ChatExecutionStatusControl()
    {
        IsVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Bottom;

        Content = new StackPanel
        {
            Children =
            {
                new Expander
                {
                    Header = _status,
                    Content = new ScrollViewer
                    {
                        MaxHeight = 300,
                        MaxWidth = 720,
                        Content = _details
                    }
                }
            }
        };

        _pulse.Tick += (_, _) =>
        {
            _dimmed = !_dimmed;
            _status.Opacity = _dimmed ? 0.46 : 0.88;
        };

        this.GetObservable(SnapshotProperty).Subscribe(ApplySnapshot);
    }

    public ChatExecutionSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    private void ApplySnapshot(ChatExecutionSnapshot? snapshot)
    {
        var terminal = snapshot?.Stage is ChatExecutionStage.Completed
            or ChatExecutionStage.Failed
            or ChatExecutionStage.Cancelled;

        IsVisible = snapshot is { IsVisible: true } && !terminal;
        if (!IsVisible)
        {
            _pulse.Stop();
            _details.Text = string.Empty;
            return;
        }

        _status.Text = snapshot!.DisplayText;
        _details.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            snapshot.Log.TakeLast(40).Select(entry =>
                $"{entry.Timestamp.ToLocalTime():HH:mm}  {entry.Summary}" +
                (string.IsNullOrWhiteSpace(entry.Detail)
                    ? string.Empty
                    : Environment.NewLine + "    " + entry.Detail)));

        if (!_pulse.IsEnabled)
        {
            _status.Opacity = 0.72;
            _pulse.Start();
        }
    }

    public void Dispose()
    {
        _pulse.Stop();
    }
}
