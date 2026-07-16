using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Controls;

/// <summary>
/// Browser-native utility cluster backed by the mounted WebView session. It exposes
/// find, bounded page zoom, visible connection/automation policy, and the existing
/// approval/audit surface without creating another browser host.
/// </summary>
public sealed class BrowserUtilitiesControl : StackPanel, IDisposable
{
    private readonly Button _zoomButton;
    private readonly Button _policyButton;
    private readonly TextBlock _policySummary;
    private readonly TextBlock _policyDetail;
    private readonly Slider _zoomSlider;
    private BrowserPageViewModel? _viewModel;
    private INotifyPropertyChanged? _notifications;
    private int _zoomPercent = 100;
    private bool _disposed;

    public BrowserUtilitiesControl()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 4;
        VerticalAlignment = VerticalAlignment.Center;

        var findButton = new Button { Content = "⌕", ToolTip = "Find on page" };
        findButton.Classes.Add("icon");
        findButton.Flyout = new Flyout { Placement = PlacementMode.Bottom, Content = BuildFindPanel() };

        _zoomButton = new Button { Content = "100%", ToolTip = "Page zoom", MinWidth = 56 };
        _zoomButton.Classes.Add("ghost");
        _zoomButton.Flyout = new Flyout { Placement = PlacementMode.Bottom, Content = BuildZoomPanel() };

        _policySummary = new TextBlock { Text = "No page", FontWeight = FontWeight.SemiBold };
        _policyDetail = new TextBlock
        {
            Text = "Navigate to inspect connection and model-automation policy.",
            Classes = { "muted" },
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };
        _policyButton = new Button { Content = "○", ToolTip = "Site security and automation policy" };
        _policyButton.Classes.Add("icon");
        _policyButton.Flyout = new Flyout { Placement = PlacementMode.Bottom, Content = BuildPolicyPanel() };
        _policyButton.Click += async (_, _) => await RefreshPolicyAsync();

