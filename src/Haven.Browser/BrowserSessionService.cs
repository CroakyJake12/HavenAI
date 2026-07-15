using Haven.Application;

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
    private IEmbeddedBrowserHost? _host;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = System.Net.DecompressionMethods.All })
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
        if (_host is not null)
            _host.StateChanged -= ForwardState;
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
        using var response = await _http.GetAsync(normalised, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _fallbackText = HtmlToText(html);
        _fallbackState = new(response.RequestMessage?.RequestUri ?? normalised, normalised.Host, false, false, false, "Page loaded in the isolated background session.");
        StateChanged?.Invoke(this, _fallbackState);
        return $"Loaded {_fallbackState.Address}. Use browser_read_page to inspect it. Interactive clicks require the Browse view to be open.";
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

    /// <summary>
    /// Runs trusted browser-UI JavaScript. Model-facing browser tools deliberately
    /// do not expose this method.
    /// </summary>
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

    private static Uri NormaliseAddress(string value)
    {
        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var direct) && direct.Scheme is "http" or "https")
            return direct;
        if (!candidate.Contains(' ') && candidate.Contains('.'))
            return new Uri("https://" + candidate, UriKind.Absolute);
        return new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(candidate), UriKind.Absolute);
    }

    private static string JavaScriptString(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static string UnwrapJavaScriptString(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        try { return System.Text.Json.JsonSerializer.Deserialize<string>(value) ?? value; }
        catch (System.Text.Json.JsonException) { return value; }
    }

    private static string HtmlToText(string html)
    {
        var withoutScripts = System.Text.RegularExpressions.Regex.Replace(html, "<script[\\s\\S]*?</script>|<style[\\s\\S]*?</style>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var text = System.Text.RegularExpressions.Regex.Replace(withoutScripts, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
    }

    public void Dispose() => _http.Dispose();
}
