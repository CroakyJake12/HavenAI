/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/MarkdownView.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns MarkdownView, LatexFormatter. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Haven.Desktop.Controls;

/// <summary>
/// Renders the Markdown produced by local models without changing the stored message.
/// The renderer is deliberately local and dependency-free so chat rendering never
/// needs a browser, remote script, or a second copy of message content.
/// </summary>
public sealed partial class MarkdownView : UserControl
{
    /// <summary>
    /// Stores markdown property locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    /// <summary>
    /// Stores blocks locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _blocks = new() { Spacing = 8 };

    static MarkdownView()
    {
        MarkdownProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.Render());
    }

    public MarkdownView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = _blocks;
        Render();
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>
    /// Performs the render step owned by this component.
    /// </summary>
    private void Render()
    {
        _blocks.Children.Clear();
        var source = (Markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (source.Length == 0) return;

        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length;)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var language = line.Trim()[3..].Trim();
                var code = new StringBuilder();
                index++;
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    if (code.Length > 0) code.AppendLine();
                    code.Append(lines[index++]);
                }
                if (index < lines.Length) index++;
                _blocks.Children.Add(CreateCodeBlock(code.ToString(), language));
                continue;
            }

            if (line.TrimStart().StartsWith("$$", StringComparison.Ordinal))
            {
                var math = new StringBuilder();
                var trimmed = line.Trim();
                if (trimmed.Length > 4 && trimmed.EndsWith("$$", StringComparison.Ordinal))
                {
                    math.Append(trimmed[2..^2]);
                    index++;
                }
                else
                {
                    var first = trimmed[2..];
                    if (first.Length > 0) math.Append(first);
                    index++;
                    while (index < lines.Length)
                    {
                        var candidate = lines[index].Trim();
                        var end = candidate.EndsWith("$$", StringComparison.Ordinal);
                        if (math.Length > 0) math.Append(' ');
                        math.Append(end ? candidate[..^2] : candidate);
                        index++;
                        if (end) break;
                    }
                }
                _blocks.Children.Add(CreateMathBlock(math.ToString()));
                continue;
            }

            if (line.TrimStart().StartsWith("\\[", StringComparison.Ordinal))
            {
                var math = new StringBuilder();
                var trimmed = line.Trim();
                if (trimmed.Length >= 4 && trimmed.EndsWith("\\]", StringComparison.Ordinal))
                {
                    math.Append(trimmed[2..^2]);
                    index++;
                }
                else
                {
                    var first = trimmed[2..];
                    if (first.Length > 0) math.Append(first);
                    index++;
                    while (index < lines.Length)
                    {
                        var candidate = lines[index].Trim();
                        var end = candidate.EndsWith("\\]", StringComparison.Ordinal);
                        if (math.Length > 0) math.Append(' ');
                        math.Append(end ? candidate[..^2] : candidate);
                        index++;
                        if (end) break;
                    }
                }
                _blocks.Children.Add(CreateMathBlock(math.ToString()));
                continue;
            }

            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                _blocks.Children.Add(CreateRichText(heading.Groups[2].Value.Trim(), Math.Max(15, 25 - level * 2),
                    level <= 2 ? FontWeight.SemiBold : FontWeight.Medium, level <= 2 ? 5 : 2));
                index++;
                continue;
            }

            if (IsRule(line))
            {
                _blocks.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 5), Background = Brush("HavenLineStrongBrush", "#2B2D31") });
                index++;
                continue;
            }

            if (IsTableHeader(lines, index))
            {
                var tableLines = new List<string> { line };
                index += 2;
                while (index < lines.Length && lines[index].Contains('|') && !string.IsNullOrWhiteSpace(lines[index]))
                    tableLines.Add(lines[index++]);
                _blocks.Children.Add(CreateTable(tableLines));
                continue;
            }

            if (ListPattern().IsMatch(line))
            {
                var list = new StackPanel { Spacing = 5, Margin = new Thickness(3, 1, 0, 2) };
                while (index < lines.Length)
                {
                    var item = ListPattern().Match(lines[index]);
                    if (!item.Success) break;
                    var marker = item.Groups[1].Value;
                    var content = item.Groups[2].Value;
                    var task = TaskPattern().Match(content);
                    if (task.Success)
                    {
                        marker = task.Groups[1].Value.Equals("x", StringComparison.OrdinalIgnoreCase) ? "☑" : "☐";
                        content = task.Groups[2].Value;
                    }
                    else if (marker is "-" or "*" or "+") marker = "•";
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*") };
                    row.Children.Add(new TextBlock
                    {
                        Text = marker,
                        Foreground = Brush("HavenAccentBrush", "#72E0BD"),
                        FontSize = 13,
                        TextAlignment = TextAlignment.Right,
                        Margin = new Thickness(0, 1, 7, 0)
                    });
                    var text = CreateRichText(content.Trim(), 14, FontWeight.Normal, 0);
                    Grid.SetColumn(text, 1);
                    row.Children.Add(text);
                    list.Children.Add(row);
                    index++;
                }
                _blocks.Children.Add(list);
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                var quote = new StringBuilder();
                while (index < lines.Length && lines[index].TrimStart().StartsWith('>'))
                {
                    if (quote.Length > 0) quote.Append(' ');
                    quote.Append(lines[index].TrimStart().TrimStart('>').TrimStart());
                    index++;
                }
                _blocks.Children.Add(new Border
                {
                    BorderBrush = Brush("HavenAccentBrush", "#72E0BD"),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Background = Brush("HavenPanel2Brush", "#111721"),
                    CornerRadius = new CornerRadius(0, 8, 8, 0),
                    Padding = new Thickness(12, 8),
                    Child = CreateRichText(quote.ToString(), 13, FontWeight.Normal, 0, FontStyle.Italic)
                });
                continue;
            }

            var paragraph = new StringBuilder(line.Trim());
            index++;
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]) && !StartsBlock(lines, index))
            {
                paragraph.Append(lines[index - 1].EndsWith("  ", StringComparison.Ordinal) ? "\n" : " ");
                paragraph.Append(lines[index].Trim());
                index++;
            }
            _blocks.Children.Add(CreateRichText(paragraph.ToString(), 14, FontWeight.Normal, 0));
        }
    }

    /// <summary>
    /// Creates code block with the invariants required by its callers.
    /// </summary>
    private Control CreateCodeBlock(string code, string language)
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
        if (!string.IsNullOrWhiteSpace(language))
        {
            panel.Children.Add(new TextBlock
            {
                Text = language.ToUpperInvariant(),
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = Brush("HavenMuted2Brush", "#657184"),
                Margin = new Thickness(1, 0, 0, 7)
            });
        }
        var text = new SelectableTextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 12,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("HavenTextSoftBrush", "#C9D2DE")
        };
        Grid.SetRow(text, 1);
        panel.Children.Add(text);
        return new Border
        {
            Background = Brush("HavenBackgroundBrush", "#080B10"),
            BorderBrush = Brush("HavenLineStrongBrush", "#2B2D31"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9),
            Child = panel
        };
    }

    /// <summary>
    /// Creates math block with the invariants required by its callers.
    /// </summary>
    private Control CreateMathBlock(string latex)
    {
        return new Border
        {
            Background = Brush("HavenBackgroundBrush", "#080B10"),
            BorderBrush = Brush("HavenLineBrush", "#1B1D22"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 11),
            Child = new SelectableTextBlock
            {
                Text = LatexFormatter.Format(latex),
                FontFamily = new FontFamily("Cambria Math, STIX Two Math, Segoe UI Symbol"),
                FontSize = 18,
                Foreground = Brush("HavenTextBrush", "#EDF2F7"),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch
            }
        };
    }

    /// <summary>
    /// Creates table with the invariants required by its callers.
    /// </summary>
    private Control CreateTable(IReadOnlyList<string> rows)
    {
        var values = rows.Select(SplitTableRow).ToArray();
        var columns = Math.Max(1, values.Max(row => row.Length));
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", columns)))
        };
        for (var row = 0; row < values.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var column = 0; column < values[row].Length; column++)
            {
                var cell = new Border
                {
                    Background = row == 0 ? Brush("HavenPanel3Brush", "#151D29") : Brush("HavenPanel2Brush", "#111721"),
                    BorderBrush = Brush("HavenLineStrongBrush", "#2B2D31"),
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(9, 7),
                    Child = CreateRichText(values[row][column], 12, row == 0 ? FontWeight.SemiBold : FontWeight.Normal, 0)
                };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }
        return new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = grid };
    }

    /// <summary>
    /// Creates rich text with the invariants required by its callers.
    /// </summary>
    private TextBlock CreateRichText(string text, double size, FontWeight weight, double bottomMargin, FontStyle style = FontStyle.Normal)
    {
        var block = new TextBlock
        {
            FontSize = size,
            FontWeight = weight,
            FontStyle = style,
            LineHeight = Math.Max(19, size * 1.55),
            Foreground = Brush("HavenTextSoftBrush", "#C9D2DE"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, bottomMargin)
        };
        block.Inlines ??= new InlineCollection();
        AddInlines(block.Inlines, text);
        return block;
    }

    /// <summary>
    /// Performs the add inlines step owned by this component.
    /// </summary>
    private void AddInlines(InlineCollection inlines, string text)
    {
        var position = 0;
        foreach (Match match in InlinePattern().Matches(text))
        {
            if (match.Index > position) inlines.Add(new Run(text[position..match.Index]));
            var token = match.Value;
            if (token.StartsWith("**", StringComparison.Ordinal) || token.StartsWith("__", StringComparison.Ordinal))
            {
                var span = new Bold();
                AddInlines(span.Inlines, token[2..^2]);
                inlines.Add(span);
            }
            else if (token.StartsWith('*') || token.StartsWith('_'))
            {
                var span = new Italic();
                AddInlines(span.Inlines, token[1..^1]);
                inlines.Add(span);
            }
            else if (token.StartsWith('`'))
            {
                var run = new Run(token[1..^1]);
                run.FontFamily = new FontFamily("Cascadia Code, Consolas");
                run.Foreground = Brush("HavenAccentBrush", "#72E0BD");
                inlines.Add(run);
            }
            else if (token.StartsWith('!'))
            {
                var image = ImagePattern().Match(token);
                inlines.Add(new Run(image.Success ? $"[Image: {image.Groups[1].Value}]" : token));
            }
            else if (token.StartsWith('['))
            {
                var link = LinkPattern().Match(token);
                if (link.Success)
                {
                    var run = new Run($"{link.Groups[1].Value} ({link.Groups[2].Value})");
                    run.Foreground = Brush("HavenBlueBrush", "#5AA6FF");
                    inlines.Add(run);
                }
                else inlines.Add(new Run(token));
            }
            else if (token.StartsWith("\\(", StringComparison.Ordinal))
            {
                var run = new Run(LatexFormatter.Format(token[2..^2]));
                run.FontFamily = new FontFamily("Cambria Math, STIX Two Math, Segoe UI Symbol");
                run.Foreground = Brush("HavenTextBrush", "#EDF2F7");
                inlines.Add(run);
            }
            else if (token.StartsWith('$'))
            {
                var run = new Run(LatexFormatter.Format(token[1..^1]));
                run.FontFamily = new FontFamily("Cambria Math, STIX Two Math, Segoe UI Symbol");
                run.Foreground = Brush("HavenTextBrush", "#EDF2F7");
                inlines.Add(run);
            }
            else if (token == "\n") inlines.Add(new LineBreak());
            else inlines.Add(new Run(token));
            position = match.Index + match.Length;
        }
        if (position < text.Length) inlines.Add(new Run(text[position..]));
    }

    /// <summary>
    /// Performs the brush step owned by this component.
    /// </summary>
    private IBrush Brush(string key, string fallback)
    {
        if (Avalonia.Application.Current?.TryFindResource(key, ActualThemeVariant, out var resource) == true && resource is IBrush brush)
            return brush;
        return new SolidColorBrush(Color.Parse(fallback));
    }

    /// <summary>
    /// Performs the starts block step owned by this component.
    /// </summary>
    private static bool StartsBlock(string[] lines, int index)
    {
        var line = lines[index];
        return line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
               line.TrimStart().StartsWith("$$", StringComparison.Ordinal) ||
               line.TrimStart().StartsWith("\\[", StringComparison.Ordinal) ||
               line.TrimStart().StartsWith('>') || HeadingPattern().IsMatch(line) ||
               ListPattern().IsMatch(line) || IsRule(line) || IsTableHeader(lines, index);
    }

    /// <summary>
    /// Reports whether is rule is true for the current state.
    /// </summary>
    private static bool IsRule(string line)
    {
        var compact = line.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.Length >= 3 && (compact.All(character => character == '-') || compact.All(character => character == '*') || compact.All(character => character == '_'));
    }

    /// <summary>
    /// Reports whether is table header is true for the current state.
    /// </summary>
    private static bool IsTableHeader(string[] lines, int index) =>
        index + 1 < lines.Length && lines[index].Contains('|') && TableDividerPattern().IsMatch(lines[index + 1]);

    /// <summary>
    /// Performs the split table row step owned by this component.
    /// </summary>
    private static string[] SplitTableRow(string line) => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();

    /// <summary>
    /// Performs the heading pattern step owned by this component.
    /// </summary>
    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingPattern();

    /// <summary>
    /// Performs the list pattern step owned by this component.
    /// </summary>
    [GeneratedRegex(@"^\s*((?:\d+[.)])|[-*+])\s+(.+)$")]
    private static partial Regex ListPattern();

    /// <summary>
    /// Performs the task pattern step owned by this component.
    /// </summary>
    [GeneratedRegex(@"^\[([ xX])\]\s+(.+)$")]
    private static partial Regex TaskPattern();

    /// <summary>
    /// Performs the table divider pattern step owned by this component.
    /// </summary>
    [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?\s*$")]
    private static partial Regex TableDividerPattern();

    /// <summary>
    /// Performs the inline pattern step owned by this component.
    /// </summary>
    [GeneratedRegex(@"(\*\*.+?\*\*|__.+?__|(?<!\*)\*[^*\n]+?\*(?!\*)|(?<!_)_[^_\n]+?_(?!_)|`[^`\n]+`|!\[[^\]]*\]\([^\s)]+\)|\[[^\]]+\]\([^\s)]+\)|\\\(.+?\\\)|(?<!\$)\$(?!\$).+?(?<!\$)\$(?!\$)|\n)")]
    private static partial Regex InlinePattern();

    /// <summary>
    /// Performs the link pattern step owned by this component.
    /// </summary>
    [GeneratedRegex(@"^\[([^\]]+)\]\(([^\s)]+)\)$")]
    private static partial Regex LinkPattern();

    /// <summary>
    /// Performs the image pattern step owned by this component.
    /// </summary>
    [GeneratedRegex(@"^!\[([^\]]*)\]\(([^\s)]+)\)$")]
    private static partial Regex ImagePattern();
}

