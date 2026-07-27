/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/NotesMediaAssetServiceTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns NotesMediaAssetServiceTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents notes media asset service tests and keeps its related state and behavior together.
/// </summary>
public sealed class NotesMediaAssetServiceTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the imported media verifies and copies atomically step owned by this component.
    /// </summary>
    [Fact]
    public async Task ImportedMediaVerifiesAndCopiesAtomically()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var attachments = new SecureNotesAttachmentStore(
            new NotesAttachmentStore(_paths, diagnostics),
            _paths);
        var service = new NotesMediaAssetService(attachments, diagnostics);
        var source = Path.Combine(_paths.DataDirectory, "source.png");
        await File.WriteAllBytesAsync(source, [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4]);
        var media = await attachments.ImportAsync(source, CancellationToken.None);
        var destination = Path.Combine(_paths.DataDirectory, "export", "copy.png");

        var verification = await service.VerifyAsync(media, CancellationToken.None);
        var result = await service.SaveCopyAsync(media, destination, CancellationToken.None);

        Assert.True(verification.SizeMatches);
        Assert.True(verification.HashMatches);
        Assert.Equal(Path.GetFullPath(destination), result);
        Assert.Equal(await File.ReadAllBytesAsync(source), await File.ReadAllBytesAsync(destination));
    }

    /// <summary>
    /// Performs the tampered managed media fails closed step owned by this component.
    /// </summary>
    [Fact]
    public async Task TamperedManagedMediaFailsClosed()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var attachments = new SecureNotesAttachmentStore(
            new NotesAttachmentStore(_paths, diagnostics),
            _paths);
        var service = new NotesMediaAssetService(attachments, diagnostics);
        var source = Path.Combine(_paths.DataDirectory, "source.wav");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4, 5, 6]);
        var media = await attachments.ImportAsync(source, CancellationToken.None);
        var managed = await attachments.ResolvePathAsync(media.AttachmentId, CancellationToken.None);
        await using (var stream = new FileStream(managed, FileMode.Append, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(new byte[] { 7, 8, 9 });
            await stream.FlushAsync();
            stream.Flush(flushToDisk: true);
        }

        var verification = await service.VerifyAsync(media, CancellationToken.None);

        Assert.False(verification.SizeMatches);
        Assert.False(verification.HashMatches);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.SaveCopyAsync(media, Path.Combine(_paths.DataDirectory, "blocked.wav"), CancellationToken.None));
    }

    /// <summary>
    /// Performs the replacement preserves user metadata and creates new managed identity step owned by this component.
    /// </summary>
    [Fact]
    public async Task ReplacementPreservesUserMetadataAndCreatesNewManagedIdentity()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var attachments = new SecureNotesAttachmentStore(
            new NotesAttachmentStore(_paths, diagnostics),
            _paths);
        var service = new NotesMediaAssetService(attachments, diagnostics);
        var first = Path.Combine(_paths.DataDirectory, "first.mp3");
        var second = Path.Combine(_paths.DataDirectory, "second.mp3");
        await File.WriteAllBytesAsync(first, [1, 2, 3]);
        await File.WriteAllBytesAsync(second, [4, 5, 6, 7]);
        var current = await attachments.ImportAsync(first, CancellationToken.None);
        current.AltText = "Audio description";
        current.Caption = "Interview excerpt";
        current.Wrapping = "Square";
        current.Width = 620;
        current.Height = 140;
        current.Rotation = 12;
        current.CropLeft = 0.1;

        var replacement = await service.ReplaceAsync(current, second, CancellationToken.None);

        Assert.NotEqual(current.AttachmentId, replacement.AttachmentId);
        Assert.Equal("Audio description", replacement.AltText);
        Assert.Equal("Interview excerpt", replacement.Caption);
        Assert.Equal("Square", replacement.Wrapping);
        Assert.Equal(620, replacement.Width);
        Assert.Equal(140, replacement.Height);
        Assert.Equal(12, replacement.Rotation);
        Assert.Equal(0.1, replacement.CropLeft);
        Assert.True((await service.VerifyAsync(replacement, CancellationToken.None)).HashMatches);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-media-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }

        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath { get; }
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory { get; }
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory { get; }
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory { get; }
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath { get; }

        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
