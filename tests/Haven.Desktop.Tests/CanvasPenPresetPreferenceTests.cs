using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class CanvasPenPresetPreferenceTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public void Custom_pen_preset_survives_service_restart()
    {
        var preferences = new UserPreferencesService(_paths);
        var saved = preferences.SaveCanvasPenPreset(new CanvasPenPresetPreference(
            string.Empty, "Study pen", "Pen", "#FF336699", .7, 5, "Uniform"));

        Assert.NotNull(saved);
        var reopened = new UserPreferencesService(_paths);
        var restored = Assert.Single(reopened.CanvasPenPresets, value => value.Name == "Study pen");
        Assert.Equal(saved!.Id, restored.Id);
        Assert.Equal("#FF336699", restored.Color);
        Assert.Equal(.7, restored.Opacity, 3);
        Assert.Equal(5, restored.Thickness, 3);
        Assert.Equal("Uniform", restored.Effect);
    }

    [Fact]
    public void Malformed_canvas_preset_payload_does_not_poison_other_preferences()
    {
        var preferences = new UserPreferencesService(_paths);
        preferences.SetDefaultTabAppKey("canvas-test-app");
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["canvasPenPresets"] = "not-an-object";
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var reopened = new UserPreferencesService(_paths);
        Assert.Equal("canvas-test-app", reopened.DefaultTabAppKey);
        Assert.True(reopened.CanvasPenPresetPreferencesWritable);
        Assert.Contains(reopened.CanvasPenPresets, value => value.Id == "builtin-blue");
    }

    [Fact]
    public void Future_canvas_preset_version_is_read_only_and_preserved()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        File.WriteAllText(path, """{"defaultTabAppKey":"future-safe","canvasPenPresets":{"version":99,"customPresets":[{"id":"future","name":"Future pen","tool":"Pen","color":"#FF010203","opacity":0.5,"thickness":4,"effect":"Pressure"}]}}""");
        var preferences = new UserPreferencesService(_paths);

        Assert.Equal("future-safe", preferences.DefaultTabAppKey);
        Assert.False(preferences.CanvasPenPresetPreferencesWritable);
        Assert.Null(preferences.SaveCanvasPenPreset(new CanvasPenPresetPreference("", "Blocked", "Pen", "#FFFFFFFF", 1, 3, "Pressure")));
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(99, json.RootElement.GetProperty("canvasPenPresets").GetProperty("version").GetInt32());
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-canvas-preset-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
