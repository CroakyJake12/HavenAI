using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Write;

internal sealed partial class WordWriteHavenScene
{
    private void BuildLibraryLanding()
    {
        DocumentHost.SetValue(HavenProperties.MaxWidth, HavenLength.Px(1180));
        DocumentHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        DocumentHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("26px 28px 34px 28px"));
        DocumentHost.SetValue(HavenProperties.Gap, HavenLength.Px(18));

        var title = new HavenText("Write") { Level = TextLevel.H2 };
        title.SetValue(HavenProperties.FontSize, 34d);
        title.SetValue(HavenProperties.FontWeight, 750);
        DocumentHost.Add(title);
        var subtitle = new HavenText("Create, import or reopen a document. Your work stays editable and saved locally.") { Level = TextLevel.Paragraph };
        subtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        DocumentHost.Add(subtitle);

        var actions = new Container { Name = "Write.Library.Actions", Layout = HavenLayout.Wrap };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var create = LandingButton("Write.Library.New", "New document", ButtonVariant.Primary);
        create.Invoked += (_, _) => NewRequested?.Invoke(this, EventArgs.Empty);
        var import = LandingButton("Write.Library.Import", "Open / import document", ButtonVariant.Secondary);
        import.Invoked += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty);
        actions.Add(create);
        actions.Add(import);
        DocumentHost.Add(actions);

        DocumentHost.Add(LandingHeading("Starting points"));
        var starters = new Container { Name = "Write.Library.Starters", Layout = HavenLayout.Wrap };
        starters.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var blank = LandingButton("Write.Library.Blank", "Blank document", ButtonVariant.Navigation);
        blank.Invoked += (_, _) => NewRequested?.Invoke(this, EventArgs.Empty);
        starters.Add(blank);
        DocumentHost.Add(starters);

        DocumentHost.Add(LandingHeading("Recent"));
        var recent = new Container { Name = "Write.Library.Recent", Layout = HavenLayout.Wrap };
        recent.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        if (_libraryDocuments.Count == 0)
        {
            recent.Add(Caption("No documents yet. Create a blank document or import a supported file."));
        }
        else
        {
            foreach (var document in _libraryDocuments.OrderByDescending(value => value.UpdatedAt))
            {
                var card = new Container { Name = $"Write.Library.Card.{document.Id:N}", Layout = HavenLayout.Vertical };
                card.SetValue(HavenProperties.Width, HavenLength.Px(270));
                card.SetValue(HavenProperties.MinHeight, HavenLength.Px(124));
                card.SetValue(HavenProperties.Background, "SurfaceRaised");
                card.SetValue(HavenProperties.BorderColor, "Border");
                card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
                card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
                card.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px"));
                card.SetValue(HavenProperties.Gap, HavenLength.Px(6));
                var open = LandingButton($"Write.Library.Open.{document.Id:N}", document.Title, ButtonVariant.Navigation);
                open.SetValue(HavenProperties.FontSize, 16d);
                open.Invoked += (_, _) => DocumentOpenRequested?.Invoke(document.Id);
                card.Add(open);
                card.Add(Caption($"{document.WordCount} words · updated {document.UpdatedAt.LocalDateTime:g} · v{document.Version}{(document.HasRecovery ? " · recovery available" : string.Empty)}"));
                recent.Add(card);
            }
        }
        DocumentHost.Add(recent);
        DocumentHost.Add(Caption(_libraryDocuments.Count == 1 ? "1 document saved locally" : $"{_libraryDocuments.Count} documents saved locally"));
    }

    private static HavenText LandingHeading(string text)
    {
        var heading = new HavenText(text) { Level = TextLevel.H4 };
        heading.SetValue(HavenProperties.FontSize, 18d);
        heading.SetValue(HavenProperties.FontWeight, 700);
        return heading;
    }

    private static HavenButton LandingButton(string name, string label, ButtonVariant variant)
    {
        var button = new HavenButton { Name = name, Content = label, Variant = variant };
        button.Accessibility.AccessibleName = label;
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(42));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 14px"));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        return button;
    }
}
