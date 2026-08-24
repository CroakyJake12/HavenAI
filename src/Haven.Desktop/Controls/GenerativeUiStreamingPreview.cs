using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.Services;

namespace Haven.Desktop.Controls;

/// <summary>
/// Immediate, progressively assembled Haven skeleton shown while a model is
/// still streaming a haven-ui directive. It deliberately mirrors the expected
/// template shape instead of displaying a generic spinner over a blank area.
/// </summary>
internal sealed partial class GenerativeUiStreamingPreview : UserControl, IDisposable
{
    private readonly TextBlock _title = new()
    {
        Text = "Building interactive content",
        FontSize = 15,
        FontWeight = FontWeight.ExtraBold
    };
    private readonly TextBlock _detail = new()
    {
        Text = "Reading the generated structure…",
        Classes = { "muted" },
        FontSize = 11
    };
    private readonly StackPanel _pieces = new() { Spacing = 10 };
    private readonly DispatcherTimer _pulse;
    private string _templateKey = string.Empty;
    private int _stage;
    private bool _bright;
    private bool _disposed;

    public GenerativeUiStreamingPreview()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        IsHitTestVisible = false;
        Content = new HavenLoadingState
        {
            Content = new HavenCard
            {
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                            Children =
                            {
                                new StackPanel { Spacing = 2, Children = { _title, _detail } },
                                Column(new HavenProgressBar
                                {
                                    IsIndeterminate = true,
                                    Width = 118,
                                    Height = 5,
                                    VerticalAlignment = VerticalAlignment.Center
                                }, 1)
                            }
                        },
                        _pieces
                    }
                }
            }
        };

        _pulse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(440) };
        _pulse.Tick += OnPulse;
        if (!MotionPreferencesService.Current.ReduceAnimations) _pulse.Start();
        Update(string.Empty);
    }

    public string TemplateKey => _templateKey;
    public int Stage => _stage;

    public static bool LooksLikeDirective(string content) =>
        !string.IsNullOrWhiteSpace(content)
        && (content.Contains("```haven-ui", StringComparison.OrdinalIgnoreCase)
            || TemplateRegex().IsMatch(content));

    public void Update(string content)
    {
        if (_disposed) return;
        content ??= string.Empty;
        var match = TemplateRegex().Match(content);
        var template = match.Success ? match.Groups[1].Value : string.Empty;
        if (template.Equals("custom", StringComparison.OrdinalIgnoreCase)
            && content.Contains("HavenCanvas", StringComparison.OrdinalIgnoreCase))
            template = "whiteboard";
        var stage = EstimateStage(content, template);
        if (template == _templateKey && stage == _stage) return;
        _templateKey = template;
        _stage = stage;
        RebuildPieces();
    }

    private void RebuildPieces()
    {
        _pieces.Children.Clear();
        _title.Text = "Building " + DisplayName(_templateKey);
        _detail.Text = _stage switch
        {
            <= 1 => "Reading the generated structure…",
            2 => "The layout is taking shape…",
            3 => "Adding content and interactions…",
            _ => "Wiring the final controls…"
        };

        switch (_templateKey)
        {
            case "card-deck":
                var cards = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions(_stage >= 3 ? "*,*" : "*"),
                    ColumnSpacing = 14
                };
                cards.Children.Add(SkeletonCard(210));
                if (_stage >= 3) cards.Children.Add(Column(SkeletonCard(210), 1));
                _pieces.Children.Add(cards);
                if (_stage >= 4) _pieces.Children.Add(SkeletonBar(42, 0.58));
                break;
            case "whiteboard":
                _pieces.Children.Add(SkeletonCard(280));
                if (_stage >= 3) _pieces.Children.Add(SkeletonBar(46, 0.76));
                if (_stage >= 4) _pieces.Children.Add(SkeletonBar(34, 0.48));
                break;
            case "dashboard":
            case "data-grid":
                _pieces.Children.Add(SkeletonBar(54, 0.42));
                if (_stage >= 2) _pieces.Children.Add(SkeletonCard(120));
                if (_stage >= 3) _pieces.Children.Add(SkeletonCard(120));
                if (_stage >= 4) _pieces.Children.Add(SkeletonBar(40, 0.64));
                break;
            case "structured-form":
            case "assessment":
                _pieces.Children.Add(SkeletonBar(34, 0.38));
                if (_stage >= 2) _pieces.Children.Add(SkeletonBar(52, 0.88));
                if (_stage >= 3) _pieces.Children.Add(SkeletonBar(52, 0.88));
                if (_stage >= 4) _pieces.Children.Add(SkeletonBar(42, 0.34));
                break;
            default:
                _pieces.Children.Add(SkeletonBar(38, 0.52));
                if (_stage >= 2) _pieces.Children.Add(SkeletonCard(128));
                if (_stage >= 3) _pieces.Children.Add(SkeletonBar(48, 0.82));
                if (_stage >= 4) _pieces.Children.Add(SkeletonCard(92));
                break;
        }
    }

    private static HavenCard SkeletonCard(double height) => new()
    {
        Height = height,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(18),
        Background = ResourceBrush("HavenPanel2Brush", Color.Parse("#EEEFEA")),
        Opacity = 0.72
    };

    private static HavenCard SkeletonBar(double height, double widthFraction) => new()
    {
        Height = height,
        MaxWidth = 760 * widthFraction,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(Math.Min(16, height / 2)),
        Background = ResourceBrush("HavenPanel2Brush", Color.Parse("#EEEFEA")),
        Opacity = 0.72
    };

    private void OnPulse(object? sender, EventArgs e)
    {
        _bright = !_bright;
        foreach (var piece in _pieces.Children) piece.Opacity = _bright ? 0.92 : 0.58;
    }

    private static int EstimateStage(string content, string template)
    {
        if (string.IsNullOrEmpty(template)) return 1;
        var structuralTokens = content.Count(character => character is '{' or '[' or ',');
        if (structuralTokens >= 18 || content.Length >= 900) return 4;
        if (structuralTokens >= 9 || content.Length >= 520) return 3;
        return 2;
    }

    private static string DisplayName(string template) => template switch
    {
        "card-deck" => "Flashcards",
        "whiteboard" => "Whiteboard",
        "data-grid" => "Data Grid",
        "structured-form" => "Form",
        "choice-prompt" => "Choices",
        "task-list" => "Task List",
        "assessment" => "Assessment",
        "workflow" => "Workflow",
        "dashboard" => "Dashboard",
        "graph" => "Graph",
        "calculator" => "Calculator",
        "custom" => "Custom Interface",
        _ => "Interactive Content"
    };

    private static T Column<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pulse.Stop();
        _pulse.Tick -= OnPulse;
        Content = null;
    }

    [GeneratedRegex("\\\"template\\\"\\s*:\\s*\\\"([a-z0-9-]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TemplateRegex();
}
