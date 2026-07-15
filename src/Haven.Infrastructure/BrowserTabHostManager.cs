using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

public interface IBrowserTabSession : IDisposable
{
    Guid Id { get; }
    string Title { get; }
    string Url { get; }
    string? Favicon { get; }
    BrowserTabPrivacy Privacy { get; }
    bool IsLoading { get; }
    bool IsSuspended { get; }
    DateTimeOffset LastActiveAt { get; }
    Task NavigateAsync(string url, CancellationToken cancellationToken);
    Task BackAsync(CancellationToken cancellationToken);
    Task ForwardAsync(CancellationToken cancellationToken);
    Task ReloadAsync(CancellationToken cancellationToken);
    Task<string> GetVisibleTextAsync(CancellationToken cancellationToken);
    Task<string> GetHtmlAsync(CancellationToken cancellationToken);
    Task SuspendAsync();
    Task ResumeAsync();
}

public sealed class BrowserTabSession : IBrowserTabSession
{
    private string _title = "New Tab";
    private string _url = "about:blank";
    private string? _favicon;
    private bool _isLoading;
    private bool _isSuspended;
    private DateTimeOffset _lastActiveAt = DateTimeOffset.UtcNow;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;

    public BrowserTabSession(Guid id, BrowserTabPrivacy privacy)
    {
        Id = id;
        Privacy = privacy;
        _history.Add(_url);
        _historyIndex = 0;
    }

    public Guid Id { get; }
    public string Title { get => _title; set => _title = value; }
    public string Url { get => _url; set => _url = value; }
    public string? Favicon { get => _favicon; set => _favicon = value; }
    public BrowserTabPrivacy Privacy { get; }
    public bool IsLoading { get => _isLoading; set => _isLoading = value; }
    public bool IsSuspended { get => _isSuspended; private set => _isSuspended = value; }
    public DateTimeOffset LastActiveAt { get => _lastActiveAt; set => _lastActiveAt = value; }
    public IReadOnlyList<string> History => _history;
    public bool CanGoBack => _historyIndex > 0;
    public bool CanGoForward => _historyIndex < _history.Count - 1;

    public Task NavigateAsync(string url, CancellationToken cancellationToken)
    {
        _url = url;
        _lastActiveAt = DateTimeOffset.UtcNow;
        _isLoading = true;
        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(url);
        _historyIndex = _history.Count - 1;
        return Task.CompletedTask;
    }

    public Task BackAsync(CancellationToken cancellationToken)
    {
        if (CanGoBack)
        {
            _historyIndex--;
            _url = _history[_historyIndex];
            _lastActiveAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task ForwardAsync(CancellationToken cancellationToken)
    {
        if (CanGoForward)
        {
            _historyIndex++;
            _url = _history[_historyIndex];
            _lastActiveAt = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        _lastActiveAt = DateTimeOffset.UtcNow;
        _isLoading = true;
        return Task.CompletedTask;
    }

    public Task<string> GetVisibleTextAsync(CancellationToken cancellationToken)
    {
        _lastActiveAt = DateTimeOffset.UtcNow;
        return Task.FromResult(string.Empty);
    }

    public Task<string> GetHtmlAsync(CancellationToken cancellationToken)
    {
        _lastActiveAt = DateTimeOffset.UtcNow;
        return Task.FromResult(string.Empty);
    }

    public Task SuspendAsync()
    {
        _isSuspended = true;
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        _isSuspended = false;
        _lastActiveAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public void Dispose() { }
}

public sealed class BrowserTabHostManager : IBrowserTabHostManager
{
    private readonly ConcurrentDictionary<Guid, IBrowserTabSession> _sessions = new();
    private Guid _activeTabId;

    public int MaxTabs { get; } = 20;

    public IBrowserTabSession CreateTab(BrowserTabPrivacy privacy = BrowserTabPrivacy.Standard)
    {
        var session = new BrowserTabSession(Guid.NewGuid(), privacy);
        _sessions[session.Id] = session;
        if (_sessions.Count == 1) _activeTabId = session.Id;
        return session;
    }

    public IBrowserTabSession? GetTab(Guid tabId)
    {
        return _sessions.TryGetValue(tabId, out var session) ? session : null;
    }

    public IBrowserTabSession? GetActiveTab()
    {
        return GetTab(_activeTabId);
    }

    public void SetActiveTab(Guid tabId)
    {
        if (_sessions.ContainsKey(tabId)) _activeTabId = tabId;
    }

    public IReadOnlyList<IBrowserTabSession> GetAllTabs()
    {
        return _sessions.Values.ToArray();
    }

    public void CloseTab(Guid tabId)
    {
        if (_sessions.TryRemove(tabId, out var session))
        {
            session.Dispose();
            if (_activeTabId == tabId)
                _activeTabId = _sessions.Keys.FirstOrDefault();
        }
    }

    public void SuspendBackgroundTabs(Guid activeTabId)
    {
        foreach (var (id, session) in _sessions)
        {
            if (id != activeTabId && !session.IsSuspended)
                _ = session.SuspendAsync();
        }
    }

    public void ResumeTab(Guid tabId)
    {
        if (_sessions.TryGetValue(tabId, out var session) && session.IsSuspended)
            _ = session.ResumeAsync();
    }

    public Task<int> GetActiveTabCountAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_sessions.Count);
    }

    public Task<bool> CanCompleteAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_sessions.Count < MaxTabs);
    }

    public Task<string> GetCompletionSummaryAsync(CancellationToken cancellationToken)
    {
        var tabs = _sessions.Values.ToArray();
        var summary = $"Active tabs: {tabs.Length}";
        if (tabs.Length > 0)
            summary += $" | Active: {tabs.FirstOrDefault(t => t.Id == _activeTabId)?.Title ?? "None"}";
        return Task.FromResult(summary);
    }
}
