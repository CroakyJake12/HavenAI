using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using IOPath = System.IO.Path;

namespace Haven.Android;

[Activity(
    Label = "Browse Models",
    Theme = "@style/Theme.AppCompat.Light.NoActionBar",
    Exported = false)]
public sealed class ModelBrowserActivity : Activity
{
    private const string PreferenceName = "haven_model_browser";
    private const string RecommendationPromptKey = "recommendations_prompted";
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    private LinearLayout? _results;
    private TextView? _status;
    private EditText? _search;
    private Spinner? _feature;
    private Spinner? _parameters;
    private bool _searching;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildUi();
        _ = LoadRecommendedAsync();
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
        root.SetPadding(Dp(16), Dp(18), Dp(16), Dp(12));
        root.SetBackgroundColor(Color.Rgb(24, 18, 38));

        var heading = new TextView(this)
        {
            Text = "Browse Models",
            TextSize = 25,
            Typeface = Typeface.DefaultBold
        };
        heading.SetTextColor(Color.White);
        root.AddView(heading);

        var subtitle = new TextView(this)
        {
            Text = "Recommended GGUF models are selected for this device. Search by model name or narrow results by capability and parameter size.",
            TextSize = 14
        };
        subtitle.SetTextColor(Color.Argb(220, 225, 218, 240));
        subtitle.SetPadding(0, Dp(6), 0, Dp(12));
        root.AddView(subtitle);

        _search = new EditText(this)
        {
            Hint = "Model name, creator, or feature",
            SingleLine = true
        };
        _search.SetTextColor(Color.White);
        _search.SetHintTextColor(Color.Argb(180, 235, 225, 255));
        root.AddView(_search, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(52));

        var filters = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        _feature = CreateSpinner(new[] { "Any feature", "Reasoning", "Vision", "Coding", "Tool use" });
        _parameters = CreateSpinner(new[] { "Any size", "Under 2B", "2B–4B", "4B–8B", "8B+" });
        filters.AddView(_feature, new LinearLayout.LayoutParams(0, Dp(52), 1f)
        {
            RightMargin = Dp(4)
        });
        filters.AddView(_parameters, new LinearLayout.LayoutParams(0, Dp(52), 1f)
        {
            LeftMargin = Dp(4)
        });
        root.AddView(filters);

        var actions = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        var browse = ActionButton("Search Hugging Face", () => _ = SearchAsync());
        var local = ActionButton("Import local GGUF", () =>
        {
            StartActivity(new Intent(this, typeof(ModelImportActivity)));
        });
        actions.AddView(browse, new LinearLayout.LayoutParams(0, Dp(52), 1f)
        {
            RightMargin = Dp(4)
        });
        actions.AddView(local, new LinearLayout.LayoutParams(0, Dp(52), 1f)
        {
            LeftMargin = Dp(4)
        });
        root.AddView(actions);

        _status = new TextView(this)
        {
            Text = "Checking this device…",
            TextSize = 13
        };
        _status.SetTextColor(Color.Argb(220, 214, 193, 255));
        _status.SetPadding(0, Dp(10), 0, Dp(8));
        root.AddView(_status);

