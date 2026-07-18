/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Browser/BrowserDataService.cs, in the Browser layer, which isolates browser state, safety policy, transport, and automation.
 * What: This file owns BrowserBookmark, BrowserHistoryEntry, BrowserTabState, SavedLogin, BrowserExtensionDefinition, BrowserSettings, BrowserDataService, BrowserData, WindowsCredentialVault, Credential. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Browser capabilities are isolated behind explicit policy boundaries because navigation and automation process untrusted external content.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

/// <summary>
/// Represents browser bookmark and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserBookmark(Guid Id, string Title, string Address, string Group, DateTimeOffset CreatedAt);
/// <summary>
/// Represents browser history entry and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserHistoryEntry(Guid Id, string Title, string Address, DateTimeOffset VisitedAt);
/// <summary>
/// Represents browser tab state and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserTabState(Guid Id, string Title, string Address, BrowserTabPrivacy Privacy, string Group, DateTimeOffset UpdatedAt);
/// <summary>
/// Represents saved login and keeps its related state and behavior together.
/// </summary>
public sealed record SavedLogin(Guid Id, string Origin, string Username, DateTimeOffset UpdatedAt);
/// <summary>
/// Represents browser extension definition and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserExtensionDefinition(Guid Id, string Name, string Description, IReadOnlyList<string> AllowedOrigins,
    string Script, bool IsEnabled, bool ConvertedFromChrome, DateTimeOffset UpdatedAt);
/// <summary>
/// Represents browser settings and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserSettings(string HomePage, string SearchTemplate, bool SaveHistory, bool OfferToSaveLogins,
    bool RestoreTabs, bool EnableExtensions, bool VerticalTabs)
{
    /// <summary>
    /// Gets or updates default, the bindable or domain state represented by this property.
    /// </summary>
    public static BrowserSettings Default { get; } = new("https://www.google.com", "https://www.google.com/search?q={query}", true, true, true, true, false);
}

