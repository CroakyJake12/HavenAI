using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

public sealed class BrowserBackgroundPageLoader(IBrowserNavigationPolicy policy)
{
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private const int MaximumTextCharacters = 120_000;

    public async Task<BrowserPageSnapshot> LoadAsync(Uri address, CancellationToken cancellationToken)
    {
        await using var lease = await BrowserPinnedHttpTransport.SendAsync(
            policy,
            address,
            maximumRedirects: 8,
            timeout: TimeSpan.FromSeconds(45),
            cancellationToken).ConfigureAwait(false);
        var response = lease.Response;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            throw new InvalidOperationException("The background page exceeds Haven's 8 MB extraction limit.");

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null
            && !mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
            && !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            && !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Background extraction does not treat '{mediaType}' as a readable page. Request a user-approved download instead.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > MaximumResponseBytes)
                throw new InvalidOperationException("The background page exceeded Haven's 8 MB extraction limit while streaming.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        var encoding = ResolveEncoding(response.Content.Headers.ContentType?.CharSet);
        var source = encoding.GetString(output.GetBuffer(), 0, checked((int)output.Length));
        var isHtml = mediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true
                     || source.Contains("<html", StringComparison.OrdinalIgnoreCase)
                     || source.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase);
        var title = isHtml ? ExtractTitle(source) : lease.FinalAddress.Host;
        var headings = isHtml ? ExtractHeadings(source) : Array.Empty<string>();
        var text = isHtml ? HtmlToText(source) : NormalizeText(source);
        var truncated = text.Length > MaximumTextCharacters;
        if (truncated) text = text[..MaximumTextCharacters];
        return new BrowserPageSnapshot(
            lease.FinalAddress,
            string.IsNullOrWhiteSpace(title) ? lease.FinalAddress.Host : title,
            text,
            headings,
            [],
            DateTimeOffset.UtcNow,
            false,
            truncated);
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return Encoding.UTF8;
        try { return Encoding.GetEncoding(charset.Trim().Trim('"', '\'')); }
        catch (ArgumentException) { return Encoding.UTF8; }
    }

    private static string ExtractTitle(string html)
    {
        var match = Regex.Match(html, "<title[^>]*>(?<value>[\\s\\S]*?)</title>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? NormalizeText(WebUtility.HtmlDecode(StripTags(match.Groups["value"].Value))) : string.Empty;
    }

    private static IReadOnlyList<string> ExtractHeadings(string html) => Regex.Matches(
            html,
            "<h[1-6][^>]*>(?<value>[\\s\\S]*?)</h[1-6]>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        .Select(match => NormalizeText(WebUtility.HtmlDecode(StripTags(match.Groups["value"].Value))))
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Take(100)
        .ToArray();

    private static string HtmlToText(string html)
    {
        var withoutNoise = Regex.Replace(
            html,
            "<(script|style|noscript|template)[^>]*>[\\s\\S]*?</\\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return NormalizeText(WebUtility.HtmlDecode(StripTags(withoutNoise)));
    }

    private static string StripTags(string value) => Regex.Replace(value, "<[^>]+>", " ", RegexOptions.CultureInvariant);
    private static string NormalizeText(string value) => Regex.Replace(value.Replace("\0", string.Empty, StringComparison.Ordinal), "\\s+", " ").Trim();
}
