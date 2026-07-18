/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/BrowserTabHostManager.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns IBrowserTabSession, BrowserTabSession, BrowserTabHostManager. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines the browser tab session contract so callers depend on a capability rather than one implementation.
/// </summary>
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

/// <summary>
/// Represents browser tab session and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserTabSession : IBrowserTabSession
{
    /// <summary>
    /// Stores title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _title = "New Tab";
    /// <summary>
    /// Stores url locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _url = "about:blank";
    /// <summary>
    /// Stores favicon locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string? _favicon;
    /// <summary>
    /// Stores is loading locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isLoading;
    /// <summary>
    /// Stores is suspended locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isSuspended;
    /// <summary>
    /// Stores last active at locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset _lastActiveAt = DateTimeOffset.UtcNow;
    /// <summary>
    /// Stores history locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly List<string> _history = [];
    /// <summary>
    /// Stores history index locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _historyIndex = -1;

    public BrowserTabSession(Guid id, BrowserTabPrivacy privacy)
    {
        Id = id;
        Privacy = privacy;
        _history.Add(_url);
        _historyIndex = 0;
    }

    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; }
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get => _title; set => _title = value; }
    /// <summary>
    /// Gets or updates url, the bindable or domain state represented by this property.
    /// </summary>
    public string Url { get => _url; set => _url = value; }
    /// <summary>
    /// Gets or updates favicon, the bindable or domain state represented by this property.
    /// </summary>
    public string? Favicon { get => _favicon; set => _favicon = value; }
    /// <summary>
    /// Gets or updates privacy, the bindable or domain state represented by this property.
    /// </summary>
    public BrowserTabPrivacy Privacy { get; }
    /// <summary>
    /// Reports whether loading applies to the current state.
    /// </summary>
    public bool IsLoading { get => _isLoading; set => _isLoading = value; }
    /// <summary>
    /// Reports whether suspended applies to the current state.
    /// </summary>
    public bool IsSuspended { get => _isSuspended; private set => _isSuspended = value; }
    /// <summary>
    /// Gets or updates last active at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset LastActiveAt { get => _lastActiveAt; set => _lastActiveAt = value; }
    /// <summary>
    /// Gets or updates history, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<string> History => _history;
    /// <summary>
    /// Reports whether go back applies to the current state.
    /// </summary>
    public bool CanGoBack => _historyIndex > 0;
    /// <summary>
    /// Reports whether go forward applies to the current state.
    /// </summary>
    public bool CanGoForward => _historyIndex < _history.Count - 1;

    /// <summary>
    /// Performs navigate asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs back asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs forward asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs reload asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        _lastActiveAt = DateTimeOffset.UtcNow;
        _isLoading = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves visible text async for the current operation.
    /// </summary>
    public Task<string> GetVisibleTextAsync(CancellationToken cancellationToken)
    {
        _lastActiveAt = DateTimeOffset.UtcNow;
        return Task.FromResult(string.Empty);
    }

    /// <summary>
    /// Retrieves html async for the current operation.
    /// </summary>
    public Task<string> GetHtmlAsync(CancellationToken cancellationToken)
    {
        _lastActiveAt = DateTimeOffset.UtcNow;
        return Task.FromResult(string.Empty);
    }

    /// <summary>
    /// Performs suspend asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task SuspendAsync()
    {
        _isSuspended = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs resume asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task ResumeAsync()
    {
        _isSuspended = false;
        _lastActiveAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() { }
}

/// <summary>
/// Represents browser tab host manager and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserTabHostManager : IBrowserTabHostManager
{
    /// <summary>
    /// Stores sessions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, IBrowserTabSession> _sessions = new();
    /// <summary>
    /// Stores active tab id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Guid _activeTabId;

    /// <summary>
    /// Gets or updates max tabs, the bindable or domain state represented by this property.
    /// </summary>
    public int MaxTabs { get; } = 20;

    /// <summary>
    /// Creates tab with the invariants required by its callers.
    /// </summary>
    public IBrowserTabSession CreateTab(BrowserTabPrivacy privacy = BrowserTabPrivacy.Standard)
    {
        var session = new BrowserTabSession(Guid.NewGuid(), privacy);
        _sessions[session.Id] = session;
        if (_sessions.Count == 1) _activeTabId = session.Id;
        return session;
    }

    /// <summary>
    /// Retrieves tab for the current operation.
    /// </summary>
    public IBrowserTabSession? GetTab(Guid tabId)
    {
        return _sessions.TryGetValue(tabId, out var session) ? session : null;
    }

    /// <summary>
    /// Retrieves active tab for the current operation.
    /// </summary>
    public IBrowserTabSession? GetActiveTab()
    {
        return GetTab(_activeTabId);
    }

    /// <summary>
    /// Performs the set active tab step owned by this component.
    /// </summary>
    public void SetActiveTab(Guid tabId)
    {
        if (_sessions.ContainsKey(tabId)) _activeTabId = tabId;
    }

    /// <summary>
    /// Retrieves all tabs for the current operation.
    /// </summary>
    public IReadOnlyList<IBrowserTabSession> GetAllTabs()
    {
        return _sessions.Values.ToArray();
    }

    /// <summary>
    /// Performs the close tab step owned by this component.
    /// </summary>
    public void CloseTab(Guid tabId)
    {
        if (_sessions.TryRemove(tabId, out var session))
        {
            session.Dispose();
            if (_activeTabId == tabId)
                _activeTabId = _sessions.Keys.FirstOrDefault();
        }
    }

    /// <summary>
    /// Performs the suspend background tabs step owned by this component.
    /// </summary>
    public void SuspendBackgroundTabs(Guid activeTabId)
    {
        foreach (var (id, session) in _sessions)
        {
            if (id != activeTabId && !session.IsSuspended)
                _ = session.SuspendAsync();
        }
    }

    /// <summary>
    /// Performs the resume tab step owned by this component.
    /// </summary>
    public void ResumeTab(Guid tabId)
    {
        if (_sessions.TryGetValue(tabId, out var session) && session.IsSuspended)
            _ = session.ResumeAsync();
    }

    /// <summary>
    /// Retrieves active tab count async for the current operation.
    /// </summary>
    public Task<int> GetActiveTabCountAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_sessions.Count);
    }

    /// <summary>
    /// Reports whether complete async applies to the current state.
    /// </summary>
    public Task<bool> CanCompleteAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_sessions.Count < MaxTabs);
    }

    /// <summary>
    /// Retrieves completion summary async for the current operation.
    /// </summary>
    public Task<string> GetCompletionSummaryAsync(CancellationToken cancellationToken)
    {
        var tabs = _sessions.Values.ToArray();
        var summary = $"Active tabs: {tabs.Length}";
        if (tabs.Length > 0)
            summary += $" | Active: {tabs.FirstOrDefault(t => t.Id == _activeTabId)?.Title ?? "None"}";
        return Task.FromResult(summary);
    }
}
