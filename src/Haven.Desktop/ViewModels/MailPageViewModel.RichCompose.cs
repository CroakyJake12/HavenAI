namespace Haven.Desktop.ViewModels;

public sealed partial class MailPageViewModel
{
    private string _composeHtmlBody = string.Empty;

    public string ComposeHtmlBody
    {
        get => _composeHtmlBody;
        private set => SetProperty(ref _composeHtmlBody, value);
    }

    public void SetComposeRichBody(string html, string plainText)
    {
        var htmlChanged = !string.Equals(ComposeHtmlBody, html, StringComparison.Ordinal);
        var textChanged = !string.Equals(ComposeBody, plainText, StringComparison.Ordinal);
        if (htmlChanged) ComposeHtmlBody = html;
        if (textChanged) ComposeBody = plainText;
        if (htmlChanged || textChanged) NotifyComposeChanged();
    }

    public Task CloseComposeSafelyAsync() => CloseComposeAfterAutosaveAsync();
    public Task SaveDraftNowAsync() => SaveDraftAsync();
    public void RequestSendAfterEditorFlush() => RequestSend();

    private void ResetRichCompose() => ComposeHtmlBody = string.Empty;
}
