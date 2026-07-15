using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class LocalMediaToolLocator : ILocalMediaToolLocator
{
    public string? FindExecutable(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var executableName = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name + ".exe"
            : name;
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", executableName);
        if (File.Exists(bundled)) return bundled;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        }
        return null;
    }
}

public sealed class MessageAttachmentService(
    IAppPaths paths,
    IConversationProductionRepository repository,
    ILocalMediaToolLocator tools) : IMessageAttachmentService
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".fs", ".vb", ".java", ".kt", ".kts", ".cpp", ".c", ".h", ".hpp", ".rs", ".go", ".py", ".js", ".jsx", ".ts", ".tsx",
        ".html", ".htm", ".css", ".scss", ".sass", ".less", ".xml", ".xaml", ".axaml", ".json", ".jsonc", ".yaml", ".yml", ".toml",
        ".sql", ".ps1", ".sh", ".bash", ".cmd", ".bat", ".md", ".razor", ".vue", ".svelte", ".gradle", ".csproj", ".fsproj", ".vbproj", ".sln"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".tsv", ".log", ".ini", ".cfg", ".conf", ".rtf"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".m4v"
    };

    public async Task<MessageAttachment> ImportAsync(
        Guid conversationId,
        Guid? messageId,
        Guid? branchId,
        string path,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new AttachmentProcessingOptions();
        if (conversationId == Guid.Empty) throw new ArgumentException("Conversation identifier is required.", nameof(conversationId));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Attachment path is required.", nameof(path));
        var sourcePath = Path.GetFullPath(path.Trim());
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The attachment no longer exists.", sourcePath);

        var info = new FileInfo(sourcePath);
        var extension = info.Extension.ToLowerInvariant();
        var kind = Classify(extension);
        var limit = SizeLimit(kind, options);
        if (info.Length <= 0) throw new InvalidOperationException("The attachment is empty.");
        if (info.Length > limit)
            throw new InvalidOperationException($"{info.Name} is {FormatBytes(info.Length)}, above Haven's {FormatBytes(limit)} limit for {kind.ToString().ToLowerInvariant()} attachments.");

        var id = Guid.NewGuid();
        var conversationDirectory = Path.Combine(paths.AttachmentsDirectory, conversationId.ToString("N"));
        Directory.CreateDirectory(conversationDirectory);
        var storedName = id.ToString("N") + extension;
        var storedPath = Path.Combine(conversationDirectory, storedName);
        await CopyAtomicallyAsync(sourcePath, storedPath, cancellationToken).ConfigureAwait(false);
        var sha256 = await HashAsync(storedPath, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var mediaType = MediaTypeFor(extension, kind);

        var state = AttachmentProcessingState.Ready;
        var method = kind == MessageAttachmentKind.Image ? AttachmentAnalysisMethod.DirectlyAnalysed : AttachmentAnalysisMethod.None;
        var extracted = string.Empty;
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceExtension"] = extension,
            ["processingNotice"] = string.Empty
        };

        try
        {
            switch (kind)
            {
                case MessageAttachmentKind.PlainText:
                case MessageAttachmentKind.SourceCode:
                    extracted = await ReadBoundedTextAsync(storedPath, options.MaxExtractedCharacters, cancellationToken).ConfigureAwait(false);
                    method = AttachmentAnalysisMethod.TextExtracted;
                    metadata["processingNotice"] = "Text was read directly from the attached file.";
                    break;
                case MessageAttachmentKind.Word:
                case MessageAttachmentKind.PowerPoint:
                case MessageAttachmentKind.Spreadsheet:
                    extracted = await ExtractOpenXmlTextAsync(storedPath, kind, options.MaxExtractedCharacters, cancellationToken).ConfigureAwait(false);
                    method = AttachmentAnalysisMethod.TextExtracted;
                    metadata["processingNotice"] = "Visible document text was extracted locally. Complex layout, formulas, charts and embedded objects may not be represented.";
                    break;
                case MessageAttachmentKind.Pdf:
                    (extracted, method, state, metadata["processingNotice"]) = await ProcessPdfAsync(storedPath, options, cancellationToken).ConfigureAwait(false);
                    break;
                case MessageAttachmentKind.Audio:
                    (extracted, method, state, metadata) = await ProcessAudioAsync(storedPath, metadata, options, cancellationToken).ConfigureAwait(false);
                    break;
                case MessageAttachmentKind.Video:
                    (extracted, method, state, metadata) = await ProcessVideoAsync(storedPath, conversationDirectory, id, metadata, options, cancellationToken).ConfigureAwait(false);
                    break;
                case MessageAttachmentKind.Image:
                    metadata["processingNotice"] = "The image will be sent directly to a vision-capable model.";
                    break;
                default:
                    state = AttachmentProcessingState.Unsupported;
                    metadata["processingNotice"] = "The file is stored, but Haven has no safe local extractor for this format.";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(storedPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or XmlException or UnauthorizedAccessException or InvalidOperationException)
        {
            state = AttachmentProcessingState.Failed;
            metadata["processingNotice"] = "Local processing failed: " + ex.Message;
        }

        var attachment = new MessageAttachment(
            id, conversationId, messageId, branchId, info.Name, storedName, mediaType, kind, info.Length, sha256,
            state, method, extracted, JsonSerializer.Serialize(metadata), now, DateTimeOffset.UtcNow);
        return await repository.UpsertAttachmentAsync(attachment, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AttachmentPromptContext> BuildPromptContextAsync(
        Guid conversationId,
        IReadOnlyCollection<Guid>? attachmentIds,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new AttachmentProcessingOptions();
        var selected = (await repository.GetAttachmentsAsync(conversationId, null, cancellationToken).ConfigureAwait(false))
            .Where(item => attachmentIds is null || attachmentIds.Count == 0 || attachmentIds.Contains(item.Id))
            .OrderBy(item => item.CreatedAt)
            .ToArray();
        var images = new List<string>();
        var notices = new List<string>();
        var text = new StringBuilder();
        var conversationDirectory = Path.Combine(paths.AttachmentsDirectory, conversationId.ToString("N"));

        foreach (var attachment in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var storedPath = Path.Combine(conversationDirectory, attachment.StoredName);
            var notice = NoticeFor(attachment);
            notices.Add($"{attachment.OriginalName}: {notice}");

            if (attachment.Kind == MessageAttachmentKind.Image && attachment.ProcessingState == AttachmentProcessingState.Ready && File.Exists(storedPath))
            {
                images.Add(Convert.ToBase64String(await File.ReadAllBytesAsync(storedPath, cancellationToken).ConfigureAwait(false)));
            }

            if (attachment.Kind == MessageAttachmentKind.Video)
            {
                foreach (var frame in ReadFramePaths(attachment.MetadataJson).Take(options.MaxVideoFrames))
                {
                    var framePath = Path.GetFullPath(Path.Combine(conversationDirectory, frame));
                    if (!IsInside(framePath, conversationDirectory) || !File.Exists(framePath)) continue;
                    images.Add(Convert.ToBase64String(await File.ReadAllBytesAsync(framePath, cancellationToken).ConfigureAwait(false)));
                }
            }

            if (!string.IsNullOrWhiteSpace(attachment.ExtractedText))
            {
                text.Append("\n\n--- Attached ").Append(attachment.Kind).Append(": ").Append(attachment.OriginalName).Append(" ---\n")
                    .Append("Analysis method: ").Append(attachment.AnalysisMethod).Append("\n")
                    .Append(attachment.ExtractedText);
            }
        }

        return new AttachmentPromptContext(images, Truncate(text.ToString(), options.MaxExtractedCharacters), notices, selected);
    }

    public async Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        MessageAttachment? attachment = null;
        // Attachment IDs are globally unique, so a bounded scan of recent conversation attachment folders
        // is unnecessary; the owning caller should normally retain the record. The repository deletion is
        // still authoritative and physical cleanup is best-effort.
        await repository.DeleteAttachmentAsync(attachmentId, cancellationToken).ConfigureAwait(false);
        if (attachment is null) return;
        var directory = Path.Combine(paths.AttachmentsDirectory, attachment.ConversationId.ToString("N"));
        TryDelete(Path.Combine(directory, attachment.StoredName));
        var frameDirectory = Path.Combine(directory, attachment.Id.ToString("N") + "-frames");
        TryDeleteDirectory(frameDirectory);
    }

    private async Task<(string Text, AttachmentAnalysisMethod Method, AttachmentProcessingState State, object Notice)> ProcessPdfAsync(
        string path,
        AttachmentProcessingOptions options,
        CancellationToken cancellationToken)
    {
        var pdftotext = tools.FindExecutable("pdftotext");
        if (pdftotext is null)
            return (string.Empty, AttachmentAnalysisMethod.None, AttachmentProcessingState.Unsupported,
                "The PDF is stored, but local PDF text extraction is unavailable. Install or bundle pdftotext to enable extraction.");
        var result = await RunAsync(pdftotext, ["-layout", "-enc", "UTF-8", path, "-"], TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            return (string.Empty, AttachmentAnalysisMethod.None, AttachmentProcessingState.Failed, "PDF text extraction failed: " + Truncate(result.Error, 500));
        return (Truncate(result.Output, options.MaxExtractedCharacters), AttachmentAnalysisMethod.TextExtracted, AttachmentProcessingState.Ready,
            "Text was extracted locally from the PDF. Scanned pages and complex layout may require vision or OCR and may be incomplete.");
    }

    private async Task<(string Text, AttachmentAnalysisMethod Method, AttachmentProcessingState State, Dictionary<string, object?> Metadata)> ProcessAudioAsync(
        string path,
        Dictionary<string, object?> metadata,
        AttachmentProcessingOptions options,
        CancellationToken cancellationToken)
    {
        await AddProbeMetadataAsync(path, metadata, cancellationToken).ConfigureAwait(false);
        var whisper = tools.FindExecutable("whisper-cli") ?? tools.FindExecutable("main");
        if (whisper is null)
        {
            metadata["processingNotice"] = "Audio metadata was inspected, but no local Whisper CLI was available. No transcript was inferred.";
            return (string.Empty, AttachmentAnalysisMethod.InferredFromMetadata, AttachmentProcessingState.Ready, metadata);
        }

        var outputPrefix = Path.Combine(Path.GetTempPath(), "haven-whisper-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await RunAsync(whisper, ["-f", path, "-otxt", "-of", outputPrefix], TimeSpan.FromMinutes(20), cancellationToken).ConfigureAwait(false);
            var transcriptPath = outputPrefix + ".txt";
            if (result.ExitCode == 0 && File.Exists(transcriptPath))
            {
                metadata["processingNotice"] = "Audio was transcribed locally with the available Whisper CLI.";
                return (Truncate(await File.ReadAllTextAsync(transcriptPath, cancellationToken).ConfigureAwait(false), options.MaxExtractedCharacters),
                    AttachmentAnalysisMethod.Transcribed, AttachmentProcessingState.Ready, metadata);
            }
            metadata["processingNotice"] = "Audio metadata was inspected, but local transcription failed. No transcript was invented.";
            metadata["transcriptionError"] = Truncate(result.Error, 1_000);
            return (string.Empty, AttachmentAnalysisMethod.InferredFromMetadata, AttachmentProcessingState.Ready, metadata);
        }
        finally
        {
            TryDelete(outputPrefix + ".txt");
            TryDelete(outputPrefix + ".json");
            TryDelete(outputPrefix + ".srt");
            TryDelete(outputPrefix + ".vtt");
        }
    }

    private async Task<(string Text, AttachmentAnalysisMethod Method, AttachmentProcessingState State, Dictionary<string, object?> Metadata)> ProcessVideoAsync(
        string path,
        string conversationDirectory,
        Guid attachmentId,
        Dictionary<string, object?> metadata,
        AttachmentProcessingOptions options,
        CancellationToken cancellationToken)
    {
        await AddProbeMetadataAsync(path, metadata, cancellationToken).ConfigureAwait(false);
        var ffmpeg = tools.FindExecutable("ffmpeg");
        if (ffmpeg is null)
        {
            metadata["processingNotice"] = "Video metadata was inspected. No frames or audio were analysed because local ffmpeg was unavailable.";
            return (string.Empty, AttachmentAnalysisMethod.InferredFromMetadata, AttachmentProcessingState.Ready, metadata);
        }

        var frameDirectoryName = attachmentId.ToString("N") + "-frames";
        var frameDirectory = Path.Combine(conversationDirectory, frameDirectoryName);
        Directory.CreateDirectory(frameDirectory);
        var pattern = Path.Combine(frameDirectory, "frame-%03d.jpg");
        var durationSeconds = ReadDurationSeconds(metadata);
        var interval = durationSeconds is > 0
            ? Math.Max(1, (int)Math.Ceiling(durationSeconds.Value / Math.Max(1, options.MaxVideoFrames)))
            : 30;
        var frameResult = await RunAsync(ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-i", path, "-vf", $"fps=1/{interval},scale='min(1280,iw)':-2", "-frames:v", options.MaxVideoFrames.ToString(System.Globalization.CultureInfo.InvariantCulture), "-q:v", "3", pattern],
            TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
        var framePaths = Directory.Exists(frameDirectory)
            ? Directory.GetFiles(frameDirectory, "frame-*.jpg").OrderBy(item => item, StringComparer.OrdinalIgnoreCase).Select(Path.GetFileName).Select(name => Path.Combine(frameDirectoryName, name!)).ToArray()
            : [];
        metadata["sampledFrames"] = framePaths;
        metadata["frameIntervalSeconds"] = interval;

        var transcript = string.Empty;
        var audioMethod = AttachmentAnalysisMethod.None;
        var temporaryAudio = Path.Combine(Path.GetTempPath(), "haven-video-audio-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var audioResult = await RunAsync(ffmpeg,
                ["-hide_banner", "-loglevel", "error", "-i", path, "-vn", "-ac", "1", "-ar", "16000", "-f", "wav", temporaryAudio],
                TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
            if (audioResult.ExitCode == 0 && File.Exists(temporaryAudio))
            {
                var audio = await ProcessAudioAsync(temporaryAudio, new Dictionary<string, object?>(), options, cancellationToken).ConfigureAwait(false);
                transcript = audio.Text;
                audioMethod = audio.Method;
                metadata["audioAnalysis"] = audio.Metadata;
            }
        }
        finally
        {
            TryDelete(temporaryAudio);
        }

        if (framePaths.Length == 0 && string.IsNullOrWhiteSpace(transcript))
        {
            metadata["processingNotice"] = "Video metadata was inspected, but frame sampling and transcription did not produce analysable content.";
            metadata["frameSamplingError"] = Truncate(frameResult.Error, 1_000);
            return (string.Empty, AttachmentAnalysisMethod.InferredFromMetadata, AttachmentProcessingState.Ready, metadata);
        }

        metadata["processingNotice"] = framePaths.Length > 0 && audioMethod == AttachmentAnalysisMethod.Transcribed
            ? $"Video was sampled into {framePaths.Length} frames and its audio was transcribed locally. This is not full-frame video understanding."
            : framePaths.Length > 0
                ? $"Video was sampled into {framePaths.Length} frames. This is not full-frame video understanding; audio was not transcribed."
                : "Video audio was transcribed locally, but no frames were sampled.";
        return (transcript, framePaths.Length > 0 ? AttachmentAnalysisMethod.SampledFrames : audioMethod, AttachmentProcessingState.Ready, metadata);
    }

    private async Task AddProbeMetadataAsync(string path, Dictionary<string, object?> metadata, CancellationToken cancellationToken)
    {
        var ffprobe = tools.FindExecutable("ffprobe");
        if (ffprobe is null) return;
        var result = await RunAsync(ffprobe,
            ["-v", "error", "-show_entries", "format=duration,format_name,bit_rate:stream=codec_type,codec_name,width,height,sample_rate,channels", "-of", "json", path],
            TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output)) return;
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            metadata["probe"] = document.RootElement.Clone();
        }
        catch (JsonException) { }
    }

    private static double? ReadDurationSeconds(IReadOnlyDictionary<string, object?> metadata)
    {
        if (!metadata.TryGetValue("probe", out var probe) || probe is not JsonElement element) return null;
        if (!element.TryGetProperty("format", out var format) || !format.TryGetProperty("duration", out var duration)) return null;
        return double.TryParse(duration.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static async Task<string> ExtractOpenXmlTextAsync(
        string path,
        MessageAttachmentKind kind,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var prefixes = kind switch
        {
            MessageAttachmentKind.Word => new[] { "word/document.xml", "word/header", "word/footer", "word/footnotes.xml", "word/endnotes.xml" },
            MessageAttachmentKind.PowerPoint => new[] { "ppt/slides/slide", "ppt/notesSlides/notesSlide" },
            MessageAttachmentKind.Spreadsheet => new[] { "xl/sharedStrings.xml", "xl/worksheets/sheet" },
            _ => []
        };
        var builder = new StringBuilder();
        foreach (var entry in archive.Entries.Where(entry => prefixes.Any(prefix => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var entryStream = entry.Open();
            using var reader = XmlReader.Create(entryStream, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            });
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element || reader.IsEmptyElement) continue;
                if (reader.LocalName is not ("t" or "v")) continue;
                var value = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(value)) continue;
                builder.Append(value.Trim()).Append(' ');
                if (builder.Length >= maxCharacters) return Truncate(builder.ToString(), maxCharacters);
            }
            builder.AppendLine();
        }
        return Truncate(builder.ToString().Trim(), maxCharacters);
    }

    private static async Task<string> ReadBoundedTextAsync(string path, int maxCharacters, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024, leaveOpen: false);
        var buffer = new char[Math.Min(maxCharacters + 1, 32 * 1024)];
        var builder = new StringBuilder(Math.Min(maxCharacters, 128 * 1024));
        while (builder.Length <= maxCharacters)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            builder.Append(buffer, 0, read);
        }
        return Truncate(builder.ToString(), maxCharacters);
    }

    private static MessageAttachmentKind Classify(string extension)
    {
        if (ImageExtensions.Contains(extension)) return MessageAttachmentKind.Image;
        if (SourceExtensions.Contains(extension)) return MessageAttachmentKind.SourceCode;
        if (TextExtensions.Contains(extension)) return MessageAttachmentKind.PlainText;
        if (extension == ".pdf") return MessageAttachmentKind.Pdf;
        if (extension is ".docx" or ".docm") return MessageAttachmentKind.Word;
        if (extension is ".pptx" or ".pptm") return MessageAttachmentKind.PowerPoint;
        if (extension is ".xlsx" or ".xlsm" or ".ods") return MessageAttachmentKind.Spreadsheet;
        if (AudioExtensions.Contains(extension)) return MessageAttachmentKind.Audio;
        if (VideoExtensions.Contains(extension)) return MessageAttachmentKind.Video;
        return MessageAttachmentKind.Other;
    }

    private static long SizeLimit(MessageAttachmentKind kind, AttachmentProcessingOptions options) => kind switch
    {
        MessageAttachmentKind.Image => options.MaxImageBytes,
        MessageAttachmentKind.Audio => options.MaxAudioBytes,
        MessageAttachmentKind.Video => options.MaxVideoBytes,
        _ => options.MaxDocumentBytes
    };

    private static string MediaTypeFor(string extension, MessageAttachmentKind kind) => extension switch
    {
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".gif" => "image/gif", ".bmp" => "image/bmp",
        ".pdf" => "application/pdf", ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation", ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".csv" => "text/csv", ".json" => "application/json", ".xml" => "application/xml", ".md" => "text/markdown", ".txt" => "text/plain",
        ".wav" => "audio/wav", ".mp3" => "audio/mpeg", ".m4a" => "audio/mp4", ".flac" => "audio/flac", ".ogg" => "audio/ogg",
        ".mp4" => "video/mp4", ".webm" => "video/webm", ".mkv" => "video/x-matroska", ".mov" => "video/quicktime",
        _ when kind == MessageAttachmentKind.SourceCode => "text/plain",
        _ => "application/octet-stream"
    };

    private static string NoticeFor(MessageAttachment attachment)
    {
        try
        {
            using var document = JsonDocument.Parse(attachment.MetadataJson);
            if (document.RootElement.TryGetProperty("processingNotice", out var notice) && notice.GetString() is { Length: > 0 } value) return value;
        }
        catch (JsonException) { }
        return attachment.ProcessingState switch
        {
            AttachmentProcessingState.Ready => "Attachment is ready.",
            AttachmentProcessingState.Pending => "Attachment processing is pending.",
            AttachmentProcessingState.Unsupported => "Attachment is stored but this format is not locally analysable.",
            _ => "Attachment processing failed."
        };
    }

    private static IReadOnlyList<string> ReadFramePaths(string metadataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (!document.RootElement.TryGetProperty("sampledFrames", out var frames) || frames.ValueKind != JsonValueKind.Array) return [];
            return frames.EnumerateArray().Select(item => item.GetString()).OfType<string>().ToArray();
        }
        catch (JsonException) { return []; }
    }

    private static async Task CopyAtomicallyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(true);
            }
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"{Path.GetFileName(executable)} exceeded the {timeout.TotalMinutes:0.#}-minute processing limit.");
        }
    }

    private static string Truncate(string value, int maxCharacters) => value.Length <= maxCharacters ? value : value[..maxCharacters] + "\n[content truncated by Haven]";
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.#} GB" : bytes >= 1024L * 1024 ? $"{bytes / (1024d * 1024):0.#} MB" : $"{bytes / 1024d:0.#} KB";
    private static bool IsInside(string path, string root) => path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
