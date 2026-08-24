using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Write;

internal sealed partial class WordWriteHavenScene
{
    private void BuildRibbonGroups()
    {
        var commands = RibbonContent.Children.ToList();
        foreach (var command in commands)
            command.Parent?.Remove(command);

        var definitions = _tab switch
        {
            WordWriteRibbonTab.Home => new[]
            {
                Group("Font", "Write.Home.Style", "Write.Home.Font", "Write.Home.Size"),
                Group("Formatting", "Write.Home.Bold", "Write.Home.Italic", "Write.Home.Underline", "Write.Home.Strike", "Write.Home.TextColour", "Write.Home.Highlight"),
                Group("Paragraph", "Write.Align.", "Write.Home.LineSpacing", "Write.Home.Outdent", "Write.Home.Indent"),
                Group("Selection", "Write.Table.", "Write.Media.", "Write.Shape.")
            },
            WordWriteRibbonTab.Insert => new[]
            {
                Group("Text", "Write.Insert.Paragraph", "Write.Insert.Heading", "Write.Insert.Quote", "Write.Insert.Bullets", "Write.Insert.Numbering", "Write.Insert.Checklist"),
                Group("Tables", "Write.Insert.Table"),
                Group("Illustrations", "Write.Insert.Image", "Write.Insert.CustomShape", "Write.Insert.Equation", "Write.Insert.Divider", "Write.Insert.Link")
            },
            WordWriteRibbonTab.Layout => new[]
            {
                Group("Page setup", "Write.Layout.A4", "Write.Layout.Letter", "Write.Layout.Portrait", "Write.Layout.Landscape", "Write.Layout.PageNumbers"),
                Group("Document view", "Write.Layout.Mode."),
                Group("Zoom", "Write.Zoom.")
            },
            WordWriteRibbonTab.Review => new[]
            {
                Group("Find and replace", "Write.Review.Find", "Write.Review.Replace"),
                Group("Comments", "Write.Review.Comment", "Write.Review.AddComment"),
                Group("Sources", "Write.Review.SourceTitle", "Write.Review.Authors", "Write.Review.Url", "Write.Review.AddSource")
            },
            _ => []
        };

        var remaining = new List<HavenElement>(commands);
        foreach (var definition in definitions)
        {
            var matches = remaining
                .Where(command => definition.Prefixes.Any(prefix => command.Name?.StartsWith(prefix, StringComparison.Ordinal) == true))
                .ToList();
            if (matches.Count == 0)
                continue;

            RibbonContent.Add(CreateRibbonGroup(definition.Label, matches));
            foreach (var match in matches)
                remaining.Remove(match);
        }

        if (remaining.Count > 0)
            RibbonContent.Add(CreateRibbonGroup(_tab == WordWriteRibbonTab.Review ? "Document" : "More", remaining));
    }

    private static Container CreateRibbonGroup(string label, IReadOnlyList<HavenElement> commands)
    {
        var group = new Container { Name = $"Write.Ribbon.Group.{label.Replace(" ", string.Empty, StringComparison.Ordinal)}", Layout = HavenLayout.Vertical };
        group.Accessibility.AccessibleName = label + " tools";
        group.SetValue(HavenProperties.Background, "Surface");
        group.SetValue(HavenProperties.BorderColor, "Border");
        group.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        group.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
        group.SetValue(HavenProperties.Padding, HavenThickness.Parse("7px 8px 5px 8px"));
        group.SetValue(HavenProperties.Gap, HavenLength.Px(4));

        var tools = new Container { Name = group.Name + ".Tools", Layout = HavenLayout.Wrap };
        tools.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        foreach (var command in commands)
        {
            RestyleRibbonCommand(command);
            tools.Add(command);
        }

        var caption = Caption(label);
        caption.Name = group.Name + ".Label";
        caption.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        group.Add(tools);
        group.Add(caption);
        return group;
    }

    private static void RestyleRibbonCommand(HavenElement command)
    {
        command.SetValue(HavenProperties.Foreground, "ButtonTextSecondary");
        command.SetValue(HavenProperties.FontSize, 12d);
        command.SetValue(HavenProperties.FontWeight, 500);
        command.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
        command.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(7)));
        if (command is HavenButton button && button.Variant == ButtonVariant.Tertiary)
            button.Variant = ButtonVariant.Ghost;
    }

    private static void StyleRibbonTab(HavenButton button, bool selected)
    {
        button.Variant = ButtonVariant.Text;
        button.SetValue(HavenProperties.Background, selected ? "AccentMuted" : "Transparent");
        button.SetValue(HavenProperties.Foreground, selected ? "ButtonTextPrimary" : "ButtonTextSecondary");
        button.SetValue(HavenProperties.FontSize, 13d);
        button.SetValue(HavenProperties.FontWeight, selected ? 700 : 500);
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("6px 12px"));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(6)));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(30));
    }

    private static RibbonGroupDefinition Group(string label, params string[] prefixes) => new(label, prefixes);

    private static Container CreateRuler()
    {
        var ruler = new Container { Name = "Write.Ruler", Layout = HavenLayout.Grid, Columns = string.Join(' ', Enumerable.Repeat("1fr", 10)), Rows = "Auto" };
        ruler.Accessibility.AccessibleName = "Horizontal page ruler";
        ruler.SetValue(HavenProperties.Background, "SurfaceRaised");
        ruler.SetValue(HavenProperties.BorderColor, "Border");
        ruler.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        ruler.SetValue(HavenProperties.Padding, HavenThickness.Parse("3px 24px"));
        for (var index = 0; index < 10; index++)
        {
            var marker = Caption(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            marker.Name = $"Write.Ruler.Mark.{index}";
            marker.SetValue(HavenProperties.Column, index);
            marker.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
            ruler.Add(marker);
        }
        return ruler;
    }

    private sealed record RibbonGroupDefinition(string Label, IReadOnlyList<string> Prefixes);
}
