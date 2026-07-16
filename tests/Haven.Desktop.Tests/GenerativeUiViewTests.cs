using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;

namespace Haven.Desktop.Tests;

public sealed class GenerativeUiViewTests
{
    [AvaloniaFact]
    public void ThemeStudioAxamlLoadsWithAiAndManualTabs()
    {
        var view = new GenerativeUiThemeSelectorView();
        var window = new Window { Content = view };
        try
        {
            window.Show();
            var headers = view.GetVisualDescendants()
                .OfType<TabItem>()
                .Select(item => Convert.ToString(item.Header, System.Globalization.CultureInfo.InvariantCulture))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.Contains("Create with AI", headers);
            Assert.Contains("Configure manually", headers);
        }
        finally
        {
            window.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public void SettingsReplacesLegacyThemeCreatorWithOneGenerativeUiSelector()
    {
        var settings = new SettingsView();
        var window = new Window { Content = settings };
        try
        {
            window.Show();
            var selectors = settings.GetVisualDescendants()
                .OfType<GenerativeUiThemeSelectorView>()
                .ToArray();
            var text = settings.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(item => item.Text ?? string.Empty)
                .ToArray();

            Assert.Single(selectors);
            Assert.Contains(text, value => value.Equals("GENERATIVE UI", StringComparison.Ordinal));
            Assert.DoesNotContain(text, value => value.Equals("Theme Creator", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            window.Close();
            settings.Dispose();
        }
    }

    [AvaloniaFact]
    public void GeneratedPageAxamlLoadsRealShortcutAndTimerWidgets()
    {
        var invoked = new List<string>();
        var definition = new GeneratedPageDefinition(
            "focus",
            "Focus page",
            "A generated page test.",
            "clock",
            1,
            [
                new GeneratedWidgetDefinition(
                    "shortcuts",
                    GeneratedWidgetKind.ShortcutGrid,
                    "Shortcuts",
                    null,
                    null,
                    0,
                    ["new-chat", "studio"]),
                new GeneratedWidgetDefinition(
                    "timer",
                    GeneratedWidgetKind.Timer,
                    "Timer",
                    "A local timer.",
                    null,
                    300,
                    [])
            ]);
        var viewModel = new GeneratedPageViewModel(definition, commandId =>
        {
            invoked.Add(commandId);
            return Task.CompletedTask;
        });
        var view = new GeneratedPageView { DataContext = viewModel };
        var window = new Window { Content = view };
        try
        {
            window.Show();
            Assert.Equal(2, viewModel.Widgets.Count);
            Assert.True(viewModel.Widgets[0].IsShortcutGrid);
            Assert.True(viewModel.Widgets[1].IsTimer);
            Assert.Equal("05:00", viewModel.Widgets[1].TimeLabel);
            Assert.Equal(2, viewModel.Widgets[0].Shortcuts.Count);

            viewModel.Widgets[0].Shortcuts[0].InvokeCommand.Execute(null);
            Assert.Contains("new-chat", invoked);

            var visibleButtons = view.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.NotEmpty(visibleButtons);
        }
        finally
        {
            window.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public void PaletteEditorAxamlLoadsEveryRequiredVariantField()
    {
        var editor = new ThemePaletteEditorView();
        var window = new Window { Content = editor };
        try
        {
            window.Show();
            var textBoxes = editor.GetVisualDescendants().OfType<TextBox>().ToArray();
            Assert.Equal(26, textBoxes.Length);
        }
        finally
        {
            window.Close();
        }
    }
}
