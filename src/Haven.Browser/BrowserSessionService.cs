using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

public sealed record BrowserSnapshot(Uri? Address, string Title, bool CanGoBack, bool CanGoForward, bool IsLoading, string Status);

public interface IEmbeddedBrowserHost
{
    event EventHandler<BrowserSnapshot>? StateChanged;
    BrowserSnapshot State { get; }
    Task NavigateAsync(Uri address, CancellationToken cancellationToken);
    Task GoBackAsync(CancellationToken cancellationToken);
    Task GoForwardAsync(CancellationToken cancellationToken);
    Task ReloadAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken);
    Task OpenDeveloperToolsAsync(CancellationToken cancellationToken);
}

public sealed class BrowserSessionService(IAppPaths paths) : IBrowserToolService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex ReferencePattern = new("^haven-[0-9]{1,4}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private IEmbeddedBrowserHost? _host;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    private Uri? _fallbackAddress;
    private string _fallbackText = string.Empty;
    private BrowserSnapshot _fallbackState = new(null, "Browser", false, false, false, "Browser view is not attached.");

    public string ProfileDirectory => paths.BrowserProfileDirectory;
    public bool IsInteractiveAvailable => _host is not null;
    public BrowserSnapshot State => _host?.State ?? _fallbackState;

    public event EventHandler<BrowserSnapshot>? StateChanged;

    public void Attach(IEmbeddedBrowserHost host)
    {
        if (_host is not null) _host.StateChanged -= ForwardState;
        _host = host;
        _host.StateChanged += ForwardState;
        StateChanged?.Invoke(this, host.State);
    }

    public void Detach(IEmbeddedBrowserHost host)
    {
        if (!ReferenceEquals(_host, host)) return;
        _host.StateChanged -= ForwardState;
        _host = null;
    }

    public async Task<string> NavigateAsync(string value, CancellationToken cancellationToken)
    {
        var normalised = NormaliseAddress(value);
        _fallbackAddress = normalised;
        if (_host is not null)
        {
            await _host.NavigateAsync(normalised, cancellationToken).ConfigureAwait(false);
            return $"Navigating to {normalised}.";
        }
        _fallbackState = new(normalised, normalised.Host, false, false, true, "Loading without a visible browser host…");
        StateChanged?.Invoke(this, _fallbackState);
        using var response = await _http.GetAsync(normalised, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 8L * 1024 * 1024)
            throw new InvalidOperationException("The background page exceeds Haven's 8 MB extraction limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var buffer = new char[16 * 1024];
        var builder = new System.Text.StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (builder.Length + read > 8 * 1024 * 1024) throw new InvalidOperationException("The background page exceeded Haven's 8 MB extraction limit while streaming.");
            builder.Append(buffer, 0, read);
        }
        _fallbackText = HtmlToText(builder.ToString());
        _fallbackState = new(response.RequestMessage?.RequestUri ?? normalised, normalised.Host, false, false, false, "Page loaded in the isolated background session.");
        StateChanged?.Invoke(this, _fallbackState);
        return $"Loaded {_fallbackState.Address}. Use browser_snapshot to inspect it. Interactive actions require the Browse view to be open.";
    }

    public async Task<string> BackAsync(CancellationToken cancellationToken)
    {
        await RequireHost(x => x.GoBackAsync(cancellationToken)).ConfigureAwait(false);
        return "Went back.";
    }

    public async Task<string> ForwardAsync(CancellationToken cancellationToken)
    {
        await RequireHost(x => x.GoForwardAsync(cancellationToken)).ConfigureAwait(false);
        return "Went forward.";
    }

    public Task ReloadAsync(CancellationToken cancellationToken) => RequireHost(x => x.ReloadAsync(cancellationToken));
    public Task StopAsync(CancellationToken cancellationToken) => RequireHost(x => x.StopAsync(cancellationToken));

    public async Task<string> ReloadAsync(bool clearSiteCache, CancellationToken cancellationToken)
    {
        if (_host is null && _fallbackAddress is not null) return await NavigateAsync(_fallbackAddress.ToString(), cancellationToken).ConfigureAwait(false);
        EnsureHost();
        if (clearSiteCache)
            await _host!.ExecuteScriptAsync("(async()=>{try{for(const k of await caches.keys())await caches.delete(k);}catch{} location.reload(); return 'cache-cleared';})()", cancellationToken).ConfigureAwait(false);
        else
            await _host!.ReloadAsync(cancellationToken).ConfigureAwait(false);
        return clearSiteCache ? "Cleared this site's Cache Storage and reloaded." : "Reloaded the page.";
    }

    public async Task<string> ExtractVisibleTextAsync(CancellationToken cancellationToken)
    {
        if (_host is null) return _fallbackText;
        var result = await RequireHost(x => x.ExecuteScriptAsync("document.body?.innerText ?? ''", cancellationToken)).ConfigureAwait(false);
        return UnwrapJavaScriptString(result);
    }

    public Task<string> ReadVisibleTextAsync(CancellationToken cancellationToken) => ExtractVisibleTextAsync(cancellationToken);

    public async Task<BrowserPageSnapshot> CaptureStructuredPageAsync(CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            var text = _fallbackText.Length <= 120_000 ? _fallbackText : _fallbackText[..120_000];
            return new BrowserPageSnapshot(_fallbackState.Address, _fallbackState.Title, text, [], [], DateTimeOffset.UtcNow, false, text.Length != _fallbackText.Length);
        }

        const string script = """
            (() => {
              const maxText = 120000;
              const maxElements = 400;
              const visible = e => {
                const s = getComputedStyle(e); const r = e.getBoundingClientRect();
                return s.visibility !== 'hidden' && s.display !== 'none' && r.width > 0 && r.height > 0;
              };
              document.querySelectorAll('[data-haven-ref]').forEach(e => e.removeAttribute('data-haven-ref'));
              const candidates = [...document.querySelectorAll('a,button,input,textarea,select,[role=button]')]
                .filter(visible).slice(0, maxElements);
              const elements = candidates.map((e, i) => {
                const reference = `haven-${i + 1}`; e.setAttribute('data-haven-ref', reference);
                const tag = e.tagName.toLowerCase();
                const type = (e.getAttribute('type') || '').toLowerCase();
                const autocomplete = (e.getAttribute('autocomplete') || '').toLowerCase();
                const sensitive = type === 'password' || type === 'file' || type === 'hidden'
                  || /cc-|credit|card|one-time-code|new-password|current-password/.test(autocomplete);
                const submits = (tag === 'button' && (!type || type === 'submit'))
                  || (tag === 'input' && (type === 'submit' || type === 'image'));
                return {
                  reference,
                  kind: tag === 'a' ? 'link' : (tag === 'textarea' ? 'textarea' : tag === 'select' ? 'select' : tag === 'input' ? 'input' : 'button'),
                  text: ((e.innerText || e.value || e.getAttribute('aria-label') || e.getAttribute('title') || e.getAttribute('placeholder') || '') + '').trim().slice(0, 500),
                  address: tag === 'a' ? (e.href || null) : null,
                  name: e.getAttribute('name'),
                  inputType: type || null,
                  isSensitive: sensitive,
                  submitsForm: submits
                };
              });
              const rawText = document.body?.innerText || '';
              return JSON.stringify({
                address: location.href,
                title: document.title || location.hostname,
                text: rawText.slice(0, maxText),
                headings: [...document.querySelectorAll('h1,h2,h3,h4,h5,h6')].filter(visible).slice(0,100).map(e => (e.innerText || '').trim().slice(0,500)),
                elements,
                wasTruncated: rawText.length > maxText || candidates.length >= maxElements
              });
            })()
            """;
        var raw = await RequireHost(x => x.ExecuteScriptAsync(script, cancellationToken)).ConfigureAwait(false);
        var json = UnwrapJavaScriptString(raw);
        var dto = JsonSerializer.Deserialize<PageSnapshotDto>(json, JsonOptions)
                  ?? throw new InvalidOperationException("The browser returned an empty structured snapshot.");
        var address = Uri.TryCreate(dto.Address, UriKind.Absolute, out var parsed) ? parsed : State.Address;
        return new BrowserPageSnapshot(
            address,
            string.IsNullOrWhiteSpace(dto.Title) ? address?.Host ?? "Browser" : dto.Title,
            dto.Text ?? string.Empty,
            dto.Headings?.Where(value => !string.IsNullOrWhiteSpace(value)).Take(100).ToArray() ?? [],
            dto.Elements?.Take(400).Select(item => new BrowserPageElement(
                item.Reference ?? string.Empty,
                item.Kind ?? "element",
                item.Text ?? string.Empty,
                item.Address,
                item.Name,
                item.InputType,
                item.IsSensitive,
                item.SubmitsForm)).Where(item => ReferencePattern.IsMatch(item.Reference)).ToArray() ?? [],
            DateTimeOffset.UtcNow,
            true,
            dto.WasTruncated);
    }

    public async Task<string> ClickReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        ValidateReference(reference);
        var script = $"""
            (() => {{
              const wanted = {JavaScriptString(reference)};
              const e = [...document.querySelectorAll('[data-haven-ref]')].find(x => x.getAttribute('data-haven-ref') === wanted);
              if (!e) return 'stale-reference';
              e.scrollIntoView({{ block: 'center', inline: 'nearest' }});
              e.click();
              return 'clicked ' + wanted;
            }})()
            """;
        return UnwrapJavaScriptString(await RequireHost(x => x.ExecuteScriptAsync(script, cancellationToken)).ConfigureAwait(false));
    }

    public async Task<string> FillReferenceAsync(string reference, string value, CancellationToken cancellationToken)
    {
        ValidateReference(reference);
        var script = $"""
            (() => {{
              const wanted = {JavaScriptString(reference)};
              const value = {JavaScriptString(value)};
              const e = [...document.querySelectorAll('[data-haven-ref]')].find(x => x.getAttribute('data-haven-ref') === wanted);
              if (!e) return 'stale-reference';
              const type = (e.getAttribute('type') || '').toLowerCase();
              const autocomplete = (e.getAttribute('autocomplete') || '').toLowerCase();
              if (type === 'password' || type === 'file' || type === 'hidden' || /cc-|credit|card|one-time-code|new-password|current-password/.test(autocomplete)) return 'sensitive-field-blocked';
              if (!(e instanceof HTMLInputElement || e instanceof HTMLTextAreaElement || e instanceof HTMLSelectElement)) return 'not-editable';
              e.focus();
              if (e instanceof HTMLSelectElement) e.value = value;
              else {{
                const proto = e instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
                const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
                if (setter) setter.call(e, value); else e.value = value;
              }}
              e.dispatchEvent(new Event('input', {{ bubbles: true }}));
              e.dispatchEvent(new Event('change', {{ bubbles: true }}));
              return 'filled ' + wanted;
            }})()
            """;
        return UnwrapJavaScriptString(await RequireHost(x => x.ExecuteScriptAsync(script, cancellationToken)).ConfigureAwait(false));
    }

    public async Task<string> ClickAsync(string selector, CancellationToken cancellationToken) =>
        UnwrapJavaScriptString(await RequireHost(x => x.ExecuteScriptAsync($"(() => {{ const e = document.querySelector({JavaScriptString(selector)}); if (!e) return 'not-found'; e.click(); return 'clicked'; }})()", cancellationToken)).ConfigureAwait(false));

    public async Task<string> ClickTextAsync(string text, CancellationToken cancellationToken) =>
        UnwrapJavaScriptString(await RequireHost(x => x.ExecuteScriptAsync($"(() => {{ const wanted={JavaScriptString(text)}.toLowerCase(); const e=[...document.querySelectorAll('a,button,[role=button],input[type=submit]')].find(x=>(x.innerText||x.value||'').trim().toLowerCase().includes(wanted)); if(!e)return 'not-found'; e.scrollIntoView({{block:'center'}}); e.click(); return 'clicked '+(e.innerText||e.value||'').trim(); }})()", cancellationToken)).ConfigureAwait(false));

    public async Task<string> FillAsync(string selector, string value, CancellationToken cancellationToken) =>
        UnwrapJavaScriptString(await RequireHost(x => x.ExecuteScriptAsync($"(() => {{ const e = document.querySelector({JavaScriptString(selector)}); if (!e) return 'not-found'; e.focus(); e.value = {JavaScriptString(value)}; e.dispatchEvent(new Event('input', {{bubbles:true}})); e.dispatchEvent(new Event('change', {{bubbles:true}})); return 'filled'; }})()", cancellationToken)).ConfigureAwait(false));

    public async Task<string> ScrollAsync(double x, double y, CancellationToken cancellationToken) =>
        UnwrapJavaScriptString(await RequireHost(xHost => xHost.ExecuteScriptAsync($"window.scrollBy({x.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {y.ToString(System.Globalization.CultureInfo.InvariantCulture)}); 'scrolled'", cancellationToken)).ConfigureAwait(false));

    public async Task PrintAsync(CancellationToken cancellationToken) =>
        _ = await RequireHost(x => x.ExecuteScriptAsync("window.print(); 'print-opened'", cancellationToken)).ConfigureAwait(false);

    public Task OpenDeveloperToolsAsync(CancellationToken cancellationToken) => RequireHost(x => x.OpenDeveloperToolsAsync(cancellationToken));

    public async Task<string> ExecuteUiScriptAsync(string script, CancellationToken cancellationToken) =>
        UnwrapJavaScriptString(await RequireHost(x => x.ExecuteScriptAsync(script, cancellationToken)).ConfigureAwait(false));

    private void ForwardState(object? sender, BrowserSnapshot state) => StateChanged?.Invoke(this, state);

    private Task RequireHost(Func<IEmbeddedBrowserHost, Task> action)
    {
        EnsureHost();
        return action(_host!);
    }

    private Task<T> RequireHost<T>(Func<IEmbeddedBrowserHost, Task<T>> action)
    {
        EnsureHost();
        return action(_host!);
    }

    private void EnsureHost()
    {
        if (_host is null) throw new InvalidOperationException("The embedded browser is not currently attached to a native view.");
    }

    private static void ValidateReference(string reference)
    {
        if (!ReferencePattern.IsMatch(reference)) throw new ArgumentException("The browser element reference is invalid or stale.", nameof(reference));
    }

    private static Uri NormaliseAddress(string value)
    {
        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var direct) && direct.Scheme is "http" or "https") return direct;
        if (!candidate.Contains(' ') && candidate.Contains('.')) return new Uri("https://" + candidate, UriKind.Absolute);
        return new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(candidate), UriKind.Absolute);
    }

    private static string JavaScriptString(string value) => JsonSerializer.Serialize(value);

    private static string UnwrapJavaScriptString(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        try { return JsonSerializer.Deserialize<string>(value) ?? value; }
        catch (JsonException) { return value; }
    }

    private static string HtmlToText(string html)
    {
        var withoutScripts = Regex.Replace(html, "<script[\\s\\S]*?</script>|<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
        var text = Regex.Replace(withoutScripts, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, "\\s+", " ").Trim();
    }

    public void Dispose() => _http.Dispose();

    private sealed record PageSnapshotDto(
        string? Address,
        string? Title,
        string? Text,
        IReadOnlyList<string>? Headings,
        IReadOnlyList<PageElementDto>? Elements,
        bool WasTruncated);

    private sealed record PageElementDto(
        string? Reference,
        string? Kind,
        string? Text,
        string? Address,
        string? Name,
        string? InputType,
        bool IsSensitive,
        bool SubmitsForm);
}