        var safety = new Button { Content = "⚑", ToolTip = "Browser approvals, downloads and audit" };
        safety.Classes.Add("icon");
        safety.Flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new BrowserSafetyView()
        };

        Children.Add(findButton);
        Children.Add(_zoomButton);
        Children.Add(_policyButton);
        Children.Add(safety);
        DataContextChanged += OnDataContextChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DataContextChanged -= OnDataContextChanged;
        if (_notifications is not null) _notifications.PropertyChanged -= OnViewModelPropertyChanged;
        _notifications = null;
        _viewModel = null;
        foreach (var button in Children.OfType<Button>()) button.Flyout?.Hide();
        GC.SuppressFinalize(this);
    }

    private Control BuildFindPanel()
    {
        var query = new TextBox { PlaceholderText = "Find on this page", MinWidth = 260 };
        var status = new TextBlock { Classes = { "muted" }, FontSize = 10, TextWrapping = TextWrapping.Wrap };
        var previous = new Button { Content = "Previous" };
        var next = new Button { Content = "Next" };
        var clear = new Button { Content = "Clear highlight" };

        async Task FindAsync(bool backwards)
        {
            var vm = _viewModel;
            var text = query.Text?.Trim();
            if (vm is null || string.IsNullOrWhiteSpace(text))
            {
                status.Text = "Enter text to find.";
                return;
            }
            try
            {
                var literal = JsonSerializer.Serialize(text);
                var result = await vm.Browser.ExecuteScriptAsync(
                    $"window.find({literal}, false, {(backwards ? "true" : "false")}, true, false, true, false)",
                    CancellationToken.None);
                status.Text = string.Equals(result?.Trim('"'), "true", StringComparison.OrdinalIgnoreCase)
                    ? "Match selected."
                    : "No further match was found.";
            }
            catch (Exception ex)
            {
                status.Text = "Find failed: " + ex.Message;
            }
        }

        previous.Click += async (_, _) => await FindAsync(backwards: true);
        next.Click += async (_, _) => await FindAsync(backwards: false);
        query.KeyDown += async (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.Enter) return;
            args.Handled = true;
            await FindAsync(backwards: false);
        };
        clear.Click += async (_, _) =>
        {
            query.Text = string.Empty;
            try
            {
                if (_viewModel is not null)
                    await _viewModel.Browser.ExecuteScriptAsync("window.getSelection()?.removeAllRanges()", CancellationToken.None);
                status.Text = "Find cleared.";
            }
            catch (Exception ex) { status.Text = "Could not clear find: " + ex.Message; }
        };

        return new StackPanel
        {
            Width = 340,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "FIND ON PAGE", FontWeight = FontWeight.SemiBold, FontSize = 11 },
                query,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { previous, next, clear }
                },
                status
            }
        };
    }

    private Control BuildZoomPanel()
    {
        _zoomSlider = new Slider
        {
            Minimum = 50,
            Maximum = 200,
            Value = 100,
            TickFrequency = 10,
            IsSnapToTickEnabled = true,
            SmallChange = 10,
            LargeChange = 25,
            MinWidth = 260
        };
        var status = new TextBlock
        {
            Text = "Zoom affects the current page only.",
            Classes = { "muted" },
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };
        _zoomSlider.ValueChanged += async (_, args) =>
        {
            var value = Math.Clamp((int)Math.Round(args.NewValue / 10d) * 10, 50, 200);
            await ApplyZoomAsync(value, status);
        };
        var reset = new Button { Content = "Reset to 100%" };
        reset.Click += async (_, _) =>
        {
            _zoomSlider.Value = 100;
            await ApplyZoomAsync(100, status);
        };
        return new StackPanel
        {
            Width = 310,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "PAGE ZOOM", FontWeight = FontWeight.SemiBold, FontSize = 11 },
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "50%", VerticalAlignment = VerticalAlignment.Center, FontSize = 9 },
                        WithColumn(_zoomSlider, 1),
                        WithColumn(new TextBlock { Text = "200%", VerticalAlignment = VerticalAlignment.Center, FontSize = 9 }, 2)
                    }
                },
                reset,
                status
            }
        };
    }

    private Control BuildPolicyPanel() => new StackPanel
    {
        Width = 380,
        Spacing = 7,
        Children =
        {
            new TextBlock { Text = "SITE AND AUTOMATION POLICY", FontWeight = FontWeight.SemiBold, FontSize = 11 },
            _policySummary,
            _policyDetail,
            new Separator(),
            new TextBlock
            {
                Text = "User navigation and model-driven browsing are separate. Model navigation permits only HTTP/HTTPS public-network destinations, blocks embedded credentials and local/internal addresses, and rechecks DNS resolution before use.",
                Classes = { "muted2" },
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap
            }
        }
    };

    private async Task ApplyZoomAsync(int value, TextBlock status)
    {
        _zoomPercent = value;
        _zoomButton.Content = value + "%";
        var vm = _viewModel;
        if (vm is null) return;
        try
        {
            await vm.Browser.ExecuteScriptAsync(
                $"document.documentElement.style.zoom='{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}%'",
                CancellationToken.None);
            status.Text = $"Current page zoom: {value}%";
        }
        catch (Exception ex)
        {
            status.Text = "Zoom failed: " + ex.Message;
        }
    }

    private async Task RefreshPolicyAsync()
    {
        var vm = _viewModel;
        var raw = vm?.Browser.State.Address?.AbsoluteUri ?? vm?.Address;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var address))
        {
            _policyButton.Content = "○";
            _policySummary.Text = "No active web origin";
            _policyDetail.Text = "Navigate to an HTTP or HTTPS page to inspect its policy.";
            return;
        }

        var encrypted = address.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        _policyButton.Content = encrypted ? "●" : "!";
        _policySummary.Text = encrypted
            ? $"Encrypted HTTPS · {address.Host}"
            : $"Unencrypted {address.Scheme.ToUpperInvariant()} · {address.Host}";

        var policy = App.Services?.GetService<IBrowserNavigationPolicy>();
        if (policy is null)
        {
            _policyDetail.Text = "Model-automation policy is not available in this process.";
            return;
        }
        try
        {
            var assessment = await policy.AssessAsync(address, CancellationToken.None);
            _policyDetail.Text = assessment.IsAllowed
                ? "Model-driven navigation allowed after public-network DNS validation. " + assessment.Reason
                : "Model-driven navigation blocked. " + assessment.Reason;
        }
        catch (Exception ex)
        {
            _policyDetail.Text = "Policy assessment failed closed: " + ex.Message;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_notifications is not null) _notifications.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as BrowserPageViewModel;
        _notifications = _viewModel;
        if (_notifications is not null) _notifications.PropertyChanged += OnViewModelPropertyChanged;
        _ = RefreshPolicyAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrowserPageViewModel.Address) or nameof(BrowserPageViewModel.SelectedTab))
            _ = RefreshPolicyAsync();
    }

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
