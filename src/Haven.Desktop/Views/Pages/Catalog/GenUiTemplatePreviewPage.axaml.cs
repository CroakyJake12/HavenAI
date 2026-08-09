using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.Catalog;

/// <summary>Code-behind development harness required by Clause 85.47.4.</summary>
public sealed partial class GenUiTemplatePreviewPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly IGenUiTemplateRepository _templates;
    private readonly GenerativeUiEventRouter _router;
    private readonly GenUiInstanceStore _instances;
    private readonly CalculatorTemplateRuntime _calculator;
    private GenerativeUiSurface? _surface;
    private IReadOnlyList<GenUiTemplateDefinition> _results = [];
    private Guid? _previewInstanceId;
    private string? _selectedKey;
    private bool _disposed;

    public GenUiTemplatePreviewPage(
        HavenEventBus bus,
        IGenUiTemplateRepository templates,
        GenerativeUiEventRouter router,
        GenUiInstanceStore instances,
        CalculatorTemplateRuntime calculator)
    {
        _bus = bus;
        _templates = templates;
        _router = router;
        _instances = instances;
        _calculator = calculator;
        InitializeComponent();
        ViewportFrame.Width = 900;
        WireEvents();
        _ = RefreshAsync();
    }

    private void WireEvents()
    {
        Register("TemplateLab.Search", SearchBox);
        Register("TemplateLab.Viewport.Desktop", DesktopWidthButton);
        Register("TemplateLab.Viewport.Mobile", MobileWidthButton);
        Register("TemplateLab.Refresh", RefreshButton);
        SearchBox.TextChanged += async (_, _) => await RefreshAsync();
        DesktopWidthButton.Click += (_, _) => SetViewportWidth(900);
        MobileWidthButton.Click += (_, _) => SetViewportWidth(390);
        RefreshButton.Click += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_disposed) return;
        try
        {
            _results = await _templates.SearchAsync(
                SearchBox.Text ?? string.Empty,
                OperatingSystem.IsAndroid() ? CapabilityPlatform.Android : CapabilityPlatform.Windows,
                null,
                100,
                CancellationToken.None);
            TemplateList.Children.Clear();
            foreach (var template in _results)
            {
                var captured = template;
                var button = new HavenButton
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(11, 9),
                    Content = new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = captured.Name, FontWeight = Avalonia.Media.FontWeight.ExtraBold },
                            new TextBlock
                            {
                                Text = $"{captured.Category} · {captured.Maturity} · {captured.Version}",
                                Classes = { "muted" },
                                FontSize = 10
                            }
                        }
                    }
                };
                button.Classes.Add("sidebar");
                button.Click += (_, _) => SelectTemplate(captured);
                TemplateList.Children.Add(button);
            }

            if (_results.Count > 0
                && (PreviewHost.Content is null || !_results.Any(item => item.Key == _selectedKey)))
                SelectTemplate(_results[0]);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Trace("Registry error: " + exception.Message);
        }
    }

    private void SelectTemplate(GenUiTemplateDefinition template)
    {
        _surface?.Dispose();
        if (_previewInstanceId is Guid oldInstance) _instances.Remove(oldInstance);
        _surface = null;
        _previewInstanceId = null;
        _selectedKey = template.Key;
        PreviewHost.Content = null;
        TracePanel.Children.Clear();
        SelectedTitle.Text = template.Name;
        SelectedMeta.Text = $"{template.Description}\n{template.CanonicalImplementation} · {template.Platforms} · " +
                            $"Agent {template.AgentInteraction} · State {template.StateOwnership} · " +
                            $"{(template.SupportsOffline ? "offline" : "network required")}";

        if (template.Key != "calculator")
        {
            PreviewHost.Content = new HavenAdaptiveSurface
            {
                Width = Math.Max(330, ViewportFrame.Width - 24),
                MinHeight = 360,
                Classes = { "card" },
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = template.Name, FontSize = 24, FontWeight = Avalonia.Media.FontWeight.ExtraBold },
                        new TextBlock
                        {
                            Text = "Registry foundation only. It remains outside production coverage until its full feature-completeness contract and tests pass.",
                            Classes = { "muted" },
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            MaxWidth = 560,
                            TextAlignment = Avalonia.Media.TextAlignment.Center
                        }
                    }
                }
            };
            Trace("No false preview: this foundation is not marked production-complete.");
            return;
        }

        _surface = new GenerativeUiSurface(_router, _instances)
        {
            Width = Math.Max(330, ViewportFrame.Width - 24),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _surface.SemanticEventEmitted += (_, semanticEvent) =>
            Trace($"EVENT {semanticEvent.EventType} · {semanticEvent.ComponentId} · {semanticEvent.ActionId}");
        _surface.ActionCompleted += (_, result) =>
            Trace($"RESULT {result.Status} · {result.Summary} · {result.Patches.Count} patch(es)");
        PreviewHost.Content = _surface;
        var document = _calculator.Create(Guid.NewGuid());
        _previewInstanceId = document.Origin.InstanceId;
        _surface.Present(document);
        Trace("Calculator preview uses the real structured event/router/patch loop.");
    }

    private void SetViewportWidth(double width)
    {
        ViewportFrame.Width = width;
        if (PreviewHost.Content is Control preview) preview.Width = Math.Max(330, width - 24);
    }

    private void Trace(string message)
    {
        TracePanel.Children.Insert(0, new TextBlock
        {
            Text = $"{DateTimeOffset.Now:HH:mm:ss}  {message}",
            FontSize = 10,
            FontWeight = Avalonia.Media.FontWeight.Medium,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        while (TracePanel.Children.Count > 100) TracePanel.Children.RemoveAt(TracePanel.Children.Count - 1);
    }

    private void Register(string name, Control control)
    {
        _bus.RegisterElement(name, control);
        _bus.WirePointerEvents(name, control);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _surface?.Dispose();
        if (_previewInstanceId is Guid instanceId) _instances.Remove(instanceId);
        _previewInstanceId = null;
        _surface = null;
    }
}
