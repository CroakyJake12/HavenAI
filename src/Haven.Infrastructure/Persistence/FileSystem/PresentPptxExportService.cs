using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class PresentPptxExportService : IPresentExportService
{
    private const long SlideWidth = 12_192_000;
    private const long SlideHeight = 6_858_000;

    public IReadOnlyList<string> ExportExtensions { get; } = [".pptx"];

    public async Task<string> ExportAsync(
        PresentDocument document,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.GetExtension(destinationPath).Equals(".pptx", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Present currently exports conventional presentations as .pptx files.");

        document.Normalize();
        var fullDestination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestination);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var temporary = fullDestination + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await WritePackageAsync(document, temporary, cancellationToken).ConfigureAwait(false);
            ValidatePackage(temporary, document.Slides.Count);
            File.Move(temporary, fullDestination, overwrite: true);
            return fullDestination;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static async Task WritePackageAsync(
        PresentDocument document,
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        await WriteEntryAsync(archive, "[Content_Types].xml", BuildContentTypes(document.Slides.Count), cancellationToken);
        await WriteEntryAsync(archive, "_rels/.rels", RootRelationships(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/presentation.xml", BuildPresentation(document.Slides.Count), cancellationToken);
        await WriteEntryAsync(archive, "ppt/_rels/presentation.xml.rels", BuildPresentationRelationships(document.Slides.Count), cancellationToken);
        await WriteEntryAsync(archive, "ppt/slideMasters/slideMaster1.xml", SlideMaster(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/slideMasters/_rels/slideMaster1.xml.rels", SlideMasterRelationships(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/slideLayouts/slideLayout1.xml", SlideLayout(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", SlideLayoutRelationships(), cancellationToken);
        await WriteEntryAsync(archive, "ppt/theme/theme1.xml", Theme(), cancellationToken);

        for (var index = 0; index < document.Slides.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var number = index + 1;
            await WriteEntryAsync(archive, $"ppt/slides/slide{number}.xml", BuildSlide(document.Slides[index]), cancellationToken);
            await WriteEntryAsync(archive, $"ppt/slides/_rels/slide{number}.xml.rels", SlideRelationships(), cancellationToken);
        }

        archive.Dispose();
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildContentTypes(int slideCount)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        builder.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        builder.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        builder.Append("<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>");
        builder.Append("<Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/>");
        builder.Append("<Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/>");
        builder.Append("<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>");
        for (var index = 1; index <= slideCount; index++)
            builder.Append($"<Override PartName=\"/ppt/slides/slide{index}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>");
        builder.Append("</Types>");
        return builder.ToString();
    }

    private static string RootRelationships() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"ppt/presentation.xml\"/>" +
        "</Relationships>";

    private static string BuildPresentation(int slideCount)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<p:presentation xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">");
        builder.Append("<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst><p:sldIdLst>");
        for (var index = 0; index < slideCount; index++)
            builder.Append($"<p:sldId id=\"{256 + index}\" r:id=\"rId{index + 2}\"/>");
        builder.Append("</p:sldIdLst>");
        builder.Append($"<p:sldSz cx=\"{SlideWidth}\" cy=\"{SlideHeight}\" type=\"screen16x9\"/>");
        builder.Append("<p:notesSz cx=\"6858000\" cy=\"9144000\"/><p:defaultTextStyle><a:defPPr/></p:defaultTextStyle></p:presentation>");
        return builder.ToString();
    }

    private static string BuildPresentationRelationships(int slideCount)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        builder.Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/>");
        for (var index = 0; index < slideCount; index++)
            builder.Append($"<Relationship Id=\"rId{index + 2}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide{index + 1}.xml\"/>");
        builder.Append("</Relationships>");
        return builder.ToString();
    }

    private static string SlideMaster() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sldMaster xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld name=""><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr></p:spTree></p:cSld><p:clrMap accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" bg1="lt1" bg2="lt2" folHlink="folHlink" hlink="hlink" tx1="dk1" tx2="dk2"/><p:sldLayoutIdLst><p:sldLayoutId id="1" r:id="rId1"/></p:sldLayoutIdLst><p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>
""";

    private static string SlideMasterRelationships() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="../theme/theme1.xml"/></Relationships>
""";

    private static string SlideLayout() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sldLayout xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" type="blank" preserve="1"><p:cSld name="Blank"><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr></p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>
""";

    private static string SlideLayoutRelationships() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" Target="../slideMasters/slideMaster1.xml"/></Relationships>
""";

    private static string Theme() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Haven"><a:themeElements><a:clrScheme name="Haven"><a:dk1><a:srgbClr val="1F1F1F"/></a:dk1><a:lt1><a:srgbClr val="FFFFFF"/></a:lt1><a:dk2><a:srgbClr val="343434"/></a:dk2><a:lt2><a:srgbClr val="F4F4F4"/></a:lt2><a:accent1><a:srgbClr val="E65F42"/></a:accent1><a:accent2><a:srgbClr val="4F7CAC"/></a:accent2><a:accent3><a:srgbClr val="6A9A6B"/></a:accent3><a:accent4><a:srgbClr val="8C6FB3"/></a:accent4><a:accent5><a:srgbClr val="C28A3D"/></a:accent5><a:accent6><a:srgbClr val="4F9A96"/></a:accent6><a:hlink><a:srgbClr val="0563C1"/></a:hlink><a:folHlink><a:srgbClr val="954F72"/></a:folHlink></a:clrScheme><a:fontScheme name="Haven"><a:majorFont><a:latin typeface="Aptos Display"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont><a:minorFont><a:latin typeface="Aptos"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont></a:fontScheme><a:fmtScheme name="Haven"><a:fillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:fillStyleLst><a:lnStyleLst><a:ln w="6350"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln><a:ln w="12700"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln><a:ln w="19050"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>
""";

    private static string BuildSlide(PresentSlide slide)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<p:sld xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"><p:cSld><p:spTree>");
        builder.Append(GroupShapeProperties());
        builder.Append(TextShape(2, "Title", slide.Title, 0.06, 0.055, 0.88, 0.16, 3200, true, false));
        var shapeId = 3;
        foreach (var element in slide.Elements.OrderBy(item => item.Order))
        {
            var text = ExportText(element);
            if (string.IsNullOrWhiteSpace(text) && element.Kind == PresentElementKind.Text)
                continue;
            var fallback = element.Kind != PresentElementKind.Text;
            builder.Append(TextShape(shapeId++, $"Element {element.Id:N}", text, element.X, element.Y, element.Width, element.Height, 2200, false, fallback));
        }
        builder.Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
        return builder.ToString();
    }

    private static string GroupShapeProperties() =>
        "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";

    private static string TextShape(
        int id, string name, string text, double x, double y, double width, double height,
        int fontSize, bool bold, bool fallback)
    {
        var xEmu = ToEmu(x, SlideWidth);
        var yEmu = ToEmu(y, SlideHeight);
        var widthEmu = Math.Max(1, ToEmu(width, SlideWidth));
        var heightEmu = Math.Max(1, ToEmu(height, SlideHeight));
        var paragraphs = BuildParagraphs(text, fontSize, bold);
        var fill = fallback
            ? "<a:solidFill><a:srgbClr val=\"FFF4F1\"/></a:solidFill><a:ln><a:solidFill><a:srgbClr val=\"E65F42\"/></a:solidFill></a:ln>"
            : "<a:noFill/><a:ln><a:noFill/></a:ln>";
        return $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"{Xml(name)}\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"{xEmu}\" y=\"{yEmu}\"/><a:ext cx=\"{widthEmu}\" cy=\"{heightEmu}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>{fill}</p:spPr><p:txBody><a:bodyPr wrap=\"square\"/><a:lstStyle/>{paragraphs}</p:txBody></p:sp>";
    }

    private static string BuildParagraphs(string text, int fontSize, bool bold)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append("<a:p><a:r><a:rPr lang=\"en-GB\"");
            builder.Append($" sz=\"{fontSize}\"");
            if (bold) builder.Append(" b=\"1\"");
            builder.Append($"/><a:t>{Xml(line)}</a:t></a:r><a:endParaRPr lang=\"en-GB\" sz=\"{fontSize}\"/></a:p>");
        }
        return builder.ToString();
    }

    private static string ExportText(PresentElement element) => element.Kind switch
    {
        PresentElementKind.Text => element.Text,
        PresentElementKind.Image => "[Image preserved in Haven] " + FirstNonEmpty(element.AlternativeText, element.Text, element.AssetId),
        PresentElementKind.Shape => "[Shape preserved in Haven] " + FirstNonEmpty(element.Text, element.ShapeType),
        PresentElementKind.GenUi => "[Interactive Haven content — open in Haven to use] " + FirstNonEmpty(element.AlternativeText, element.Text),
        _ => "[Haven content preserved in native presentation]"
    };

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static long ToEmu(double normalized, long total) =>
        (long)Math.Round(Math.Clamp(double.IsFinite(normalized) ? normalized : 0, 0, 1) * total);

    private static string SlideRelationships() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/></Relationships>
""";

    private static async Task WriteEntryAsync(
        ZipArchive archive, string path, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: false);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidatePackage(string path, int slideCount)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var required = new[]
        {
            "[Content_Types].xml",
            "_rels/.rels",
            "ppt/presentation.xml",
            "ppt/_rels/presentation.xml.rels",
            "ppt/slideMasters/slideMaster1.xml",
            "ppt/slideLayouts/slideLayout1.xml",
            "ppt/theme/theme1.xml"
        };
        foreach (var part in required)
            ValidateXmlPart(archive, part);
        for (var index = 1; index <= slideCount; index++)
        {
            ValidateXmlPart(archive, $"ppt/slides/slide{index}.xml");
            ValidateXmlPart(archive, $"ppt/slides/_rels/slide{index}.xml.rels");
        }
    }

    private static void ValidateXmlPart(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"The PPTX package is missing {path}.");
        using var stream = entry.Open();
        _ = XDocument.Load(stream, LoadOptions.None);
    }

    private static string Xml(string? value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
