/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WorkspaceRetrievalIndexer.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns WorkspaceRetrievalIndexer. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents workspace retrieval indexer and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceRetrievalIndexer(IRetrievalIndexService retrieval) : IWorkspaceRetrievalIndexer
{
    /// <summary>
    /// Stores allowed extensions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".fs", ".vb", ".java", ".kt", ".cpp", ".c", ".h", ".hpp", ".rs", ".go", ".py", ".js", ".jsx", ".ts", ".tsx",
        ".html", ".css", ".scss", ".json", ".jsonc", ".xml", ".xaml", ".axaml", ".yaml", ".yml", ".toml", ".ini", ".cfg",
        ".md", ".txt", ".sql", ".ps1", ".sh", ".cmd", ".bat", ".csproj", ".fsproj", ".vbproj", ".sln", ".props", ".targets"
    };

    /// <summary>
    /// Stores excluded directories locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", "packages", "artifacts", "dist", "build", ".next", ".nuxt", "coverage"
    };

    /// <summary>
    /// Performs index project async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<RetrievalIndexReport> IndexProjectAsync(Guid projectId, string rootPath, CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project identifier is required.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("Project root is required.", nameof(rootPath));
        var root = Path.GetFullPath(rootPath.Trim());
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The project root no longer exists.");
        var scope = new RetrievalScope(RetrievalScopeKind.Project, projectId);
        var existing = (await retrieval.GetDocumentsAsync(scope, cancellationToken).ConfigureAwait(false))
            .Where(item => item.SourceType == "project-file")
            .ToDictionary(item => item.SourceId, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notices = new List<string>();
        var indexed = 0;
        var unchanged = 0;
        var skipped = 0;

        foreach (var path in EnumerateFiles(root, maximumFiles: 1_500, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(path);
            if (!AllowedExtensions.Contains(extension)) { skipped++; continue; }
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 2L * 1024 * 1024) { skipped++; continue; }
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)) { skipped++; continue; }
            seen.Add(relative);
            string text;
            try { text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                skipped++;
                notices.Add($"Skipped {relative}: {ex.Message}");
                continue;
            }
            var hash = Hash(text);
            if (existing.TryGetValue(relative, out var current) && current.ContentHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
            {
                unchanged++;
                continue;
            }
            await retrieval.IndexTextAsync(scope, "project-file", relative, relative, text, cancellationToken).ConfigureAwait(false);
            indexed++;
        }

        var removed = 0;
        foreach (var stale in existing.Keys.Where(key => !seen.Contains(key)))
        {
            await retrieval.RemoveSourceAsync(scope, "project-file", stale, cancellationToken).ConfigureAwait(false);
            removed++;
        }
        if (seen.Count >= 1_500) notices.Add("Project indexing stopped at the 1,500-file safety limit.");
        return new RetrievalIndexReport(indexed, unchanged, removed, skipped, notices);
    }

    /// <summary>
    /// Performs index subject async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<RetrievalIndexReport> IndexSubjectAsync(
        ContainerDefinition subject,
        IReadOnlyList<Lesson> lessons,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(lessons);
        if (subject.Id == Guid.Empty) throw new ArgumentException("Subject identifier is required.", nameof(subject));
        var scope = new RetrievalScope(RetrievalScopeKind.Subject, subject.Id);
        var existing = (await retrieval.GetDocumentsAsync(scope, cancellationToken).ConfigureAwait(false))
            .ToDictionary(item => item.SourceType + "\n" + item.SourceId, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indexed = 0;
        var unchanged = 0;

        var subjectText = $"Subject: {subject.Name}\n\nContext:\n{subject.Context}\n\nInstructions:\n{subject.Instructions}";
        await Index("subject", subject.Id.ToString("N"), subject.Name, subjectText);
        foreach (var lesson in lessons.OrderBy(item => item.TopicGroup, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var lessonText = $"Lesson: {lesson.Name}\nTopic group: {lesson.TopicGroup}\nStructure:\n{lesson.StructureJson}";
            await Index("lesson", lesson.Id.ToString("N"), lesson.Name, lessonText);
        }

        var removed = 0;
        foreach (var document in existing.Values.Where(item => !seen.Contains(item.SourceType + "\n" + item.SourceId)))
        {
            await retrieval.RemoveSourceAsync(scope, document.SourceType, document.SourceId, cancellationToken).ConfigureAwait(false);
            removed++;
        }
        return new RetrievalIndexReport(indexed, unchanged, removed, 0, []);

        async Task Index(string sourceType, string sourceId, string title, string text)
        {
            var key = sourceType + "\n" + sourceId;
            seen.Add(key);
            var hash = Hash(text);
            if (existing.TryGetValue(key, out var current) && current.ContentHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
            {
                unchanged++;
                return;
            }
            await retrieval.IndexTextAsync(scope, sourceType, sourceId, title, text, cancellationToken).ConfigureAwait(false);
            indexed++;
        }
    }

    /// <summary>
    /// Performs the enumerate files step owned by this component.
    /// </summary>
    private static IEnumerable<string> EnumerateFiles(string root, int maximumFiles, CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        var yielded = 0;
        while (stack.Count > 0 && yielded < maximumFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = stack.Pop();
            IEnumerable<string> children;
            try { children = Directory.EnumerateFileSystemEntries(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try { attributes = File.GetAttributes(child); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!ExcludedDirectories.Contains(Path.GetFileName(child))) stack.Push(child);
                    continue;
                }
                yield return child;
                yielded++;
                if (yielded >= maximumFiles) yield break;
            }
        }
    }

    /// <summary>
    /// Reports whether hash is true for the current state.
    /// </summary>
    private static string Hash(string text)
    {
        var normalized = text.Replace("\0", string.Empty, StringComparison.Ordinal).ReplaceLineEndings("\n").Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }
}
