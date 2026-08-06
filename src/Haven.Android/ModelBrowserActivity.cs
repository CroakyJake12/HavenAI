using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Haven.Android;

[Activity(
    Label = "Browse Models",
    Theme = "@style/Theme.AppCompat.Light.NoActionBar",
    Exported = false)]
public sealed class ModelBrowserActivity : Activity
{
    private const string PreferenceName = "haven_model_browser";
    private const string PromptedKey = "recommended_models_prompted";
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    private LinearLayout? _results;
    private TextView? _status;
    private EditText? _query;
    private Spinner? _feature;
    private Spinner? _parameters;
    private bool _busy;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildUi();
        _ = LoadRecommendationsAsync();
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

        root.AddView(Label("Browse Models", 25, true));
        var subtitle = Label(
            "Recommended GGUF models are selected for this device. Search by name or filter by capability and parameter size.",
            14);
        subtitle.SetPadding(0, Dp(6), 0, Dp(12));
        root.AddView(subtitle);

        _query = new EditText(this)
        {
            Hint = "Model name, creator, or feature",
            SingleLine = true
        };
        _query.SetTextColor(Color.White);
        _query.SetHintTextColor(Color.Argb(180, 235, 225, 255));
        root.AddView(_query, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(52)));

        var filters = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        _feature = SpinnerFor("Any feature", "Reasoning", "Vision", "Coding", "Tool use");
        _parameters = SpinnerFor("Any size", "Under 2B", "2B–4B", "4B–8B", "8B+");
        filters.AddView(_feature, Weighted(Dp(52), right: Dp(4)));
        filters.AddView(_parameters, Weighted(Dp(52), left: Dp(4)));
        root.AddView(filters);

        var actions = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        actions.AddView(ActionButton("Search Hugging Face", () => _ = SearchAsync()), Weighted(Dp(52), right: Dp(4)));
        actions.AddView(ActionButton("Import local GGUF", () =>
            StartActivity(new Intent(this, typeof(ModelImportActivity)))), Weighted(Dp(52), left: Dp(4)));
        root.AddView(Actions);

        _status = Label("Checking this device…", 13);
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

    private async Task LoadRecommendationsAsync()
    {
        var ramGb = GetTotalRamGb();
        _status!.Text = $"Recommendations for about {ramGb:0.#} GB RAM";
        var query = ramGb < 4 ? "1b gguf" : ramGb < 7 ? "2b instruct gguf" : "4b instruct gguf";
        await SearchAndRenderAsync(query, "Recommended for this device");
        await PromptForRecommendedPairAsync(ramGb);
    }

    private async Task SearchAsync()
    {
        var terms = new List<string>();
        var query = _query?.Text?.Trim();
        var feature = _feature?.SelectedItem?.ToString() ?? "Any feature";
        var parameters = _parameters?.SelectedItem?.ToString() ?? "Any size";

        if (!string.IsNullOrWhiteSpace(query))
            terms.Add(query);
        if (!feature.StartsWith("Any", StringComparison.Ordinal))
            terms.Add(feature);

        var parameterTerm = parameters switch
        {
            "Under 2B" => "1b",
            "2B–4B" => "2b 3b 4b",
            "4B–8B" => "7b 8b",
            "8B+" => "9b",
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(parameterTerm))
            terms.Add(parameterTerm);

        terms.Add("gguf");
        await SearchAndRenderAsync(string.Join(' ', terms), "Search results");
    }

    private async Task SearchAndRenderAsync(string query, string heading)
    {
        if (_busy || _results is null || _status is null)
            return;

        _busy = true;
        _status.Text = "Searching Hugging Face…";
        try
        {
            var models = await SearchModelsAsync(query);
            _results.RemoveAllViews();
            _results.AddView(Label(heading, 18, true));

            if (models.Count == 0)
            {
                _results.AddView(Label("No matching GGUF models were found.", 14));
                _status.Text = "No matching models.";
                return;
            }

            foreach (var model in models)
                AddModelCard(model);

            _status.Text = $"{models.Count} model{(models.Count == 1 ? string.Empty : "s")} found.";
        }
        catch (Exception ex)
        {
            _results.RemoveAllViews();
            _results.AddView(Label("Hugging Face could not be reached. Local GGUF import remains available.", 14));
            _status.Text = "Search failed: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task<IReadOnlyList<HfModel>> SearchModelsAsync(string query)
    {
        var uri = "https://huggingface.co/api/models"
            + "?search=" + Uri.EscapeDataString(query)
            + "&filter=gguf&full=true&sort=downloads&direction=-1&limit=18";
        return await Http.GetFromJsonAsync<List<HfModel>>(uri) ?? [];
    }

    private void AddModelCard(HfModel model)
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        card.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        card.SetBackgroundColor(Color.Argb(80, 255, 255, 255));

        card.AddView(Label(model.ModelId, 16, true));
        var tags = model.Tags?.Where(tag => tag is not "gguf").Take(6).ToArray() ?? [];
        var detail = Label(tags.Length == 0 ? "GGUF model" : string.Join(" • ", tags), 12);
        detail.SetPadding(0, Dp(4), 0, Dp(6));
        card.AddView(detail);

        card.AddView(
            ActionButton("Download recommended GGUF", () => _ = DownloadModelAsync(model, true)),
            new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(48)));

        _results!.AddView(card, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = Dp(8)
        });
    }

    private async Task PromptForRecommendedPairAsync(double ramGb)
    {
        if (ModelDirectory().EnumerateFiles("*.gguf").Any())
            return;

        var preferences = GetSharedPreferences(PreferenceName, FileCreationMode.Private);
        if (preferences?.GetBoolean(PromptedKey, false) == true)
            return;

        preferences?.Edit()?.PutBoolean(PromptedKey, true)?.Apply();
        var approved = await ConfirmAsync(
            "Download recommended models?",
            "Haven can download one lightweight model and one reasoning or vision model selected for this device. Nothing downloads without your permission.");
        if (!approved)
            return;

        var queries = ramGb < 4
            ? new[] { "tinyllama 1.1b chat gguf", "deepseek r1 distill 1.5b gguf" }
            : ramGb < 7
                ? new[] { "gemma 2 2b instruct gguf", "qwen2 vl 2b instruct gguf" }
                : new[] { "gemma 2 2b instruct gguf", "qwen2.5 vl 3b instruct gguf" };

        foreach (var query in queries)
        {
            var model = (await SearchModelsAsync(query)).FirstOrDefault(item => SelectGguf(item) is not null);
            if (model is not null)
                await DownloadModelAsync(model, false);
        }
    }

    private async Task DownloadModelAsync(HfModel model, bool requireConfirmation)
    {
        if (_status is null)
            return;

        var file = SelectGguf(model);
        if (file is null)
        {
            _status.Text = "No downloadable GGUF file was exposed for this model.";
            return;
        }

        if (requireConfirmation && !await ConfirmAsync(
                "Download model?",
                $"{model.ModelId}\n\n{file}\n\nThe model will be stored in Haven's private model library."))
            return;

        _status.Text = "Downloading " + file + "…";
        try
        {
            var destination = UniquePath(ModelDirectory(), Path.GetFileName(file));
            var escaped = Uri.EscapeDataString(file).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
            var url = $"https://huggingface.co/{model.ModelId}/resolve/main/{escaped}?download=true";
            await using var input = await Http.GetStreamAsync(url);
            await using var output = File.Create(destination);
            await input.CopyToAsync(output);
            _status.Text = "Downloaded " + Path.GetFileName(destination) + ".";
        }
        catch (Exception ex)
        {
            _status.Text = "Download failed: " + ex.Message;
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
                .SetCancelable(false)
                .SetNegativeButton("Not now", (_, _) => completion.TrySetResult(false))
                .SetPositiveButton("Continue", (_, _) => completion.TrySetResult(true))
                .Create();
            dialog.Show();
        });
        return completion.Task;
    }

    private static string? SelectGguf(HfModel model)
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

    private DirectoryInfo ModelDirectory()
    {
        var root = FilesDir?.AbsolutePath
            ?? throw new IOException("Haven application storage is unavailable.");
        return Directory.CreateDirectory(Path.Combine(root, "models"));
    }

    private static string UniquePath(DirectoryInfo directory, string fileName)
    {
        var safe = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "model.gguf";

        var candidate = Path.Combine(directory.FullName, safe);
        var stem = Path.GetFileNameWithoutExtension(safe);
        var extension = Path.GetExtension(safe);
        var index = 2;
        while (File.Exists(candidate))
            candidate = Path.Combine(directory.FullName, $"{stem}-{index++}{extension}");
        return candidate;
    }

    private double GetTotalRamGb()
    {
        var manager = GetSystemService(ActivityService) as ActivityManager;
        var info = new ActivityManager.MemoryInfo();
        manager?.GetMemoryInfo(info);
        return info.TotalMem <= 0 ? 4 : info.TotalMem / 1024d / 1024d / 1024d;
    }

    private TextView Label(string text, float size, bool bold = false)
    {
        var view = new TextView(this)
        {
            Text = text,
            TextSize = size,
            Typeface = bold ? Typeface.DefaultBold : Typeface.Default
        };
        view.SetTextColor(Color.White);
        return view;
    }

    private Spinner SpinnerFor(params string[] values)
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

    private static LinearLayout.LayoutParams Weighted(int height, int left = 0, int right = 0)
        => new(0, height, 1f)
        {
            LeftMargin = left,
            RightMargin = right
        };

    private int Dp(int value)
        => (int)Math.Round(value * (Resources?.DisplayMetrics?.Density ?? 1f));

    private sealed record HfModel(
        [property: JsonPropertyName("modelId")] string ModelId,
        [property: JsonPropertyName("tags")] string[]? Tags,
        [property: JsonPropertyName("siblings")] HfFile[]? Siblings);

    private sealed record HfFile(
        [property: JsonPropertyName("rfilename")] string FileName);
}
