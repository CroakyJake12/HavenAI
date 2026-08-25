using Haven.Application;
using Haven.Desktop.ViewModels;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Mail;

/// <summary>Haven UI-owned Mail information architecture and interaction surface.</summary>
internal sealed class MailHavenScene : IDisposable
{
    private readonly MailPageViewModel _viewModel;
    private readonly Container _body;
    private readonly Container _accountStrip;
    private readonly Container _stateCard;
    private readonly HavenText _stateTitle;
    private readonly HavenText _stateDescription;
    private readonly Container _mailboxArea;
    private readonly MailboxView _wide;
    private readonly MailboxView _compact;
    private readonly MailboxView _narrow;
    private readonly Container _composePanel;
    private Container _composeAttachments = null!;
    private readonly Container _sendConfirmation;
    private readonly HavenText _status;
    private readonly HavenText _updated;
    private HavenText _composeStatus = null!;
    private readonly Input _search;
    private Input _to = null!;
    private Input _cc = null!;
    private Input _bcc = null!;
    private Input _subject = null!;
    private Input _messageBody = null!;
    private readonly HavenButton _unreadFilter;
    private readonly HavenButton _flaggedFilter;
    private bool _syncingInputs;
    private bool _disposed;

    public MailHavenScene(MailPageViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Root = new Page { Name = "Mail.Root", Layout = HavenLayout.Overlay };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));

        _body = new Container { Name = "Mail.Body", Layout = HavenLayout.Vertical };
        _body.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _body.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        _body.SetValue(HavenProperties.Padding, HavenThickness.Parse("22px 24px 28px 24px"));
        _body.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        Root.Add(_body);

        var header = new Container { Name = "Mail.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        header.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        header.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        var identity = new Container { Layout = HavenLayout.Vertical };
        identity.SetValue(HavenProperties.Gap, HavenLength.Px(3));
        identity.Add(Heading("Mail.Title", "Mail", TextLevel.H1));
        identity.Add(Muted("Mail.Subtitle", "Your connected inboxes, without turning email into a chat."));
        header.Add(identity);
        var actions = new Container { Layout = HavenLayout.Wrap };
        actions.SetValue(HavenProperties.Column, 1);
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        actions.Add(Action("Mail.Refresh", "Refresh", ButtonVariant.Ghost, (_, _) => _viewModel.RefreshCommand.Execute(null)));
        actions.Add(Action("Mail.Compose", "New message", ButtonVariant.Primary, (_, _) => _viewModel.ComposeCommand.Execute(null)));
        header.Add(actions);
        _body.Add(header);

        _accountStrip = new Container { Name = "Mail.Accounts", Layout = HavenLayout.Wrap };
        _accountStrip.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _accountStrip.SetValue(HavenProperties.Gap, HavenLength.Px(7));
        _body.Add(_accountStrip);

        var findRow = new Container { Name = "Mail.FindRow", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto Auto", Rows = "Auto" };
        findRow.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        findRow.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        _search = new Input { Name = "Mail.Search", Placeholder = "Search mail", SubmitOnEnter = true };
        _search.TextChanged += OnSearchTextChanged;
        findRow.Add(_search);
        var searchButton = Action("Mail.SearchButton", "Search", ButtonVariant.Secondary, (_, _) => _viewModel.SearchCommand.Execute(null));
        searchButton.SetValue(HavenProperties.Column, 1);
        findRow.Add(searchButton);
        _unreadFilter = Action("Mail.Filter.Unread", "Unread", ButtonVariant.Ghost, (_, _) => ToggleUnread());
        _unreadFilter.SetValue(HavenProperties.Column, 2);
        findRow.Add(_unreadFilter);
        _flaggedFilter = Action("Mail.Filter.Flagged", "Flagged", ButtonVariant.Ghost, (_, _) => ToggleFlagged());
        _flaggedFilter.SetValue(HavenProperties.Column, 3);
        findRow.Add(_flaggedFilter);
        _body.Add(findRow);

        var statusRow = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        statusRow.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _status = Muted("Mail.Status", string.Empty);
        statusRow.Add(_status);
        _updated = Muted("Mail.Updated", string.Empty);
        _updated.SetValue(HavenProperties.Column, 1);
        statusRow.Add(_updated);
        _body.Add(statusRow);

        _stateCard = Card("Mail.State");
        _stateCard.SetValue(HavenProperties.MaxWidth, HavenLength.Px(680));
        _stateTitle = Heading("Mail.State.Title", string.Empty, TextLevel.H2);
        _stateDescription = Muted("Mail.State.Description", string.Empty);
        _stateCard.Add(_stateTitle);
        _stateCard.Add(_stateDescription);
        _stateCard.Add(Action("Mail.State.Refresh", "Try again", ButtonVariant.Secondary, (_, _) => _viewModel.RefreshCommand.Execute(null)));
        _body.Add(_stateCard);

        _mailboxArea = new Container { Name = "Mail.MailboxArea", Layout = HavenLayout.Overlay };
        _mailboxArea.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _mailboxArea.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        _mailboxArea.SetValue(HavenProperties.MinHeight, HavenLength.Px(480));
        _wide = BuildMailbox("Wide", "210px 360px 1fr", showFolderPanel: true, stacked: false);
        _wide.Root.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, HavenLength.Px(1000)));
        _mailboxArea.Add(_wide.Root);
        _compact = BuildMailbox("Compact", "330px 1fr", showFolderPanel: false, stacked: false);
        _compact.Root.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, HavenLength.Px(680), HavenLength.Px(999.999)));
        _mailboxArea.Add(_compact.Root);
        _narrow = BuildMailbox("Narrow", "1fr", showFolderPanel: false, stacked: true);
        _narrow.Root.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, maximum: HavenLength.Px(679.999)));
        _mailboxArea.Add(_narrow.Root);
        _body.Add(_mailboxArea);

        _composePanel = BuildComposePanel();
        Root.Add(_composePanel);
        _sendConfirmation = BuildSendConfirmation();
        Root.Add(_sendConfirmation);

        Refresh();
    }

    public Page Root { get; }
    public event EventHandler? AttachRequested;
    public event EventHandler<MailAttachmentDescriptor>? AttachmentDownloadRequested;

    public void Refresh()
    {
        if (_disposed) return;
        _status.Content = _viewModel.IsStale ? _viewModel.Status + " · showing cached mail" : _viewModel.Status;
        _updated.Content = _viewModel.LastUpdatedLabel;
        _unreadFilter.Variant = _viewModel.UnreadOnly ? ButtonVariant.Primary : ButtonVariant.Ghost;
        _flaggedFilter.Variant = _viewModel.FlaggedOnly ? ButtonVariant.Primary : ButtonVariant.Ghost;
        SyncInputs();
        RenderAccounts();
        RenderState();
        RenderMailbox(_wide);
        RenderMailbox(_compact);
        RenderMailbox(_narrow);
        RenderComposeAttachments();
        _composeStatus.Content = _viewModel.ComposeStatus;
        _composePanel.SetValue(HavenProperties.Visibility, _viewModel.IsComposeOpen ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        _sendConfirmation.SetValue(HavenProperties.Visibility, _viewModel.IsSendConfirmationOpen ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    private MailboxView BuildMailbox(string key, string columns, bool showFolderPanel, bool stacked)
    {
        var root = new Container { Name = $"Mail.Mailbox.{key}", Layout = HavenLayout.Vertical };
        root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Gap, HavenLength.Px(10));

        var folderChips = new Container { Name = $"Mail.FolderChips.{key}", Layout = HavenLayout.Wrap };
        folderChips.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        folderChips.SetValue(HavenProperties.Visibility, showFolderPanel ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        root.Add(folderChips);

        var panes = new Container { Name = $"Mail.Panes.{key}", Layout = stacked ? HavenLayout.Vertical : HavenLayout.Grid, Columns = columns, Rows = "1fr" };
        panes.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        panes.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        panes.SetValue(HavenProperties.Gap, HavenLength.Px(10));

        Container? folders = null;
        var messageColumn = showFolderPanel ? 1 : 0;
        var readingColumn = stacked ? 0 : showFolderPanel ? 2 : 1;
        if (showFolderPanel)
        {
            folders = Card($"Mail.Folders.{key}");
            folders.SetValue(HavenProperties.Column, 0);
            folders.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
            panes.Add(folders);
        }

        var messages = Card($"Mail.Messages.{key}");
        messages.SetValue(HavenProperties.Column, messageColumn);
        messages.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        if (stacked) messages.SetValue(HavenProperties.MinHeight, HavenLength.Px(300));
        panes.Add(messages);

        var reading = Card($"Mail.Reading.{key}");
        reading.SetValue(HavenProperties.Column, readingColumn);
        reading.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        if (stacked) reading.SetValue(HavenProperties.MinHeight, HavenLength.Px(380));
        panes.Add(reading);
        root.Add(panes);
        return new MailboxView(root, folderChips, folders, messages, reading);
    }

    private void RenderAccounts()
    {
        Clear(_accountStrip);
        foreach (var account in _viewModel.Accounts)
        {
            var selected = Equals(account, _viewModel.SelectedAccount);
            var button = Action($"Mail.Account.{account.AccountId:N}", account.DisplayName + " · " + account.Address, selected ? ButtonVariant.Primary : ButtonVariant.Secondary, (_, _) => _viewModel.SelectedAccount = account);
            button.Accessibility.AccessibleName = $"Use mail account {account.DisplayName}, {account.Address}";
            _accountStrip.Add(button);
        }
        _accountStrip.SetValue(HavenProperties.Visibility, _viewModel.Accounts.Count > 1 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    private void RenderState()
    {
        var blocking = _viewModel.State is MailUiState.ConnectionRequired or MailUiState.PermissionRequired or MailUiState.Offline or MailUiState.Error;
        var (title, description) = _viewModel.State switch
        {
            MailUiState.ConnectionRequired => ("Connect a mail account", "Mail reuses Haven's Google and Microsoft connected accounts. Connect one in Settings, then refresh Mail."),
            MailUiState.PermissionRequired => ("Mail permission needed", "Reconnect this account in Settings to grant Mail access. Haven will not pretend an older token can read or send email."),
            MailUiState.Offline => ("Mail is offline", _viewModel.Status),
            MailUiState.Error => ("Mail couldn't load", _viewModel.Status),
            _ => (string.Empty, string.Empty)
        };
        _stateTitle.Content = title;
        _stateDescription.Content = description;
        _stateCard.SetValue(HavenProperties.Visibility, blocking ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        _mailboxArea.SetValue(HavenProperties.Visibility, !blocking || _viewModel.Messages.Count > 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    private void RenderMailbox(MailboxView view)
    {
        RenderFolders(view.FolderChips, compact: true);
        if (view.Folders is not null) RenderFolders(view.Folders, compact: false);
        RenderMessages(view.Messages);
        RenderReading(view.Reading);
    }

    private void RenderFolders(Container target, bool compact)
    {
        Clear(target);
        if (!compact) target.Add(Heading(null, "Folders & labels", TextLevel.H3));
        foreach (var folder in _viewModel.Folders)
        {
            var label = folder.DisplayName + (folder.UnreadCount is > 0 ? $" ({folder.UnreadCount})" : string.Empty);
            var selected = Equals(folder, _viewModel.SelectedFolder);
            var button = Action($"Mail.Folder.{folder.Id}.{(compact ? "Chip" : "Rail")}", label, selected ? ButtonVariant.Primary : compact ? ButtonVariant.Ghost : ButtonVariant.Secondary, (_, _) => _viewModel.SelectedFolder = folder);
            if (!compact) button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            button.Accessibility.AccessibleName = $"Open {folder.DisplayName}";
            target.Add(button);
        }
    }

    private void RenderMessages(Container target)
    {
        Clear(target);
        var heading = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        heading.Add(Heading(null, _viewModel.FolderLabel, TextLevel.H3));
        var count = Muted(null, $"{_viewModel.Messages.Count} message{(_viewModel.Messages.Count == 1 ? string.Empty : "s")}");
        count.SetValue(HavenProperties.Column, 1);
        heading.Add(count);
        target.Add(heading);
        if (_viewModel.Messages.Count == 0)
        {
            target.Add(Muted(null, _viewModel.State == MailUiState.Empty ? "No messages match this view." : "No messages loaded."));
            return;
        }
        foreach (var message in _viewModel.Messages) target.Add(BuildMessageTile(message));
    }

    private Container BuildMessageTile(MailMessageSummary message)
    {
        var selected = _viewModel.SelectedSummary?.Id == message.Id;
        var tile = new MessageTile($"Open email {message.Subject}") { Name = $"Mail.Message.{message.Id}", Layout = HavenLayout.Vertical };
        tile.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        tile.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        tile.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        tile.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        tile.SetValue(HavenProperties.Background, selected ? "AccentMuted" : "Surface");
        tile.SetValue(HavenProperties.BorderColor, selected ? "AccentSecondary" : "Border");
        tile.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        var top = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        top.Add(Heading(null, (message.IsRead ? string.Empty : "• ") + message.From.Label, TextLevel.Caption));
        var date = Muted(null, message.ReceivedAt.LocalDateTime.ToString("dd MMM HH:mm"));
        date.SetValue(HavenProperties.Column, 1);
        top.Add(date);
        tile.Add(top);
        tile.Add(Heading(null, string.IsNullOrWhiteSpace(message.Subject) ? "(No subject)" : message.Subject, TextLevel.Paragraph));
        tile.Add(Muted(null, message.Preview));
        tile.Invoked += (_, _) => _viewModel.SelectedSummary = message;
        return tile;
    }

    private void RenderReading(Container target)
    {
        Clear(target);
        var selected = _viewModel.SelectedMessage;
        if (selected is null)
        {
            target.Add(Heading(null, "Select a message", TextLevel.H2));
            target.Add(Muted(null, "The email opens here with its real thread and provider-backed actions."));
            return;
        }

        target.Add(Heading(null, string.IsNullOrWhiteSpace(selected.Subject) ? "(No subject)" : selected.Subject, TextLevel.H2));
        target.Add(Heading(null, selected.From.Label, TextLevel.Paragraph));
        target.Add(Muted(null, selected.ReceivedAt.LocalDateTime.ToString("dddd d MMMM yyyy, HH:mm")));
        var actions = new Container { Layout = HavenLayout.Wrap };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        actions.Add(Action("Mail.Reply", "Reply", ButtonVariant.Secondary, (_, _) => _viewModel.ReplyCommand.Execute(null)));
        actions.Add(Action("Mail.ReplyAll", "Reply all", ButtonVariant.Secondary, (_, _) => _viewModel.ReplyAllCommand.Execute(null)));
        actions.Add(Action("Mail.Forward", "Forward", ButtonVariant.Secondary, (_, _) => _viewModel.ForwardCommand.Execute(null)));
        if (_viewModel.CanArchive) actions.Add(Action("Mail.Archive", "Archive", ButtonVariant.Ghost, (_, _) => _viewModel.ArchiveCommand.Execute(null)));
        if (_viewModel.CanDelete) actions.Add(Action("Mail.Delete", "Delete", ButtonVariant.Ghost, (_, _) => _viewModel.DeleteCommand.Execute(null)));
        if (_viewModel.CanChangeReadState) actions.Add(Action("Mail.ReadState", _viewModel.ReadActionLabel, ButtonVariant.Ghost, (_, _) => _viewModel.ToggleReadCommand.Execute(null)));
        if (_viewModel.CanFlag) actions.Add(Action("Mail.Flag", _viewModel.FlagActionLabel, ButtonVariant.Ghost, (_, _) => _viewModel.ToggleFlagCommand.Execute(null)));
        actions.Add(Action("Mail.Summarize", "Summarize", ButtonVariant.Ghost, (_, _) => _viewModel.SummarizeCommand.Execute(null)));
        actions.Add(Action("Mail.DraftReply", "Draft reply", ButtonVariant.Ghost, (_, _) => _viewModel.DraftWithAiCommand.Execute(null)));
        target.Add(actions);

        if (selected.Attachments.Count > 0)
        {
            var attachments = new Container { Layout = HavenLayout.Wrap };
            attachments.SetValue(HavenProperties.Gap, HavenLength.Px(6));
            foreach (var attachment in selected.Attachments)
            {
                var captured = attachment;
                attachments.Add(Action($"Mail.Attachment.{attachment.Id}", attachment.FileName, ButtonVariant.Secondary, (_, _) => AttachmentDownloadRequested?.Invoke(this, captured)));
            }
            target.Add(attachments);
        }

        if (_viewModel.IsAiPanelVisible)
        {
            var ai = Card("Mail.Ai");
            ai.SetValue(HavenProperties.Background, "AccentMuted");
            var aiHeader = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
            aiHeader.Add(Heading(null, _viewModel.AiTitle, TextLevel.H3));
            var close = Action("Mail.Ai.Close", "Close", ButtonVariant.Ghost, (_, _) => _viewModel.CloseAiCommand.Execute(null));
            close.SetValue(HavenProperties.Column, 1);
            aiHeader.Add(close);
            ai.Add(aiHeader);
            ai.Add(Muted(null, _viewModel.AiText));
            target.Add(ai);
        }

        foreach (var message in _viewModel.ThreadMessages)
        {
            var card = Card($"Mail.Thread.{message.Id}");
            var top = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
            top.Add(Heading(null, message.From.Label, TextLevel.Paragraph));
            var when = Muted(null, message.ReceivedAt.LocalDateTime.ToString("dd MMM yyyy, HH:mm"));
            when.SetValue(HavenProperties.Column, 1);
            top.Add(when);
            card.Add(top);
            card.Add(new HavenText(string.IsNullOrWhiteSpace(message.PlainTextBody) ? "(No plain-text body available.)" : message.PlainTextBody) { Level = TextLevel.Paragraph });
            target.Add(card);
        }
    }

    private Container BuildComposePanel()
    {
        var panel = Card("Mail.Compose");
        panel.SetValue(HavenProperties.Width, HavenLength.Px(700));
        panel.SetValue(HavenProperties.MaxWidth, HavenLength.Percent(96));
        panel.SetValue(HavenProperties.Height, HavenLength.Percent(92));
        panel.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        panel.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        panel.SetValue(HavenProperties.Margin, HavenThickness.Parse("18px"));
        panel.SetValue(HavenProperties.ZIndex, 20);
        panel.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        var head = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        head.Add(Heading(null, "Compose", TextLevel.H2));
        var close = Action("Mail.Compose.Close", "Close", ButtonVariant.Ghost, (_, _) => _viewModel.CloseComposeCommand.Execute(null));
        close.SetValue(HavenProperties.Column, 1);
        head.Add(close);
        panel.Add(head);
        panel.Add(Muted(null, "Drafts are provider-backed when available. Sending always requires confirmation."));
        _to = ComposeInput("Mail.Compose.To", "To (separate recipients with ; )", value => _viewModel.ComposeTo = value);
        _cc = ComposeInput("Mail.Compose.Cc", "Cc", value => _viewModel.ComposeCc = value);
        _bcc = ComposeInput("Mail.Compose.Bcc", "Bcc", value => _viewModel.ComposeBcc = value);
        _subject = ComposeInput("Mail.Compose.Subject", "Subject", value => _viewModel.ComposeSubject = value);
        _messageBody = ComposeInput("Mail.Compose.Body", "Write your message", value => _viewModel.ComposeBody = value);
        _messageBody.Multiline = true;
        _messageBody.SetValue(HavenProperties.MinHeight, HavenLength.Px(260));
        panel.Add(_to); panel.Add(_cc); panel.Add(_bcc); panel.Add(_subject); panel.Add(_messageBody);
        _composeAttachments = new Container { Name = "Mail.Compose.Attachments", Layout = HavenLayout.Wrap };
        _composeAttachments.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        panel.Add(_composeAttachments);
        _composeStatus = Muted("Mail.Compose.Status", string.Empty);
        panel.Add(_composeStatus);
        var actions = new Container { Layout = HavenLayout.Wrap };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        if (_viewModel.CanUseAttachments) actions.Add(Action("Mail.Compose.Attach", "Attach", ButtonVariant.Secondary, (_, _) => AttachRequested?.Invoke(this, EventArgs.Empty)));
        if (_viewModel.CanSaveDraft) actions.Add(Action("Mail.Compose.Save", "Save draft", ButtonVariant.Secondary, (_, _) => _viewModel.SaveDraftCommand.Execute(null)));
        if (_viewModel.CanSend) actions.Add(Action("Mail.Compose.Send", "Send", ButtonVariant.Primary, (_, _) => _viewModel.RequestSendCommand.Execute(null)));
        panel.Add(actions);
        return panel;
    }

    private Container BuildSendConfirmation()
    {
        var overlay = new Container { Name = "Mail.SendConfirmation", Layout = HavenLayout.Overlay };
        overlay.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.Background, "Overlay");
        overlay.SetValue(HavenProperties.ZIndex, 30);
        var card = Card("Mail.SendConfirmation.Card");
        card.SetValue(HavenProperties.Width, HavenLength.Px(440));
        card.SetValue(HavenProperties.MaxWidth, HavenLength.Percent(92));
        card.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        card.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        card.Add(Heading(null, "Send this email?", TextLevel.H2));
        card.Add(Muted(null, "Sending is a real external action. Check recipients and subject before confirming."));
        var actions = new Container { Layout = HavenLayout.Wrap };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        actions.Add(Action("Mail.SendConfirmation.Cancel", "Go back", ButtonVariant.Secondary, (_, _) => _viewModel.CancelSendCommand.Execute(null)));
        actions.Add(Action("Mail.SendConfirmation.Confirm", "Confirm send", ButtonVariant.Primary, (_, _) => _viewModel.ConfirmSendCommand.Execute(null)));
        card.Add(actions);
        overlay.Add(card);
        return overlay;
    }

    private void RenderComposeAttachments()
    {
        Clear(_composeAttachments);
        foreach (var attachment in _viewModel.ComposeAttachments)
        {
            var row = new Container { Layout = HavenLayout.Horizontal };
            row.SetValue(HavenProperties.Gap, HavenLength.Px(6));
            row.Add(Muted(null, attachment.FileName + " · " + attachment.SizeLabel));
            row.Add(Action("Mail.Compose.Attachment.Remove." + attachment.FileName, "Remove", ButtonVariant.Ghost, (_, _) => _viewModel.RemoveComposeAttachmentCommand.Execute(attachment)));
            _composeAttachments.Add(row);
        }
    }

    private Input ComposeInput(string name, string placeholder, Action<string> update)
    {
        var input = new Input { Name = name, Placeholder = placeholder };
        input.TextChanged += (_, _) =>
        {
            if (_syncingInputs) return;
            update(input.Text);
            _viewModel.NotifyComposeChanged();
        };
        return input;
    }

    private void SyncInputs()
    {
        _syncingInputs = true;
        try
        {
            _search.Text = _viewModel.SearchText;
            _to.Text = _viewModel.ComposeTo;
            _cc.Text = _viewModel.ComposeCc;
            _bcc.Text = _viewModel.ComposeBcc;
            _subject.Text = _viewModel.ComposeSubject;
            _messageBody.Text = _viewModel.ComposeBody;
        }
        finally { _syncingInputs = false; }
    }

    private void OnSearchTextChanged(object? sender, EventArgs e)
    {
        if (!_syncingInputs) _viewModel.SearchText = _search.Text;
    }

    private void ToggleUnread()
    {
        _viewModel.UnreadOnly = !_viewModel.UnreadOnly;
        _viewModel.SearchCommand.Execute(null);
        Refresh();
    }

    private void ToggleFlagged()
    {
        _viewModel.FlaggedOnly = !_viewModel.FlaggedOnly;
        _viewModel.SearchCommand.Execute(null);
        Refresh();
    }

    private static void Clear(Container container)
    {
        foreach (var child in container.Children.ToArray()) container.Remove(child);
    }

    private static Container Card(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        card.SetValue(HavenProperties.Shadow, "Card");
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(9));
        return card;
    }

    private static HavenText Heading(string? name, string content, TextLevel level) => new(content) { Name = name, Level = level };
    private static HavenText Muted(string? name, string content)
    {
        var text = new HavenText(content) { Name = name, Level = TextLevel.Caption };
        text.SetValue(HavenProperties.Foreground, "TextSecondary");
        return text;
    }

    private static HavenButton Action(string name, string content, ButtonVariant variant, EventHandler handler)
    {
        var button = new HavenButton { Name = name, Content = content, Variant = variant };
        button.Invoked += handler;
        return button;
    }

    private sealed class MessageTile : Container
    {
        public MessageTile(string accessibleName)
        {
            Accessibility.Role = HavenAccessibleRole.Button;
            Accessibility.Focusable = true;
            Accessibility.AccessibleName = accessibleName;
            SetValue(HavenProperties.Hover, true, HavenValueSource.Default);
            SetValue(HavenProperties.Cursor, HavenCursor.Pointer, HavenValueSource.Default);
            SetValue(HavenProperties.Transition, ButtonDefaults.HoverTransition, HavenValueSource.Default);
        }

        protected override void OnStateChanged()
        {
            ClearValue(HavenProperties.Scale, HavenValueSource.State);
            if (State.HasFlag(HavenElementState.Hover)) SetValue(HavenProperties.Scale, 1.006d, HavenValueSource.State);
            if (State.HasFlag(HavenElementState.Pressed)) SetValue(HavenProperties.Scale, .992d, HavenValueSource.State);
        }
    }

    private sealed record MailboxView(Container Root, Container FolderChips, Container? Folders, Container Messages, Container Reading);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _search.TextChanged -= OnSearchTextChanged;
    }
}
