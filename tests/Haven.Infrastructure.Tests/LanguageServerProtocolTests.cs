/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/LanguageServerProtocolTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns LanguageServerProtocolTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents language server protocol tests and keeps its related state and behavior together.
/// </summary>
public sealed class LanguageServerProtocolTests
{
    /// <summary>
    /// Performs the codec round trips utf8 json using byte content length step owned by this component.
    /// </summary>
    [Fact]
    public async Task CodecRoundTripsUtf8JsonUsingByteContentLength()
    {
        using var stream = new MemoryStream();
        var message = JsonSerializer.SerializeToElement(new
        {
            jsonrpc = "2.0",
            id = 7,
            result = new { text = "héllo 世界" }
        });

        await LanguageServerProtocolCodec.WriteMessageAsync(stream, message, CancellationToken.None);
        stream.Position = 0;
        var decoded = await LanguageServerProtocolCodec.ReadMessageAsync(stream, CancellationToken.None);

        Assert.NotNull(decoded);
        Assert.Equal(7, decoded!.Value.GetProperty("id").GetInt32());
        Assert.Equal("héllo 世界", decoded.Value.GetProperty("result").GetProperty("text").GetString());
    }

    /// <summary>
    /// Performs the codec rejects messages without content length step owned by this component.
    /// </summary>
    [Fact]
    public async Task CodecRejectsMessagesWithoutContentLength()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("Content-Type: application/json\r\n\r\n{}"));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await LanguageServerProtocolCodec.ReadMessageAsync(stream, CancellationToken.None));
    }

    /// <summary>
    /// Performs the text edits apply from end and preserve utf16 positions step owned by this component.
    /// </summary>
    [Fact]
    public void TextEditsApplyFromEndAndPreserveUtf16Positions()
    {
        const string original = "alpha 😀 beta\nsecond line\n";
        var edits = new LanguageServerTextEdit[]
        {
            new(new CodeRange(new CodePosition(1, 0), new CodePosition(1, 6)), "updated"),
            new(new CodeRange(new CodePosition(0, 9), new CodePosition(0, 13)), "BETA")
        };

        var updated = LanguageServerTextEditApplicator.Apply(original, edits);

        Assert.Equal("alpha 😀 BETA\nupdated line\n", updated);
    }

    /// <summary>
    /// Performs the text edits reject overlapping ranges step owned by this component.
    /// </summary>
    [Fact]
    public void TextEditsRejectOverlappingRanges()
    {
        const string original = "abcdef";
        var edits = new LanguageServerTextEdit[]
        {
            new(new CodeRange(new CodePosition(0, 1), new CodePosition(0, 4)), "one"),
            new(new CodeRange(new CodePosition(0, 3), new CodePosition(0, 5)), "two")
        };

        Assert.Throws<InvalidOperationException>(() => LanguageServerTextEditApplicator.Apply(original, edits));
    }

    /// <summary>
    /// Performs the unified diff contains reviewed old and new lines step owned by this component.
    /// </summary>
    [Fact]
    public void UnifiedDiffContainsReviewedOldAndNewLines()
    {
        var diff = UnifiedDiffBuilder.Build("src/Test.cs", "one\ntwo\n", "one\nthree\n");

        Assert.Contains("--- a/src/Test.cs", diff);
        Assert.Contains("+++ b/src/Test.cs", diff);
        Assert.Contains("-two", diff);
        Assert.Contains("+three", diff);
    }
}
