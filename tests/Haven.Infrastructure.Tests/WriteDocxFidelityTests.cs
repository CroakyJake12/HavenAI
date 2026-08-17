using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class WriteDocxFidelityTests : IDisposable
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task Rich_docx_export_emits_word_formatting_and_round_trips_supported_features()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var formats = new NotesImportExportService(new NotesDocumentValidator(), diagnostics);
        var document = NotesDocument.Create("Word fidelity");
        var section = document.Sections[0];
        section.Header = "Haven Write header";
        section.Footer = "Coursework footer";
        var page = section.Pages[0];
        page.Blocks.Clear();

        var paragraph = new NotesBlock
        {
            Kind = NotesBlockKind.Paragraph,
            StyleId = "normal",
            PlainText = "Bold linked text",
            Order = 0,
            Paragraph = new NotesParagraphFormat
            {
                Alignment = NotesTextAlignment.Justify,
                LineSpacing = 1.5,
                SpaceBefore = 6,
                SpaceAfter = 12,
                IndentLeft = 24,
                IndentRight = 18,
                FirstLineIndent = 12,
                KeepWithNext = true,
                PageBreakBefore = true
            },
            Runs =
            [
                new NotesTextRun
                {
                    Text = "Bold ",
                    FontFamily = "Aptos",
                    FontSize = 18,
                    Bold = true,
                    Foreground = "#FF112233",
                    Background = "#FFFFFF00",
                    Language = "en-GB"
                },
                new NotesTextRun
                {
                    Text = "linked text",
                    FontFamily = "Times New Roman",
                    FontSize = 13,
                    Italic = true,
                    Underline = true,
                    StrikeThrough = true,
                    Link = "https://example.test/reference",
                    Foreground = "#FF336699"
                }
            ]
        };
        page.Blocks.Add(paragraph);
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.List,
            Order = 1,
            List = new NotesListData
            {
                Kind = NotesListKind.Numbered,
                StartNumber = 3,
                Items =
                [
                    new NotesListItem { Text = "First numbered item", Level = 0 },
                    new NotesListItem { Text = "Nested numbered item", Level = 1 }
                ]
            }
        });
        var table = NotesTableData.Create(2, 2);
        table.HeaderRow = true;
        table.Rows[0].IsHeader = true;
        table.Rows[0].Cells[0].Text = "Heading";
        table.Rows[0].Cells[0].Background = "#FFCCE5FF";
        table.Rows[0].Cells[0].VerticalAlignment = "Center";
        table.Rows[1].Cells[0].Text = "Body";
        page.Blocks.Add(new NotesBlock { Kind = NotesBlockKind.Table, Order = 2, Table = table });

        document.PageSetup.WidthPoints = 792;
        document.PageSetup.HeightPoints = 612;
        document.PageSetup.Orientation = "Landscape";
        document.PageSetup.MarginTopPoints = 54;
        document.PageSetup.MarginRightPoints = 36;
        document.PageSetup.MarginBottomPoints = 54;
        document.PageSetup.MarginLeftPoints = 36;
        document.PageSetup.ShowPageNumbers = true;

        var path = Path.Combine(_paths.DataDirectory, "word-fidelity.docx");
        await formats.ExportAsync(document, path, CancellationToken.None);

        using (var archive = ZipFile.OpenRead(path))
        {
            Assert.NotNull(archive.GetEntry("word/document.xml"));
            Assert.NotNull(archive.GetEntry("word/styles.xml"));
            Assert.NotNull(archive.GetEntry("word/numbering.xml"));
            Assert.NotNull(archive.GetEntry("word/header1.xml"));
            Assert.NotNull(archive.GetEntry("word/footer1.xml"));
            Assert.NotNull(archive.GetEntry("word/_rels/document.xml.rels"));

            var wordDocument = await ReadXmlAsync(archive.GetEntry("word/document.xml")!);
            var relationships = await ReadXmlAsync(archive.GetEntry("word/_rels/document.xml.rels")!);
            var firstRunProperties = wordDocument.Descendants(W + "rPr").First();
            Assert.NotNull(firstRunProperties.Element(W + "b"));
            Assert.Equal("Aptos", (string?)firstRunProperties.Element(W + "rFonts")?.Attribute(W + "ascii"));
            Assert.Equal("36", (string?)firstRunProperties.Element(W + "sz")?.Attribute(W + "val"));
            Assert.Equal("112233", (string?)firstRunProperties.Element(W + "color")?.Attribute(W + "val"));
            Assert.Equal("FFFF00", (string?)firstRunProperties.Element(W + "shd")?.Attribute(W + "fill"));
            Assert.Contains(wordDocument.Descendants(W + "rPr"), value => value.Element(W + "i") is not null && value.Element(W + "u") is not null && value.Element(W + "strike") is not null);
            Assert.Contains(wordDocument.Descendants(W + "jc"), value => (string?)value.Attribute(W + "val") == "both");
            Assert.Contains(wordDocument.Descendants(W + "pgSz"), value => (string?)value.Attribute(W + "orient") == "landscape");
            Assert.Contains(relationships.Descendants(PackageRels + "Relationship"), value => (string?)value.Attribute("Target") == "https://example.test/reference" && (string?)value.Attribute("TargetMode") == "External");
        }

        var imported = await formats.ImportAsync(path, CancellationToken.None);
        var importedParagraph = imported.Sections[0].Pages[0].Blocks.First(block => block.Kind == NotesBlockKind.Paragraph);
        Assert.Equal(2, importedParagraph.Runs.Count);
        Assert.Equal("Aptos", importedParagraph.Runs[0].FontFamily);
        Assert.Equal(18, importedParagraph.Runs[0].FontSize);
        Assert.True(importedParagraph.Runs[0].Bold);
        Assert.Equal("#FF112233", importedParagraph.Runs[0].Foreground);
        Assert.Equal("#FFFFFF00", importedParagraph.Runs[0].Background);
        Assert.Equal("en-GB", importedParagraph.Runs[0].Language);
        Assert.True(importedParagraph.Runs[1].Italic);
        Assert.True(importedParagraph.Runs[1].Underline);
        Assert.True(importedParagraph.Runs[1].StrikeThrough);
        Assert.Equal("https://example.test/reference", importedParagraph.Runs[1].Link);
        Assert.Equal(NotesTextAlignment.Justify, importedParagraph.Paragraph.Alignment);
        Assert.Equal(1.5, importedParagraph.Paragraph.LineSpacing, 3);
        Assert.Equal(24, importedParagraph.Paragraph.IndentLeft, 3);
        Assert.Equal(12, importedParagraph.Paragraph.FirstLineIndent, 3);
        Assert.True(importedParagraph.Paragraph.KeepWithNext);
        Assert.True(importedParagraph.Paragraph.PageBreakBefore);

        var importedList = imported.Sections[0].Pages[0].Blocks.Single(block => block.List is not null).List!;
        Assert.Equal(NotesListKind.Numbered, importedList.Kind);
        Assert.Equal(3, importedList.StartNumber);
        Assert.Equal(1, importedList.Items[1].Level);
        var importedTable = imported.Sections[0].Pages[0].Blocks.Single(block => block.Table is not null).Table!;
        Assert.True(importedTable.HeaderRow);
        Assert.Equal("#FFCCE5FF", importedTable.Rows[0].Cells[0].Background);
        Assert.Equal("Center", importedTable.Rows[0].Cells[0].VerticalAlignment);
        Assert.Equal(792, imported.PageSetup.WidthPoints, 3);
        Assert.Equal(612, imported.PageSetup.HeightPoints, 3);
        Assert.Equal("Landscape", imported.PageSetup.Orientation);
        Assert.Equal(36, imported.PageSetup.MarginLeftPoints, 3);
        Assert.True(imported.PageSetup.ShowPageNumbers);
        Assert.Equal("Haven Write header", imported.Sections[0].Header);
        Assert.Equal("Coursework footer", imported.Sections[0].Footer);
    }

    [Fact]
    public async Task Docx_import_reads_independent_wordprocessingml_formatting_and_external_link_metadata()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var formats = new NotesImportExportService(new NotesDocumentValidator(), diagnostics);
        var path = Path.Combine(_paths.DataDirectory, "external-word.docx");
        await CreateExternalWordPackageAsync(path);

        var imported = await formats.ImportAsync(path, CancellationToken.None);

        var block = imported.Sections[0].Pages[0].Blocks.First(value => value.Kind == NotesBlockKind.Paragraph);
        Assert.Equal(NotesTextAlignment.Right, block.Paragraph.Alignment);
        Assert.Equal(2, block.Runs.Count);
        Assert.Equal("Calibri", block.Runs[0].FontFamily);
        Assert.Equal(16, block.Runs[0].FontSize);
        Assert.True(block.Runs[0].Bold);
        Assert.True(block.Runs[0].Italic);
        Assert.True(block.Runs[0].Underline);
        Assert.True(block.Runs[0].StrikeThrough);
        Assert.Equal("#FFAA5500", block.Runs[0].Foreground);
        Assert.Equal("#FF00FF00", block.Runs[0].Background);
        Assert.Equal("fr-FR", block.Runs[0].Language);
        Assert.Equal("https://example.test/external", block.Runs[1].Link);
        Assert.Equal("external link", block.Runs[1].Text);
        Assert.Equal("Landscape", imported.PageSetup.Orientation);
        Assert.Equal(720, imported.PageSetup.WidthPoints, 3);
        Assert.Equal(540, imported.PageSetup.HeightPoints, 3);
    }

    private static async Task CreateExternalWordPackageAsync(string path)
    {
        await using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        const string documentXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p>
                  <w:pPr><w:jc w:val="right"/><w:spacing w:before="120" w:after="240" w:line="360" w:lineRule="auto"/><w:ind w:left="360" w:firstLine="180"/></w:pPr>
                  <w:r><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:sz w:val="32"/><w:b/><w:i/><w:u w:val="single"/><w:strike/><w:color w:val="AA5500"/><w:highlight w:val="green"/><w:lang w:val="fr-FR"/></w:rPr><w:t xml:space="preserve">Formatted </w:t></w:r>
                  <w:hyperlink r:id="rId9"><w:r><w:t>external link</w:t></w:r></w:hyperlink>
                </w:p>
                <w:sectPr><w:pgSz w:w="14400" w:h="10800" w:orient="landscape"/><w:pgMar w:top="1080" w:right="720" w:bottom="1080" w:left="720"/></w:sectPr>
              </w:body>
            </w:document>
            """;
        const string relationshipsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId9" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.test/external" TargetMode="External"/>
            </Relationships>
            """;
        await WriteEntryAsync(archive, "word/document.xml", documentXml);
        await WriteEntryAsync(archive, "word/_rels/document.xml.rels", relationshipsXml);
    }

    private static async Task<XDocument> ReadXmlAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        return await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteAsync(content);
        await writer.FlushAsync();
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-write-docx-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