/// <summary>
/// Represents browser data service and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserDataService : IDisposable
{
    /// <summary>
    /// Stores current schema version locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int CurrentSchemaVersion = 1;
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    /// <summary>
    /// Stores path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _path;
    /// <summary>
    /// Stores backup path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _backupPath;
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>
    /// Stores data locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private BrowserData _data;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    public BrowserDataService(IAppPaths paths)
    {
        _path = Path.Combine(paths.DataDirectory, "browser-data.json");
        _backupPath = _path + ".bak";
        _data = Load();
    }

    /// <summary>
    /// Gets or updates bookmarks, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<BrowserBookmark> Bookmarks => _data.Bookmarks.OrderBy(item => item.Group).ThenBy(item => item.Title).ToArray();
    /// <summary>
    /// Gets or updates history, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<BrowserHistoryEntry> History => _data.History.OrderByDescending(item => item.VisitedAt).ToArray();
    /// <summary>
    /// Gets or updates tabs, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<BrowserTabState> Tabs => _data.Tabs.OrderBy(item => item.UpdatedAt).ToArray();
    /// <summary>
    /// Gets or updates logins, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<SavedLogin> Logins => _data.Logins.OrderBy(item => item.Origin).ThenBy(item => item.Username).ToArray();
    /// <summary>
    /// Gets or updates extensions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<BrowserExtensionDefinition> Extensions => _data.Extensions.OrderBy(item => item.Name).ToArray();
    /// <summary>
    /// Gets or updates settings, the bindable or domain state represented by this property.
    /// </summary>
    public BrowserSettings Settings => _data.Settings;

    /// <summary>
    /// Performs add bookmark async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task AddBookmarkAsync(string title, string address, string group, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Bookmark address must be an HTTP or HTTPS URL.", nameof(address));

        return MutateAndSaveAsync(data =>
        {
            var existing = data.Bookmarks.FirstOrDefault(item => item.Address.Equals(uri.ToString(), StringComparison.OrdinalIgnoreCase));
            var bookmark = new BrowserBookmark(existing?.Id ?? Guid.NewGuid(), string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim(), uri.ToString(),
                string.IsNullOrWhiteSpace(group) ? "Bookmarks" : group.Trim(), existing?.CreatedAt ?? DateTimeOffset.UtcNow);
            return data with { Bookmarks = data.Bookmarks.Where(item => item.Id != bookmark.Id).Append(bookmark).ToArray() };
        }, cancellationToken);
    }

    /// <summary>
    /// Performs remove bookmark async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task RemoveBookmarkAsync(Guid id, CancellationToken cancellationToken) =>
        MutateAndSaveAsync(data => data with { Bookmarks = data.Bookmarks.Where(item => item.Id != id).ToArray() }, cancellationToken);

    /// <summary>
    /// Performs record visit async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task RecordVisitAsync(string title, string address, bool isPrivate, CancellationToken cancellationToken)
    {
        if (isPrivate || !_data.Settings.SaveHistory || !Uri.TryCreate(address, UriKind.Absolute, out _)) return Task.CompletedTask;
        return MutateAndSaveAsync(data => data with
        {
            History = data.History.Prepend(new BrowserHistoryEntry(Guid.NewGuid(), string.IsNullOrWhiteSpace(title) ? address : title.Trim(), address,
                DateTimeOffset.UtcNow)).Take(2000).ToArray()
        }, cancellationToken);
    }

    /// <summary>
    /// Performs clear history async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task ClearHistoryAsync(CancellationToken cancellationToken) =>
        MutateAndSaveAsync(data => data with { History = [] }, cancellationToken);

    /// <summary>
    /// Performs save tabs async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task SaveTabsAsync(IEnumerable<BrowserTabState> tabs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var safeTabs = tabs.Where(item => item.Privacy != BrowserTabPrivacy.Private)
            .Where(item => Uri.TryCreate(item.Address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .GroupBy(item => item.Id)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .OrderByDescending(item => item.UpdatedAt)
            .Take(60)
            .ToArray();
        return MutateAndSaveAsync(data => data with { Tabs = safeTabs }, cancellationToken);
    }

    /// <summary>
    /// Performs save settings async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task SaveSettingsAsync(BrowserSettings settings, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(settings.HomePage, UriKind.Absolute, out var home) || home.Scheme is not ("http" or "https"))
            throw new ArgumentException("Home page must be an HTTP or HTTPS URL.", nameof(settings));
        if (!settings.SearchTemplate.Contains("{query}", StringComparison.Ordinal))
            throw new ArgumentException("Search template must contain {query}.", nameof(settings));
        return MutateAndSaveAsync(data => data with { Settings = settings }, cancellationToken);
    }

    /// <summary>
    /// Performs save login async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SaveLoginAsync(string origin, string username, string password, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Secure browser login storage currently requires Windows Credential Manager.");
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("Login origin is invalid.");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) throw new ArgumentException("Username and password are required.");

        var canonicalOrigin = uri.GetLeftPart(UriPartial.Authority);
        var existing = _data.Logins.FirstOrDefault(item => item.Origin.Equals(canonicalOrigin, StringComparison.OrdinalIgnoreCase) && item.Username.Equals(username, StringComparison.Ordinal));
        var login = new SavedLogin(existing?.Id ?? Guid.NewGuid(), canonicalOrigin, username.Trim(), DateTimeOffset.UtcNow);
        var target = Target(login);
        WindowsCredentialVault.Write(target, login.Username, password);
        try
        {
            await MutateAndSaveAsync(data => data with { Logins = data.Logins.Where(item => item.Id != login.Id).Append(login).ToArray() }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            WindowsCredentialVault.Delete(target);
            throw;
        }
    }

    /// <summary>
    /// Performs the read password step owned by this component.
    /// </summary>
    public string? ReadPassword(SavedLogin login) => OperatingSystem.IsWindows() ? WindowsCredentialVault.Read(Target(login)) : null;

    /// <summary>
    /// Performs delete login async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteLoginAsync(SavedLogin login, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(login);
        var password = OperatingSystem.IsWindows() ? WindowsCredentialVault.Read(Target(login)) : null;
        await MutateAndSaveAsync(data => data with { Logins = data.Logins.Where(item => item.Id != login.Id).ToArray() }, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (OperatingSystem.IsWindows()) WindowsCredentialVault.Delete(Target(login));
        }
        catch
        {
            if (password is not null)
                await MutateAndSaveAsync(data => data with { Logins = data.Logins.Append(login).ToArray() }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Performs import haven extension async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<BrowserExtensionDefinition> ImportHavenExtensionAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var fullManifest = Path.GetFullPath(manifestPath);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fullManifest, cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var description = root.TryGetProperty("description", out var descriptionElement) ? descriptionElement.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Extension manifest requires a name.");
        var allowed = ReadStringArray(root, "allowedOrigins");
        var scriptPath = root.TryGetProperty("script", out var scriptElement) ? scriptElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(scriptPath)) throw new InvalidOperationException("Extension manifest requires a script path.");
        var script = await ReadInsideAsync(Path.GetDirectoryName(fullManifest)!, scriptPath, cancellationToken).ConfigureAwait(false);
        return await SaveExtensionAsync(new BrowserExtensionDefinition(Guid.NewGuid(), name.Trim(), description.Trim(), allowed, script,
            false, false, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs convert chrome extension async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<BrowserExtensionDefinition> ConvertChromeExtensionAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var fullManifest = Path.GetFullPath(manifestPath);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fullManifest, cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        var forbidden = new[] { "nativeMessaging", "proxy", "debugger", "webRequestBlocking", "management", "downloads.open" };
        var permissions = ReadStringArray(root, "permissions");
        var denied = permissions.Where(permission => forbidden.Contains(permission, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (denied.Length > 0) throw new InvalidOperationException("Chrome extension requests capabilities Haven will not grant: " + string.Join(", ", denied));
        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Chrome manifest requires a name.");
        var description = root.TryGetProperty("description", out var descriptionElement) ? descriptionElement.GetString() ?? string.Empty : string.Empty;
        var matches = new List<string>();
        var scripts = new StringBuilder();
        if (root.TryGetProperty("content_scripts", out var contentScripts) && contentScripts.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in contentScripts.EnumerateArray())
            {
                matches.AddRange(ReadStringArray(item, "matches"));
                foreach (var scriptPath in ReadStringArray(item, "js"))
                {
                    scripts.AppendLine(await ReadInsideAsync(Path.GetDirectoryName(fullManifest)!, scriptPath, cancellationToken).ConfigureAwait(false));
                    if (scripts.Length > 512_000) throw new InvalidOperationException("Converted extension scripts exceed Haven's 512 KB limit.");
                }
            }
        }
        if (scripts.Length == 0) throw new InvalidOperationException("No convertible content script was found. Background services, native messaging, and privileged Chrome APIs are intentionally unsupported.");
        return await SaveExtensionAsync(new BrowserExtensionDefinition(Guid.NewGuid(), name.Trim(), description.Trim(), matches.Distinct().ToArray(),
            scripts.ToString(), false, true, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs set extension enabled async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task SetExtensionEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken) =>
        MutateAndSaveAsync(data => data with
        {
            Extensions = data.Extensions.Select(item => item.Id == id ? item with { IsEnabled = enabled, UpdatedAt = DateTimeOffset.UtcNow } : item).ToArray()
        }, cancellationToken);

    /// <summary>
    /// Performs delete extension async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteExtensionAsync(Guid id, CancellationToken cancellationToken) =>
        MutateAndSaveAsync(data => data with { Extensions = data.Extensions.Where(item => item.Id != id).ToArray() }, cancellationToken);

    /// <summary>
    /// Retrieves scripts for for the current operation.
    /// </summary>
    public IReadOnlyList<BrowserExtensionDefinition> GetScriptsFor(Uri address)
    {
        if (!_data.Settings.EnableExtensions) return [];
        return _data.Extensions.Where(item => item.IsEnabled && item.AllowedOrigins.Any(pattern => OriginMatches(pattern, address))).ToArray();
    }

    /// <summary>
    /// Performs save extension async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<BrowserExtensionDefinition> SaveExtensionAsync(BrowserExtensionDefinition extension, CancellationToken cancellationToken)
    {
        await MutateAndSaveAsync(data => data with
        {
            Extensions = data.Extensions.Where(item => item.Id != extension.Id).Append(extension).ToArray()
        }, cancellationToken).ConfigureAwait(false);
        return extension;
    }

    /// <summary>
    /// Performs the load step owned by this component.
    /// </summary>
    private BrowserData Load()
    {
        if (TryLoad(_path, out var primary)) return primary;

        QuarantineInvalidPrimary();
        if (TryLoad(_backupPath, out var backup)) return backup;
        return BrowserData.Empty;
    }

    /// <summary>
    /// Performs the normalize step owned by this component.
    /// </summary>
    private static BrowserData Normalize(BrowserData data)
    {
        if (data.SchemaVersion > CurrentSchemaVersion)
            throw new JsonException($"Browser data schema {data.SchemaVersion} is newer than supported schema {CurrentSchemaVersion}.");

        return data with
        {
            SchemaVersion = CurrentSchemaVersion,
            Bookmarks = data.Bookmarks ?? [],
            History = (data.History ?? []).Take(2000).ToArray(),
            Tabs = (data.Tabs ?? []).Where(item => item.Privacy != BrowserTabPrivacy.Private).Take(60).ToArray(),
            Logins = data.Logins ?? [],
            Extensions = data.Extensions ?? [],
            Settings = data.Settings ?? BrowserSettings.Default
        };
    }

    /// <summary>
    /// Attempts to deserialize and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryDeserialize(string path, out BrowserData data)
    {
        data = BrowserData.Empty;
        if (!File.Exists(path)) return false;
        var parsed = JsonSerializer.Deserialize<BrowserData>(File.ReadAllText(path), JsonOptions);
        if (parsed is null) return false;
        data = Normalize(parsed);
        return true;
    }

    /// <summary>
    /// Reports whether is recoverable load failure is true for the current state.
    /// </summary>
    private static bool IsRecoverableLoadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException;

    /// <summary>
    /// Attempts to load and reports the result without using failure for normal control flow.
    /// </summary>
    private bool TryLoad(string path, out BrowserData data)
    {
        try { return TryDeserialize(path, out data); }
        catch (Exception ex) when (IsRecoverableLoadFailure(ex))
        {
            data = BrowserData.Empty;
            return false;
        }
    }

    /// <summary>
    /// Performs the quarantine invalid primary step owned by this component.
    /// </summary>
    private void QuarantineInvalidPrimary()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var quarantine = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Move(_path, quarantine, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Performs mutate and save async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task MutateAndSaveAsync(Func<BrowserData, BrowserData> mutation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var original = _data;
            var candidate = Normalize(mutation(original));
            try
            {
                await SaveCoreAsync(candidate, cancellationToken).ConfigureAwait(false);
                _data = candidate;
            }
            catch
            {
                _data = original;
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs save core async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SaveCoreAsync(BrowserData candidate, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(candidate, JsonOptions), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_path))
                File.Replace(temporary, _path, _backupPath, true);
            else
                File.Move(temporary, _path, false);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Performs the read string array step owned by this component.
    /// </summary>
    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).OfType<string>().ToArray();
    }

    /// <summary>
    /// Performs read inside async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<string> ReadInsideAsync(string root, string relative, CancellationToken cancellationToken)
    {
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(canonicalRoot, relative));
        if (!path.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Extension script escapes its source folder.");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 512_000) throw new InvalidOperationException("Extension script is missing or too large.");
        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the origin matches step owned by this component.
    /// </summary>
    private static bool OriginMatches(string pattern, Uri address)
    {
        if (pattern == "<all_urls>" || pattern is "http://*/*" or "https://*/*") return true;
        if (Uri.TryCreate(pattern.Replace("*.", string.Empty, StringComparison.Ordinal), UriKind.Absolute, out var uri))
            return address.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase) || address.Host.EndsWith("." + uri.Host, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    /// <summary>
    /// Performs the target step owned by this component.
    /// </summary>
    private static string Target(SavedLogin login) => $"Haven.Browser|{login.Origin}|{login.Id:N}";

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }

    /// <summary>
    /// Represents browser data and keeps its related state and behavior together.
    /// </summary>
    private sealed record BrowserData(IReadOnlyList<BrowserBookmark> Bookmarks, IReadOnlyList<BrowserHistoryEntry> History,
        IReadOnlyList<BrowserTabState> Tabs, IReadOnlyList<SavedLogin> Logins, IReadOnlyList<BrowserExtensionDefinition> Extensions,
        BrowserSettings Settings, int SchemaVersion = CurrentSchemaVersion)
    {
        /// <summary>
        /// Gets or updates empty, the bindable or domain state represented by this property.
        /// </summary>
        public static BrowserData Empty { get; } = new([], [], [], [], [], BrowserSettings.Default, CurrentSchemaVersion);
    }
}

/// <summary>
/// Represents windows credential vault and keeps its related state and behavior together.
/// </summary>
internal static class WindowsCredentialVault
{
    /// <summary>
    /// Stores generic locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const uint Generic = 1;
    /// <summary>
    /// Stores persist local machine locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const uint PersistLocalMachine = 2;

    /// <summary>
    /// Performs the write step owned by this component.
    /// </summary>
    public static void Write(string target, string username, string password)
    {
        var bytes = Encoding.Unicode.GetBytes(password);
        if (bytes.Length > 512) throw new ArgumentException("Password exceeds Windows Credential Manager's generic credential limit.");
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = Generic,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = username
            };
            if (!CredWrite(ref credential, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    /// <summary>
    /// Performs the read step owned by this component.
    /// </summary>
    public static string? Read(string target)
    {
        if (!CredRead(target, Generic, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return credential.CredentialBlob == IntPtr.Zero ? null : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally { CredFree(pointer); }
    }

    /// <summary>
    /// Performs the delete step owned by this component.
    /// </summary>
    public static void Delete(string target)
    {
        if (!CredDelete(target, Generic, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168) throw new System.ComponentModel.Win32Exception(error);
        }
    }

    /// <summary>
    /// Represents credential and keeps its related state and behavior together.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        /// <summary>
        /// Stores flags locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public uint Flags;
        /// <summary>
        /// Stores type locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public uint Type;
        /// <summary>
        /// Stores target name locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public string TargetName;
        /// <summary>
        /// Stores comment locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public string? Comment;
        /// <summary>
        /// Stores last written locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        /// <summary>
        /// Stores credential blob size locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public uint CredentialBlobSize;
        /// <summary>
        /// Stores credential blob locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public IntPtr CredentialBlob;
        /// <summary>
        /// Stores persist locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public uint Persist;
        /// <summary>
        /// Stores attribute count locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public uint AttributeCount;
        /// <summary>
        /// Stores attributes locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public IntPtr Attributes;
        /// <summary>
        /// Stores target alias locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public string? TargetAlias;
        /// <summary>
        /// Stores user name locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public string UserName;
    }

    /// <summary>
    /// Performs the cred write step owned by this component.
    /// </summary>
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref Credential credential, uint flags);
    /// <summary>
    /// Performs the cred read step owned by this component.
    /// </summary>
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    /// <summary>
    /// Performs the cred delete step owned by this component.
    /// </summary>
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    /// <summary>
    /// Performs the cred free step owned by this component.
    /// </summary>
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
