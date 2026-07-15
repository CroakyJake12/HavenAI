using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class LanguageServerProtocolTests
{
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

    [Fact]
    public async Task CodecRejectsMessagesWithoutContentLength()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("Content-Type: application/json\r\n\r\n{}"));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await LanguageServerProtocolCodec.ReadMessageAsync(stream, CancellationToken.None));
    }

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
