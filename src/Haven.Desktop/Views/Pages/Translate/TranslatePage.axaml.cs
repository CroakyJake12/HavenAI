using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Translate;

public sealed partial class TranslatePage : UserControl, IDisposable
{
    private const string PreferencesKey = "translate.workspace.preferences.v1";
    private readonly TranslateService _translator;
    private readonly IVersionedSettingsStore _settings;
    private CancellationTokenSource? _operationCancellation;
    private bool _activated;
    private bool _loadingPreferences;
    private string? _lastDetectedSourceCode;

    private static readonly LanguageOption[] SourceLanguages =
    [
        new("auto", "Auto-detect"), new("en", "English"), new("es", "Spanish"), new("fr", "French"), new("de", "German"),
        new("it", "Italian"), new("pt", "Portuguese"), new("nl", "Dutch"), new("pl", "Polish"), new("ru", "Russian"),
        new("uk", "Ukrainian"), new("tr", "Turkish"), new("ar", "Arabic"), new("he", "Hebrew"), new("hi", "Hindi"),
        new("bn", "Bengali"), new("zh", "Chinese"), new("ja", "Japanese"), new("ko", "Korean"), new("vi", "Vietnamese"),
        new("id", "Indonesian"), new("th", "Thai"), new("sv", "Swedish"), new("da", "Danish"), new("no", "Norwegian"),
        new("fi", "Finnish"), new("cs", "Czech"), new("el", "Greek"), new("ro", "Romanian"), new("hu", "Hungarian")
    ];

    private static readonly LanguageOption[] TargetLanguages = SourceLanguages.Where(item => item.Code != "auto").ToArray();
    private static readonly string[] Tones = ["Natural", "Formal", "Casual", "Technical", "Literal"];

    public TranslatePage(TranslateService translator, IVersionedSettingsStore settings)
    {
        _translator = translator;
        _settings = settings;
        InitializeComponent();
        SourceLanguage.ItemsSource = SourceLanguages;
        TargetLanguage.ItemsSource = TargetLanguages;
        Tone.ItemsSource = Tones;
        SourceLanguage.SelectedItem = SourceLanguages[0];
        TargetLanguage.SelectedItem = TargetLanguages.First(item => item.Code == "es");
        Tone.SelectedItem = Tones[0];
    }

