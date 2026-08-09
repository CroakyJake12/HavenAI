/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Controls/ProductionMarkdownView.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns MarkdownCodeAction, MarkdownCodeActionRequest, ProductionMarkdownView, TextObserver. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Haven.Desktop.Controls;

/// <summary>
/// Lists the supported markdown code action values used to make state explicit and type-safe.
/// </summary>
public enum MarkdownCodeAction
{
    Copy,
    AskToRun,
    AskToApply
}

/// <summary>
/// Represents markdown code action request and keeps its related state and behavior together.
/// </summary>
public sealed record MarkdownCodeActionRequest(MarkdownCodeAction Action, string Language, string Code);

/// <summary>
/// Represents production markdown view and keeps its related state and behavior together.
/// </summary>
public sealed class ProductionMarkdownView : UserControl
{
    /// <summary>
    /// Stores text property locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<ProductionMarkdownView, string>(nameof(Text), string.Empty);
    /// <summary>
    /// Stores inline pattern locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex InlinePattern = new(
        "(`[^`]+`|\\*\\*[^*]+\\*\\*|(?<!\\*)\\*[^*]+\\*(?!\\*)|\\[[^\\]]+\\]\\([^)]+\\)|\\$[^$\\n]+\\$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// Stores ordered list locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex OrderedList = new("^\\s*(\\d+)[.)]\\s+(.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// Stores task list locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex TaskList = new("^\\s*[-*+]\\s+\\[([ xX])\\]\\s+(.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// Stores bullet list locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex BulletList = new("^\\s*[-*+]\\s+(.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _root = new() { Spacing = 8 };

    public ProductionMarkdownView()
    {
        Content = _root;
        this.GetObservable(TextProperty).Subscribe(new TextObserver(this));
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Stores code action requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event Action<MarkdownCodeActionRequest>? CodeActionRequested;

    /// <summary>
    /// Performs the rebuild step owned by this component.
    /// </summary>
    private void Rebuild(string? value)
    {
        _root.Children.Clear();
        var text = (value ?? string.Empty).ReplaceLineEndings("\n");
        if (text.Length == 0) return;
        var lines = text.Split('\n');
        var paragraph = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                var language = line[3..].Trim();
                var code = new StringBuilder();
                index++;
                while (index < lines.Length && !lines[index].StartsWith("```", StringComparison.Ordinal))
                {
                    code.AppendLine(lines[index]);
                    index++;
                }
                _root.Children.Add(BuildCodeBlock(language, code.ToString().TrimEnd()));
                continue;
            }
            if (line.Trim() == "$$")
            {
                FlushParagraph(paragraph);
                var equation = new StringBuilder();
                index++;
                while (index < lines.Length && lines[index].Trim() != "$$")
                {
                    equation.AppendLine(lines[index]);
                    index++;
                }
                _root.Children.Add(BuildEquation(equation.ToString().Trim()));
                continue;
            }
            if (TryBuildTable(lines, ref index, out var table))
            {
                FlushParagraph(paragraph);
                _root.Children.Add(table);
                continue;
            }
            if (TryBuildHeading(line, out var heading))
            {
                FlushParagraph(paragraph);
                _root.Children.Add(heading);
                continue;
            }
            if (line.StartsWith('>'))
            {
                FlushParagraph(paragraph);
                _root.Children.Add(BuildQuote(line.TrimStart('>', ' ')));
                continue;
            }
            if (TaskList.Match(line) is { Success: true } task)
            {
                FlushParagraph(paragraph);
                _root.Children.Add(BuildTask(task.Groups[2].Value, task.Groups[1].Value != " "));
                continue;
            }
            if (OrderedList.Match(line) is { Success: true } ordered)
            {
                FlushParagraph(paragraph);
                _root.Children.Add(BuildListItem(ordered.Groups[1].Value + ".", ordered.Groups[2].Value));
                continue;
            }
            if (BulletList.Match(line) is { Success: true } bullet)
            {
                FlushParagraph(paragraph);
                _root.Children.Add(BuildListItem("•", bullet.Groups[1].Value));
                continue;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(paragraph);
                continue;
            }
            paragraph.Add(line);
        }
        FlushParagraph(paragraph);
    }

    /// <summary>
    /// Performs the flush paragraph step owned by this component.
    /// </summary>
    private void FlushParagraph(List<string> paragraph)
    {
        if (paragraph.Count == 0) return;
        _root.Children.Add(BuildInlineText(string.Join("\n", paragraph), 14, FontWeight.Normal));
        paragraph.Clear();
    }

    /// <summary>
    /// Attempts to build heading and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryBuildHeading(string line, out Control heading)
    {
        var count = line.TakeWhile(character => character == '#').Count();
        if (count is < 1 or > 6 || line.Length <= count || line[count] != ' ')
        {
            heading = null!;
            return false;
        }
        var size = count switch { 1 => 25, 2 => 21, 3 => 18, 4 => 16, _ => 14 };
        heading = BuildInlineText(line[(count + 1)..].Trim(), size, FontWeight.SemiBold);
        return true;
    }

    /// <summary>
    /// Builds quote from the currently available inputs.
    /// </summary>
    private static Control BuildQuote(string text) => new HavenAdaptiveSurface
    {
        BorderThickness = new Thickness(3, 0, 0, 0),
        BorderBrush = Brush("HavenAccentSecondaryBrush"),
        Background = Brush("HavenPanel2Brush"),
        CornerRadius = new CornerRadius(0, 8, 8, 0),
        Padding = new Thickness(12, 8),
        Child = BuildInlineText(text, 13, FontWeight.Normal, FontStyle.Italic)
    };

    /// <summary>
    /// Builds task from the currently available inputs.
    /// </summary>
    private static Control BuildTask(string text, bool isChecked) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        ColumnSpacing = 8,
        Children =
        {
            new HavenCheckBox { IsChecked = isChecked, IsEnabled = false, VerticalAlignment = VerticalAlignment.Top },
            WithColumn(BuildInlineText(text, 13, FontWeight.Normal), 1)
        }
    };

    /// <summary>
    /// Builds list item from the currently available inputs.
    /// </summary>
    private static Control BuildListItem(string marker, string text) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("28,*"),
        ColumnSpacing = 4,
        Children =
        {
            new TextBlock { Text = marker, Foreground = Brush("HavenAccentSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Right },
            WithColumn(BuildInlineText(text, 13, FontWeight.Normal), 1)
        }
    };

    /// <summary>
    /// Builds code block from the currently available inputs.
    /// </summary>
    private Border BuildCodeBlock(string language, string code)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 6 };
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language) ? "code" : language,
            FontSize = 10,
            Foreground = Brush("HavenMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(ActionButton("Copy", 1, MarkdownCodeAction.Copy, language, code));
        header.Children.Add(ActionButton("Ask to run", 2, MarkdownCodeAction.AskToRun, language, code));
        header.Children.Add(ActionButton("Ask to apply", 3, MarkdownCodeAction.AskToApply, language, code));
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(header);
        var codeBox = new HavenTextInput
        {
            Text = code,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 12,
            MinHeight = 42,
            MaxHeight = 500
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(
            codeBox,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(
            codeBox,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        stack.Children.Add(codeBox);
        return new HavenAdaptiveSurface
        {
            Background = Brush("HavenPanel3Brush"),
            BorderBrush = Brush("HavenLineStrongBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Child = stack
        };
    }

    /// <summary>
    /// Performs the action button step owned by this component.
    /// </summary>
    private Button ActionButton(string label, int column, MarkdownCodeAction action, string language, string code)
    {
        var button = new HavenButton { Content = label, FontSize = 10, Padding = new Thickness(8, 4) };
        Grid.SetColumn(button, column);
        button.Click += async (_, _) =>
        {
            if (action == MarkdownCodeAction.Copy)
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is not null) await clipboard.SetTextAsync(code);
            }
            CodeActionRequested?.Invoke(new MarkdownCodeActionRequest(action, language, code));
        };
        return button;
    }

    /// <summary>
    /// Builds equation from the currently available inputs.
    /// </summary>
    private static Control BuildEquation(string latex)
    {
        var display = FormatLatex(latex);
        return new HavenAdaptiveSurface
        {
            Background = Brush("HavenPanel2Brush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9),
            Child = new SelectableTextBlock
            {
                Text = display,
                FontFamily = new FontFamily("Cambria Math, STIX Two Math, serif"),
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    /// <summary>
    /// Attempts to build table and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryBuildTable(string[] lines, ref int index, out Control table)
    {
        table = null!;
        if (index + 1 >= lines.Length || !lines[index].Contains('|', StringComparison.Ordinal)) return false;
        var separatorCells = SplitTableRow(lines[index + 1]);
        if (separatorCells.Count == 0 || separatorCells.Any(cell => !Regex.IsMatch(cell.Trim(), "^:?-{3,}:?$", RegexOptions.CultureInvariant))) return false;
        var rows = new List<IReadOnlyList<string>> { SplitTableRow(lines[index]) };
        index += 2;
        while (index < lines.Length && lines[index].Contains('|', StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(lines[index]))
        {
            rows.Add(SplitTableRow(lines[index]));
            index++;
        }
        index--;
        var columns = Math.Max(1, rows.Max(row => row.Count));
        var grid = new Grid { ColumnSpacing = 1, RowSpacing = 1, Background = Brush("HavenLineStrongBrush") };
        for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        for (var row = 0; row < rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var column = 0; column < columns; column++)
            {
                var text = column < rows[row].Count ? rows[row][column].Trim() : string.Empty;
                var cell = new HavenAdaptiveSurface
                {
                    Background = Brush(row == 0 ? "HavenPanel3Brush" : "HavenPanel2Brush"),
                    Padding = new Thickness(8, 6),
                    Child = BuildInlineText(text, 12, row == 0 ? FontWeight.SemiBold : FontWeight.Normal)
                };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }
        table = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = grid
        };
        return true;
    }

    /// <summary>
    /// Performs the split table row step owned by this component.
    /// </summary>
    private static IReadOnlyList<string> SplitTableRow(string line) => line.Trim().Trim('|').Split('|').Select(value => value.Trim()).ToArray();

    /// <summary>
    /// Builds inline text from the currently available inputs.
    /// </summary>
    private static TextBlock BuildInlineText(string text, double size, FontWeight weight, FontStyle style = FontStyle.Normal)
    {
        var block = new TextBlock { FontSize = size, FontWeight = weight, FontStyle = style, TextWrapping = TextWrapping.Wrap };
        // Keep ordinary text in Text rather than manufacturing a single Run.
        // This gives accessibility APIs, copying, and headless rendering the
        // actual content while reserving Inlines for genuinely mixed styling.
        if (!InlinePattern.IsMatch(text))
        {
            block.Text = text;
            return block;
        }
        var position = 0;
        foreach (Match match in InlinePattern.Matches(text))
        {
            if (match.Index > position) block.Inlines!.Add(new Run(text[position..match.Index]));
            var token = match.Value;
            if (token.StartsWith("**", StringComparison.Ordinal) && token.EndsWith("**", StringComparison.Ordinal))
                block.Inlines!.Add(new Run(token[2..^2]) { FontWeight = FontWeight.SemiBold });
            else if (token.StartsWith('*') && token.EndsWith('*'))
                block.Inlines!.Add(new Run(token[1..^1]) { FontStyle = FontStyle.Italic });
            else if (token.StartsWith('`') && token.EndsWith('`'))
                block.Inlines!.Add(new Run(token[1..^1]) { FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), Background = Brush("HavenPanel3Brush") });
            else if (token.StartsWith('$') && token.EndsWith('$'))
                block.Inlines!.Add(new Run(FormatLatex(token[1..^1])) { FontFamily = new FontFamily("Cambria Math, STIX Two Math, serif") });
            else
            {
                var close = token.IndexOf("](", StringComparison.Ordinal);
                var label = token[1..close];
                var url = token[(close + 2)..^1];
                block.Inlines!.Add(new Run(label) { Foreground = Brush("HavenAccentSecondaryBrush"), TextDecorations = TextDecorations.Underline });
                block.Inlines!.Add(new Run(" (" + url + ")") { Foreground = Brush("HavenMutedBrush") });
            }
            position = match.Index + match.Length;
        }
        if (position < text.Length) block.Inlines!.Add(new Run(text[position..]));
        return block;
    }

    /// <summary>
    /// Performs the format latex step owned by this component.
    /// </summary>
    private static string FormatLatex(string latex)
    {
        var value = latex.Trim();
        value = Regex.Replace(value, "\\\\frac\\{([^{}]+)\\}\\{([^{}]+)\\}", "$1⁄$2", RegexOptions.CultureInvariant);
        value = Regex.Replace(value, "\\\\sqrt\\{([^{}]+)\\}", "√($1)", RegexOptions.CultureInvariant);
        value = Regex.Replace(value, "\\^\\{([^{}]+)\\}", "^($1)", RegexOptions.CultureInvariant);
        value = Regex.Replace(value, "_\\{([^{}]+)\\}", "_($1)", RegexOptions.CultureInvariant);
        return value
            .Replace("\\times", "×", StringComparison.Ordinal)
            .Replace("\\cdot", "·", StringComparison.Ordinal)
            .Replace("\\leq", "≤", StringComparison.Ordinal)
            .Replace("\\geq", "≥", StringComparison.Ordinal)
            .Replace("\\neq", "≠", StringComparison.Ordinal)
            .Replace("\\rightarrow", "→", StringComparison.Ordinal)
            .Replace("\\leftarrow", "←", StringComparison.Ordinal)
            .Replace("\\infty", "∞", StringComparison.Ordinal)
            .Replace("\\sum", "∑", StringComparison.Ordinal)
            .Replace("\\int", "∫", StringComparison.Ordinal)
            .Replace("\\alpha", "α", StringComparison.Ordinal)
            .Replace("\\beta", "β", StringComparison.Ordinal)
            .Replace("\\gamma", "γ", StringComparison.Ordinal)
            .Replace("\\delta", "δ", StringComparison.Ordinal)
            .Replace("\\theta", "θ", StringComparison.Ordinal)
            .Replace("\\lambda", "λ", StringComparison.Ordinal)
            .Replace("\\pi", "π", StringComparison.Ordinal)
            .Replace("\\sigma", "σ", StringComparison.Ordinal)
            .Replace("\\omega", "ω", StringComparison.Ordinal);
    }

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    /// <summary>
    /// Performs the brush step owned by this component.
    /// </summary>
    private static IBrush? Brush(string key) => Avalonia.Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;

    /// <summary>
    /// Represents text observer and keeps its related state and behavior together.
    /// </summary>
    private sealed class TextObserver(ProductionMarkdownView owner) : IObserver<string>
    {
        /// <summary>
        /// Handles the completed event raised by the UI or runtime.
        /// </summary>
        public void OnCompleted() { }
        /// <summary>
        /// Handles the error event raised by the UI or runtime.
        /// </summary>
        public void OnError(Exception error) { }
        /// <summary>
        /// Handles the next event raised by the UI or runtime.
        /// </summary>
        public void OnNext(string value) => owner.Rebuild(value);
    }
}
