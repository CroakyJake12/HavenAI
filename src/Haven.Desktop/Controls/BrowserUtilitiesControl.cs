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
/// Utilities for the already-mounted browser session. No second WebView or competing
/// navigation implementation is created here.
/// </summary>
public sealed class BrowserUtilitiesControl : StackPanel, IDisposable
{
    private readonly Button _zoomButton;
    private readonly Button _policyButton;
    private readonly TextBlock _policySummary;
    private readonly TextBlock _policyDetail;
    private readonly TextBlock _navigationStatus;
    private Slider _zoomSlider = null!;
    private BrowserPageViewModel? _viewModel;
    private INotifyPropertyChanged? _notifications;
    private CancellationTokenSource _lifetime = new();
    private int _policyVersion;
    private bool _disposed;

    public BrowserUtilitiesControl()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 4;
        VerticalAlignment = VerticalAlignment.Center;

        var find = UtilityButton("⌕", "Find on page", BuildFindPanel());
        _zoomButton = UtilityButton("100%", "Page zoom", BuildZoomPanel());
        _zoomButton.MinWidth = 56;
        _zoomButton.Classes.Remove("icon");
        _zoomButton.Classes.Add("ghost");

        _policySummary = new TextBlock { Text = "No page", FontWeight = FontWeight.SemiBold };
        _policyDetail = Muted("Navigate to inspect connection and model-automation policy.", 10);
        _navigationStatus = Muted("No navigation has been attempted in this tab.", 10);
        _policyButton = UtilityButton("○", "Site security and automation policy", BuildPolicyPanel());
        _policyButton.Click += async (_, _) => await RefreshPolicyAsync();

