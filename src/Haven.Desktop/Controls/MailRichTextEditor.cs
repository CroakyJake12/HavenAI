using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Haven.Desktop.Controls;

public sealed class MailRichTextChangedEventArgs(string html, string plainText) : EventArgs
{
    public string Html { get; } = html;
    public string PlainText { get; } = plainText;
}

/// <summary>
/// Local-only WYSIWYG editor for email bodies. The document never navigates to remote content.
/// </summary>
public sealed class MailRichTextEditor : UserControl, IDisposable
{
    private readonly NativeWebView _webView = new();
    private string _html = string.Empty;
    private string _plainText = string.Empty;
    private bool _ready;
    private bool _disposed;

    public MailRichTextEditor()
    {
        MinHeight = 240;
        Content = _webView;
        _webView.NavigationStarted += OnNavigationStarted;
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.NewWindowRequested += OnNewWindowRequested;
        _webView.WebMessageReceived += OnWebMessageReceived;

        var bytes = Encoding.UTF8.GetBytes(BuildDocument());
        _webView.Navigate(new Uri("data:text/html;charset=utf-8;base64," + Convert.ToBase64String(bytes), UriKind.Absolute));
    }

    public event EventHandler<MailRichTextChangedEventArgs>? ContentChanged;
    public string Html => _html;
    public string PlainText => _plainText;

    public async Task SetContentAsync(string? html, string? plainText = null)
    {
        _html = html ?? string.Empty;
        _plainText = plainText ?? string.Empty;
        if (!_ready || _disposed) return;
        await PushContentAsync().ConfigureAwait(false);
    }

    public async Task FlushAsync()
    {
        if (!_ready || _disposed) return;
        try
        {
            var raw = await _webView.InvokeScript("JSON.stringify(window.havenMailSnapshot())").ConfigureAwait(false);
            ApplyScriptSnapshot(raw, raiseChanged: true);
        }
        catch
        {
            // Keep the last WebMessageReceived snapshot if the native adapter is temporarily unavailable.
        }
    }

