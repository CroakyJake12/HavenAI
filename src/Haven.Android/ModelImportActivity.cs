using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Net;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using Uri = Android.Net.Uri;
using System.Text.Json;

namespace Haven.Android;

[Activity(
    Label = "Import models",
    Theme = "@style/Theme.AppCompat.Light.NoActionBar",
    Exported = false)]
public sealed class ModelImportActivity : Activity
{
    private const int PickModelsRequest = 8201;
    private const int PickFolderRequest = 8202;
    private const string IndexFileName = "imported-models.json";

    private LinearLayout? _list;
    private TextView? _status;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildUi();
        RefreshList();
    }

    private void BuildUi()
    {
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        root.SetPadding(Dp(18), Dp(20), Dp(18), Dp(18));
        root.SetBackgroundColor(Color.Rgb(24, 18, 38));

        var title = new TextView(this)
        {
            Text = "Add local models",
            TextSize = 25,
            Typeface = Typeface.DefaultBold
        };
        title.SetTextColor(Color.White);
        root.AddView(title);

        var explanation = new TextView(this)
        {
            Text = "Choose existing .gguf model files or a model folder. Haven copies selected models into its private model library so they remain available after the picker closes. Android does not allow Haven to silently read another app's private storage, so PocketPal models must be selected through the system file picker.",
            TextSize = 14
        };
        explanation.SetTextColor(Color.Argb(220, 225, 218, 240));
        explanation.SetPadding(0, Dp(8), 0, Dp(14));
        root.AddView(explanation);

        var buttonRow = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };

        var files = ActionButton("Choose GGUF files", PickModelFiles);
        files.LayoutParameters = new LinearLayout.LayoutParams(0, Dp(52), 1f)
        {
            RightMargin = Dp(5)
        };
        buttonRow.AddView(files);

        var folder = ActionButton("Choose folder", PickModelFolder);
        folder.LayoutParameters = new LinearLayout.LayoutParams(0, Dp(52), 1f)
        {
            LeftMargin = Dp(5)
        };
        buttonRow.AddView(folder);
        root.AddView(buttonRow);

        _status = new TextView(this)
        {
            Text = "No import running.",
            TextSize = 13
        };
        _status.SetTextColor(Color.Argb(220, 214, 193, 255));
        _status.SetPadding(0, Dp(12), 0, Dp(8));
        root.AddView(_status);

        var heading = new TextView(this)
        {
            Text = "Imported models",
            TextSize = 18,
            Typeface = Typeface.DefaultBold
        };
        heading.SetTextColor(Color.White);
        heading.SetPadding(0, Dp(8), 0, Dp(6));
        root.AddView(heading);

        _list = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        var scroll = new ScrollView(this);
        scroll.AddView(_list);
        root.AddView(scroll, new LinearLayout.Layout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1f));

        SetContentView(root);
    }

    private Button ActionButton(string label, Action action)
    {
        var button = new Button(this)
        {
            Text = label,
            AllCaps = false
        };
        button.SetTextColor(Color.White);
        button.SetBackgroundColor(Color.Rgb(89, 48, 145));
        button.Click += (_, _) => action();
        return button;
    }

    private void PickModelFiles()
    {
        var intent = new Intent(Intent.ActionOpenDocument)
            .AddCategory(Intent.CategoryOpenable)
            .SetType("application/octet-stream");
        intent.PutExtra(Intent.ExtraAllowMultiple, true);
        StartActivityForResult(intent, PickModelsRequest);
    }

    private void PickModelFolder()
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        StartActivityForResult(intent, PickFolderRequest);
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (resultCode != Result.Ok || data is null)
            return;

        try
        {
            _status!.Text = "Importing models…";

            if (requestCode == PickModelsRequest)
            {
                var uris = new List<Uri>();
                if (data.Data is not null)
                    uris.Add(data.Data);

                var clip = data.ClipData;
                if (clip is not null)
                {
                    for (var index = 0; index < clip.ItemCount; index++)
                    {
                        var uri = clip.GetItemAt(index)?.Uri;
                        if (uri is not null)
                            uris.Add(uri);
                    }
                }

                var imported = 0;
                foreach (var uri in uris.DistinctBy(item => item.ToString()))
                {
                    if (await ImportUriAsync(uri))
                        imported++;
                }

                _status.Text = imported == 0
                    ? "No compatible .gguf files were imported."
                    : $"Imported {imported} model file{(imported == 1 ? string.Empty : "s")}.";
            }
            else if (requestCode == PickFolderRequest && data.Data is Uri treeUri)
            {
                ContentResolver?.TakePersistableUriPermission(treeUri, ActivityFlags.GrantReadUriPermission);
                var imported = await ImportDocumentTreeAsync(treeUri);
                _status.Text = imported == 0
                    ? "No .gguf files were found in that folder."
                    : $"Imported {imported} model file{(imported == 1 ? string.Empty : "s")} from the folder.";
            }

            RefreshList();
        }
        catch (Exception exception)
        {
            _status!.Text = "Import failed: " + exception.Message;
        }
    }

    private async Task<int> ImportDocumentTreeAsync(Uri treeUri)
    {
        var rootDocumentId = DocumentsContract.GetTreeDocumentId(treeUri);
        if (string.IsNullOrWhiteSpace(rootDocumentId))
            return 0;

        var rootUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, rootDocumentId);
        return await ImportDocumentDirectoryAsync(treeUri, rootUri, depth: 0);
    }

    private async Task<int> ImportDocumentDirectoryAsync(Uri treeUri, Uri documentUri, int depth)
    {
        if (depth > 12 || ContentResolver is null)
            return 0;

        var documentId = DocumentsContract.GetDocumentId(documentUri);
        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, documentId);
        var imported = 0;

        using var cursor = ContentResolver.Query(
            childrenUri,
            new[]
            {
                DocumentsContract.Document.ColumnDocumentId,
                DocumentsContract.Document.ColumnDisplayName,
                DocumentsContract.Document.ColumnMimeType
            },
            null,
            null,
            null);

        if (cursor is null)
            return 0;

        var idColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDocumentId);
        var nameColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDisplayName);
        var mimeColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnMimeType);

        while (cursor.MoveToNext())
        {
            var childId = cursor.GetString(idColumn);
            var displayName = cursor.GetString(nameColumn) ?? string.Empty;
            var mimeType = cursor.GetString(mimeColumn) ?? string.Empty;
            var childUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, childId);

            if (string.Equals(mimeType, DocumentsContract.Document.MimeTypeDir, StringComparison.Ordinal))
            {
                imported += await ImportDocumentDirectoryAsync(treeUri, childUri, depth + 1);
            }
            else if (displayName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                     && await ImportUriAsync(childUri, displayName))
            {
                imported++;
            }
        }

        return imported;
    }

    private async Task<bool> ImportUriAsync(Uri uri, string? knownName = null)
    {
        var name = knownName ?? GetDisplayName(uri);
        if (!name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            return false;

        var modelDirectory = GetModelDirectory();
        var destinationName = MakeUniqueFileName(modelDirectory, SanitizeFileName(name));
        var destinationPath = Path.Combine(modelDirectory.FullName, destinationName);

        await using var input = ContentResolver?.OpenInputStream(uri)
                                ?? throw new IOException("The selected file could not be opened.");
        await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output);

        await UpdateIndexAsync();
        return true;
    }

    private string GetDisplayName(Uri uri)
    {
        if (ContentResolver is not null)
        {
            using var cursor = ContentResolver.Query(
                uri,
                new[] { OpenableColumns.DisplayName },
                null,
                null,
                null);

            if (cursor is not null && cursor.MoveToFirst())
            {
                var column = cursor.GetColumnIndex(OpenableColumns.DisplayName);
                if (column >= 0)
                {
                    var name = cursor.GetString(column);
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }
        }

        return uri.LastPathSegment ?? "model.gguf";
    }

    private void RefreshList()
    {
        if (_list is null)
            return;

        _list.RemoveAllViews();
        var files = GetModelDirectory()
            .EnumerateFiles("*.gguf", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            var empty = new TextView(this)
            {
                Text = "No local models have been imported yet.",
                TextSize = 14
            };
            empty.SetTextColor(Color.Argb(210, 225, 218, 240));
            _list.AddView(empty);
            return;
        }

        foreach (var file in files)
        {
            var row = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
                Gravity = GravityFlags.CenterVertical
            };
            row.SetPadding(0, Dp(5), 0, Dp(5));

            var text = new TextView(this)
            {
                Text = $"{file.Name}\n{FormatBytes(file.Length)}",
                TextSize = 14,
                LayoutParameters = new LinearLayout.LayoutParams(0, Dp(58), 1f)
            };
            text.SetTextColor(Color.White);
            row.AddView(text);

            var remove = new Button(this)
            {
                Text = "Remove",
                AllCaps = false
            };
            remove.Click += (_, _) =>
            {
                try
                {
                    file.Delete();
                    UpdateIndexAsync().GetAwaiter().GetResult();
                    RefreshList();
                }
                catch (Exception exception)
                {
                    _status!.Text = "Could not remove model: " + exception.Message;
                }
            };
            row.AddView(remove);
            _list.AddView(row);
        }
    }

    private DirectoryInfo GetModelDirectory()
    {
        var root = FilesDir?.AbsolutePath
                   ?? throw new IOException("Haven's application storage is unavailable.");
        return Directory.CreateDirectory(Path.Combine(root, "models"));
    }

    private async Task UpdateIndexAsync()
    {
        var directory = GetModelDirectory();
        var index = directory
            .EnumerateFiles("*.gguf")
            .Select(file => new ImportedModel(file.Name, file.FullName, file.Length, file.LastWriteTimeUtc))
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, IndexFileName), json);
    }

    private static string SanitizeFileName(string name)
    {
        var clean = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(clean) ? "model.gguf" : clean;
    }

    private static string MakeUniqueFileName(DirectoryInfo directory, string requestedName)
    {
        var name = Path.GetFileNameWithoutExtension(requestedName);
        var extension = Path.GetExtension(requestedName);
        var candidate = requestedName;
        var index = 2;

        while (File.Exists(Path.Combine(directory.FullName, candidate)))
            candidate = $"{name}-{index++}{extension}";

        return candidate;
    }

    private static string FormatBytes(long bytes)
    {
        var value = (double)bytes;
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private int Dp(int value)
        => (int)Math.Round(value * (Resources?.DisplayMetrics?.Density ?? 1f));

    private sealed record ImportedModel(string Name, string Path, long SizeBytes, DateTime LastModifiedUtc);
}
