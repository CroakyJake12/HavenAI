using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Translate;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private TranslatePage? _translatePage;

    private TranslatePage CreateTranslatePage()
    {
        var services = App.Services ?? throw new InvalidOperationException("Haven services are unavailable while opening Translate.");
        var attachments = services.GetRequiredService<IMessageAttachmentService>();
        var page = new TranslatePage(new TranslateService(_ollama, _preferences, attachments), _versionedSettings);
        page.CopyRequested += (_, text) => CopyRequested?.Invoke(this, text);
        return page;
    }

    private async Task<TranslatePage> OpenTranslateAsync(bool forceNewTab, string? instruction = null, IReadOnlyList<string>? files = null)
    {
        TranslatePage page;
        string key;
        if (forceNewTab)
        {
            page = CreateTranslatePage();
            key = $"translate-{Guid.NewGuid():N}";
        }
        else
        {
            _translatePage ??= CreateTranslatePage();
            page = _translatePage;
            key = "translate";
        }

        await page.ActivateAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(instruction) || files is { Count: > 0 })
            await page.SetInitialTaskAsync(instruction ?? string.Empty, files ?? [], CancellationToken.None);

        AddOrSelectTab(key, "Translate", page, forceNewTab, HavenSurface.Translate, forceNewTab);
        ApplyShellVisualState();
        page.FocusSource();
        return page;
    }
}
