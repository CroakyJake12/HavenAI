using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Android.Widget;
using System.Collections.Concurrent;

namespace Haven.Android;

internal sealed record AndroidSharedContextPayload(string? Text, IReadOnlyList<string> Files);

internal static class AndroidSharedContextStore
{
    private static AndroidSharedContextPayload? _pending;

    public static void Enqueue(AndroidSharedContextPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Interlocked.Exchange(ref _pending, payload);
    }

    public static AndroidSharedContextPayload? Take() =>
        Interlocked.Exchange(ref _pending, null);
}

[Activity(
    Label = "Ask Haven",
    Theme = "@style/Theme.AppCompat.Light.NoActionBar",
    Icon = "@drawable/haven_icon",
    Exported = true,
    NoHistory = true,
    ExcludeFromRecents = true)]
[IntentFilter(
    new[] { Intent.ActionSend },
    Categories = new[] { Intent.CategoryDefault },
    DataMimeType = "*/*")]
[IntentFilter(
    new[] { Intent.ActionSendMultiple },
    Categories = new[] { Intent.CategoryDefault },
    DataMimeType = "*/*")]
[IntentFilter(
    new[] { Intent.ActionProcessText },
    Categories = new[] { Intent.CategoryDefault },
    DataMimeType = "text/plain")]
public sealed class HavenShareActivity : Activity
{
    private const long MaximumSharedPayloadBytes = 750L * 1024 * 1024;
    private const int MaximumSharedFiles = 16;
    private static readonly TimeSpan StagingLifetime = TimeSpan.FromHours(24);

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        await ForwardAsync(Intent);
    }

    protected override async void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        await ForwardAsync(intent);
    }

    private async Task ForwardAsync(Intent? intent)
    {
        try
        {
            if (intent is null)
                return;

            var text = ReadSharedText(intent);
            var files = new List<string>();
            long stagedBytes = 0;
            var failures = 0;

            CleanupStaging();
            foreach (var uri in CollectSharedUris(intent).Take(MaximumSharedFiles))
            {
                try
                {
                    var remainingBytes = MaximumSharedPayloadBytes - stagedBytes;
                    if (remainingBytes <= 0)
                    {
                        failures++;
                        break;
                    }

                    var staged = await StageAsync(uri, files.Count, remainingBytes, CancellationToken.None);
                    files.Add(staged.Path);
                    stagedBytes += staged.SizeBytes;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    failures++;
                    global::Android.Util.Log.Warn("HavenShare", "Could not stage shared content: " + exception.Message);
                }
            }

            if (string.IsNullOrWhiteSpace(text) && files.Count == 0)
            {
                Toast.MakeText(this, "Nothing shareable was provided to Haven.", ToastLength.Short)?.Show();
                return;
            }

            AndroidSharedContextStore.Enqueue(new AndroidSharedContextPayload(text, files));

            var launch = new Intent(this, typeof(MainActivity));
            launch.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            launch.PutExtra("haven_surface", "assistant");
            StartActivity(launch);

            if (failures > 0)
                Toast.MakeText(this, "Some shared files could not be attached.", ToastLength.Long)?.Show();
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("HavenShare", "Share intake failed: " + exception);
            Toast.MakeText(this, "Haven could not open the shared content.", ToastLength.Long)?.Show();
        }
        finally
        {
            Finish();
        }
    }

    private static string? ReadSharedText(Intent intent)
    {
        var key = string.Equals(intent.Action, Intent.ActionProcessText, StringComparison.Ordinal)
            ? Intent.ExtraProcessText
            : Intent.ExtraText;
        var text = intent.GetCharSequenceExtra(key)?.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return text.Length <= 200_000 ? text : text[..200_000];
    }

    private static IReadOnlyList<global::Android.Net.Uri> CollectSharedUris(Intent intent)
    {
        var result = new List<global::Android.Net.Uri>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(global::Android.Net.Uri? uri)
        {
            if (uri is null || !string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
                return;
            var key = uri.ToString();
            if (!string.IsNullOrWhiteSpace(key) && seen.Add(key))
                result.Add(uri);
        }

        var clip = intent.ClipData;
        if (clip is not null)
        {
            for (var index = 0; index < clip.ItemCount; index++)
                Add(clip.GetItemAt(index)?.Uri);
        }

        Add(intent.Data);

        var extraStream = intent.Extras?.Get(Intent.ExtraStream);
        switch (extraStream)
        {
            case global::Android.Net.Uri single:
                Add(single);
                break;
            case System.Collections.IEnumerable multiple:
                foreach (var item in multiple)
                    if (item is global::Android.Net.Uri uri) Add(uri);
                break;
        }

        return result;
    }

    private async Task<StagedSharedFile> StageAsync(
        global::Android.Net.Uri uri,
        int index,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var cacheRoot = CacheDir?.AbsolutePath
            ?? throw new InvalidOperationException("Android cache storage is unavailable.");
        var directory = Path.Combine(cacheRoot, "haven-shared-intake");
        Directory.CreateDirectory(directory);

        var displayName = ResolveDisplayName(uri, index);
        var destination = Path.Combine(directory, $"{Guid.NewGuid():N}-{displayName}");

        try
        {
            await using var input = OpenInput(uri);
            await using var output = new FileStream(
                destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > maximumBytes)
                    throw new InvalidOperationException("The shared payload is larger than Haven's intake limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (total == 0)
                throw new InvalidOperationException("The shared file is empty.");
            return new StagedSharedFile(destination, total);
        }
        catch
        {
            try { if (File.Exists(destination)) File.Delete(destination); } catch { }
            throw;
        }
    }

    private Stream OpenInput(global::Android.Net.Uri uri)
    {
        if (!string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Haven only accepts Android-granted content URIs from the share sheet.");
        return ContentResolver?.OpenInputStream(uri)
            ?? throw new InvalidOperationException("The shared content provider did not expose a readable stream.");
    }

    private string ResolveDisplayName(global::Android.Net.Uri uri, int index)
    {
        string? name = null;
        try
        {
            using ICursor? cursor = ContentResolver?.Query(
                uri, new[] { IOpenableColumns.DisplayName }, null, null, null);
            if (cursor?.MoveToFirst() == true)
            {
                var column = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                if (column >= 0) name = cursor.GetString(column);
            }
        }
        catch
        {
        }

        name = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(name))
            name = $"shared-{index + 1}";

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? $"shared-{index + 1}" : safe;
    }

    private sealed record StagedSharedFile(string Path, long SizeBytes);

    private void CleanupStaging()
    {
        try
        {
            var cacheRoot = CacheDir?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(cacheRoot)) return;
            var directory = Path.Combine(cacheRoot, "haven-shared-intake");
            if (!Directory.Exists(directory)) return;
            var threshold = DateTime.UtcNow - StagingLifetime;
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < threshold) File.Delete(path);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
