using Haven.Application;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Present;

internal sealed partial class PresentHavenScene
{
    private void ApplyWorkspacePolish()
    {
        RebuildPolishedLibrary();

        // Present is a creative canvas. Keep the shell open and let the slide carry the visual weight.
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 12px 8px 12px"));
        WorkspaceHost.Columns = "148px 1fr";
        WorkspaceHost.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        WorkspaceHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        WorkspaceHost.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        WorkspaceHost.SetValue(HavenProperties.Responsive, true);
        WorkspaceHost.SetValue(HavenProperties.Background, "Transparent");

        SlideRail.SetValue(HavenProperties.Width, HavenLength.Px(148));
        SlideRail.SetValue(HavenProperties.MinWidth, HavenLength.Px(148));
        SlideRail.SetValue(HavenProperties.MaxWidth, HavenLength.Px(148));
        SlideRail.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        SlideRail.SetValue(HavenProperties.Background, "Transparent");
        SlideRail.SetValue(HavenProperties.BorderWidth, HavenLength.Px(0));
        SlideRail.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(0)));
        SlideRail.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 4px 4px 0px"));
        SlideRail.ClearValue(HavenProperties.Shadow, HavenValueSource.Explicit);

        StageHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        StageHost.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        StageHost.SetValue(HavenProperties.Responsive, true);
        StageHost.SetValue(HavenProperties.Background, "Transparent");
        StageHost.SetValue(HavenProperties.BorderWidth, HavenLength.Px(0));
        StageHost.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(0)));
        StageHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px"));

        CanvasOverlay.SetValue(HavenProperties.Background, "Transparent");
        CanvasOverlay.SetValue(HavenProperties.BorderWidth, HavenLength.Px(0));
        CanvasOverlay.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(0)));
        CanvasOverlay.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 8px 6px 8px"));

        ConfigureFloatingPill(WorkspacePill, 46, 8);
        ConfigureFloatingPill(ContextPill, 42, 58);
        if (WorkspacePill.Children.OfType<HavenButton>().FirstOrDefault(button => button.Name == "Present.Insert") is { } insert)
        {
            insert.Variant = ButtonVariant.Icon;
            insert.SetValue(HavenProperties.Width, HavenLength.Px(32));
            insert.SetValue(HavenProperties.MinWidth, HavenLength.Px(32));
            insert.SetValue(HavenProperties.MaxWidth, HavenLength.Px(32));
        }
        if (WorkspacePill.Children.OfType<HavenButton>().FirstOrDefault(button => button.Name == "Present.Workspace.Present") is { } present)
            present.Variant = ButtonVariant.Primary;
        foreach (var compact in ContextPill.Children.OfType<HavenButton>().Where(button => button.Name is "Present.Context.Bold" or "Present.Context.Italic"))
        {
            compact.Variant = ButtonVariant.Icon;
            compact.SetValue(HavenProperties.Width, HavenLength.Px(30));
            compact.SetValue(HavenProperties.MinWidth, HavenLength.Px(30));
            compact.SetValue(HavenProperties.MaxWidth, HavenLength.Px(30));
        }
        DeckTitleInput.SetValue(HavenProperties.Width, HavenLength.Px(190));
        DeckTitleInput.SetValue(HavenProperties.Height, HavenLength.Px(32));
        DeckTitleInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
        DeckTitleInput.SetValue(HavenProperties.MaxHeight, HavenLength.Px(32));
        DeckTitleInput.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        DeckTitleInput.SetValue(HavenProperties.FontSize, 13d);

        StructuredEditor.SetValue(HavenProperties.Width, HavenLength.Px(280));
        StructuredEditor.SetValue(HavenProperties.Margin, HavenThickness.Parse("70px 10px 0px 0px"));
        StructuredEditor.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));

        if (StageHost.Children.FirstOrDefault(child => child.Name == "Present.NotesBar") is Container notesBar)
        {
            notesBar.SetValue(HavenProperties.Height, HavenLength.Px(44));
            notesBar.SetValue(HavenProperties.MinHeight, HavenLength.Px(44));
            notesBar.SetValue(HavenProperties.MaxHeight, HavenLength.Px(44));
            notesBar.SetValue(HavenProperties.Background, "Transparent");
            notesBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(0));
            notesBar.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(0)));
            notesBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("3px 8px"));
            SlideTitleInput.SetValue(HavenProperties.Height, HavenLength.Px(36));
            SlideTitleInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
            SlideTitleInput.SetValue(HavenProperties.MaxHeight, HavenLength.Px(36));
            NotesInput.SetValue(HavenProperties.Height, HavenLength.Px(36));
            NotesInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
            NotesInput.SetValue(HavenProperties.MaxHeight, HavenLength.Px(36));
        }

        MenuBar.SetValue(HavenProperties.Height, HavenLength.Px(30));
        MenuBar.SetValue(HavenProperties.MinHeight, HavenLength.Px(30));
        MenuBar.SetValue(HavenProperties.MaxHeight, HavenLength.Px(30));
        MenuBar.SetValue(HavenProperties.Background, "Transparent");
        MenuBar.SetValue(HavenProperties.Gap, HavenLength.Px(0));
        foreach (var child in MenuBar.Children.OfType<HavenButton>())
        {
            child.SetValue(HavenProperties.Height, HavenLength.Px(28));
            child.SetValue(HavenProperties.MinHeight, HavenLength.Px(28));
            child.SetValue(HavenProperties.MaxHeight, HavenLength.Px(28));
            child.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        }
    }

    private void RebuildPolishedLibrary()
    {
        ClearChildren(LibraryHost);
        LibraryHost.SetValue(HavenProperties.MaxWidth, HavenLength.Px(1320));
        LibraryHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("30px 36px"));
        LibraryHost.SetValue(HavenProperties.Gap, HavenLength.Px(20));

        var hero = new Container { Name = "Present.Library.Hero", Layout = HavenLayout.Grid, Columns = "2fr 1fr", Rows = "Auto" };
        hero.SetValue(HavenProperties.Background, "SurfaceRaised");
        hero.SetValue(HavenProperties.BorderColor, "Border");
        hero.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        hero.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(24)));
        hero.SetValue(HavenProperties.Padding, HavenThickness.Parse("28px"));
        hero.SetValue(HavenProperties.Gap, HavenLength.Px(24));
        hero.SetValue(HavenProperties.Shadow, "Card");

        var copy = new Container { Name = "Present.Library.Hero.Copy", Layout = HavenLayout.Vertical };
        copy.SetValue(HavenProperties.Column, 0);
        copy.SetValue(HavenProperties.Gap, HavenLength.Px(11));
        copy.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        var title = new HavenText("Present") { Level = TextLevel.H2 };
        title.SetValue(HavenProperties.FontSize, 38d);
        copy.Add(title);
        var subtitle = new HavenText("Create, import or reopen a presentation. Your decks stay editable and local.") { Level = TextLevel.Paragraph };
        subtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        subtitle.SetValue(HavenProperties.FontSize, 16d);
        copy.Add(subtitle);

        var actions = new Container { Name = "Present.Library.Create", Layout = HavenLayout.Wrap };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        actions.SetValue(HavenProperties.Margin, HavenThickness.Parse("8px 0px 0px 0px"));
        actions.Add(LibraryAction("Present.Library.New", "+  New presentation", ButtonVariant.Primary, () => NewDeckRequested?.Invoke(this, EventArgs.Empty)));
        actions.Add(LibraryAction("Present.Library.Import", "Open / import .pptx", ButtonVariant.Secondary, () => ImportRequested?.Invoke(this, EventArgs.Empty)));
        actions.Add(LibraryAction("Present.Library.AI", "Create with AI", ButtonVariant.Tertiary, () => AiCreateRequested?.Invoke(this, EventArgs.Empty)));
        copy.Add(actions);
        var localCaption = new HavenText("Local-first  ·  PowerPoint import/export  ·  Editable tables and charts") { Level = TextLevel.Caption };
        localCaption.SetValue(HavenProperties.Foreground, "TextSecondary");
        copy.Add(localCaption);
        hero.Add(copy);

        var preview = new Container { Name = "Present.Library.Hero.Preview", Layout = HavenLayout.Vertical };
        preview.SetValue(HavenProperties.Column, 1);
        preview.SetValue(HavenProperties.MinHeight, HavenLength.Px(190));
        preview.SetValue(HavenProperties.Background, "Surface");
        preview.SetValue(HavenProperties.BorderColor, "Accent");
        preview.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        preview.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        preview.SetValue(HavenProperties.Padding, HavenThickness.Parse("22px"));
        preview.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        preview.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        var kicker = new HavenText("PRESENTATION") { Level = TextLevel.Caption };
        kicker.SetValue(HavenProperties.Foreground, "Accent");
        preview.Add(kicker);
        var previewTitle = new HavenText("Turn ideas into a deck that still feels yours.") { Level = TextLevel.H3 };
        previewTitle.SetValue(HavenProperties.FontSize, 22d);
        preview.Add(previewTitle);
        var previewDetail = new HavenText("Start blank, pick a template, or import an existing PowerPoint.") { Level = TextLevel.Caption };
        previewDetail.SetValue(HavenProperties.Foreground, "TextSecondary");
        preview.Add(previewDetail);
        hero.Add(preview);
        LibraryHost.Add(hero);

        LibraryHost.Add(PolishedSectionHeading("Templates", "Start with a useful structure, then make it yours."));
        var templates = new Container { Name = "Present.Library.Templates", Layout = HavenLayout.Wrap };
        templates.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        templates.Add(TemplateCard("Title + content", "A clean opening slide and content structure.", "title-content"));
        templates.Add(TemplateCard("Lesson deck", "Teaching flow with objectives and explanation slides.", "lesson"));
        templates.Add(TemplateCard("Project pitch", "A concise story for ideas, progress and next steps.", "pitch"));
        LibraryHost.Add(templates);

        LibraryHost.Add(PolishedSectionHeading("Pinned", "Keep important decks within reach."));
        LibraryHost.Add(PinnedDecks);
        LibraryHost.Add(PolishedSectionHeading("Recent", "Pick up where you left off."));
        LibraryHost.Add(RecentDecks);
    }

    private Container PolishedSectionHeading(string title, string detail)
    {
        var row = new Container { Layout = HavenLayout.Vertical };
        row.SetValue(HavenProperties.Gap, HavenLength.Px(2));
        var heading = new HavenText(title) { Level = TextLevel.H4 };
        heading.SetValue(HavenProperties.FontSize, 19d);
        row.Add(heading);
        var caption = new HavenText(detail) { Level = TextLevel.Caption };
        caption.SetValue(HavenProperties.Foreground, "TextSecondary");
        row.Add(caption);
        return row;
    }

    private Container TemplateCard(string title, string detail, string templateId)
    {
        var card = new Container { Name = $"Present.Template.Card.{templateId}", Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Width, HavenLength.Px(278));
        card.SetValue(HavenProperties.MinHeight, HavenLength.Px(166));
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        card.SetValue(HavenProperties.Shadow, "Card");
        var preview = new Container { Layout = HavenLayout.Vertical };
        preview.SetValue(HavenProperties.MinHeight, HavenLength.Px(76));
        preview.SetValue(HavenProperties.Background, "Surface");
        preview.SetValue(HavenProperties.BorderColor, "Accent");
        preview.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        preview.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        preview.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        preview.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        var previewTitle = new HavenText(title) { Level = TextLevel.H4 };
        previewTitle.SetValue(HavenProperties.FontSize, 16d);
        preview.Add(previewTitle);
        var previewDetail = new HavenText(detail) { Level = TextLevel.Caption };
        previewDetail.SetValue(HavenProperties.Foreground, "TextSecondary");
        preview.Add(previewDetail);
        card.Add(preview);
        var use = ActionButton($"Present.Template.{templateId}", title, ButtonVariant.Navigation, () => TemplateRequested?.Invoke(templateId));
        use.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        card.Add(use);
        return card;
    }

    private HavenButton LibraryAction(string name, string content, ButtonVariant variant, Action action)
    {
        var button = ActionButton(name, content, variant, action);
        button.Accessibility.AccessibleName = content.Replace("+  ", string.Empty, StringComparison.Ordinal);
        button.SetValue(HavenProperties.Height, HavenLength.Px(42));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(42));
        button.SetValue(HavenProperties.MaxHeight, HavenLength.Px(42));
        return button;
    }

    private void FillDeckGalleryPolished(Container gallery, IEnumerable<PresentDocumentSummary> documents)
    {
        ClearChildren(gallery);
        gallery.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        var materialized = documents.ToArray();
        if (materialized.Length == 0)
        {
            var empty = new Container { Layout = HavenLayout.Vertical };
            empty.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            empty.SetValue(HavenProperties.MinHeight, HavenLength.Px(92));
            empty.SetValue(HavenProperties.Background, "SurfaceRaised");
            empty.SetValue(HavenProperties.BorderColor, "Border");
            empty.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            empty.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
            empty.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px"));
            empty.SetValue(HavenProperties.Gap, HavenLength.Px(4));
            var isPinned = gallery.Name?.Contains("Pinned", StringComparison.Ordinal) == true;
            empty.Add(new HavenText(isPinned ? "No pinned presentations yet" : "No recent presentations yet") { Level = TextLevel.H4 });
            var hint = new HavenText(isPinned ? "Pin a deck to keep it here." : "Create or import a presentation and it will appear here.") { Level = TextLevel.Caption };
            hint.SetValue(HavenProperties.Foreground, "TextSecondary");
            empty.Add(hint);
            gallery.Add(empty);
            return;
        }

        foreach (var document in materialized)
        {
            var card = new Container { Name = $"Present.Library.Card.{document.Id:N}", Layout = HavenLayout.Vertical };
            card.SetValue(HavenProperties.Width, HavenLength.Px(348));
            card.SetValue(HavenProperties.MinHeight, HavenLength.Px(144));
            card.SetValue(HavenProperties.Background, "SurfaceRaised");
            card.SetValue(HavenProperties.BorderColor, "Border");
            card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
            card.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px"));
            card.SetValue(HavenProperties.Gap, HavenLength.Px(8));
            card.SetValue(HavenProperties.Shadow, "Card");

            var preview = new Container { Layout = HavenLayout.Vertical };
            preview.SetValue(HavenProperties.MinHeight, HavenLength.Px(64));
            preview.SetValue(HavenProperties.Background, "Surface");
            preview.SetValue(HavenProperties.BorderColor, document.Pinned ? "Accent" : "Border");
            preview.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            preview.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
            preview.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 12px"));
            var previewTitle = new HavenText(document.Title) { Level = TextLevel.H4 };
            previewTitle.SetValue(HavenProperties.FontSize, 15d);
            preview.Add(previewTitle);
            var count = new HavenText($"{document.SlideCount} slide{(document.SlideCount == 1 ? string.Empty : "s")}") { Level = TextLevel.Caption };
            count.SetValue(HavenProperties.Foreground, "TextSecondary");
            preview.Add(count);
            card.Add(preview);

            var open = ActionButton($"Present.Library.Open.{document.Id:N}", document.Title, ButtonVariant.Navigation, () => OpenDocumentRequested?.Invoke(document.Id));
            open.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            card.Add(open);
            var meta = new Container { Layout = HavenLayout.Horizontal };
            meta.SetValue(HavenProperties.Gap, HavenLength.Px(8));
            var detail = new HavenText($"Edited {document.UpdatedAt.LocalDateTime:g}") { Level = TextLevel.Caption };
            detail.SetValue(HavenProperties.Foreground, "TextSecondary");
            meta.Add(detail);
            meta.Add(ActionButton($"Present.Library.Pin.{document.Id:N}", document.Pinned ? "Unpin" : "Pin", ButtonVariant.Ghost, () => PinDocumentRequested?.Invoke(document.Id)));
            card.Add(meta);
            gallery.Add(card);
        }
    }

    private void ConfigureFloatingPill(Container pill, double height, double top)
    {
        pill.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        pill.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        pill.SetValue(HavenProperties.Height, HavenLength.Px(height));
        pill.SetValue(HavenProperties.MinHeight, HavenLength.Px(height));
        pill.SetValue(HavenProperties.MaxHeight, HavenLength.Px(height));
        pill.SetValue(HavenProperties.Margin, HavenThickness.Parse($"{top}px 0px 0px 0px"));
        pill.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px 7px"));
        pill.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(height / 2d)));
        foreach (var child in pill.Children)
        {
            child.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
            if (child is HavenButton button)
            {
                button.SetValue(HavenProperties.Height, HavenLength.Px(Math.Max(30, height - 12)));
                button.SetValue(HavenProperties.MinHeight, HavenLength.Px(Math.Max(30, height - 12)));
                button.SetValue(HavenProperties.MaxHeight, HavenLength.Px(Math.Max(30, height - 12)));
            }
        }
    }

    private void PolishSlideRail()
    {
        foreach (var child in SlideRail.Children.OfType<HavenButton>())
        {
            child.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            child.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
            child.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
            child.SetValue(HavenProperties.BorderWidth, HavenLength.Px(0));
            if (child.Name == "Present.Rail.Add")
            {
                child.SetValue(HavenProperties.Height, HavenLength.Px(36));
                child.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
                child.SetValue(HavenProperties.MaxHeight, HavenLength.Px(36));
                continue;
            }
            child.SetValue(HavenProperties.Height, HavenLength.Px(64));
            child.SetValue(HavenProperties.MinHeight, HavenLength.Px(64));
            child.SetValue(HavenProperties.MaxHeight, HavenLength.Px(64));
        }
    }
}
