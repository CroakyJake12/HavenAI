using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class PresentPptxImportService : IPresentImportService
{
    private const double EmuPerInch = 914_400d;
    private static readonly XNamespace Presentation = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public IReadOnlyList<string> ImportExtensions { get; } = [".pptx"];

    public PresentImportSupport Support { get; } = new(
        ".pptx",
        "Editable PPTX subset: slide order and size, slide titles, and ordinary text boxes with geometry are reconstructed. Unsupported PowerPoint features are not presented as lossless.",
        ["slide order", "slide dimensions", "slide titles", "plain text boxes", "text-box geometry"],
        ["native images and media", "native shape styling", "charts and SmartArt", "speaker notes", "animations and transitions", "theme/layout fidelity"]);

    public async Task<PresentDocument> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(sourcePath);
        if (!Path.GetExtension(fullPath).Equals(".pptx", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Present currently imports .pptx presentations through its documented editable subset.");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The PowerPoint presentation was not found.", fullPath);

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var presentation = await ReadXmlAsync(archive, "ppt/presentation.xml", cancellationToken).ConfigureAwait(false);
        var relationships = await ReadXmlAsync(archive, "ppt/_rels/presentation.xml.rels", cancellationToken).ConfigureAwait(false);

        var document = PresentDocument.Create(Path.GetFileNameWithoutExtension(fullPath));
        document.Slides.Clear();
        ApplySlideSize(document, presentation);
        var layoutId = document.Layouts[0].Id;
        var relationshipMap = relationships
            .Root?
            .Elements(PackageRelationships + "Relationship")
            .Where(element => element.Attribute("Id") is not null && element.Attribute("Target") is not null)
            .ToDictionary(
                element => (string)element.Attribute("Id")!,
                element => (string)element.Attribute("Target")!,
                StringComparer.Ordinal) ?? new Dictionary<string, string>(StringComparer.Ordinal);

        var slideIds = presentation.Descendants(Presentation + "sldId").ToArray();
        for (var index = 0; index < slideIds.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relationshipId = (string?)slideIds[index].Attribute(OfficeRelationships + "id");
            if (relationshipId is null || !relationshipMap.TryGetValue(relationshipId, out var target)) continue;
            var partPath = ResolvePresentationPart(target);
            var slideXml = await ReadXmlAsync(archive, partPath, cancellationToken).ConfigureAwait(false);
            document.Slides.Add(ParseSlide(slideXml, index, layoutId, document.SlideSize));
        }

        if (document.Slides.Count == 0)
            throw new InvalidDataException("The PPTX contains no readable slides in Present's supported subset.");

        document.Metadata["pptx-import-support"] = Support.Description;
        document.Normalize();
        return document;
    }

    private static PresentSlide ParseSlide(XDocument xml, int order, Guid layoutId, PresentSlideSize slideSize)
    {
        var slide = new PresentSlide
        {
            Order = order,
            LayoutId = layoutId,
            Title = $"Slide {order + 1}",
            Elements = []
        };
        var bodyAssigned = false;
        foreach (var shape in xml.Descendants(Presentation + "sp"))
        {
            var text = string.Concat(shape.Descendants(Drawing + "t").Select(node => node.Value));
            if (string.IsNullOrEmpty(text)) continue;
            var name = (string?)shape
                .Element(Presentation + "nvSpPr")?
                .Element(Presentation + "cNvPr")?
                .Attribute("name") ?? string.Empty;
            if (name.StartsWith("Title", StringComparison.OrdinalIgnoreCase))
            {
                slide.Title = text;
                continue;
            }

            var element = new PresentElement
            {
                Kind = PresentElementKind.Text,
                Role = bodyAssigned ? string.Empty : PresentElementRoles.Body,
                Text = text,
                AlternativeText = name
            };
            ApplyGeometry(element, shape, slideSize);
            slide.Elements.Add(element);
            bodyAssigned = true;
        }
        return slide;
    }

    private static void ApplyGeometry(PresentElement element, XElement shape, PresentSlideSize slideSize)
    {
        var transform = shape.Element(Presentation + "spPr")?.Element(Drawing + "xfrm");
        var offset = transform?.Element(Drawing + "off");
        var extent = transform?.Element(Drawing + "ext");
        if (offset is null || extent is null) return;
        var slideWidth = Math.Max(1, slideSize.WidthInches * EmuPerInch);
        var slideHeight = Math.Max(1, slideSize.HeightInches * EmuPerInch);
        element.X = ParseLong(offset.Attribute("x")) / slideWidth;
        element.Y = ParseLong(offset.Attribute("y")) / slideHeight;
        element.Width = ParseLong(extent.Attribute("cx")) / slideWidth;
        element.Height = ParseLong(extent.Attribute("cy")) / slideHeight;
    }

    private static void ApplySlideSize(PresentDocument document, XDocument presentation)
    {
        var size = presentation.Descendants(Presentation + "sldSz").FirstOrDefault();
        var cx = ParseLong(size?.Attribute("cx"));
        var cy = ParseLong(size?.Attribute("cy"));
        if (cx <= 0 || cy <= 0) return;
        var width = cx / EmuPerInch;
        var height = cy / EmuPerInch;
        document.SlideSize = new PresentSlideSize
        {
            Preset = ResolvePreset(width, height),
            WidthInches = width,
            HeightInches = height
        };
        document.SlideSize.Normalize();
    }

    private static PresentSlideSizePreset ResolvePreset(double width, double height)
    {
        if (Nearly(width, 13.333333) && Nearly(height, 7.5)) return PresentSlideSizePreset.Widescreen16By9;
        if (Nearly(width, 10) && Nearly(height, 7.5)) return PresentSlideSizePreset.Standard4By3;
        if (Nearly(width, 7.5) && Nearly(height, 13.333333)) return PresentSlideSizePreset.Portrait9By16;
        return PresentSlideSizePreset.Custom;
    }

    private static bool Nearly(double left, double right) => Math.Abs(left - right) < 0.02;

    private static long ParseLong(XAttribute? attribute) =>
        long.TryParse(attribute?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string ResolvePresentationPart(string target)
    {
        var normalized = target.Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("../", StringComparison.Ordinal)) normalized = normalized[3..];
        return normalized.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase) ? normalized : "ppt/" + normalized;
    }

    private static async Task<XDocument> ReadXmlAsync(ZipArchive archive, string path, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"The PPTX is missing required part '{path}'.");
        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return XDocument.Parse(text, LoadOptions.PreserveWhitespace);
    }
}
