using System.Text.Json;
using Haven.Desktop.Views;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class HavenDevSimulatedJourneyTests
{
    [Fact]
    public void Fixture_is_inert_neutral_extension_and_every_observed_output_is_simulated()
    {
        var fixture = SimulatedAospFixture.Load();

        Assert.Equal(HavenDevJourneyPresentationState.SimulatedEvidenceLabel, fixture.EvidenceLabel);
        Assert.All(fixture.VirtualFiles, file => Assert.EndsWith(".fixture.txt", file.ResourceName, StringComparison.Ordinal));
        Assert.Contains(fixture.VirtualFiles, file => file.Path.EndsWith("Android.bp", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.VirtualFiles, file => string.Equals(file.ResourceName, "Android.bp", StringComparison.OrdinalIgnoreCase));
        Assert.All(fixture.Outputs.Values, output => Assert.StartsWith("[SIMULATED — NOT REAL AOSP VALIDATION]", output, StringComparison.Ordinal));
    }

    [Fact]
    public void Simulated_journey_switches_views_fails_navigates_fixes_rebuilds_deploys_tests_and_reviews_changes()
    {
        var fake = new SimulatedHavenDevJourney(SimulatedAospFixture.Load());
        using var scene = new ProjectHavenScene();

        scene.SyncHavenDev(fake.State);
        Assert.Equal(HavenVisibility.Visible, scene.HavenDevPanel.GetValue(HavenProperties.Visibility));
        Assert.Equal("SIMULATED — NOT REAL AOSP VALIDATION", scene.HavenDevEvidence.Content);
        Assert.Contains("Apps / HelloHaven / Sources / Greeting.java", scene.HavenDevExplorerItems.Content);
        Assert.Equal("Simulated evidence; not real AOSP validation", scene.HavenDevEvidence.Accessibility.AccessibleName);

        fake.SwitchMode(HavenDevExplorerMode.Filesystem);
        scene.SyncHavenDev(fake.State);
        Assert.Contains("packages/apps/HelloHaven/Android.bp", scene.HavenDevExplorerItems.Content);
        Assert.Equal(HavenElementState.Selected, scene.HavenDevFilesystemButton.State & HavenElementState.Selected);

        fake.Build();
        scene.SyncHavenDev(fake.State);
        Assert.Contains("error: cannot find symbol", scene.HavenDevOutput.Content);
        Assert.NotNull(fake.State.Diagnostic);
        Assert.Equal(8, fake.State.Diagnostic!.Line);

        HavenDevDiagnosticPresentation? requestedDiagnostic = null;
        scene.HavenDevDiagnosticRequested += diagnostic => requestedDiagnostic = diagnostic;
        scene.RequestHavenDevDiagnosticNavigation();
        Assert.Equal(fake.State.Diagnostic, requestedDiagnostic);

        fake.ApplyFix();
        fake.Build();
        scene.SyncHavenDev(fake.State);
        Assert.Contains("build completed successfully", scene.HavenDevOutput.Content);
        Assert.True(fake.State.CanDeploy);

        fake.Deploy();
        scene.SyncHavenDev(fake.State);
        Assert.Contains("HAVEN_SIM_001", scene.HavenDevDeployStatus.Content);

        fake.Show(HavenDevTool.Device);
        scene.SyncHavenDev(fake.State);
        Assert.Contains("Haven Simulated Device", scene.HavenDevOutput.Content);

        fake.Show(HavenDevTool.Logcat);
        scene.SyncHavenDev(fake.State);
        Assert.Contains("Greeting=Hello from Haven", scene.HavenDevOutput.Content);

        fake.RunTests();
        scene.SyncHavenDev(fake.State);
        Assert.Contains("1 total · 1 passed · 0 failed", scene.HavenDevOutput.Content);

        fake.Show(HavenDevTool.Changes);
        scene.SyncHavenDev(fake.State);
        Assert.Contains("M packages/apps/HelloHaven/src/com/haven/hello/Greeting.java", scene.HavenDevOutput.Content);
    }

    [Fact]
    public void Journey_controls_are_accessible_and_do_not_replace_existing_project_shell()
    {
        var fake = new SimulatedHavenDevJourney(SimulatedAospFixture.Load());
        using var scene = new ProjectHavenScene();
        scene.SyncHavenDev(fake.State);

        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(x => x.Name == "Project.Editor"));
        Assert.NotNull(scene.Root.DescendantsAndSelf().Single(x => x.Name == "Project.Assistant"));
        Assert.All(new[] { scene.HavenDevLogicalButton, scene.HavenDevFilesystemButton, scene.HavenDevDeployButton }, button =>
        {
            Assert.True(button.Accessibility.Focusable);
            Assert.False(string.IsNullOrWhiteSpace(button.Accessibility.AccessibleName));
        });
        Assert.Equal("Navigate to simulated build diagnostic", scene.HavenDevDiagnosticButton.Accessibility.AccessibleName);
    }

    private sealed class SimulatedHavenDevJourney
    {
        private readonly SimulatedAospFixture _fixture;
        private bool _fixed;
        private bool _buildSucceeded;
        private HavenDevExplorerMode _mode = HavenDevExplorerMode.Logical;
        private HavenDevTool _tool = HavenDevTool.Build;
        private string _output = "[SIMULATED — NOT REAL AOSP VALIDATION]\nOpen the fixture and run Build.";
        private string _deployStatus = "SIMULATED deploy not run";
        private HavenDevDiagnosticPresentation? _diagnostic;

        public SimulatedHavenDevJourney(SimulatedAospFixture fixture) => _fixture = fixture;

        public HavenDevJourneyPresentationState State => new(
            _fixture.EvidenceLabel,
            _mode,
            _mode == HavenDevExplorerMode.Logical ? _fixture.LogicalRows : _fixture.FilesystemRows,
            _fixture.Edit.Path,
            _tool,
            _output,
            _buildSucceeded,
            _deployStatus,
            _diagnostic);

        public void SwitchMode(HavenDevExplorerMode mode) => _mode = mode;

        public void ApplyFix()
        {
            var source = _fixture.VirtualFiles.Single(file => file.Path == _fixture.Edit.Path).Content;
            Assert.Contains(_fixture.Edit.BrokenText, source);
            Assert.Equal(8, SourceLine(source, _fixture.Edit.BrokenText));
            _fixed = true;
            _diagnostic = null;
        }

        public void Build()
        {
            _tool = HavenDevTool.Build;
            _buildSucceeded = _fixed;
            _output = _fixture.Outputs[_fixed ? "buildPassed" : "buildFailed"];
            _diagnostic = _fixed ? null : new(_fixture.Edit.Path, _fixture.Edit.Line, "Error", "JAVAC-SIM-001", "cannot find symbol");
        }

        public void Deploy()
        {
            Assert.True(_buildSucceeded);
            _deployStatus = _fixture.Outputs["deployPassed"];
        }

        public void RunTests()
        {
            Assert.True(_fixed);
            _tool = HavenDevTool.Tests;
            _output = _fixture.Outputs["tests"];
        }

        public void Show(HavenDevTool tool)
        {
            _tool = tool;
            _output = tool switch
            {
                HavenDevTool.Device => _fixture.Outputs["device"],
                HavenDevTool.Logcat => _fixture.Outputs["logcat"],
                HavenDevTool.Changes => _fixture.Outputs["changes"],
                HavenDevTool.Problems when _diagnostic is not null => $"[SIMULATED — NOT REAL AOSP VALIDATION]\n{_diagnostic.RelativePath}:{_diagnostic.Line}: {_diagnostic.Message}",
                _ => _output
            };
        }

        private static int SourceLine(string source, string text)
        {
            var offset = source.IndexOf(text, StringComparison.Ordinal);
            Assert.True(offset >= 0);
            return source[..offset].Count(c => c == '\n') + 1;
        }
    }

    private sealed record SimulatedAospFixture(
        string EvidenceLabel,
        IReadOnlyList<VirtualFile> VirtualFiles,
        IReadOnlyList<string> LogicalRows,
        IReadOnlyList<string> FilesystemRows,
        EditScenario Edit,
        IReadOnlyDictionary<string, string> Outputs)
    {
        public static SimulatedAospFixture Load()
        {
            var fixturePath = FindFixturePath();
            using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
            var root = document.RootElement;
            var files = root.GetProperty("virtualFiles").EnumerateArray().Select(file => new VirtualFile(
                file.GetProperty("path").GetString()!,
                file.GetProperty("resourceName").GetString()!,
                file.GetProperty("content").GetString()!)).ToArray();
            var editJson = root.GetProperty("edit");
            var outputs = root.GetProperty("outputs").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString()!, StringComparer.Ordinal);
            return new(
                root.GetProperty("evidenceLabel").GetString()!,
                files,
                root.GetProperty("logicalRows").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                root.GetProperty("filesystemRows").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                new(editJson.GetProperty("path").GetString()!, editJson.GetProperty("line").GetInt32(), editJson.GetProperty("brokenText").GetString()!, editJson.GetProperty("fixedText").GetString()!),
                outputs);
        }

        private static string FindFixturePath()
        {
            var relative = Path.Combine("tests", "Haven.Desktop.Tests", "TestFixtures", "HavenDev", "Resources", "simulated-aosp-workspace.fixture.json");
            for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            {
                var candidate = Path.Combine(current.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("Could not locate the SIMULATED Haven Dev fixture resource.", relative);
        }
    }

    private sealed record VirtualFile(string Path, string ResourceName, string Content);
    private sealed record EditScenario(string Path, int Line, string BrokenText, string FixedText);
}
