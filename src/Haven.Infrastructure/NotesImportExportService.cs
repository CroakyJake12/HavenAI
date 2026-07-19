/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/NotesImportExportService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns NotesImportExportService, StringCharacterExtensions, SimpleNotesPdfWriter. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents notes import export service and keeps its related state and behavior together.
/// </summary>
public sealed partial class NotesImportExportService(
    INotesDocumentValidator validator,
    IProductionDiagnostics diagnostics) : INotesImportExportService
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Gets or updates import extensions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<string> ImportExtensions { get; } =
        [".haven-notes.json", ".json", ".txt", ".md", ".markdown", ".html", ".htm", ".csv", ".rtf", ".docx", ".odt"];

    /// <summary>
    /// Gets or updates export extensions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<string> ExportExtensions { get; } =
        [".haven-notes.json", ".txt", ".md", ".html", ".csv", ".rtf", ".docx", ".odt", ".pdf"];

    /// <summary>
    /// Performs import asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesDocument> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("The selected Notes import file does not exist.", sourcePath);
        var extension = EffectiveExtension(sourcePath);
        NotesDocument document = extension switch
        {
            ".haven-notes.json" or ".json" => await ImportNativeAsync(sourcePath, cancellationToken).ConfigureAwait(false),
            ".txt" => NotesFromText(await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false), Path.GetFileNameWithoutExtension(sourcePath), NotesBlockKind.Paragraph),
            ".md" or ".markdown" => NotesFromMarkdown(await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false), Path.GetFileNameWithoutExtension(sourcePath)),
            ".html" or ".htm" => NotesFromHtml(await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false), Path.GetFileNameWithoutExtension(sourcePath)),
            ".csv" => NotesFromCsv(await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false), Path.GetFileNameWithoutExtension(sourcePath)),
            ".rtf" => NotesFromText(DecodeRtf(await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false)), Path.GetFileNameWithoutExtension(sourcePath), NotesBlockKind.Paragraph),
            ".docx" => await ImportDocxAsync(sourcePath, cancellationToken).ConfigureAwait(false),
            ".odt" => await ImportOdtAsync(sourcePath, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Notes cannot import '{extension}'. Supported formats: {string.Join(", ", ImportExtensions)}")
        };
        document.Id = Guid.NewGuid();
        document.Title = string.IsNullOrWhiteSpace(document.Title) ? Path.GetFileNameWithoutExtension(sourcePath) : document.Title;
        document.CreatedAt = DateTimeOffset.UtcNow;
        document.UpdatedAt = document.CreatedAt;
        document.Version = 0;
        document.Revisions.Add(new NotesRevision
        {
            Kind = NotesRevisionKind.Imported,
            Summary = "Imported from " + Path.GetFileName(sourcePath),
            Author = Environment.UserName,
            CreatedAt = DateTimeOffset.UtcNow
        });
        var validation = validator.Validate(document);
        if (!validation.IsValid) throw new InvalidDataException("Imported Notes content failed validation: " + string.Join(" | ", validation.Issues.Where(issue => issue.IsError).Take(10).Select(issue => issue.Path + ": " + issue.Message)));
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Information,
            "notes",
            "document-imported",
            "A file was imported into a new Haven Notes document.",
            new Dictionary<string, string>
            {
                ["format"] = extension,
                ["documentId"] = document.Id.ToString("D")
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return document;
    }

    /// <summary>
    /// Performs export asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> ExportAsync(NotesDocument document, string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("An export destination is required.", nameof(destinationPath));
        var validation = validator.Validate(document);
        if (!validation.IsValid) throw new InvalidDataException("Notes export was blocked because the document is invalid.");
        var extension = EffectiveExtension(destinationPath);
        if (!ExportExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException($"Notes cannot export '{extension}'. Supported formats: {string.Join(", ", ExportExtensions)}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        var temporary = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            switch (extension)
            {
                case ".haven-notes.json":
                case ".json":
                    await WriteJsonAsync(temporary, document, cancellationToken).ConfigureAwait(false);
                    break;
                case ".txt":
                    await WriteTextDurablyAsync(temporary, RenderPlainText(document), cancellationToken).ConfigureAwait(false);
                    break;
                case ".md":
                    await WriteTextDurablyAsync(temporary, RenderMarkdown(document), cancellationToken).ConfigureAwait(false);
                    break;
                case ".html":
                    await WriteTextDurablyAsync(temporary, RenderHtml(document), cancellationToken).ConfigureAwait(false);
                    break;
                case ".csv":
                    await WriteTextDurablyAsync(temporary, RenderCsv(document), cancellationToken).ConfigureAwait(false);
                    break;
                case ".rtf":
                    await WriteTextDurablyAsync(temporary, RenderRtf(document), cancellationToken).ConfigureAwait(false);
                    break;
                case ".docx":
                    await ExportDocxAsync(document, temporary, cancellationToken).ConfigureAwait(false);
                    break;
                case ".odt":
                    await ExportOdtAsync(document, temporary, cancellationToken).ConfigureAwait(false);
                    break;
                case ".pdf":
                    await SimpleNotesPdfWriter.WriteAsync(document, temporary, cancellationToken).ConfigureAwait(false);
                    break;
            }
            ReplaceFile(temporary, destinationPath);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "notes",
                "document-exported",
                "A Haven Notes document was exported.",
                new Dictionary<string, string>
                {
                    ["documentId"] = document.Id.ToString("D"),
                    ["format"] = extension,
                    ["interactiveFallbacks"] = document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Any(block => block.Kind is NotesBlockKind.HtmlWidget or NotesBlockKind.Canvas) ? "true" : "false"
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return destinationPath;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <summary>
    /// Performs print asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task PrintAsync(NotesDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Printing Haven Notes currently requires the Windows desktop host.");
        var directory = Path.Combine(Path.GetTempPath(), "Haven", "Print");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "haven-notes-" + document.Id.ToString("N") + ".pdf");
        await SimpleNotesPdfWriter.WriteAsync(document, path, cancellationToken).ConfigureAwait(false);
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Verb = "print",
                UseShellExecute = true,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Windows did not accept the print request.");
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "notes",
                "print-requested",
                "A print-ready Notes PDF was handed to the Windows print handler.",
                new Dictionary<string, string> { ["documentId"] = document.Id.ToString("D") },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Haven created the print-ready PDF, but Windows could not open a print handler. The file remains at: " + path, ex);
        }
    }

    /// <summary>
    /// Performs import native asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<NotesDocument> ImportNativeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<NotesDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidDataException("The native Notes file was empty.");
    }

    /// <summary>
    /// Performs the notes from text step owned by this component.
    /// </summary>
    private static NotesDocument NotesFromText(string text, string title, NotesBlockKind kind)
    {
        var document = NotesDocument.Create(title);
        var page = document.Sections[0].Pages[0];
        page.Blocks.Clear();
        foreach (var paragraph in SplitParagraphs(text)) page.Blocks.Add(new NotesBlock { Kind = kind, PlainText = paragraph, Order = page.Blocks.Count });
        if (page.Blocks.Count == 0) page.Blocks.Add(NotesBlock.CreateParagraph());
        return document;
    }

    /// <summary>
    /// Performs the notes from markdown step owned by this component.
    /// </summary>
    private static NotesDocument NotesFromMarkdown(string markdown, string title)
    {
        var document = NotesDocument.Create(title);
        var blocks = document.Sections[0].Pages[0].Blocks;
        blocks.Clear();
        var code = new StringBuilder();
        var inCode = false;
        var listBuffer = new List<NotesListItem>();
        foreach (var raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode) { blocks.Add(new NotesBlock { Kind = NotesBlockKind.Code, PlainText = code.ToString().TrimEnd(), StyleId = "code", Order = blocks.Count }); code.Clear(); }
                inCode = !inCode;
                continue;
            }
            if (inCode) { code.AppendLine(raw); continue; }
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                listBuffer.Add(new NotesListItem { Text = line[2..] });
                continue;
            }
            FlushList();
            var hashes = line.TakeWhile(character => character == '#').Count();
            if (hashes > 0 && hashes <= 6 && line.Length > hashes && line[hashes] == ' ')
                blocks.Add(new NotesBlock { Kind = NotesBlockKind.Heading, PlainText = line[(hashes + 1)..], StyleId = hashes == 1 ? "heading-1" : "heading-2", Order = blocks.Count, Metadata = { ["headingLevel"] = hashes.ToString(System.Globalization.CultureInfo.InvariantCulture) } });
            else if (line.StartsWith("> ", StringComparison.Ordinal))
                blocks.Add(new NotesBlock { Kind = NotesBlockKind.Quote, PlainText = line[2..], StyleId = "quote", Order = blocks.Count });
            else if (line.Trim() == "---")
                blocks.Add(new NotesBlock { Kind = NotesBlockKind.Divider, Order = blocks.Count });
            else if (!string.IsNullOrWhiteSpace(line))
                blocks.Add(new NotesBlock { Kind = NotesBlockKind.Paragraph, PlainText = StripMarkdownInline(line), Order = blocks.Count });
        }
        FlushList();
        if (inCode && code.Length > 0) blocks.Add(new NotesBlock { Kind = NotesBlockKind.Code, PlainText = code.ToString(), StyleId = "code", Order = blocks.Count });
        if (blocks.Count == 0) blocks.Add(NotesBlock.CreateParagraph());
        return document;

        void FlushList()
        {
            if (listBuffer.Count == 0) return;
            blocks.Add(new NotesBlock { Kind = NotesBlockKind.List, List = new NotesListData { Kind = NotesListKind.Bulleted, Items = listBuffer.ToList() }, Order = blocks.Count });
            listBuffer.Clear();
        }
    }

    /// <summary>
    /// Performs the notes from html step owned by this component.
    /// </summary>
    private static NotesDocument NotesFromHtml(string html, string title)
    {
        var document = NotesDocument.Create(title);
        var page = document.Sections[0].Pages[0];
        page.Blocks.Clear();
        var fallback = WebUtility.HtmlDecode(TagPattern().Replace(ScriptStylePattern().Replace(html, string.Empty), " "));
        fallback = WhitespacePattern().Replace(fallback, " ").Trim();
        page.Blocks.Add(new NotesBlock
        {
            Kind = NotesBlockKind.HtmlWidget,
            Order = 0,
            Html = new NotesHtmlData
            {
                HtmlSource = html,
                AllowScripts = false,
                AllowNetwork = false,
                AllowForms = false,
                AllowPopups = false,
                FallbackText = fallback
            }
        });
        return document;
    }

    /// <summary>
    /// Performs the notes from csv step owned by this component.
    /// </summary>
    private static NotesDocument NotesFromCsv(string csv, string title)
    {
        var document = NotesDocument.Create(title);
        var table = new NotesTableData();
        foreach (var line in csv.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var row = new NotesTableRow();
            foreach (var value in ParseCsvLine(line)) row.Cells.Add(new NotesTableCell { Text = value });
            if (row.Cells.Count > 0) table.Rows.Add(row);
        }
        if (table.Rows.Count == 0) table = NotesTableData.Create(1, 1);
        var max = table.Rows.Max(row => row.Cells.Count);
        foreach (var row in table.Rows) while (row.Cells.Count < max) row.Cells.Add(new NotesTableCell());
        document.Sections[0].Pages[0].Blocks = [new NotesBlock { Kind = NotesBlockKind.Table, Table = table }];
        return document;
    }

    /// <summary>
    /// Performs import docx asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<NotesDocument> ImportDocxAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("The DOCX package does not contain word/document.xml.");
        await using var xmlStream = entry.Open();
        var xml = await XDocument.LoadAsync(xmlStream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var document = NotesDocument.Create(Path.GetFileNameWithoutExtension(path));
        var blocks = document.Sections[0].Pages[0].Blocks;
        blocks.Clear();
        foreach (var element in xml.Descendants(word + "body").Elements())
        {
            if (element.Name == word + "p")
            {
                var text = string.Concat(element.Descendants(word + "t").Select(value => value.Value));
                var style = element.Descendants(word + "pStyle").Attributes(word + "val").FirstOrDefault()?.Value;
                blocks.Add(new NotesBlock
                {
                    Kind = style?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true ? NotesBlockKind.Heading : NotesBlockKind.Paragraph,
                    PlainText = text,
                    StyleId = style?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true ? "heading-1" : "normal",
                    Order = blocks.Count
                });
            }
            else if (element.Name == word + "tbl")
            {
                var table = new NotesTableData();
                foreach (var rowElement in element.Elements(word + "tr"))
                {
                    var row = new NotesTableRow();
                    foreach (var cellElement in rowElement.Elements(word + "tc"))
                        row.Cells.Add(new NotesTableCell { Text = string.Join("\n", cellElement.Descendants(word + "p").Select(paragraph => string.Concat(paragraph.Descendants(word + "t").Select(value => value.Value)))) });
                    table.Rows.Add(row);
                }
                if (table.Rows.Count > 0) blocks.Add(new NotesBlock { Kind = NotesBlockKind.Table, Table = table, Order = blocks.Count });
            }
        }
        if (blocks.Count == 0) blocks.Add(NotesBlock.CreateParagraph());
        return document;
    }

    /// <summary>
    /// Performs import odt asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<NotesDocument> ImportOdtAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("content.xml") ?? throw new InvalidDataException("The ODT package does not contain content.xml.");
        await using var xmlStream = entry.Open();
        var xml = await XDocument.LoadAsync(xmlStream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        XNamespace textNamespace = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        XNamespace tableNamespace = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        var document = NotesDocument.Create(Path.GetFileNameWithoutExtension(path));
        var blocks = document.Sections[0].Pages[0].Blocks;
        blocks.Clear();
        foreach (var element in xml.Descendants().Where(element => element.Name == textNamespace + "p" || element.Name == textNamespace + "h" || element.Name == tableNamespace + "table"))
        {
            if (element.Name == tableNamespace + "table")
            {
                var table = new NotesTableData();
                foreach (var rowElement in element.Elements(tableNamespace + "table-row"))
                {
                    var row = new NotesTableRow();
                    foreach (var cellElement in rowElement.Elements(tableNamespace + "table-cell")) row.Cells.Add(new NotesTableCell { Text = string.Join("\n", cellElement.Descendants(textNamespace + "p").Select(value => value.Value)) });
                    table.Rows.Add(row);
                }
                if (table.Rows.Count > 0) blocks.Add(new NotesBlock { Kind = NotesBlockKind.Table, Table = table, Order = blocks.Count });
            }
            else
            {
                blocks.Add(new NotesBlock
                {
                    Kind = element.Name == textNamespace + "h" ? NotesBlockKind.Heading : NotesBlockKind.Paragraph,
                    PlainText = element.Value,
                    StyleId = element.Name == textNamespace + "h" ? "heading-1" : "normal",
                    Order = blocks.Count
                });
            }
        }
        if (blocks.Count == 0) blocks.Add(NotesBlock.CreateParagraph());
        return document;
    }

    /// <summary>
    /// Performs the render plain text step owned by this component.
    /// </summary>
    private static string RenderPlainText(NotesDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine(document.Title).AppendLine(new string('=', document.Title.Length)).AppendLine();
        foreach (var section in document.Sections)
        {
            builder.AppendLine(section.Title).AppendLine(new string('-', Math.Max(3, section.Title.Length)));
            if (!string.IsNullOrWhiteSpace(section.Header)) builder.AppendLine("Header: " + section.Header);
            foreach (var page in section.Pages)
            {
                foreach (var block in page.Blocks) RenderBlockText(builder, block);
                builder.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(section.Footer)) builder.AppendLine("Footer: " + section.Footer);
        }
        if (document.Citations.Count > 0)
        {
            builder.AppendLine("References").AppendLine("----------");
            foreach (var citation in document.Citations) builder.AppendLine($"[{citation.Key}] {citation.Authors}. {citation.Title}. {citation.Year}. {citation.Url}".Trim());
        }
        return builder.ToString();
    }

    /// <summary>
    /// Performs the render markdown step owned by this component.
    /// </summary>
    private static string RenderMarkdown(NotesDocument document)
    {
        var builder = new StringBuilder().Append("# ").AppendLine(document.Title).AppendLine();
        foreach (var section in document.Sections)
        {
            builder.Append("## ").AppendLine(section.Title).AppendLine();
            foreach (var page in section.Pages)
            foreach (var block in page.Blocks)
            {
                switch (block.Kind)
                {
                    case NotesBlockKind.Heading: builder.Append("### ").AppendLine(block.PlainText).AppendLine(); break;
                    case NotesBlockKind.Quote: builder.Append("> ").AppendLine(block.PlainText.ReplaceLineEndings("\n> ")).AppendLine(); break;
                    case NotesBlockKind.Code: builder.AppendLine("```").AppendLine(block.PlainText).AppendLine("```").AppendLine(); break;
                    case NotesBlockKind.List when block.List is not null:
                        for (var index = 0; index < block.List.Items.Count; index++)
                        {
                            var item = block.List.Items[index];
                            var marker = block.List.Kind switch { NotesListKind.Numbered => $"{block.List.StartNumber + index}.", NotesListKind.Checklist => item.Checked ? "- [x]" : "- [ ]", _ => "-" };
                            builder.Append(marker).Append(' ').AppendLine(item.Text);
                        }
                        builder.AppendLine();
                        break;
                    case NotesBlockKind.Table when block.Table is not null:
                        RenderMarkdownTable(builder, block.Table);
                        break;
                    case NotesBlockKind.Equation when block.Equation is not null:
                        builder.AppendLine("$$").AppendLine(block.Equation.Source).AppendLine("$$").AppendLine();
                        break;
                    case NotesBlockKind.HtmlWidget when block.Html is not null:
                        builder.AppendLine(block.Html.HtmlSource).AppendLine();
                        break;
                    case NotesBlockKind.Flashcard when block.Flashcard is not null:
                        builder.AppendLine("> [!FLASHCARD]").Append("> **Question:** ").AppendLine(block.Flashcard.Front).Append("> **Answer:** ").AppendLine(block.Flashcard.Back).AppendLine();
                        break;
                    case NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video when block.Media is not null:
                        builder.Append("[").Append(block.Media.AltText).Append("](").Append(block.Media.StoredPath).AppendLine(")").AppendLine();
                        break;
                    case NotesBlockKind.Divider: builder.AppendLine("---").AppendLine(); break;
                    default: builder.AppendLine(block.PlainText).AppendLine(); break;
                }
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Performs the render html step owned by this component.
    /// </summary>
    private static string RenderHtml(NotesDocument document)
    {
        var builder = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>")
            .Append(WebUtility.HtmlEncode(document.Title))
            .Append("</title><style>body{font-family:system-ui;max-width:900px;margin:40px auto;line-height:1.5;padding:0 20px}table{border-collapse:collapse;width:100%}td,th{border:1px solid #888;padding:6px}pre{overflow:auto;padding:12px;background:#eee}.flashcard{border:1px solid #888;border-radius:8px;padding:12px;margin:12px 0}.media-fallback,.canvas-fallback{border-left:4px solid #888;padding:8px;color:#555}</style></head><body><h1>")
            .Append(WebUtility.HtmlEncode(document.Title)).AppendLine("</h1>");
        foreach (var section in document.Sections)
        {
            builder.Append("<section><h2>").Append(WebUtility.HtmlEncode(section.Title)).AppendLine("</h2>");
            foreach (var page in section.Pages)
            foreach (var block in page.Blocks) RenderBlockHtml(builder, block);
            builder.AppendLine("</section>");
        }
        if (document.Citations.Count > 0)
        {
            builder.AppendLine("<section><h2>References</h2><ol>");
            foreach (var citation in document.Citations) builder.Append("<li id=\"cite-").Append(WebUtility.HtmlEncode(citation.Key)).Append("\">").Append(WebUtility.HtmlEncode($"{citation.Authors}. {citation.Title}. {citation.Year}. {citation.Url}".Trim())).AppendLine("</li>");
            builder.AppendLine("</ol></section>");
        }
        return builder.AppendLine("</body></html>").ToString();
    }

    /// <summary>
    /// Performs the render csv step owned by this component.
    /// </summary>
    private static string RenderCsv(NotesDocument document)
    {
        var tables = document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Where(block => block.Table is not null).ToArray();
        if (tables.Length == 0) throw new InvalidOperationException("CSV export requires at least one table block. No file was written.");
        var builder = new StringBuilder();
        for (var tableIndex = 0; tableIndex < tables.Length; tableIndex++)
        {
            if (tableIndex > 0) builder.AppendLine().Append("# Table ").AppendLine((tableIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var row in tables[tableIndex].Table!.Rows)
                builder.AppendLine(string.Join(",", row.Cells.Select(cell => CsvEscape(cell.Text))));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Performs the render rtf step owned by this component.
    /// </summary>
    private static string RenderRtf(NotesDocument document)
    {
        var text = RenderPlainText(document);
        var escaped = text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("{", "\\{", StringComparison.Ordinal).Replace("}", "\\}", StringComparison.Ordinal)
            .ReplaceLineEndings("\\par\n");
        return "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Segoe UI;}}\\fs24 " + escaped + "}";
    }

    /// <summary>
    /// Performs export docx asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task ExportDocxAsync(NotesDocument document, string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        await WriteZipEntryAsync(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>", cancellationToken).ConfigureAwait(false);
        await WriteZipEntryAsync(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>", cancellationToken).ConfigureAwait(false);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var body = new XElement(word + "body");
        foreach (var section in document.Sections)
        {
            body.Add(WordParagraph(word, section.Title, "Heading1"));
            foreach (var page in section.Pages)
            foreach (var block in page.Blocks)
            {
                if (block.Table is not null) body.Add(WordTable(word, block.Table));
                else foreach (var line in BlockLines(block)) body.Add(WordParagraph(word, line, block.Kind == NotesBlockKind.Heading ? "Heading2" : null));
            }
        }
        body.Add(new XElement(word + "sectPr"));
        var xml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(word + "document", new XAttribute(XNamespace.Xmlns + "w", word), body));
        await WriteZipEntryAsync(archive, "word/document.xml", xml.ToString(SaveOptions.DisableFormatting), cancellationToken).ConfigureAwait(false);
        archive.Dispose();
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(true);
    }

    /// <summary>
    /// Performs export odt asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task ExportOdtAsync(NotesDocument document, string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var mime = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        await using (var mimeStream = mime.Open()) await mimeStream.WriteAsync(Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.text"), cancellationToken).ConfigureAwait(false);
        XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        XNamespace tableNamespace = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        var content = new XElement(office + "text");
        foreach (var section in document.Sections)
        {
            content.Add(new XElement(text + "h", new XAttribute(text + "outline-level", "1"), section.Title));
            foreach (var page in section.Pages)
            foreach (var block in page.Blocks)
            {
                if (block.Table is not null)
                {
                    var table = new XElement(tableNamespace + "table", new XAttribute(tableNamespace + "name", "Table" + block.Id.ToString("N")));
                    foreach (var row in block.Table.Rows)
                    {
                        var rowElement = new XElement(tableNamespace + "table-row");
                        foreach (var cell in row.Cells) rowElement.Add(new XElement(tableNamespace + "table-cell", new XAttribute(office + "value-type", "string"), new XElement(text + "p", cell.Text)));
                        table.Add(rowElement);
                    }
                    content.Add(table);
                }
                else foreach (var line in BlockLines(block)) content.Add(block.Kind == NotesBlockKind.Heading ? new XElement(text + "h", new XAttribute(text + "outline-level", "2"), line) : new XElement(text + "p", line));
            }
        }
        var xml = new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement(office + "document-content", new XAttribute(XNamespace.Xmlns + "office", office), new XAttribute(XNamespace.Xmlns + "text", text), new XAttribute(XNamespace.Xmlns + "table", tableNamespace), new XAttribute(office + "version", "1.3"), new XElement(office + "body", content)));
        await WriteZipEntryAsync(archive, "content.xml", xml.ToString(SaveOptions.DisableFormatting), cancellationToken).ConfigureAwait(false);
        await WriteZipEntryAsync(archive, "META-INF/manifest.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.3\"><manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/><manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/></manifest:manifest>", cancellationToken).ConfigureAwait(false);
        archive.Dispose();
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(true);
    }

    /// <summary>
    /// Performs the word paragraph step owned by this component.
    /// </summary>
    private static XElement WordParagraph(XNamespace word, string text, string? style)
    {
        var paragraph = new XElement(word + "p");
        if (!string.IsNullOrWhiteSpace(style)) paragraph.Add(new XElement(word + "pPr", new XElement(word + "pStyle", new XAttribute(word + "val", style))));
        paragraph.Add(new XElement(word + "r", new XElement(word + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
        return paragraph;
    }

    /// <summary>
    /// Performs the word table step owned by this component.
    /// </summary>
    private static XElement WordTable(XNamespace word, NotesTableData table)
    {
        var element = new XElement(word + "tbl");
        foreach (var row in table.Rows)
        {
            var rowElement = new XElement(word + "tr");
            foreach (var cell in row.Cells) rowElement.Add(new XElement(word + "tc", WordParagraph(word, cell.Text, null)));
            element.Add(rowElement);
        }
        return element;
    }

    /// <summary>
    /// Performs write zip entry asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task WriteZipEntryAsync(ZipArchive archive, string name, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the render block text step owned by this component.
    /// </summary>
    private static void RenderBlockText(StringBuilder builder, NotesBlock block)
    {
        foreach (var line in BlockLines(block)) builder.AppendLine(line);
        builder.AppendLine();
    }

    /// <summary>
    /// Performs the block lines step owned by this component.
    /// </summary>
    private static IEnumerable<string> BlockLines(NotesBlock block)
    {
        switch (block.Kind)
        {
            case NotesBlockKind.List when block.List is not null:
                for (var index = 0; index < block.List.Items.Count; index++)
                {
                    var item = block.List.Items[index];
                    yield return block.List.Kind switch { NotesListKind.Numbered => $"{block.List.StartNumber + index}. {item.Text}", NotesListKind.Checklist => $"[{(item.Checked ? 'x' : ' ')}] {item.Text}", _ => "• " + item.Text };
                }
                break;
            case NotesBlockKind.Table when block.Table is not null:
                foreach (var row in block.Table.Rows) yield return string.Join(" | ", row.Cells.Select(cell => cell.Text));
                break;
            case NotesBlockKind.Equation when block.Equation is not null:
                yield return "Equation: " + block.Equation.Source;
                if (!string.IsNullOrWhiteSpace(block.Equation.AccessibleAlternative)) yield return "Accessible description: " + block.Equation.AccessibleAlternative;
                break;
            case NotesBlockKind.HtmlWidget when block.Html is not null:
                yield return "Interactive HTML widget fallback: " + (string.IsNullOrWhiteSpace(block.Html.FallbackText) ? WebUtility.HtmlDecode(TagPattern().Replace(block.Html.HtmlSource, " ")) : block.Html.FallbackText);
                break;
            case NotesBlockKind.Canvas when block.Canvas is not null:
                yield return $"Canvas ({block.Canvas.Objects.Count} objects, {block.Canvas.Strokes.Count} editable ink strokes, {block.Canvas.GhostLayers.Count} ghost layers).";
                foreach (var canvasObject in block.Canvas.Objects.Where(item => !string.IsNullOrWhiteSpace(item.Text))) yield return canvasObject.Text;
                break;
            case NotesBlockKind.Flashcard when block.Flashcard is not null:
                yield return "Flashcard question: " + block.Flashcard.Front;
                yield return "Flashcard answer: " + block.Flashcard.Back;
                break;
            case NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video when block.Media is not null:
                yield return $"{block.Kind} fallback: {block.Media.AltText}. {block.Media.Caption}".Trim();
                break;
            case NotesBlockKind.Divider:
                yield return "----------------";
                break;
            default:
                yield return block.PlainText;
                break;
        }
    }

    /// <summary>
    /// Performs the render block html step owned by this component.
    /// </summary>
    private static void RenderBlockHtml(StringBuilder builder, NotesBlock block)
    {
        switch (block.Kind)
        {
            case NotesBlockKind.Heading: builder.Append("<h3>").Append(WebUtility.HtmlEncode(block.PlainText)).AppendLine("</h3>"); break;
            case NotesBlockKind.Quote: builder.Append("<blockquote>").Append(WebUtility.HtmlEncode(block.PlainText)).AppendLine("</blockquote>"); break;
            case NotesBlockKind.Code: builder.Append("<pre><code>").Append(WebUtility.HtmlEncode(block.PlainText)).AppendLine("</code></pre>"); break;
            case NotesBlockKind.List when block.List is not null:
                builder.AppendLine(block.List.Kind == NotesListKind.Numbered ? "<ol>" : "<ul>");
                foreach (var item in block.List.Items) builder.Append("<li>").Append(block.List.Kind == NotesListKind.Checklist ? (item.Checked ? "☑ " : "☐ ") : string.Empty).Append(WebUtility.HtmlEncode(item.Text)).AppendLine("</li>");
                builder.AppendLine(block.List.Kind == NotesListKind.Numbered ? "</ol>" : "</ul>");
                break;
            case NotesBlockKind.Table when block.Table is not null:
                builder.AppendLine("<table>");
                foreach (var row in block.Table.Rows)
                {
                    builder.AppendLine("<tr>");
                    foreach (var cell in row.Cells) builder.Append("<td>").Append(WebUtility.HtmlEncode(cell.Text)).AppendLine("</td>");
                    builder.AppendLine("</tr>");
                }
                builder.AppendLine("</table>");
                break;
            case NotesBlockKind.Equation when block.Equation is not null:
                builder.Append("<figure><pre>").Append(WebUtility.HtmlEncode(block.Equation.Source)).Append("</pre><figcaption>").Append(WebUtility.HtmlEncode(block.Equation.AccessibleAlternative)).AppendLine("</figcaption></figure>");
                break;
            case NotesBlockKind.HtmlWidget when block.Html is not null:
                builder.Append("<div class=\"media-fallback\"><strong>Interactive widget fallback:</strong> ").Append(WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(block.Html.FallbackText) ? TagPattern().Replace(block.Html.HtmlSource, " ") : block.Html.FallbackText)).AppendLine("</div>");
                break;
            case NotesBlockKind.Canvas when block.Canvas is not null:
                builder.Append("<div class=\"canvas-fallback\">Canvas with ").Append(block.Canvas.Objects.Count).Append(" objects and ").Append(block.Canvas.Strokes.Count).AppendLine(" editable ink strokes. Interactive canvas data remains in the native file.</div>");
                break;
            case NotesBlockKind.Flashcard when block.Flashcard is not null:
                builder.Append("<div class=\"flashcard\"><strong>Question:</strong> ").Append(WebUtility.HtmlEncode(block.Flashcard.Front)).Append("<hr><strong>Answer:</strong> ").Append(WebUtility.HtmlEncode(block.Flashcard.Back)).AppendLine("</div>");
                break;
            case NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video when block.Media is not null:
                builder.Append("<div class=\"media-fallback\"><strong>").Append(block.Kind).Append(":</strong> ").Append(WebUtility.HtmlEncode(block.Media.AltText)).Append(" ").Append(WebUtility.HtmlEncode(block.Media.Caption)).AppendLine("</div>");
                break;
            case NotesBlockKind.Divider: builder.AppendLine("<hr>"); break;
            default: builder.Append("<p>").Append(WebUtility.HtmlEncode(block.PlainText).Replace("\n", "<br>", StringComparison.Ordinal)).AppendLine("</p>"); break;
        }
    }

    /// <summary>
    /// Performs the render markdown table step owned by this component.
    /// </summary>
    private static void RenderMarkdownTable(StringBuilder builder, NotesTableData table)
    {
        var columns = table.Rows.Max(row => row.Cells.Count);
        var rows = table.Rows.Select(row => row.Cells.Select(cell => cell.Text.Replace("|", "\\|", StringComparison.Ordinal)).Concat(Enumerable.Repeat(string.Empty, columns - row.Cells.Count)).ToArray()).ToArray();
        builder.Append("| ").Append(string.Join(" | ", rows[0])).AppendLine(" |");
        builder.Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", columns))).AppendLine(" |");
        foreach (var row in rows.Skip(1)) builder.Append("| ").Append(string.Join(" | ", row)).AppendLine(" |");
        builder.AppendLine();
    }

    /// <summary>
    /// Performs the split paragraphs step owned by this component.
    /// </summary>
    private static IEnumerable<string> SplitParagraphs(string text) => text.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    /// <summary>
    /// Performs the strip markdown inline step owned by this component.
    /// </summary>
    private static string StripMarkdownInline(string value) => InlineMarkdownPattern().Replace(value, "$1");
    /// <summary>
    /// Performs the decode rtf step owned by this component.
    /// </summary>
    private static string DecodeRtf(string rtf) => WhitespacePattern().Replace(RtfControlPattern().Replace(rtf.Replace("\\par", "\n", StringComparison.OrdinalIgnoreCase).Replace("\\tab", "\t", StringComparison.OrdinalIgnoreCase), " ").Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal), " ").Trim();

    /// <summary>
    /// Performs the parse csv line step owned by this component.
    /// </summary>
    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"') { builder.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted) { values.Add(builder.ToString()); builder.Clear(); }
            else builder.Append(character);
        }
        values.Add(builder.ToString());
        return values;
    }

    /// <summary>
    /// Performs the csv escape step owned by this component.
    /// </summary>
    private static string CsvEscape(string value) => value.ContainsAny(',', '"', '\n', '\r') ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    /// <summary>
    /// Performs the effective extension step owned by this component.
    /// </summary>
    private static string EffectiveExtension(string path) => path.EndsWith(".haven-notes.json", StringComparison.OrdinalIgnoreCase) ? ".haven-notes.json" : Path.GetExtension(path).ToLowerInvariant();

    /// <summary>
    /// Performs write json asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task WriteJsonAsync(string path, NotesDocument document, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(true);
    }

    /// <summary>
    /// Performs write text durably asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task WriteTextDurablyAsync(string path, string content, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024, leaveOpen: true);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(true);
    }

    /// <summary>
    /// Performs the replace file step owned by this component.
    /// </summary>
    private static void ReplaceFile(string temporary, string destination)
    {
        if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".bak", ignoreMetadataErrors: true);
        else File.Move(temporary, destination);
    }

    /// <summary>
    /// Attempts to delete and reports the result without using failure for normal control flow.
    /// </summary>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Performs the script style pattern step owned by this component.
    /// </summary>
    [GeneratedRegex("<script\\b[^>]*>[\\s\\S]*?</script>|<style\\b[^>]*>[\\s\\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptStylePattern();
    /// <summary>
    /// Performs the tag pattern step owned by this component.
    /// </summary>
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
    /// <summary>
    /// Performs the whitespace pattern step owned by this component.
    /// </summary>
    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespacePattern();
    /// <summary>
    /// Performs the inline markdown pattern step owned by this component.
    /// </summary>
    [GeneratedRegex("(?:\\*\\*|__|\\*|_|~~|`)(.*?)(?:\\*\\*|__|\\*|_|~~|`)")]
    private static partial Regex InlineMarkdownPattern();
    /// <summary>
    /// Performs the rtf control pattern step owned by this component.
    /// </summary>
    [GeneratedRegex("\\\\[a-zA-Z]+-?\\d* ?|\\\\'[0-9a-fA-F]{2}")]
    private static partial Regex RtfControlPattern();
}

/// <summary>
/// Represents string character extensions and keeps its related state and behavior together.
/// </summary>
internal static class StringCharacterExtensions
{
    /// <summary>
    /// Performs the contains any step owned by this component.
    /// </summary>
    public static bool ContainsAny(this string value, params char[] characters) => value.IndexOfAny(characters) >= 0;
}

/// <summary>
/// Represents simple notes pdf writer and keeps its related state and behavior together.
/// </summary>
internal static class SimpleNotesPdfWriter
{
    /// <summary>
    /// Performs write asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static async Task WriteAsync(NotesDocument document, string path, CancellationToken cancellationToken)
    {
        var lines = WrapLines(NotesDocumentText(document), 92).ToArray();
        var pages = lines.Chunk(48).ToArray();
        if (pages.Length == 0) pages = [[document.Title]];
        var objects = new List<byte[]>();
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        var pageObjectNumbers = Enumerable.Range(0, pages.Length).Select(index => 4 + index * 2).ToArray();
        objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [{string.Join(' ', pageObjectNumbers.Select(number => number + " 0 R"))}] /Count {pages.Length} >>"));
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
        for (var index = 0; index < pages.Length; index++)
        {
            var pageNumber = pageObjectNumbers[index];
            var contentNumber = pageNumber + 1;
            objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentNumber} 0 R >>"));
            var content = new StringBuilder("BT /F1 10 Tf 48 790 Td 13 TL\n");
            foreach (var line in pages[index]) content.Append('(').Append(EscapePdf(line)).AppendLine(") Tj T*");
            content.Append("ET");
            // PDF's built-in Helvetica uses a single-byte WinAnsi-like encoding.
            // Latin-1 is always available in modern .NET and replaces unsupported
            // characters without requiring the optional code-pages provider.
            var contentBytes = Encoding.Latin1.GetBytes(content.ToString());
            var header = Encoding.ASCII.GetBytes($"<< /Length {contentBytes.Length} >>\nstream\n");
            var footer = Encoding.ASCII.GetBytes("\nendstream");
            objects.Add(header.Concat(contentBytes).Concat(footer).ToArray());
        }

        await using var stream = new FileStream(path, File.Exists(path) ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        var headerBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n%HavenNotes\n");
        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(stream.Position);
            var prefix = Encoding.ASCII.GetBytes($"{index + 1} 0 obj\n");
            await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(objects[index], cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("\nendobj\n"), cancellationToken).ConfigureAwait(false);
        }
        var xref = stream.Position;
        var xrefBuilder = new StringBuilder($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) xrefBuilder.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" 00000 n ");
        xrefBuilder.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(xrefBuilder.ToString()), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(true);
    }

    /// <summary>
    /// Performs the notes document text step owned by this component.
    /// </summary>
    private static string NotesDocumentText(NotesDocument document)
    {
        var builder = new StringBuilder().AppendLine(document.Title).AppendLine();
        foreach (var section in document.Sections)
        {
            builder.AppendLine(section.Title.ToUpperInvariant()).AppendLine();
            foreach (var page in section.Pages)
            foreach (var block in page.Blocks)
            {
                foreach (var line in BlockLines(block)) builder.AppendLine(line);
                builder.AppendLine();
            }
        }
        if (document.Citations.Count > 0)
        {
            builder.AppendLine("REFERENCES");
            foreach (var citation in document.Citations) builder.AppendLine($"[{citation.Key}] {citation.Authors}. {citation.Title}. {citation.Year}. {citation.Url}");
        }
        return builder.ToString();
    }

    /// <summary>
    /// Performs the block lines step owned by this component.
    /// </summary>
    private static IEnumerable<string> BlockLines(NotesBlock block)
    {
        if (block.Table is not null) return block.Table.Rows.Select(row => string.Join(" | ", row.Cells.Select(cell => cell.Text)));
        if (block.List is not null) return block.List.Items.Select((item, index) => block.List.Kind == NotesListKind.Numbered ? $"{index + block.List.StartNumber}. {item.Text}" : "- " + item.Text);
        if (block.Equation is not null) return ["Equation: " + block.Equation.Source, block.Equation.AccessibleAlternative];
        if (block.Html is not null) return ["Interactive HTML fallback: " + (string.IsNullOrWhiteSpace(block.Html.FallbackText) ? "Content preserved in native Notes file." : block.Html.FallbackText)];
        if (block.Canvas is not null) return [$"Canvas: {block.Canvas.Objects.Count} objects, {block.Canvas.Strokes.Count} strokes. Editable data preserved in native Notes file."];
        if (block.Flashcard is not null) return ["Flashcard question: " + block.Flashcard.Front, "Flashcard answer: " + block.Flashcard.Back];
        if (block.Media is not null) return [$"{block.Kind}: {block.Media.AltText}. {block.Media.Caption}"];
        return [block.PlainText];
    }

    /// <summary>
    /// Performs the wrap lines step owned by this component.
    /// </summary>
    private static IEnumerable<string> WrapLines(string text, int width)
    {
        foreach (var paragraph in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (paragraph.Length == 0) { yield return string.Empty; continue; }
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = new StringBuilder();
            foreach (var word in words)
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > width) { yield return line.ToString(); line.Clear(); }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            if (line.Length > 0) yield return line.ToString();
        }
    }

    /// <summary>
    /// Performs the escape pdf step owned by this component.
    /// </summary>
    private static string EscapePdf(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal);
}