/// <summary>
/// Represents latex formatter and keeps its related state and behavior together.
/// </summary>
internal static partial class LatexFormatter
{
    /// <summary>
    /// Stores commands locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Commands = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["theta"] = "θ",
        ["lambda"] = "λ", ["mu"] = "μ", ["pi"] = "π", ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ", ["phi"] = "φ",
        ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω", ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ",
        ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω", ["times"] = "×", ["cdot"] = "·",
        ["pm"] = "±", ["mp"] = "∓", ["div"] = "÷", ["neq"] = "≠", ["approx"] = "≈", ["equiv"] = "≡", ["le"] = "≤", ["leq"] = "≤",
        ["ge"] = "≥", ["geq"] = "≥", ["infty"] = "∞", ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫", ["partial"] = "∂",
        ["nabla"] = "∇", ["in"] = "∈", ["notin"] = "∉", ["subset"] = "⊂", ["supset"] = "⊃", ["cup"] = "∪", ["cap"] = "∩",
        ["rightarrow"] = "→", ["leftarrow"] = "←", ["Rightarrow"] = "⇒", ["Leftarrow"] = "⇐", ["leftrightarrow"] = "↔",
        ["forall"] = "∀", ["exists"] = "∃", ["land"] = "∧", ["lor"] = "∨", ["neg"] = "¬", ["degree"] = "°"
    };

    /// <summary>
    /// Stores superscript locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyDictionary<char, char> Superscript = new Dictionary<char, char>
    {
        ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴', ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹',
        ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽', [')'] = '⁾', ['n'] = 'ⁿ', ['i'] = 'ⁱ'
    };

    /// <summary>
    /// Stores subscript locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyDictionary<char, char> Subscript = new Dictionary<char, char>
    {
        ['0'] = '₀', ['1'] = '₁', ['2'] = '₂', ['3'] = '₃', ['4'] = '₄', ['5'] = '₅', ['6'] = '₆', ['7'] = '₇', ['8'] = '₈', ['9'] = '₉',
        ['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍', [')'] = '₎', ['a'] = 'ₐ', ['e'] = 'ₑ', ['h'] = 'ₕ', ['i'] = 'ᵢ',
        ['j'] = 'ⱼ', ['k'] = 'ₖ', ['l'] = 'ₗ', ['m'] = 'ₘ', ['n'] = 'ₙ', ['o'] = 'ₒ', ['p'] = 'ₚ', ['r'] = 'ᵣ', ['s'] = 'ₛ', ['t'] = 'ₜ', ['u'] = 'ᵤ', ['v'] = 'ᵥ', ['x'] = 'ₓ'
    };

    /// <summary>
    /// Performs the format step owned by this component.
    /// </summary>
    public static string Format(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex)) return string.Empty;
        var index = 0;
        var value = FormatExpression(latex.Trim(), ref index);
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Performs the format expression step owned by this component.
    /// </summary>
    private static string FormatExpression(string source, ref int index, char? terminator = null)
    {
        var builder = new StringBuilder();
        while (index < source.Length)
        {
            var character = source[index];
            if (terminator is not null && character == terminator)
            {
                index++;
                break;
            }

            if (character == '\\')
            {
                builder.Append(ReadCommand(source, ref index));
                continue;
            }

            if (character is '^' or '_')
            {
                index++;
                var operand = ReadOperand(source, ref index);
                builder.Append(ConvertScript(operand, character == '^' ? Superscript : Subscript));
                continue;
            }

            if (character == '{')
            {
                index++;
                builder.Append(FormatExpression(source, ref index, '}'));
                continue;
            }

            if (character == '}')
            {
                index++;
                break;
            }

            builder.Append(character);
            index++;
        }
        return builder.ToString();
    }

    /// <summary>
    /// Performs the read command step owned by this component.
    /// </summary>
    private static string ReadCommand(string source, ref int index)
    {
        index++;
        if (index >= source.Length) return string.Empty;
        if (!char.IsLetter(source[index]))
        {
            var escaped = source[index++];
            return escaped is ',' or ';' or ':' or '!' ? " " : escaped.ToString();
        }

        var start = index;
        while (index < source.Length && char.IsLetter(source[index])) index++;
        var command = source[start..index];
        if (command is "left" or "right") return string.Empty;
        if (command is "quad" or "qquad") return " ";

        if (command == "frac")
        {
            if (!TryReadGroup(source, ref index, '{', '}', out var numerator) ||
                !TryReadGroup(source, ref index, '{', '}', out var denominator))
                return command;
            return $"{FormatFractionPart(Format(numerator))}⁄{FormatFractionPart(Format(denominator))}";
        }

        if (command == "sqrt")
        {
            TryReadGroup(source, ref index, '[', ']', out var rootIndex);
            if (!TryReadGroup(source, ref index, '{', '}', out var radicand)) return "√";
            var root = rootIndex switch { "3" => "∛", "4" => "∜", "" => "√", _ => ConvertScript(rootIndex, Superscript) + "√" };
            return $"{root}({Format(radicand)})";
        }

        if (command is "text" or "mathrm" or "mathbf" or "operatorname")
            return TryReadGroup(source, ref index, '{', '}', out var text) ? Format(text) : string.Empty;

        return Commands.TryGetValue(command, out var symbol) ? symbol : command;
    }

    /// <summary>
    /// Performs the read operand step owned by this component.
    /// </summary>
    private static string ReadOperand(string source, ref int index)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
        if (index >= source.Length) return string.Empty;
        if (source[index] == '{' && TryReadGroup(source, ref index, '{', '}', out var group)) return Format(group);
        if (source[index] == '\\') return ReadCommand(source, ref index);
        return source[index++].ToString();
    }

    /// <summary>
    /// Attempts to read group and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryReadGroup(string source, ref int index, char open, char close, out string value)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
        if (index >= source.Length || source[index] != open)
        {
            value = string.Empty;
            return false;
        }

        var start = ++index;
        var depth = 1;
        while (index < source.Length)
        {
            if (source[index] == open) depth++;
            else if (source[index] == close && --depth == 0)
            {
                value = source[start..index];
                index++;
                return true;
            }
            index++;
        }
        value = source[start..];
        return true;
    }

    /// <summary>
    /// Performs the format fraction part step owned by this component.
    /// </summary>
    private static string FormatFractionPart(string value) =>
        value.IndexOfAny([' ', '+', '-', '=', '±', '∓']) >= 0 ? $"({value})" : value;

    /// <summary>
    /// Performs the convert script step owned by this component.
    /// </summary>
    private static string ConvertScript(string value, IReadOnlyDictionary<char, char> map)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value) builder.Append(map.TryGetValue(character, out var converted) ? converted : character);
        return builder.ToString();
    }

}
