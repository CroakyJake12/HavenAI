using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Reads and writes the supported WordprocessingML subset used by Haven Write.</summary>
internal static class NotesDocxFormatService
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string OfficeRelationshipBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    public static async Task<NotesDocument> ImportAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("The DOCX package does not contain word/document.xml.");
        var relationships = await ReadRelationshipsAsync(archive, cancellationToken).ConfigureAwait(false);
        var numbering = await ReadNumberingAsync(archive, cancellationToken).ConfigureAwait(false);
        await using var xmlStream = entry.Open();
        var xml = await XDocument.LoadAsync(xmlStream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        var body = xml.Root?.Element(W + "body") ?? throw new InvalidDataException("The DOCX package does not contain a Word document body.");

        var document = NotesDocument.Create(Path.GetFileNameWithoutExtension(path));
        var section = document.Sections[0];
        var page = section.Pages[0];
        page.Blocks.Clear();
        NotesBlock? activeList = null;
        string? activeNumId = null;
        NotesBlock? activeChecklist = null;

        foreach (var element in body.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (element.Name == W + "p")
            {
                var numPr = element.Element(W + "pPr")?.Element(W + "numPr");
                var numId = Val(numPr?.Element(W + "numId"));
                var level = ParseInt(Val(numPr?.Element(W + "ilvl")), 0, 0, 8);
                if (!string.IsNullOrWhiteSpace(numId))
                {
                    activeChecklist = null;
                    var definition = numbering.Resolve(numId, level);
                    if (activeList is null || !string.Equals(activeNumId, numId, StringComparison.Ordinal))
                    {
                        activeList = new NotesBlock
                        {
                            Kind = NotesBlockKind.List,
                            Order = page.Blocks.Count,
                            List = new NotesListData { Kind = definition.Kind, StartNumber = definition.Start }
                        };
                        page.Blocks.Add(activeList);
                        activeNumId = numId;
                    }
                    var text = ParagraphText(element);
                    activeList.List!.Items.Add(new NotesListItem { Text = text, Level = level });
                    continue;
                }

                activeList = null;
                activeNumId = null;
                var plain = ParagraphText(element);
                var checklist = ParseChecklist(plain);
                if (checklist is not null)
                {
                    if (activeChecklist is null)
                    {
                        activeChecklist = new NotesBlock
                        {
                            Kind = NotesBlockKind.List,
                            Order = page.Blocks.Count,
                            List = new NotesListData { Kind = NotesListKind.Checklist }
                        };
                        page.Blocks.Add(activeChecklist);
                    }
                    activeChecklist.List!.Items.Add(checklist);
                    continue;
                }

                activeChecklist = null;
                page.Blocks.Add(ReadParagraph(element, relationships, page.Blocks.Count));
            }
            else if (element.Name == W + "tbl")
            {
                activeList = null;
                activeChecklist = null;
                activeNumId = null;
                page.Blocks.Add(ReadTable(element, page.Blocks.Count));
            }
        }

        if (page.Blocks.Count == 0) page.Blocks.Add(NotesBlock.CreateParagraph());
        ReadSectionProperties(body.Element(W + "sectPr"), document.PageSetup);
        await ReadHeaderFooterAsync(archive, body.Element(W + "sectPr"), relationships, section, document.PageSetup, cancellationToken).ConfigureAwait(false);
        return document;
    }

    public static async Task ExportAsync(NotesDocument document, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var context = new WriteContext();
            var body = new XElement(W + "body");
            foreach (var section in document.Sections)
            {
                if (!string.IsNullOrWhiteSpace(section.Title) && !section.Title.Equals("Section 1", StringComparison.OrdinalIgnoreCase))
                    body.Add(WritePlainParagraph(section.Title, "Heading1", new NotesParagraphFormat(), context));

                foreach (var page in section.Pages)
                foreach (var block in page.Blocks.OrderBy(value => value.Order))
                    WriteBlock(body, block, context);
            }

            var primarySection = document.Sections.FirstOrDefault();
            var sectionProperties = WriteSectionProperties(document.PageSetup, context, primarySection);
            body.Add(sectionProperties);
            var root = new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), new XAttribute(XNamespace.Xmlns + "r", R), body);
            await WriteEntryAsync(archive, "word/document.xml", new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root), cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "word/styles.xml", StylesDocument(), cancellationToken).ConfigureAwait(false);
            if (context.Lists.Count > 0)
                await WriteEntryAsync(archive, "word/numbering.xml", NumberingDocument(context.Lists), cancellationToken).ConfigureAwait(false);
            if (context.Header is not null)
                await WriteEntryAsync(archive, "word/header1.xml", HeaderDocument(context.Header), cancellationToken).ConfigureAwait(false);
            if (context.Footer is not null || document.PageSetup.ShowPageNumbers)
                await WriteEntryAsync(archive, "word/footer1.xml", FooterDocument(context.Footer ?? string.Empty, document.PageSetup.ShowPageNumbers), cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "word/_rels/document.xml.rels", RelationshipsDocument(context, document.PageSetup.ShowPageNumbers), cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "_rels/.rels", PackageRelationshipsDocument(), cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(archive, "[Content_Types].xml", ContentTypesDocument(context, document.PageSetup.ShowPageNumbers), cancellationToken).ConfigureAwait(false);
        }
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(true);
    }

    private static NotesBlock ReadParagraph(XElement paragraph, IReadOnlyDictionary<string, RelationshipInfo> relationships, int order)
    {
        var style = Val(paragraph.Element(W + "pPr")?.Element(W + "pStyle"));
        var (kind, styleId) = MapStyle(style);
        var block = new NotesBlock
        {
            Kind = kind,
            StyleId = styleId,
            Order = order,
            Paragraph = ReadParagraphFormat(paragraph.Element(W + "pPr"))
        };
        if (!string.IsNullOrWhiteSpace(style) && styleId == "normal" && !style.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            block.Metadata["docxParagraphStyle"] = style;

        foreach (var child in paragraph.Elements())
        {
            if (child.Name == W + "r") AddRun(block.Runs, child, null);
            else if (child.Name == W + "hyperlink")
            {
                var relationshipId = (string?)child.Attribute(R + "id");
                var link = relationshipId is not null && relationships.TryGetValue(relationshipId, out var relationship) && relationship.Type.EndsWith("/hyperlink", StringComparison.OrdinalIgnoreCase)
                    ? relationship.Target
                    : null;
                foreach (var run in child.Elements(W + "r")) AddRun(block.Runs, run, link);
            }
        }
        block.PlainText = string.Concat(block.Runs.Select(run => run.Text));
        return block;
    }

    private static void AddRun(List<NotesTextRun> target, XElement runElement, string? link)
    {
        var text = RunText(runElement);
        if (text.Length == 0) return;
        var properties = runElement.Element(W + "rPr");
        var fonts = properties?.Element(W + "rFonts");
        var family = Attr(fonts, "ascii") ?? Attr(fonts, "hAnsi") ?? Attr(fonts, "cs") ?? "Montserrat";
        var sizeHalfPoints = ParseDouble(Val(properties?.Element(W + "sz")), 28);
        var colour = NormaliseWordColour(Val(properties?.Element(W + "color")), "#FFEEEEEE");
        var shading = Attr(properties?.Element(W + "shd"), "fill");
        var highlight = Val(properties?.Element(W + "highlight"));
        var background = !string.IsNullOrWhiteSpace(shading) && !shading.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? NormaliseWordColour(shading, "#00000000")
            : HighlightColour(highlight);
        target.Add(new NotesTextRun
        {
            Text = text,
            FontFamily = family,
            FontSize = Math.Clamp(sizeHalfPoints / 2d, 4, 300),
            Bold = On(properties?.Element(W + "b")),
            Italic = On(properties?.Element(W + "i")),
            Underline = UnderlineOn(properties?.Element(W + "u")),
            StrikeThrough = On(properties?.Element(W + "strike")),
            Foreground = colour,
            Background = background,
            Link = link,
            Language = Attr(properties?.Element(W + "lang"), "val")
        });
    }

    private static NotesParagraphFormat ReadParagraphFormat(XElement? properties)
    {
        var spacing = properties?.Element(W + "spacing");
        var indent = properties?.Element(W + "ind");
        var lineRule = Attr(spacing, "lineRule");
        var line = ParseDouble(Attr(spacing, "line"), 300);
        var first = ParseDouble(Attr(indent, "firstLine"), 0) / 20d;
        var hanging = ParseDouble(Attr(indent, "hanging"), 0) / 20d;
        return new NotesParagraphFormat
        {
            Alignment = ParseAlignment(Val(properties?.Element(W + "jc"))),
            LineSpacing = lineRule is null or "auto" ? Math.Clamp(line / 240d, 0.5, 6) : 1.25,
            SpaceBefore = ParseDouble(Attr(spacing, "before"), 0) / 20d,
            SpaceAfter = ParseDouble(Attr(spacing, "after"), 160) / 20d,
            IndentLeft = ParseDouble(Attr(indent, "left"), 0) / 20d,
            IndentRight = ParseDouble(Attr(indent, "right"), 0) / 20d,
            FirstLineIndent = hanging > 0 ? -hanging : first,
            KeepWithNext = On(properties?.Element(W + "keepNext")),
            PageBreakBefore = On(properties?.Element(W + "pageBreakBefore"))
        };
    }

    private static NotesBlock ReadTable(XElement tableElement, int order)
    {
        var table = new NotesTableData
        {
            Style = Val(tableElement.Element(W + "tblPr")?.Element(W + "tblStyle")) ?? "grid"
        };
        foreach (var rowElement in tableElement.Elements(W + "tr"))
        {
            var row = new NotesTableRow { IsHeader = On(rowElement.Element(W + "trPr")?.Element(W + "tblHeader")) };
            foreach (var cellElement in rowElement.Elements(W + "tc"))
            {
                var text = string.Join(Environment.NewLine, cellElement.Elements(W + "p").Select(ParagraphText));
                var cellProperties = cellElement.Element(W + "tcPr");
                row.Cells.Add(new NotesTableCell
                {
                    Text = text,
                    ColumnSpan = ParseInt(Attr(cellProperties?.Element(W + "gridSpan"), "val"), 1, 1, 50),
                    Background = NormaliseWordColour(Attr(cellProperties?.Element(W + "shd"), "fill"), "#00000000"),
                    VerticalAlignment = ParseVerticalAlignment(Val(cellProperties?.Element(W + "vAlign")))
                });
            }
            if (row.Cells.Count > 0) table.Rows.Add(row);
        }
        if (table.Rows.Count == 0) table = NotesTableData.Create(1, 1);
        table.HeaderRow = table.Rows.FirstOrDefault()?.IsHeader == true;
        return new NotesBlock { Kind = NotesBlockKind.Table, Table = table, Order = order };
    }

    private static void ReadSectionProperties(XElement? section, NotesPageSetup setup)
    {
        if (section is null) return;
        var size = section.Element(W + "pgSz");
        var margins = section.Element(W + "pgMar");
        setup.WidthPoints = Math.Clamp(ParseDouble(Attr(size, "w"), setup.WidthPoints * 20) / 20d, 100, 4000);
        setup.HeightPoints = Math.Clamp(ParseDouble(Attr(size, "h"), setup.HeightPoints * 20) / 20d, 100, 4000);
        setup.Orientation = string.Equals(Attr(size, "orient"), "landscape", StringComparison.OrdinalIgnoreCase) ? "Landscape" : "Portrait";
        setup.MarginTopPoints = Math.Clamp(ParseDouble(Attr(margins, "top"), setup.MarginTopPoints * 20) / 20d, 0, 1000);
        setup.MarginRightPoints = Math.Clamp(ParseDouble(Attr(margins, "right"), setup.MarginRightPoints * 20) / 20d, 0, 1000);
        setup.MarginBottomPoints = Math.Clamp(ParseDouble(Attr(margins, "bottom"), setup.MarginBottomPoints * 20) / 20d, 0, 1000);
        setup.MarginLeftPoints = Math.Clamp(ParseDouble(Attr(margins, "left"), setup.MarginLeftPoints * 20) / 20d, 0, 1000);
    }

    private static async Task ReadHeaderFooterAsync(ZipArchive archive, XElement? sectionProperties, IReadOnlyDictionary<string, RelationshipInfo> relationships, NotesSection section, NotesPageSetup setup, CancellationToken cancellationToken)
    {
        setup.ShowPageNumbers = false;
        if (sectionProperties is null) return;
        var headerId = (string?)sectionProperties.Elements(W + "headerReference").FirstOrDefault()?.Attribute(R + "id");
        var footerId = (string?)sectionProperties.Elements(W + "footerReference").FirstOrDefault()?.Attribute(R + "id");
        if (headerId is not null && relationships.TryGetValue(headerId, out var headerRelationship))
            section.Header = await ReadRelatedPartTextAsync(archive, headerRelationship.Target, cancellationToken).ConfigureAwait(false);
        if (footerId is not null && relationships.TryGetValue(footerId, out var footerRelationship))
        {
            var footerXml = await ReadRelatedPartAsync(archive, footerRelationship.Target, cancellationToken).ConfigureAwait(false);
            if (footerXml is not null)
            {
                setup.ShowPageNumbers = footerXml.Descendants(W + "fldSimple").Any(value => ((string?)value.Attribute(W + "instr"))?.Contains("PAGE", StringComparison.OrdinalIgnoreCase) == true)
                    || footerXml.Descendants(W + "instrText").Any(value => value.Value.Contains("PAGE", StringComparison.OrdinalIgnoreCase));
                section.Footer = LiteralFooterText(footerXml, setup.ShowPageNumbers);
            }
        }

        static string LiteralFooterText(XDocument footer, bool hasPageNumbers)
        {
            var builder = new StringBuilder();
            foreach (var paragraph in footer.Descendants(W + "p"))
            {
                var fieldActive = false;
                var pageField = false;
                var fieldResult = false;
                foreach (var child in paragraph.Elements())
                {
                    if (child.Name == W + "fldSimple")
                    {
                        var instruction = (string?)child.Attribute(W + "instr");
                        if (instruction?.Contains("PAGE", StringComparison.OrdinalIgnoreCase) == true) continue;
                        builder.Append(string.Concat(child.Descendants(W + "t").Select(value => value.Value)));
                        continue;
                    }
                    if (child.Name != W + "r") continue;
                    var fieldCharacter = child.Element(W + "fldChar");
                    var fieldType = Attr(fieldCharacter, "fldCharType");
                    if (fieldType?.Equals("begin", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        fieldActive = true;
                        pageField = false;
                        fieldResult = false;
                    }
                    var instructionText = string.Concat(child.Elements(W + "instrText").Select(value => value.Value));
                    if (fieldActive && instructionText.Contains("PAGE", StringComparison.OrdinalIgnoreCase)) pageField = true;
                    if (fieldType?.Equals("separate", StringComparison.OrdinalIgnoreCase) == true) fieldResult = true;
                    if (!fieldActive || !pageField || !fieldResult) builder.Append(string.Concat(child.Elements(W + "t").Select(value => value.Value)));
                    if (fieldType?.Equals("end", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        fieldActive = false;
                        pageField = false;
                        fieldResult = false;
                    }
                }
            }
            var text = builder.ToString().TrimEnd();
            if (hasPageNumbers && text.EndsWith("·", StringComparison.Ordinal)) text = text[..^1].TrimEnd();
            return text;
        }
    }

    private static void WriteBlock(XElement body, NotesBlock block, WriteContext context)
    {
        switch (block.Kind)
        {
            case NotesBlockKind.List when block.List is not null: WriteList(body, block.List, context); break;
            case NotesBlockKind.Table when block.Table is not null: body.Add(WriteTable(block.Table, context)); break;
            case NotesBlockKind.Divider: body.Add(WritePlainParagraph("────────────────────────", null, new NotesParagraphFormat(), context)); break;
            case NotesBlockKind.Equation when block.Equation is not null: body.Add(WritePlainParagraph(block.Equation.Source, null, block.Paragraph, context)); break;
            case NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video when block.Media is not null: body.Add(WritePlainParagraph($"{block.Kind}: {block.Media.AltText} {block.Media.Caption}".Trim(), null, block.Paragraph, context)); break;
            case NotesBlockKind.Canvas when block.Canvas is not null: body.Add(WritePlainParagraph($"Canvas ({block.Canvas.Objects.Count} objects; editable data remains in the Haven document).", null, block.Paragraph, context)); break;
            case NotesBlockKind.HtmlWidget when block.Html is not null: body.Add(WritePlainParagraph(string.IsNullOrWhiteSpace(block.Html.FallbackText) ? "Interactive HTML content" : block.Html.FallbackText, null, block.Paragraph, context)); break;
            case NotesBlockKind.Flashcard when block.Flashcard is not null: body.Add(WritePlainParagraph("Question: " + block.Flashcard.Front, null, block.Paragraph, context)); body.Add(WritePlainParagraph("Answer: " + block.Flashcard.Back, null, block.Paragraph, context)); break;
            default: body.Add(WriteRichParagraph(block, context)); break;
        }
    }

    private static XElement WriteRichParagraph(NotesBlock block, WriteContext context)
    {
        var style = block.Metadata.TryGetValue("docxParagraphStyle", out var importedStyle) ? importedStyle : WordStyle(block);
        var paragraph = new XElement(W + "p", WriteParagraphProperties(block.Paragraph, style));
        var runs = block.Runs.Count > 0 ? block.Runs : [new NotesTextRun { Text = block.PlainText, Bold = block.Kind == NotesBlockKind.Heading, Italic = block.Kind == NotesBlockKind.Quote, FontFamily = block.Kind == NotesBlockKind.Code ? "Cascadia Mono" : "Montserrat", FontSize = block.Kind == NotesBlockKind.Heading ? 24 : 14 }];
        foreach (var run in runs) paragraph.Add(WriteRunOrHyperlink(run, context));
        return paragraph;
    }

    private static XElement WritePlainParagraph(string text, string? style, NotesParagraphFormat format, WriteContext context)
    {
        var paragraph = new XElement(W + "p", WriteParagraphProperties(format, style));
        paragraph.Add(WriteRunOrHyperlink(new NotesTextRun { Text = text }, context));
        return paragraph;
    }

    private static XElement WriteParagraphProperties(NotesParagraphFormat format, string? style, int? numId = null, int level = 0)
    {
        var properties = new XElement(W + "pPr");
        if (!string.IsNullOrWhiteSpace(style)) properties.Add(new XElement(W + "pStyle", A("val", style)));
        if (numId is not null) properties.Add(new XElement(W + "numPr", new XElement(W + "ilvl", A("val", level)), new XElement(W + "numId", A("val", numId.Value))));
        properties.Add(new XElement(W + "jc", A("val", format.Alignment switch { NotesTextAlignment.Center => "center", NotesTextAlignment.Right => "right", NotesTextAlignment.Justify => "both", _ => "left" })));
        properties.Add(new XElement(W + "spacing", A("before", Twips(format.SpaceBefore)), A("after", Twips(format.SpaceAfter)), A("line", Math.Clamp((int)Math.Round(format.LineSpacing * 240), 120, 1440)), A("lineRule", "auto")));
        var indent = new XElement(W + "ind", A("left", Twips(format.IndentLeft)), A("right", Twips(format.IndentRight)));
        if (format.FirstLineIndent >= 0) indent.Add(A("firstLine", Twips(format.FirstLineIndent))); else indent.Add(A("hanging", Twips(-format.FirstLineIndent)));
        properties.Add(indent);
        if (format.KeepWithNext) properties.Add(new XElement(W + "keepNext"));
        if (format.PageBreakBefore) properties.Add(new XElement(W + "pageBreakBefore"));
        return properties;
    }

    private static object WriteRunOrHyperlink(NotesTextRun run, WriteContext context)
    {
        var element = WriteRun(run);
        if (string.IsNullOrWhiteSpace(run.Link)) return element;
        var relationshipId = context.Hyperlink(run.Link);
        return new XElement(W + "hyperlink", new XAttribute(R + "id", relationshipId), A("history", 1), element);
    }

    private static XElement WriteRun(NotesTextRun run)
    {
        var properties = new XElement(W + "rPr", new XElement(W + "rFonts", A("ascii", SafeFont(run.FontFamily)), A("hAnsi", SafeFont(run.FontFamily)), A("cs", SafeFont(run.FontFamily))), new XElement(W + "sz", A("val", Math.Clamp((int)Math.Round(run.FontSize * 2), 8, 600))), new XElement(W + "szCs", A("val", Math.Clamp((int)Math.Round(run.FontSize * 2), 8, 600))));
        if (run.Bold) properties.Add(new XElement(W + "b")); if (run.Italic) properties.Add(new XElement(W + "i")); if (run.Underline) properties.Add(new XElement(W + "u", A("val", "single"))); if (run.StrikeThrough) properties.Add(new XElement(W + "strike"));
        var foreground = Rgb(run.Foreground); if (foreground is not null) properties.Add(new XElement(W + "color", A("val", foreground)));
        var background = Rgb(run.Background); if (background is not null && !background.Equals("000000", StringComparison.OrdinalIgnoreCase)) properties.Add(new XElement(W + "shd", A("val", "clear"), A("color", "auto"), A("fill", background)));
        if (!string.IsNullOrWhiteSpace(run.Language)) properties.Add(new XElement(W + "lang", A("val", run.Language)));
        var element = new XElement(W + "r", properties); AppendRunContent(element, run.Text); return element;
    }

    private static void AppendRunContent(XElement run, string text)
    {
        var buffer = new StringBuilder();
        void Flush() { if (buffer.Length == 0) return; run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), buffer.ToString())); buffer.Clear(); }
        foreach (var character in text ?? string.Empty) { if (character == '\t') { Flush(); run.Add(new XElement(W + "tab")); } else if (character == '\n') { Flush(); run.Add(new XElement(W + "br")); } else if (character != '\r') buffer.Append(character); }
        Flush(); if (!run.Elements().Any(value => value.Name != W + "rPr")) run.Add(new XElement(W + "t", string.Empty));
    }

    private static void WriteList(XElement body, NotesListData list, WriteContext context)
    {
        if (list.Kind == NotesListKind.Checklist) { foreach (var item in list.Items) { var prefix = new string('\t', Math.Clamp(item.Level, 0, 8)) + (item.Checked ? "☒ " : "☐ "); body.Add(WritePlainParagraph(prefix + item.Text, null, new NotesParagraphFormat(), context)); } return; }
        var numId = context.RegisterList(list.Kind, list.StartNumber); foreach (var item in list.Items) { var paragraph = new XElement(W + "p", WriteParagraphProperties(new NotesParagraphFormat(), null, numId, Math.Clamp(item.Level, 0, 8))); paragraph.Add(WriteRunOrHyperlink(new NotesTextRun { Text = item.Text }, context)); body.Add(paragraph); }
    }

    private static XElement WriteTable(NotesTableData table, WriteContext context)
    {
        var element = new XElement(W + "tbl", new XElement(W + "tblPr", new XElement(W + "tblStyle", A("val", string.IsNullOrWhiteSpace(table.Style) || table.Style.Equals("grid", StringComparison.OrdinalIgnoreCase) ? "TableGrid" : table.Style)), new XElement(W + "tblW", A("w", 0), A("type", "auto"))));
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++) { var row = table.Rows[rowIndex]; var rowElement = new XElement(W + "tr"); if (row.IsHeader || (table.HeaderRow && rowIndex == 0)) rowElement.Add(new XElement(W + "trPr", new XElement(W + "tblHeader"))); foreach (var cell in row.Cells) { var properties = new XElement(W + "tcPr"); if (cell.ColumnSpan > 1) properties.Add(new XElement(W + "gridSpan", A("val", cell.ColumnSpan))); var fill = Rgb(cell.Background); if (fill is not null && !fill.Equals("000000", StringComparison.OrdinalIgnoreCase)) properties.Add(new XElement(W + "shd", A("val", "clear"), A("fill", fill))); properties.Add(new XElement(W + "vAlign", A("val", cell.VerticalAlignment.ToLowerInvariant() switch { "center" => "center", "bottom" => "bottom", _ => "top" }))); var cellElement = new XElement(W + "tc", properties); foreach (var line in (cell.Text ?? string.Empty).ReplaceLineEndings("\n").Split('\n')) cellElement.Add(WritePlainParagraph(line, null, new NotesParagraphFormat(), context)); rowElement.Add(cellElement); } element.Add(rowElement); }
        return element;
    }

    private static XElement WriteSectionProperties(NotesPageSetup setup, WriteContext context, NotesSection? section)
    {
        context.Header = string.IsNullOrWhiteSpace(section?.Header) ? null : section!.Header; context.Footer = string.IsNullOrWhiteSpace(section?.Footer) ? null : section!.Footer; var properties = new XElement(W + "sectPr"); if (context.Header is not null) properties.Add(new XElement(W + "headerReference", A("type", "default"), new XAttribute(R + "id", "rIdHeader"))); if (context.Footer is not null || setup.ShowPageNumbers) properties.Add(new XElement(W + "footerReference", A("type", "default"), new XAttribute(R + "id", "rIdFooter"))); var size = new XElement(W + "pgSz", A("w", Twips(setup.WidthPoints)), A("h", Twips(setup.HeightPoints))); if (setup.Orientation.Equals("Landscape", StringComparison.OrdinalIgnoreCase)) size.Add(A("orient", "landscape")); properties.Add(size); properties.Add(new XElement(W + "pgMar", A("top", Twips(setup.MarginTopPoints)), A("right", Twips(setup.MarginRightPoints)), A("bottom", Twips(setup.MarginBottomPoints)), A("left", Twips(setup.MarginLeftPoints)), A("header", 720), A("footer", 720), A("gutter", 0))); return properties;
    }

    private static XDocument StylesDocument()
    {
        XElement Style(string id, string name, string? basedOn = null, int? size = null, bool bold = false, bool italic = false, string? font = null) { var style = new XElement(W + "style", A("type", "paragraph"), A("styleId", id), new XElement(W + "name", A("val", name))); if (basedOn is not null) style.Add(new XElement(W + "basedOn", A("val", basedOn))); var run = new XElement(W + "rPr"); if (font is not null) run.Add(new XElement(W + "rFonts", A("ascii", font), A("hAnsi", font))); if (size is not null) run.Add(new XElement(W + "sz", A("val", size * 2))); if (bold) run.Add(new XElement(W + "b")); if (italic) run.Add(new XElement(W + "i")); if (run.HasElements) style.Add(run); return style; }
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W), Style("Normal", "Normal", font: "Montserrat", size: 14), Style("Heading1", "heading 1", "Normal", 26, true), Style("Heading2", "heading 2", "Normal", 22, true), Style("Quote", "Quote", "Normal", italic: true), Style("Code", "Code", "Normal", 13, font: "Cascadia Mono")));
    }

    private static XDocument NumberingDocument(IReadOnlyList<ListSpec> lists)
    {
        var root = new XElement(W + "numbering", new XAttribute(XNamespace.Xmlns + "w", W)); foreach (var kind in lists.Select(value => value.Kind).Distinct()) { var abstractId = kind == NotesListKind.Numbered ? 2 : 1; var abstractElement = new XElement(W + "abstractNum", A("abstractNumId", abstractId), new XElement(W + "multiLevelType", A("val", "multilevel"))); for (var level = 0; level < 9; level++) { var bullet = kind == NotesListKind.Bulleted; abstractElement.Add(new XElement(W + "lvl", A("ilvl", level), new XElement(W + "start", A("val", 1)), new XElement(W + "numFmt", A("val", bullet ? "bullet" : "decimal")), new XElement(W + "lvlText", A("val", bullet ? (level % 2 == 0 ? "•" : "◦") : "%" + (level + 1).ToString(CultureInfo.InvariantCulture) + ".")), new XElement(W + "suff", A("val", "tab")), new XElement(W + "pPr", new XElement(W + "tabs", new XElement(W + "tab", A("val", "num"), A("pos", 720 + level * 360))), new XElement(W + "ind", A("left", 720 + level * 360), A("hanging", 360))))); } root.Add(abstractElement); } foreach (var list in lists) { var abstractId = list.Kind == NotesListKind.Numbered ? 2 : 1; var num = new XElement(W + "num", A("numId", list.NumId), new XElement(W + "abstractNumId", A("val", abstractId))); if (list.Start != 1) num.Add(new XElement(W + "lvlOverride", A("ilvl", 0), new XElement(W + "startOverride", A("val", list.Start)))); root.Add(num); } return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
    }

    private static XDocument RelationshipsDocument(WriteContext context, bool pageNumbers)
    {
        var root = new XElement(PackageRels + "Relationships"); root.Add(new XElement(PackageRels + "Relationship", new XAttribute("Id", "rIdStyles"), new XAttribute("Type", OfficeRelationshipBase + "styles"), new XAttribute("Target", "styles.xml"))); if (context.Lists.Count > 0) root.Add(new XElement(PackageRels + "Relationship", new XAttribute("Id", "rIdNumbering"), new XAttribute("Type", OfficeRelationshipBase + "numbering"), new XAttribute("Target", "numbering.xml"))); if (context.Header is not null) root.Add(new XElement(PackageRels + "Relationship", new XAttribute("Id", "rIdHeader"), new XAttribute("Type", OfficeRelationshipBase + "header"), new XAttribute("Target", "header1.xml"))); if (context.Footer is not null || pageNumbers) root.Add(new XElement(PackageRels + "Relationship", new XAttribute("Id", "rIdFooter"), new XAttribute("Type", OfficeRelationshipBase + "footer"), new XAttribute("Target", "footer1.xml"))); foreach (var pair in context.Hyperlinks) root.Add(new XElement(PackageRels + "Relationship", new XAttribute("Id", pair.Value), new XAttribute("Type", OfficeRelationshipBase + "hyperlink"), new XAttribute("Target", pair.Key), new XAttribute("TargetMode", "External"))); return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
    }

    private static XDocument PackageRelationshipsDocument() => new(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(PackageRels + "Relationships", new XElement(PackageRels + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", OfficeRelationshipBase + "officeDocument"), new XAttribute("Target", "word/document.xml"))));
    private static XDocument ContentTypesDocument(WriteContext context, bool pageNumbers) { var root = new XElement(ContentTypes + "Types", new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")), new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")), new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"))); if (context.Lists.Count > 0) root.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/numbering.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"))); if (context.Header is not null) root.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/header1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"))); if (context.Footer is not null || pageNumbers) root.Add(new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/footer1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"))); return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root); }
    private static XDocument HeaderDocument(string text) => new(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(W + "hdr", new XAttribute(XNamespace.Xmlns + "w", W), WritePlainParagraph(text, null, new NotesParagraphFormat(), new WriteContext())));
    private static XDocument FooterDocument(string text, bool pageNumbers) { var paragraph = new XElement(W + "p", WriteParagraphProperties(new NotesParagraphFormat { Alignment = NotesTextAlignment.Center }, null)); if (!string.IsNullOrWhiteSpace(text)) paragraph.Add(WriteRun(new NotesTextRun { Text = text + (pageNumbers ? " · " : string.Empty) })); if (pageNumbers) paragraph.Add(new XElement(W + "fldSimple", A("instr", " PAGE \\* MERGEFORMAT "), WriteRun(new NotesTextRun { Text = "1" }))); return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(W + "ftr", new XAttribute(XNamespace.Xmlns + "w", W), paragraph)); }

    private static async Task<IReadOnlyDictionary<string, RelationshipInfo>> ReadRelationshipsAsync(ZipArchive archive, CancellationToken cancellationToken) { var xml = await ReadPartAsync(archive, "word/_rels/document.xml.rels", cancellationToken).ConfigureAwait(false); if (xml?.Root is null) return new Dictionary<string, RelationshipInfo>(StringComparer.Ordinal); return xml.Root.Elements(PackageRels + "Relationship").Where(value => value.Attribute("Id") is not null).ToDictionary(value => (string)value.Attribute("Id")!, value => new RelationshipInfo((string?)value.Attribute("Type") ?? string.Empty, (string?)value.Attribute("Target") ?? string.Empty), StringComparer.Ordinal); }
    private static async Task<NumberingMap> ReadNumberingAsync(ZipArchive archive, CancellationToken cancellationToken) { var xml = await ReadPartAsync(archive, "word/numbering.xml", cancellationToken).ConfigureAwait(false); if (xml?.Root is null) return NumberingMap.Empty; var abstracts = new Dictionary<string, Dictionary<int, NumberingDefinition>>(StringComparer.Ordinal); foreach (var abstractElement in xml.Root.Elements(W + "abstractNum")) { var id = Attr(abstractElement, "abstractNumId"); if (string.IsNullOrWhiteSpace(id)) continue; var levels = new Dictionary<int, NumberingDefinition>(); foreach (var level in abstractElement.Elements(W + "lvl")) { var index = ParseInt(Attr(level, "ilvl"), 0, 0, 8); var format = Val(level.Element(W + "numFmt")); levels[index] = new NumberingDefinition(format?.Equals("bullet", StringComparison.OrdinalIgnoreCase) == true ? NotesListKind.Bulleted : NotesListKind.Numbered, ParseInt(Val(level.Element(W + "start")), 1, 1, int.MaxValue)); } abstracts[id] = levels; } var map = new Dictionary<string, Dictionary<int, NumberingDefinition>>(StringComparer.Ordinal); foreach (var num in xml.Root.Elements(W + "num")) { var numId = Attr(num, "numId"); var abstractId = Val(num.Element(W + "abstractNumId")); if (string.IsNullOrWhiteSpace(numId) || string.IsNullOrWhiteSpace(abstractId) || !abstracts.TryGetValue(abstractId, out var levels)) continue; var clone = levels.ToDictionary(pair => pair.Key, pair => pair.Value); foreach (var overrideElement in num.Elements(W + "lvlOverride")) { var level = ParseInt(Attr(overrideElement, "ilvl"), 0, 0, 8); var start = ParseInt(Val(overrideElement.Element(W + "startOverride")), clone.TryGetValue(level, out var current) ? current.Start : 1, 1, int.MaxValue); clone[level] = new NumberingDefinition(clone.TryGetValue(level, out current) ? current.Kind : NotesListKind.Numbered, start); } map[numId] = clone; } return new NumberingMap(map); }
    private static async Task<XDocument?> ReadRelatedPartAsync(ZipArchive archive, string target, CancellationToken cancellationToken) { var normalised = target.Replace('\\', '/').TrimStart('/'); if (!normalised.StartsWith("word/", StringComparison.OrdinalIgnoreCase)) normalised = "word/" + normalised; return await ReadPartAsync(archive, normalised, cancellationToken).ConfigureAwait(false); }
    private static async Task<string> ReadRelatedPartTextAsync(ZipArchive archive, string target, CancellationToken cancellationToken) { var xml = await ReadRelatedPartAsync(archive, target, cancellationToken).ConfigureAwait(false); return xml is null ? string.Empty : string.Concat(xml.Descendants(W + "t").Select(value => value.Value)); }
    private static async Task<XDocument?> ReadPartAsync(ZipArchive archive, string name, CancellationToken cancellationToken) { var entry = archive.GetEntry(name); if (entry is null) return null; await using var stream = entry.Open(); return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false); }
    private static async Task WriteEntryAsync(ZipArchive archive, string name, XDocument document, CancellationToken cancellationToken) { var entry = archive.CreateEntry(name, CompressionLevel.Optimal); await using var stream = entry.Open(); await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true); await writer.WriteAsync(document.ToString(SaveOptions.DisableFormatting).AsMemory(), cancellationToken).ConfigureAwait(false); await writer.FlushAsync(cancellationToken).ConfigureAwait(false); }

    private static string ParagraphText(XElement paragraph) => string.Concat(paragraph.Elements().Select(element => element.Name == W + "r" ? RunText(element) : element.Name == W + "hyperlink" ? string.Concat(element.Elements(W + "r").Select(RunText)) : string.Empty));
    private static string RunText(XElement run) { var builder = new StringBuilder(); foreach (var element in run.Elements()) { if (element.Name == W + "t") builder.Append(element.Value); else if (element.Name == W + "tab") builder.Append('\t'); else if (element.Name == W + "br" || element.Name == W + "cr") builder.Append('\n'); } return builder.ToString(); }
    private static NotesListItem? ParseChecklist(string text) { var level = 0; while (level < text.Length && text[level] == '\t') level++; var remaining = text[level..]; if (remaining.StartsWith("☐ ", StringComparison.Ordinal)) return new NotesListItem { Text = remaining[2..], Checked = false, Level = Math.Clamp(level, 0, 8) }; if (remaining.StartsWith("☒ ", StringComparison.Ordinal) || remaining.StartsWith("☑ ", StringComparison.Ordinal)) return new NotesListItem { Text = remaining[2..], Checked = true, Level = Math.Clamp(level, 0, 8) }; return null; }
    private static (NotesBlockKind Kind, string StyleId) MapStyle(string? style) { if (string.IsNullOrWhiteSpace(style)) return (NotesBlockKind.Paragraph, "normal"); var normalised = style.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant(); if (normalised is "heading1" or "title") return (NotesBlockKind.Heading, "heading-1"); if (normalised.StartsWith("heading", StringComparison.Ordinal)) return (NotesBlockKind.Heading, "heading-2"); if (normalised.Contains("quote", StringComparison.Ordinal)) return (NotesBlockKind.Quote, "quote"); if (normalised.Contains("code", StringComparison.Ordinal)) return (NotesBlockKind.Code, "code"); return (NotesBlockKind.Paragraph, "normal"); }
    private static string? WordStyle(NotesBlock block) => block.StyleId switch { "heading-1" => "Heading1", "heading-2" => "Heading2", "quote" => "Quote", "code" => "Code", _ => block.Kind switch { NotesBlockKind.Heading => "Heading1", NotesBlockKind.Quote => "Quote", NotesBlockKind.Code => "Code", _ => "Normal" } };
    private static NotesTextAlignment ParseAlignment(string? value) => value?.ToLowerInvariant() switch { "center" => NotesTextAlignment.Center, "right" => NotesTextAlignment.Right, "both" or "distribute" => NotesTextAlignment.Justify, _ => NotesTextAlignment.Left };
    private static string ParseVerticalAlignment(string? value) => value?.ToLowerInvariant() switch { "center" => "Center", "bottom" => "Bottom", _ => "Top" };
    private static bool On(XElement? element) { if (element is null) return false; var value = Val(element); return value is null || value is not ("0" or "false" or "off" or "none"); }
    private static bool UnderlineOn(XElement? element) => element is not null && !string.Equals(Val(element), "none", StringComparison.OrdinalIgnoreCase) && !string.Equals(Val(element), "0", StringComparison.OrdinalIgnoreCase);
    private static string? Val(XElement? element) => Attr(element, "val");
    private static string? Attr(XElement? element, string localName) => element?.Attribute(W + localName)?.Value ?? element?.Attribute(localName)?.Value;
    private static XAttribute A(string name, object value) => new(W + name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    private static int Twips(double points) => Math.Clamp((int)Math.Round((double.IsFinite(points) ? points : 0) * 20d), 0, 200000);
    private static int ParseInt(string? value, int fallback, int minimum, int maximum) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? Math.Clamp(parsed, minimum, maximum) : fallback;
    private static double ParseDouble(string? value, double fallback) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) ? parsed : fallback;
    private static string SafeFont(string? family) => string.IsNullOrWhiteSpace(family) ? "Montserrat" : family.Trim();
    private static string? Rgb(string? colour) { if (string.IsNullOrWhiteSpace(colour)) return null; var text = colour.Trim().TrimStart('#'); if (text.Length == 8) text = text[2..]; return text.Length == 6 && text.All(Uri.IsHexDigit) ? text.ToUpperInvariant() : null; }
    private static string NormaliseWordColour(string? value, string fallback) { if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase)) return fallback; var rgb = value.Trim().TrimStart('#'); return rgb.Length == 6 && rgb.All(Uri.IsHexDigit) ? "#FF" + rgb.ToUpperInvariant() : fallback; }
    private static string HighlightColour(string? value) => value?.ToLowerInvariant() switch { "yellow" => "#FFFFFF00", "green" => "#FF00FF00", "cyan" => "#FF00FFFF", "magenta" => "#FFFF00FF", "blue" => "#FF0000FF", "red" => "#FFFF0000", "darkblue" => "#FF000080", "darkcyan" => "#FF008080", "darkgreen" => "#FF008000", "darkmagenta" => "#FF800080", "darkred" => "#FF800000", "darkyellow" => "#FF808000", "darkgray" => "#FF808080", "lightgray" => "#FFC0C0C0", "black" => "#FF000000", _ => "#00000000" };

    private sealed record RelationshipInfo(string Type, string Target);
    private sealed record NumberingDefinition(NotesListKind Kind, int Start);
    private sealed class NumberingMap(Dictionary<string, Dictionary<int, NumberingDefinition>> map) { public static NumberingMap Empty { get; } = new(new Dictionary<string, Dictionary<int, NumberingDefinition>>(StringComparer.Ordinal)); public NumberingDefinition Resolve(string numId, int level) { if (map.TryGetValue(numId, out var levels)) { if (levels.TryGetValue(level, out var exact)) return exact; if (levels.TryGetValue(0, out var root)) return root; } return new NumberingDefinition(NotesListKind.Numbered, 1); } }
    private sealed record ListSpec(int NumId, NotesListKind Kind, int Start);
    private sealed class WriteContext { private int _nextNumId = 10; private int _nextHyperlinkId = 100; public List<ListSpec> Lists { get; } = []; public Dictionary<string, string> Hyperlinks { get; } = new(StringComparer.Ordinal); public string? Header { get; set; } public string? Footer { get; set; } public int RegisterList(NotesListKind kind, int start) { var id = _nextNumId++; Lists.Add(new ListSpec(id, kind, Math.Max(1, start))); return id; } public string Hyperlink(string url) { if (Hyperlinks.TryGetValue(url, out var id)) return id; id = "rIdHyperlink" + _nextHyperlinkId++.ToString(CultureInfo.InvariantCulture); Hyperlinks[url] = id; return id; } }
}