    public event EventHandler<string>? CopyRequested;

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_activated) return;
        _activated = true;
        _loadingPreferences = true;
        try
        {
            var saved = await _settings.GetAsync<TranslateWorkspacePreferences>(PreferencesKey, cancellationToken).ConfigureAwait(true);
            if (saved is not null)
            {
                SourceLanguage.SelectedItem = FindSource(saved.SourceLanguageCode) ?? SourceLanguages[0];
                TargetLanguage.SelectedItem = FindTarget(saved.TargetLanguageCode) ?? TargetLanguages.First(item => item.Code == "es");
                Tone.SelectedItem = Tones.FirstOrDefault(item => item.Equals(saved.Tone, StringComparison.OrdinalIgnoreCase)) ?? Tones[0];
                ContextText.Text = saved.Context ?? string.Empty;
            }
            StatusText.Text = "Ready.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText.Text = "Translate is ready, but saved language preferences could not be loaded.";
        }
        finally { _loadingPreferences = false; }
    }

    public void FocusSource() => SourceText.Focus();

    public async Task SetInitialTaskAsync(string instruction, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        if (files.Count > 0)
        {
            await LoadFilesAsync(files, cancellationToken).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(instruction)) ContextText.Text = instruction.Trim();
            return;
        }
        if (!string.IsNullOrWhiteSpace(instruction)) SourceText.Text = instruction.Trim();
    }

    private async void OnTranslateClick(object? sender, RoutedEventArgs e) => await TranslateAsync();

    private async Task TranslateAsync()
    {
        if (SourceLanguage.SelectedItem is not LanguageOption source || TargetLanguage.SelectedItem is not LanguageOption target) return;
        if (source.Code == target.Code)
        {
            StatusText.Text = "Choose a different target language.";
            return;
        }
        if (string.IsNullOrWhiteSpace(SourceText.Text))
        {
            StatusText.Text = "Enter text or open a file to translate.";
            SourceText.Focus();
            return;
        }

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        SetBusy(true, "Translating…");
        AmbiguityText.IsVisible = false;
        try
        {
            var result = await _translator.TranslateAsync(new TranslateRequest(
                source.Code, source.Name, target.Code, target.Name, SourceText.Text,
                Tone.SelectedItem?.ToString() ?? "Natural", ContextText.Text ?? string.Empty), _operationCancellation.Token).ConfigureAwait(true);
            OutputText.Text = result.TranslatedText;
            _lastDetectedSourceCode = result.DetectedSourceLanguageCode;
            DetectedLanguageText.Text = source.Code == "auto" ? $"Detected: {result.DetectedSourceLanguage}" : source.Name;
            ModelText.Text = string.IsNullOrWhiteSpace(result.Model) ? string.Empty : $"• {result.Model}";
            if (result.Ambiguities.Count > 0)
            {
                AmbiguityText.Text = "Ambiguity: " + string.Join(" • ", result.Ambiguities);
                AmbiguityText.IsVisible = true;
            }
            StatusText.Text = "Translation complete.";
            await SavePreferencesAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Translation cancelled or timed out.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or HttpRequestException)
        {
            StatusText.Text = ex.Message;
        }
        finally { SetBusy(false, StatusText.Text ?? "Ready."); }
    }

    private async void OnFileClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            StatusText.Text = "File selection is unavailable on this platform.";
            return;
        }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Translate files", AllowMultiple = true });
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
        if (paths.Length == 0) return;
        await LoadFilesAsync(paths, CancellationToken.None);
    }

    private async Task LoadFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        SetBusy(true, paths.Count == 1 ? "Extracting document text…" : $"Extracting text from {paths.Count} files…");
        try
        {
            var content = new StringBuilder();
            var notices = new List<string>();
            foreach (var path in paths)
            {
                var extracted = await _translator.ExtractFileAsync(path, cancellationToken).ConfigureAwait(true);
                if (content.Length > 0) content.AppendLine().AppendLine();
                content.Append("--- " + extracted.FileName + " ---").AppendLine().Append(extracted.Text);
                if (!string.IsNullOrWhiteSpace(extracted.Notice)) notices.Add(extracted.FileName + ": " + extracted.Notice);
            }
            SourceText.Text = content.ToString();
            FileNoticeText.Text = (notices.Count > 0 ? string.Join(" ", notices) + " " : string.Empty) +
                                  "Translate uses extracted text; it does not currently rewrite the original file or promise layout preservation.";
            FileNoticeText.IsVisible = true;
            StatusText.Text = paths.Count == 1 ? "File text loaded." : $"Loaded extracted text from {paths.Count} files.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = ex.Message;
        }
        finally { SetBusy(false, StatusText.Text ?? "Ready."); }
    }

    private async void OnSwapClick(object? sender, RoutedEventArgs e)
    {
        if (TargetLanguage.SelectedItem is not LanguageOption target) return;
        var sourceCode = SourceLanguage.SelectedItem is LanguageOption source && source.Code != "auto" ? source.Code : _lastDetectedSourceCode;
        if (string.IsNullOrWhiteSpace(sourceCode) || FindTarget(sourceCode) is not LanguageOption previousSource)
        {
            StatusText.Text = "Translate once so Haven can identify the source language before swapping.";
            return;
        }
        SourceLanguage.SelectedItem = FindSource(target.Code) ?? SourceLanguages[0];
        TargetLanguage.SelectedItem = previousSource;
        if (!string.IsNullOrWhiteSpace(OutputText.Text))
        {
            SourceText.Text = OutputText.Text;
            OutputText.Text = string.Empty;
        }
        DetectedLanguageText.Text = string.Empty;
        AmbiguityText.IsVisible = false;
        await SavePreferencesAsync();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _operationCancellation?.Cancel();
        SourceText.Text = string.Empty;
        OutputText.Text = string.Empty;
        DetectedLanguageText.Text = string.Empty;
        ModelText.Text = string.Empty;
        FileNoticeText.IsVisible = false;
        AmbiguityText.IsVisible = false;
        StatusText.Text = "Cleared.";
        SourceText.Focus();
    }

    private void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OutputText.Text))
        {
            StatusText.Text = "There is no translation to copy yet.";
            return;
        }
        CopyRequested?.Invoke(this, OutputText.Text);
        StatusText.Text = "Translation copied.";
    }

    private async void OnPreferenceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loadingPreferences) await SavePreferencesAsync();
    }

    private async void OnContextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_loadingPreferences && _activated) await SavePreferencesAsync();
    }

    private async Task SavePreferencesAsync()
    {
        if (!_activated || _loadingPreferences || SourceLanguage.SelectedItem is not LanguageOption source || TargetLanguage.SelectedItem is not LanguageOption target) return;
        try
        {
            await _settings.SetAsync(PreferencesKey, new TranslateWorkspacePreferences(
                source.Code, target.Code, Tone.SelectedItem?.ToString() ?? "Natural", ContextText.Text ?? string.Empty), CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
    }

    private void SetBusy(bool busy, string status)
    {
        BusyIndicator.IsVisible = busy;
        TranslateButton.IsEnabled = !busy;
        SwapButton.IsEnabled = !busy;
        StatusText.Text = status;
    }

    private static LanguageOption? FindSource(string? code) => SourceLanguages.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    private static LanguageOption? FindTarget(string? code) => TargetLanguages.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }

    public sealed record LanguageOption(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    public sealed record TranslateWorkspacePreferences(string SourceLanguageCode, string TargetLanguageCode, string Tone, string Context);
}