        _results = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        var scroll = new ScrollView(this);
        scroll.AddView(_results);
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1f));

        SetContentView(root);
    }

    private Spinner CreateSpinner(IReadOnlyList<string> values)
    {
        var spinner = new Spinner(this);
        spinner.Adapter = new ArrayAdapter<string>(
            this,
            Android.Resource.Layout.SimpleSpinnerDropDownItem,
            values);
        return spinner;
    }

    private Button ActionButton(string text, Action action)
    {
        var button = new Button(this) { Text = text };
        button.SetAllCaps(false);
        button.SetTextColor(Color.White);
        button.SetBackgroundColor(Color.Rgb(89, 48, 145));
        button.Click += (_, _) => action();
        return button;
    }

    private async Task LoadRecommendedAsync()
    {
        var ramGb = GetTotalRamGb();
        _status!.Text = $"Recommendations for a device with about {ramGb:0.#} GB RAM";
        var recommendationQuery = ramGb < 4
            ? "1b gguf"
            : ramGb < 7
                ? "2b instruct gguf"
                : "4b instruct gguf";

        await SearchAndRenderAsync(recommendationQuery, "Recommended for this device");
        PromptForRecommendedDownloadsIfNeeded(ramGb);
    }

    private async Task SearchAsync()
    {
        var text = _search?.Text?.Trim() ?? string.Empty;
        var feature = _feature?.SelectedItem?.ToString() ?? "Any feature";
        var parameters = _parameters?.SelectedItem?.ToString() ?? "Any size";
        var terms = new List<string>();
        if (!string.IsNullOrWhiteSpace(text))
            terms.Add(text);
        if (!feature.StartsWith("Any", StringComparison.Ordinal))
            terms.Add(feature);
        terms.Add(parameters switch
        {
            "Under 2B" => "1b",
            "2B–4B" => "2b 3b 4b",
            "4B–8B" => "7b 8b",
            "8B+" => "9b",
            _ => string.Empty
        });
        terms.Add("gguf");
        await SearchAndRenderAsync(
            string.Join(' ', terms.Where(term => !string.IsNullOrWhiteSpace(term))),
            "Search results");
    }

    private async Task SearchAndRenderAsync(string query, string heading)
    {
        if (_searching || _results is null || _status is null)
            return;

        _searching = true;
        _status.Text = "Searching Hugging Face…";
        try
        {
            var models = await SearchModelsAsync(query);
            _results.RemoveAllViews();
            AddSectionHeading(heading);

            if (models.Count == 0)
            {
                AddMessage("No matching GGUF models were returned. Try a model name or a broader filter.");
                _status.Text = "No matching models.";
                return;
            }

            foreach (var model in models)
                AddModelCard(model);

            _status.Text = $"{models.Count} model{(models.Count == 1 ? string.Empty : "s")} found.";
        }
        catch (Exception exception)
        {
            _results.RemoveAllViews();
            AddMessage("Hugging Face could not be reached. Local model import remains available.");
            _status.Text = "Search failed: " + exception.Message;
        }
        finally
        {
            _searching = false;
        }
    }

    private async Task<IReadOnlyList<HuggingFaceModel>> SearchModelsAsync(string query)
    {
        var uri = "https://huggingface.co/api/models"
                  + "?search=" + Uri.EscapeDataString(query)
                  + "&filter=gguf&sort=downloads&direction=-1&limit=18";
        return await Http.GetFromJsonAsync<List<HuggingFaceModel>>(uri) ?? [];
    }

    private void AddSectionHeading(string text)
    {
        var heading = new TextView(this)
        {
            Text = text,
            TextSize = 18,
            Typeface = Typeface.DefaultBold
        };
        heading.SetTextColor(Color.White);
        heading.SetPadding(0, Dp(4), 0, Dp(8));
        _results!.AddView(heading);
    }

    private void AddMessage(string text)
    {
        var message = new TextView(this)
        {
            Text = text,
            TextSize = 14
        };
        message.SetTextColor(Color.Argb(220, 225, 218, 240));
        message.SetPadding(0, Dp(8), 0, Dp(8));
        _results!.AddView(message);
    }

    private void AddModelCard(HuggingFaceModel model)
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        card.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        card.SetBackgroundColor(Color.Argb(80, 255, 255, 255));

        var name = new TextView(this)
        {
            Text = model.ModelId,
            TextSize = 16,
            Typeface = Typeface.DefaultBold
        };
        name.SetTextColor(Color.White);
        card.AddView(name);

        var tags = model.Tags?
            .Where(tag => tag is not "gguf")
            .Take(6)
            .ToArray() ?? [];
        var detail = new TextView(this)
        {
            Text = tags.Length == 0 ? "GGUF model" : string.Join(" • ", tags),
            TextSize = 12
        };
        detail.SetTextColor(Color.Argb(210, 225, 218, 240));
        detail.SetPadding(0, Dp(4), 0, Dp(6));
        card.AddView(detail);

        var download = ActionButton("Download recommended GGUF", () => _ = DownloadModelAsync(model));
        card.AddView(download, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(48)));

        _results!.AddView(card, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = Dp(8)
        });
    }

    private async Task DownloadModelAsync(HuggingFaceModel model)
    {
        if (_status is null)
            return;

        var file = SelectGgufFile(model);
        if (file is null)
        {
            _status.Text = "This result did not expose a downloadable GGUF file.";
            return;
        }

        var approved = await ConfirmAsync(
            "Download model?",
            $"{model.ModelId}\n\n{file}\n\nThe model will be stored in Haven's private model library.");
        if (!approved)
            return;

        _status.Text = "Downloading " + file + "…";
        try
        {
            var directory = GetModelDirectory();
            var destination = UniquePath(directory, IOPath.GetFileName(file));
            var url = $"https://huggingface.co/{model.ModelId}/resolve/main/{Uri.EscapeDataString(file).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)}?download=true";
            await using var response = await Http.GetStreamAsync(url);
            await using var output = File.Create(destination);
            await response.CopyToAsync(output);
            _status.Text = "Downloaded " + IOPath.GetFileName(destination) + ".";
        }
        catch (Exception exception)
        {
            _status.Text = "Download failed: " + exception.Message;
        }
    }

    private void PromptForRecommendedDownloadsIfNeeded(double ramGb)
    {
        if (GetModelDirectory().EnumerateFiles("*.gguf").Any())
            return;

        var preferences = GetSharedPreferences(PreferenceName, FileCreationMode.Private);
        if (preferences?.GetBoolean(RecommendationPromptKey, false) == true)
            return;

        preferences?.Edit()?.PutBoolean(RecommendationPromptKey, true)?.Apply();
        RunOnUiThread(async () =>
        {
            var approved = await ConfirmAsync(
                "Download recommended models?",
                "Haven can download two device-suitable models: one lightweight everyday model and one reasoning or vision-capable model. Nothing will download without your permission.");
            if (approved)
                await DownloadRecommendedPairAsync(ramGb);
        });
    }

    private async Task DownloadRecommendedPairAsync(double ramGb)
    {
        var queries = ramGb < 4
            ? new[] { "tinyllama 1.1b chat gguf", "deepseek r1 distill 1.5b gguf" }
            : ramGb < 7
                ? new[] { "gemma 2 2b instruct gguf", "qwen2 vl 2b instruct gguf" }
                : new[] { "gemma 2 2b instruct gguf", "qwen2.5 vl 3b instruct gguf" };

        foreach (var query in queries)
        {
            var model = (await SearchModelsAsync(query)).FirstOrDefault(item => SelectGgufFile(item) is not null);
            if (model is not null)
                await DownloadModelAsync(model);
        }
    }

    private Task<bool> ConfirmAsync(string title, string message)
    {
        var completion = new TaskCompletionSource<bool>();
        RunOnUiThread(() =>
        {
            var dialog = new AlertDialog.Builder(this)
                .SetTitle(title)
                .SetMessage(message)
                .SetNegativeButton("Not now", (_, _) => completion.TrySetResult(false))
                .SetPositiveButton("Continue", (_, _) => completion.TrySetResult(true))
                .SetOnCancelListener(new CancelListener(() => completion.TrySetResult(false)))
                .Create();
            dialog.Show();
        });
        return completion.Task;
    }

    private static string? SelectGgufFile(HuggingFaceModel model)
    {
        var files = model.Siblings?
            .Select(item => item.FileName)
            .Where(name => !string.IsNullOrWhiteSpace(name)
                       && name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                       && !name.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
        return files.FirstOrDefault(name => name.Contains("Q4_K_M", StringComparison.OrdinalIgnoreCase))
               ?? files.FirstOrDefault(name => name.Contains("Q4_K", StringComparison.OrdinalIgnoreCase))
               ?? files.FirstOrDefault();
    }

    private DirectoryInfo GetModelDirectory()
    {
        var root = FilesDir?.AbsolutePath
                   ?? throw new IOException("Haven's application storage is unavailable.");
        return Directory.CreateDirectory(IOPath.Combine(root, "models"));
    }

    private static string UniquePath(DirectoryInfo directory, string fileName)
    {
        var safe = string.Join("_", fileName.Split(IOPath.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "model.gguf";
        var candidate = IOPath.Combine(directory.FullName, safe);
        var stem = IOPath.GetFileNameWithoutExtension(safe);
        var extension = IOPath.GetExtension(safe);
        var index = 2;
        while (File.Exists(candidate))
            candidate = IOPath.Combine(directory.FullName, $"{stem}-{index++}{extension}");
        return candidate;
    }

    private double GetTotalRamGb()
    {
        var manager = GetSystemService(ActivityService) as ActivityManager;
        var info = new ActivityManager.MemoryInfo();
        manager?.GetMemoryInfo(info);
        return info.TotalMem <= 0 ? 4 : info.TotalMem / 1024d / 1024d / 1024d;
    }

    private int Dp(int value)
        => (int)Math.Round(value * (Resources?.DisplayMetrics?.Density ?? 1f));

    private sealed class CancelListener(Action action) : Java.Lang.Object, IDialogInterfaceOnCancelListener
    {
        public void OnCancel(IDalogInterface? dialog) => action();
    }

    private sealed record HuggingFaceModel(
        [property: JsonPropertyName("modelId")] string ModelId,
        [property: JsonPropertyName("tags")] string[]? TagsK
        [property: JsonPropertyName("siblings")] HuggingFaceFile[]? Siblings);

    private sealed record HuggingFaceFile(
        [property: JsonPropertyName("rfilename")] string FileName);
}
