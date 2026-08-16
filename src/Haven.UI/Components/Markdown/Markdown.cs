using System.Text;
using System.Text.RegularExpressions;

namespace Haven.UI.Components;

public enum MarkdownCodeAction
{
    Copy,
    AskToRun,
    AskToApply
}

public sealed record MarkdownCodeActionRequest(MarkdownCodeAction Action, string Language, string Code);

/// <summary>
/// Haven-native markdown presentation. It expands block markdown into ordinary Haven elements so
/// layout, rendering, input, accessibility and responsive behaviour stay platform-neutral.
/// </summary>
public sealed class Markdown : Container
{
    private static readonly Regex OrderedList = new("^\\s*(\\d+)[.)]\\s+(.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TaskList = new("^\\s*[-*+]\\s+\\[([ xX])\\]\\s+(.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BulletList = new("^\\s*[-*+]\\s+(.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private string _content = string.Empty;

    public Markdown()
    {
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Gap, HavenLength.Px(8), HavenValueSource.Default);
        Accessibility.Role = HavenAccessibleRole.Group;
    }

    public string Content
    {
        get => _content;
        set
        {
            var next = value ?? string.Empty;
            if (_content == next) return;
            _content = next;
            Rebuild();
        }
    }

    public event EventHandler<MarkdownCodeActionRequest>? CodeActionRequested;

    public override HavenComponentMetadata Metadata => new(
        "Markdown",
        "Components/Markdown/Markdown.cs",
        ["Markdown"],
        [],
        "Block markdown expands into canonical Haven elements; code actions are semantic events rather than platform controls.");

    private void Rebuild()
    {
        Update(() =>
        {
            foreach (var child in Children.ToArray()) Remove(child);
            var text = _content.ReplaceLineEndings("\n");
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
                    Add(BuildCodeBlock(language, code.ToString().TrimEnd()));
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
                    Add(BuildEquation(equation.ToString().Trim()));
                    continue;
                }
                if (TryBuildTable(lines, ref index, out var table))
                {
                    FlushParagraph(paragraph);
                    Add(table);
                    continue;
                }
                if (TryBuildHeading(line, out var heading))
                {
                    FlushParagraph(paragraph);
                    Add(heading);
                    continue;
                }
                if (line.StartsWith('>'))
                {
                    FlushParagraph(paragraph);
                    Add(BuildQuote(line.TrimStart('>', ' ')));
                    continue;
                }
                if (TaskList.Match(line) is { Success: true } task)
                {
                    FlushParagraph(paragraph);
                    Add(BuildListItem(task.Groups[1].Value == " " ? "☐" : "☑", task.Groups[2].Value));
                    continue;
                }
                if (OrderedList.Match(line) is { Success: true } ordered)
                {
                    FlushParagraph(paragraph);
                    Add(BuildListItem(ordered.Groups[1].Value + ".", ordered.Groups[2].Value));
                    continue;
                }
                if (BulletList.Match(line) is { Success: true } bullet)
                {
                    FlushParagraph(paragraph);
                    Add(BuildListItem("•", bullet.Groups[1].Value));
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
        });
    }

    private void FlushParagraph(List<string> paragraph)
    {
        if (paragraph.Count == 0) return;
        Add(TextBlock(FlattenInline(string.Join("\n", paragraph)), 14, 500));
        paragraph.Clear();
    }

    private static bool TryBuildHeading(string line, out HavenElement heading)
    {
        var count = line.TakeWhile(character => character == '#').Count();
        if (count is < 1 or > 6 || line.Length <= count || line[count] != ' ')
        {
            heading = null!;
            return false;
        }
        var size = count switch { 1 => 25d, 2 => 21d, 3 => 18d, 4 => 16d, _ => 14d };
        heading = TextBlock(FlattenInline(line[(count + 1)..].Trim()), size, 700);
        return true;
    }

    private static Container BuildQuote(string content)
    {
        var quote = Surface("SurfaceRaised", "Border", "0px 0px 0px 3px", "12px 8px", "0px 8px 8px 0px");
        quote.Add(TextBlock(FlattenInline(content), 13, 500));
        return quote;
    }

    private static Container BuildListItem(string marker, string content)
    {
        var row = new Container { Layout = HavenLayout.Horizontal };
        row.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        var markerText = TextBlock(marker, 13, 700);
        markerText.SetValue(HavenProperties.Foreground, "AccentSecondary");
        markerText.SetValue(HavenProperties.Width, HavenLength.Px(28));
        row.Add(markerText);
        var value = TextBlock(FlattenInline(content), 13, 500);
        value.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        row.Add(value);
        return row;
    }

    private Container BuildCodeBlock(string language, string code)
    {
        var surface = Surface("SurfaceRaised", "Border", "1px", "10px", "12px");
        var header = new Container { Layout = HavenLayout.Horizontal };
        header.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        var label = TextBlock(string.IsNullOrWhiteSpace(language) ? "code" : language, 10, 600);
        label.SetValue(HavenProperties.Foreground, "TextSecondary");
        label.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        header.Add(label);
        header.Add(CodeActionButton("Copy", MarkdownCodeAction.Copy, language, code));
        header.Add(CodeActionButton("Ask to run", MarkdownCodeAction.AskToRun, language, code));
        header.Add(CodeActionButton("Ask to apply", MarkdownCodeAction.AskToApply, language, code));
        surface.Add(header);
        var codeText = TextBlock(code, 12, 500);
        codeText.SetValue(HavenProperties.FontFamily, "Cascadia Mono");
        codeText.Accessibility.AccessibleName = "Code block";
        surface.Add(codeText);
        return surface;
    }

    private Button CodeActionButton(string label, MarkdownCodeAction action, string language, string code)
    {
        var button = new Button { Content = label, Variant = ButtonVariant.Ghost };
        button.SetValue(HavenProperties.FontSize, 10d);
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
        button.Invoked += (_, _) => CodeActionRequested?.Invoke(this, new MarkdownCodeActionRequest(action, language, code));
        return button;
    }

    private static Container BuildEquation(string latex)
    {
        var surface = Surface("SurfaceRaised", "Transparent", "0px", "12px 9px", "10px");
        var equation = TextBlock(FormatLatex(latex), 18, 500);
        equation.SetValue(HavenProperties.FontFamily, "Cambria Math");
        equation.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        surface.Add(equation);
        return surface;
    }

    private static bool TryBuildTable(string[] lines, ref int index, out HavenElement table)
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
        var grid = new Container { Layout = HavenLayout.Grid, Columns = string.Join(' ', Enumerable.Repeat("1fr", columns)), Rows = string.Join(' ', Enumerable.Repeat("Auto", rows.Count)) };
        grid.SetValue(HavenProperties.Gap, HavenLength.Px(1));
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var column = 0; column < columns; column++)
            {
                var cell = Surface(rowIndex == 0 ? "SurfaceRaised" : "Surface", "Transparent", "0px", "8px 6px", "0px");
                var text = column < rows[rowIndex].Count ? rows[rowIndex][column].Trim() : string.Empty;
                cell.Add(TextBlock(FlattenInline(text), 12, rowIndex == 0 ? 700 : 500));
                cell.SetValue(HavenProperties.Row, rowIndex);
                cell.SetValue(HavenProperties.Column, column);
                grid.Add(cell);
            }
        }
        table = grid;
        return true;
    }

    private static IReadOnlyList<string> SplitTableRow(string line) =>
        line.Trim().Trim('|').Split('|').Select(value => value.Trim()).ToArray();

    private static Text TextBlock(string content, double size, int weight)
    {
        var text = new Text { Content = content };
        text.SetValue(HavenProperties.FontSize, size);
        text.SetValue(HavenProperties.FontWeight, weight);
        text.SetValue(HavenProperties.Foreground, "TextPrimary");
        return text;
    }

    private static Container Surface(string background, string border, string borderWidth, string padding, string radius)
    {
        var surface = new Container();
        surface.SetValue(HavenProperties.Background, background);
        surface.SetValue(HavenProperties.BorderColor, border);
        surface.SetValue(HavenProperties.BorderWidth, HavenLength.Parse(borderWidth));
        surface.SetValue(HavenProperties.Padding, HavenThickness.Parse(padding));
        surface.SetValue(HavenProperties.Radius, ParseRadius(radius));
        surface.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        return surface;
    }

    private static HavenCornerRadius ParseRadius(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(HavenLength.Parse)
            .ToArray();
        return parts.Length switch
        {
            1 => HavenCornerRadius.Uniform(parts[0]),
            4 => new HavenCornerRadius(parts[0], parts[1], parts[2], parts[3]),
            _ => throw new FormatException("Markdown surface radius accepts one or four Haven lengths.")
        };
    }

    private static string FlattenInline(string text)
    {
        var value = Regex.Replace(text, "\\*\\*([^*]+)\\*\\*", "$1", RegexOptions.CultureInvariant);
        value = Regex.Replace(value, "(?<!\\*)\\*([^*]+)\\*(?!\\*)", "$1", RegexOptions.CultureInvariant);
        value = Regex.Replace(value, "`([^`]+)`", "$1", RegexOptions.CultureInvariant);
        value = Regex.Replace(value, "\\[([^\\]]+)\\]\\(([^)]+)\\)", "$1 ($2)", RegexOptions.CultureInvariant);
        value = Regex.Replace(value, "\\$([^$\\n]+)\\$", match => FormatLatex(match.Groups[1].Value), RegexOptions.CultureInvariant);
        return value;
    }

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
}