    private async Task PushContentAsync()
    {
        try
        {
            var htmlJson = JsonSerializer.Serialize(_html);
            var textJson = JsonSerializer.Serialize(_plainText);
            await _webView.InvokeScript($"window.havenMailSetContent({htmlJson},{textJson})").ConfigureAwait(false);
        }
        catch
        {
            // NavigationCompleted can race adapter startup; the next interaction will resync.
        }
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess || _disposed) return;
        _ready = true;
        _ = PushContentAsync();
    }

    private static void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs args)
    {
        if (args.Request?.Scheme is "data" or "about") return;
        args.Cancel = true;
    }

    private static void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs args) => args.Handled = true;

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs args)
    {
        if (_disposed || string.IsNullOrWhiteSpace(args.Body)) return;
        void Apply()
        {
            try
            {
                using var document = JsonDocument.Parse(args.Body);
                var root = document.RootElement;
                if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "content") return;
                var html = root.TryGetProperty("html", out var htmlNode) ? htmlNode.GetString() ?? string.Empty : string.Empty;
                var text = root.TryGetProperty("text", out var textNode) ? textNode.GetString() ?? string.Empty : string.Empty;
                ApplySnapshot(html, text, raiseChanged: true);
            }
            catch (JsonException) { }
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }

    private void ApplyScriptSnapshot(string? raw, bool raiseChanged)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        var value = raw;
        try { value = JsonSerializer.Deserialize<string>(raw) ?? raw; } catch (JsonException) { }
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            var html = root.TryGetProperty("html", out var htmlNode) ? htmlNode.GetString() ?? string.Empty : string.Empty;
            var text = root.TryGetProperty("text", out var textNode) ? textNode.GetString() ?? string.Empty : string.Empty;
            ApplySnapshot(html, text, raiseChanged);
        }
        catch (JsonException) { }
    }

    private void ApplySnapshot(string html, string plainText, bool raiseChanged)
    {
        var changed = !string.Equals(_html, html, StringComparison.Ordinal) || !string.Equals(_plainText, plainText, StringComparison.Ordinal);
        _html = html;
        _plainText = plainText;
        if (changed && raiseChanged) ContentChanged?.Invoke(this, new MailRichTextChangedEventArgs(html, plainText));
    }

    private static string BuildDocument() => """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1">
<style>
:root{color-scheme:light dark;font-family:Arial,sans-serif}*{box-sizing:border-box}html,body{margin:0;height:100%;overflow:hidden;background:transparent}body{color:light-dark(#171717,#f4f4f4);background:light-dark(#fff,#202124)}#shell{display:grid;grid-template-rows:auto 1fr;height:100%}#toolbar{display:flex;flex-wrap:wrap;gap:5px;align-items:center;padding:7px 8px;border-bottom:1px solid light-dark(#dedede,#3c4043);background:light-dark(#fafafa,#292a2d)}button,select,input[type=color]{height:30px;border:1px solid light-dark(#d5d5d5,#4b4d51);border-radius:7px;background:light-dark(#fff,#303134);color:inherit}button{min-width:30px;padding:0 8px;font-size:13px;cursor:pointer}button:hover{background:light-dark(#eee,#3c4043)}button:active{transform:translateY(1px)}select{padding:0 6px;max-width:132px}input[type=color]{width:34px;padding:3px}.sep{width:1px;height:22px;background:light-dark(#d7d7d7,#4b4d51);margin:0 2px}#editor{overflow:auto;padding:14px 16px 40px;min-height:100%;outline:none;font:14px/1.55 Arial,sans-serif;word-break:break-word}#editor:empty:before{content:'Write your message…';opacity:.46;pointer-events:none}#editor blockquote{border-left:3px solid #7b7f87;margin:8px 0;padding-left:12px;opacity:.9}#editor a{color:#4f8cff}
</style>
</head>
<body>
<div id="shell">
<div id="toolbar" role="toolbar" aria-label="Email formatting">
<select id="font" title="Font" aria-label="Font"><option value="Arial">Arial</option><option value="Calibri">Calibri</option><option value="Georgia">Georgia</option><option value="Tahoma">Tahoma</option><option value="Times New Roman">Times New Roman</option><option value="Verdana">Verdana</option><option value="monospace">Monospace</option></select>
<select id="size" title="Text size" aria-label="Text size"><option value="2">Small</option><option value="3" selected>Normal</option><option value="4">Large</option><option value="5">Larger</option><option value="6">Huge</option></select>
<span class="sep"></span><button type="button" data-cmd="bold" title="Bold (Ctrl+B)"><b>B</b></button><button type="button" data-cmd="italic" title="Italic (Ctrl+I)"><i>I</i></button><button type="button" data-cmd="underline" title="Underline (Ctrl+U)"><u>U</u></button><button type="button" data-cmd="strikeThrough" title="Strikethrough"><s>S</s></button><input id="color" type="color" value="#202124" title="Text colour" aria-label="Text colour">
<span class="sep"></span><button type="button" data-cmd="insertUnorderedList" title="Bulleted list">• List</button><button type="button" data-cmd="insertOrderedList" title="Numbered list">1. List</button><button type="button" data-cmd="outdent" title="Decrease indent">⇤</button><button type="button" data-cmd="indent" title="Increase indent">⇥</button>
<span class="sep"></span><button type="button" data-cmd="justifyLeft" title="Align left">≡</button><button type="button" data-cmd="justifyCenter" title="Align centre">≣</button><button type="button" data-cmd="justifyRight" title="Align right">≡→</button><button type="button" id="quote" title="Quote">❝</button><button type="button" id="link" title="Insert link">Link</button>
<span class="sep"></span><button type="button" data-cmd="removeFormat" title="Clear formatting">Tx</button><button type="button" data-cmd="undo" title="Undo (Ctrl+Z)">↶</button><button type="button" data-cmd="redo" title="Redo">↷</button>
</div>
<div id="editor" contenteditable="true" spellcheck="true" role="textbox" aria-multiline="true"></div>
</div>
<script>
(()=>{const editor=document.getElementById('editor');let suppress=false;const snapshot=()=>({html:editor.innerHTML,text:editor.innerText});const emit=()=>{if(!suppress&&typeof invokeCSharpAction==='function')invokeCSharpAction(JSON.stringify({kind:'content',...snapshot()}));};const command=(name,value=null)=>{editor.focus();document.execCommand(name,false,value);emit();};document.querySelectorAll('[data-cmd]').forEach(button=>button.addEventListener('mousedown',e=>e.preventDefault()));document.querySelectorAll('[data-cmd]').forEach(button=>button.addEventListener('click',()=>command(button.dataset.cmd)));document.getElementById('font').addEventListener('change',e=>command('fontName',e.target.value));document.getElementById('size').addEventListener('change',e=>command('fontSize',e.target.value));document.getElementById('color').addEventListener('change',e=>command('foreColor',e.target.value));document.getElementById('quote').addEventListener('mousedown',e=>e.preventDefault());document.getElementById('quote').addEventListener('click',()=>command('formatBlock','blockquote'));document.getElementById('link').addEventListener('mousedown',e=>e.preventDefault());document.getElementById('link').addEventListener('click',()=>{const url=prompt('Link URL');if(!url)return;if(!/^https?:\/\//i.test(url)&&!/^mailto:/i.test(url))return;command('createLink',url);});editor.addEventListener('input',emit);editor.addEventListener('blur',emit);window.havenMailSnapshot=snapshot;window.havenMailSetContent=(html,text)=>{suppress=true;editor.innerHTML=html||'';if(!html&&text)editor.innerText=text;suppress=false;return true;};})();
</script>
</body>
</html>
""";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _webView.NavigationStarted -= OnNavigationStarted;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.NewWindowRequested -= OnNewWindowRequested;
        _webView.WebMessageReceived -= OnWebMessageReceived;
        ContentChanged = null;
        Content = null;
        GC.SuppressFinalize(this);
    }
}