        Children.Add(find);
        Children.Add(_zoomButton);
        Children.Add(_policyButton);
        Children.Add(UtilityButton("⋯", "Print and developer tools", BuildToolsPanel()));
        Children.Add(UtilityButton("⚑", "Browser approvals, downloads and audit", new BrowserSafetyView()));

        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DataContextChanged -= OnDataContextChanged;
        AttachedToVisualTree -= OnAttached;
        DetachedFromVisualTree -= OnDetached;
        DetachViewModel();
        CancelLifetime(dispose: true);
        foreach (var button in Children.OfType<Button>()) button.Flyout?.Hide();
        GC.SuppressFinalize(this);
    }

    private Button UtilityButton(string content, string tooltip, Control panel)
    {
        var button = new Button { Content = content };
        ToolTip.SetTip(button, tooltip);
        button.Classes.Add("icon");
        button.Flyout = new Flyout { Placement = PlacementMode.Bottom, Content = panel };
        return button;
    }

    private Control BuildFindPanel()
    {
        var query = new TextBox { PlaceholderText = "Find on this page", MinWidth = 260 };
        var status = Muted("Enter text to search the current document.", 10);
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

            using var operation = LinkedOperation();
            try
            {
                var literal = JsonSerializer.Serialize(text);
                var result = await vm.Browser.ExecuteScriptAsync(
                    $"window.find({literal}, false, {(backwards ? "true" : "false")}, true, false, true, false)",
                    operation.Token);
                status.Text = string.Equals(result?.Trim('"'), "true", StringComparison.OrdinalIgnoreCase)
                    ? "Match selected."
                    : "No further match was found.";
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            {
                status.Text = "Find cancelled.";
            }
            catch (Exception ex)
            {
                status.Text = "Find failed: " + ex.Message;
            }
        }

        previous.Click += async (_, _) => await FindAsync(true);
        next.Click += async (_, _) => await FindAsync(false);
        query.KeyDown += async (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.Enter) return;
            args.Handled = true;
            await FindAsync(false);
        };
        clear.Click += async (_, _) =>
        {
            query.Text = string.Empty;
            var vm = _viewModel;
            if (vm is null)
            {
                status.Text = "Find cleared.";
                return;
            }
            using var operation = LinkedOperation();
            try
            {
                await vm.Browser.ExecuteScriptAsync("window.getSelection()?.removeAllRanges()", operation.Token);
                status.Text = "Find cleared.";
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            {
                status.Text = "Clear cancelled.";
            }
            catch (Exception ex)
            {
                status.Text = "Could not clear find: " + ex.Message;
            }
        };

        return Panel("FIND ON PAGE", query,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { previous, next, clear } },
            status, 340);
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
        var status = Muted("Zoom affects the current page only and resets on navigation.", 10);
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
        var scale = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                new TextBlock { Text = "50%", VerticalAlignment = VerticalAlignment.Center, FontSize = 9 },
                WithColumn(_zoomSlider, 1),
                WithColumn(new TextBlock { Text = "200%", VerticalAlignment = VerticalAlignment.Center, FontSize = 9 }, 2)
            }
        };
        return Panel("PAGE ZOOM", scale, reset, status, 310);
    }

    private Control BuildPolicyPanel() => Panel("SITE AND AUTOMATION POLICY",
        _policySummary,
        _policyDetail,
        new Separator(),
        new TextBlock { Text = "LATEST BROWSER STATUS", FontWeight = FontWeight.SemiBold, FontSize = 10 },
        _navigationStatus,
        new Separator(),
        Muted("User navigation and model-driven browsing are separate. Model navigation permits only HTTP/HTTPS public-network destinations, blocks embedded credentials and local/internal addresses, and rechecks DNS resolution before use.", 9, true),
        400);

    private Control BuildToolsPanel()
    {
        var status = Muted("Actions apply to the mounted browser document.", 10);
        var print = new Button { Content = "Print current page" };
        var developerTools = new Button { Content = "Open developer tools" };
        print.Click += async (_, _) => await RunBrowserActionAsync(
            token => _viewModel!.Browser.PrintAsync(token), "Print dialog opened.", "Print", status);
        developerTools.Click += async (_, _) => await RunBrowserActionAsync(
            token => _viewModel!.Browser.OpenDeveloperToolsAsync(token), "Developer tools opened.", "Developer tools", status);
        return Panel("PAGE TOOLS", print, developerTools, status,
            Muted("Developer tools can inspect page content and storage. Use them only for sites you trust.", 9, true), 300);
    }

    private async Task RunBrowserActionAsync(Func<CancellationToken, Task> action, string success, string name, TextBlock status)
    {
        if (_viewModel is null)
        {
            status.Text = "No active browser document.";
            return;
        }
        using var operation = LinkedOperation();
        try
        {
            await action(operation.Token);
            status.Text = success;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            status.Text = name + " cancelled.";
        }
        catch (Exception ex)
        {
            status.Text = name + " failed: " + ex.Message;
        }
    }

    private async Task ApplyZoomAsync(int value, TextBlock status)
    {
        _zoomButton.Content = value + "%";
        var vm = _viewModel;
        if (vm is null) return;
        using var operation = LinkedOperation();
        try
        {
            await vm.Browser.ExecuteScriptAsync(
                $"document.documentElement.style.zoom='{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}%'",
                operation.Token);
            status.Text = $"Current page zoom: {value}%";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            status.Text = "Zoom cancelled.";
        }
        catch (Exception ex)
        {
            status.Text = "Zoom failed: " + ex.Message;
        }
    }

    private async Task RefreshPolicyAsync()
    {
        var version = Interlocked.Increment(ref _policyVersion);
        var vm = _viewModel;
        _navigationStatus.Text = string.IsNullOrWhiteSpace(vm?.Status) ? "No navigation status is available." : vm.Status;
        var raw = vm?.Browser.State.Address?.AbsoluteUri ?? vm?.Address;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var address))
        {
            SetPolicy(version, "○", "No active web origin", "Navigate to an HTTP or HTTPS page to inspect its policy.");
            return;
        }

        var encrypted = address.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        var summary = encrypted
            ? $"Encrypted HTTPS · {address.Host}"
            : $"Unencrypted {address.Scheme.ToUpperInvariant()} · {address.Host}";
        var policy = App.Services?.GetService<IBrowserNavigationPolicy>();
        if (policy is null)
        {
            SetPolicy(version, encrypted ? "●" : "!", summary, "Model-automation policy is not available in this process.");
            return;
        }

        using var operation = LinkedOperation();
        try
        {
            var assessment = await policy.AssessAsync(address, operation.Token);
            SetPolicy(version, encrypted ? "●" : "!", summary, assessment.IsAllowed
                ? "Model-driven navigation allowed after public-network DNS validation. " + assessment.Reason
                : "Model-driven navigation blocked. " + assessment.Reason);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            SetPolicy(version, encrypted ? "●" : "!", summary, "Policy assessment failed closed: " + ex.Message);
        }
    }

    private void SetPolicy(int version, string glyph, string summary, string detail)
    {
        if (_disposed || version != Volatile.Read(ref _policyVersion)) return;
        _policyButton.Content = glyph;
        _policySummary.Text = summary;
        _policyDetail.Text = detail;
    }

    private CancellationTokenSource LinkedOperation() =>
        CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_disposed || !_lifetime.IsCancellationRequested) return;
        _lifetime.Dispose();
        _lifetime = new CancellationTokenSource();
        _ = RefreshPolicyAsync();
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => CancelLifetime(dispose: false);

    private void CancelLifetime(bool dispose)
    {
        Interlocked.Increment(ref _policyVersion);
        _lifetime.Cancel();
        if (dispose) _lifetime.Dispose();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachViewModel();
        _viewModel = DataContext as BrowserPageViewModel;
        _notifications = _viewModel;
        if (_notifications is not null) _notifications.PropertyChanged += OnViewModelPropertyChanged;
        _ = RefreshPolicyAsync();
    }

    private void DetachViewModel()
    {
        if (_notifications is not null) _notifications.PropertyChanged -= OnViewModelPropertyChanged;
        _notifications = null;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(BrowserPageViewModel.Address)
            or nameof(BrowserPageViewModel.SelectedTab)
            or nameof(BrowserPageViewModel.Status))) return;

        if (e.PropertyName is nameof(BrowserPageViewModel.Address) or nameof(BrowserPageViewModel.SelectedTab))
        {
            _zoomButton.Content = "100%";
            _zoomSlider.Value = 100;
        }
        _ = RefreshPolicyAsync();
    }

    private static StackPanel Panel(string heading, params object[] items)
    {
        var width = items.LastOrDefault() is int value ? value : 320;
        var panel = new StackPanel { Width = width, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, FontSize = 11 });
        foreach (var item in items.Take(items.Length - 1).OfType<Control>()) panel.Children.Add(item);
        return panel;
    }

    private static TextBlock Muted(string text, double size, bool secondary = false) => new()
    {
        Text = text,
        Classes = { secondary ? "muted2" : "muted" },
        FontSize = size,
        TextWrapping = TextWrapping.Wrap
    };

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
