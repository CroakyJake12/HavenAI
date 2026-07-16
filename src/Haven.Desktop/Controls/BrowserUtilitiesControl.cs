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
/// cancellable find, bounded page zoom, visible connection/navigation policy, native
/// print/developer tools, and the existing approval/audit surface without creating a
/// second browser host.
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
    private CancellationTokenSource? _operation;
    private int _policyVersion;
    private bool _disposed;

    public BrowserUtilitiesControl()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 4;
        VerticalAlignment = VerticalAlignment.Center;

        var findButton = UtilityButton("⌕", "Find on page", BuildFindPanel());

        _zoomButton = UtilityButton("100%", "Page zoom", BuildZoomPanel());
        _zoomButton.MinWidth = 56;
        _zoomButton.Classes.Remove("icon");
        _zoomButton.Classes.Add("ghost");

        _policySummary = new TextBlock { Text = "No page", FontWeight = FontWeight.SemiBold };
        _policyDetail = Muted("Navigate to inspect connection and model-automation policy.", 10);
        _navigationStatus = Muted("No navigation has been attempted in this tab.", 10);
        _policyButton = UtilityButton("○", "Site security and automation policy", BuildPolicyPanel());
        _policyButton.Click += async (_, _) => await RefreshPolicyAsync();

        var tools = UtilityButton("⋯", "Print and developer tools", BuildToolsPanel());
        var safety = UtilityButton("⚑", "Browser approvals, downloads and audit", new BrowserSafetyView());

        Children.Add(findButton);
        Children.Add(_zoomButton);
        Children.Add(_policyButton);
        Children.Add(tools);
        Children.Add(safety);

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
        CancelOperations(recreateLifetime: false);
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

            using var operation = BeginOperation();
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
            var vm = _viewModel;
            if (vm is null)
            {
                status.Text = "Find cleared.";
                return;
            }

            using var operation = BeginOperation();
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
        Width = 400,
        Spacing = 7,
        Children =
        {
            new TextBlock { Text = "SITE AND AUTOMATION POLICY", FontWeight = FontWeight.SemiBold, FontSize = 11 },
            _policySummary,
            _policyDetail,
            new Separator(),
            new TextBlock { Text = "LATEST BROWSER STATUS", FontWeight = FontWeight.SemiBold, FontSize = 10 },
            _navigationStatus,
            new Separator(),
            Muted("User navigation and model-driven browsing are separate. Model navigation permits only HTTP/HTTPS public-network destinations, blocks embedded credentials and local/internal addresses, and rechecks DNS resolution before use.", 9, secondary: true)
        }
    };

    private Control BuildToolsPanel()
    {
        var status = Muted("Actions apply to the mounted browser document.", 10);
        var print = new Button { Content = "Print current page" };
        var developerTools = new Button { Content = "Open developer tools" };

        print.Click += async (_, _) => await RunBrowserActionAsync(
            token => _viewModel!.Browser.PrintAsync(token), "Print dialog opened.", "Print", status);
        developerTools.Click += async (_, _) => await RunBrowserActionAsync(
            token => _viewModel!.Browser.OpenDeveloperToolsAsync(token), "Developer tools opened.", "Developer tools", status);

        return new StackPanel
        {
            Width = 300,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "PAGE TOOLS", FontWeight = FontWeight.SemiBold, FontSize = 11 },
                print,
                developerTools,
                status,
                Muted("Developer tools can inspect page content and storage. Use them only for sites you trust.", 9, secondary: true)
            }
        };
    }

    private async Task RunBrowserActionAsync(Func<CancellationToken, Task> action, string success, string name, TextBlock status)
    {
        if (_viewModel is null)
        {
            status.Text = "No active browser document.";
            return;
        }

        using var operation = BeginOperation();
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

        using var operation = BeginOperation();
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
        _navigationStatus.Text = string.IsNullOrWhiteSpace(vm?.Status)
            ? "No navigation status is available."
            : vm.Status;

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

        using var operation = BeginOperation(cancelPrevious: false);
        try
        {
            var assessment = await policy.AssessAsync(address, operation.Token);
            SetPolicy(version, encrypted ? "●" : "!", summary, assessment.IsAllowed
                ? "Model-driven navigation allowed after public-network DNS validation. " + assessment.Reason
                : "Model-driven navigation blocked. " + assessment.Reason);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            // A detach, disposal or newer lifecycle invalidated this result.
        }
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

    private CancellationTokenSource BeginOperation(bool cancelPrevious = true)
    {
        if (_disposed) return new CancellationTokenSource();
        if (cancelPrevious)
        {
            var previous = Interlocked.Exchange(ref _operation, null);
            previous?.Cancel();
            previous?.Dispose();
        }
        var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        if (cancelPrevious) _operation = operation;
        return operation;
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_disposed || !_lifetime.IsCancellationRequested) return;
        _lifetime.Dispose();
        _lifetime = new CancellationTokenSource();
        _ = RefreshPolicyAsync();
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => CancelOperations(recreateLifetime: true);

    private void CancelOperations(bool recreateLifetime)
    {
        Interlocked.Increment(ref _policyVersion);
        var operation = Interlocked.Exchange(ref _operation, null);
        operation?.Cancel();
        operation?.Dispose();
        _lifetime.Cancel();
        if (!recreateLifetime) _lifetime.Dispose();
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
        if (e.PropertyName is nameof(BrowserPageViewModel.Address)
            or nameof(BrowserPageViewModel.SelectedTab)
            or nameof(BrowserPageViewModel.Status))
        {
            if (e.PropertyName is nameof(BrowserPageViewModel.Address) or nameof(BrowserPageViewModel.SelectedTab))
            {
                _zoomButton.Content = "100%";
                _zoomSlider.Value = 100;
            }
            _ = RefreshPolicyAsync();
        }
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
