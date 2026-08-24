using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Application.Play;
using Haven.Desktop.Views.Pages.Play;

namespace Haven.Desktop.Tests;

public sealed class PlayPageTests
{
    [AvaloniaFact]
    public async Task Play_page_renders_real_discovery_surface_and_empty_library_state()
    {
        var settings = new MemorySettingsStore();
        var appHandler = new GenUiAppEventHandler();
        var service = new PlaySessionService(settings, appHandler);
        var router = new GenerativeUiEventRouter([appHandler], new BoundedGenUiEventAuditSink(), new GenUiInstanceStore());
        var page = new PlayPage(service, router);
        var window = new Window { Width = 1200, Height = 900, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            window.UpdateLayout();

            Assert.NotNull(page.FindControl<Control>("CreateButton"));
            Assert.NotNull(page.FindControl<Control>("SearchBox"));
            Assert.NotNull(page.FindControl<Control>("CategoryPanel"));
            Assert.NotNull(page.FindControl<Control>("FeaturedPanel"));
            Assert.NotNull(page.FindControl<Control>("RecentPanel"));
            var empty = Assert.IsType<TextBlock>(page.FindControl<Control>("RecentEmptyText"));
            Assert.True(empty.IsVisible);
            Assert.Contains("recent Play sessions", empty.Text, StringComparison.OrdinalIgnoreCase);
            var featured = Assert.IsType<WrapPanel>(page.FindControl<Control>("FeaturedPanel"));
            Assert.True(featured.Children.Count >= 2);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private sealed class MemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class =>
            Task.FromResult(_values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : null);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            _values[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsExportManifest { Settings = new Dictionary<string, string>(_values) });
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
        {
            foreach (var item in manifest.Settings) _values[item.Key] = item.Value;
            return Task.FromResult(new SettingsImportResult(true, new Dictionary<string, string>(_values), "Imported"));
        }
    }
}
