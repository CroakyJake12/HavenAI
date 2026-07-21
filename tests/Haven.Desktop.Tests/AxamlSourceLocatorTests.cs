/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/AxamlSourceLocatorTests.cs in the automated test suite.
 * What: Protects exact-name, unique-type, ambiguous-name, ambiguous-type, and project-root source-location behavior.
 * Why: The built-in developer tools must never jump to an arbitrary AXAML element when a runtime control is ambiguous.
 */

using Haven.Desktop.DeveloperTools;

namespace Haven.Desktop.Tests;

public sealed class AxamlSourceLocatorTests
{
    [Fact]
    public void Locate_prefers_an_exact_named_element()
    {
        using var project = TestProject.Create();
        project.Write("Views/MainView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid>
                <Button x:Name="SaveButton" Content="Save" />
                <Button Content="Other" />
              </Grid>
            </UserControl>
            """);

        var result = AxamlSourceLocator.Locate(project.Root, "SaveButton", "Button");

        Assert.NotNull(result);
        Assert.True(result!.IsExact);
        Assert.Equal(4, result.Line);
        Assert.Contains("SaveButton", result.Snippet);
    }

    [Fact]
    public void Locate_uses_a_unique_unnamed_control_type()
    {
        using var project = TestProject.Create();
        project.Write("Views/UniqueView.axaml", """
            <UserControl xmlns="https://github.com/avaloniaui">
              <Grid>
                <CalendarDatePicker />
              </Grid>
            </UserControl>
            """);

        var result = AxamlSourceLocator.Locate(project.Root, null, "CalendarDatePicker");

        Assert.NotNull(result);
        Assert.False(result!.IsExact);
        Assert.Equal(3, result.Line);
    }

    [Fact]
    public void Locate_does_not_guess_when_a_name_is_reused()
    {
        using var project = TestProject.Create();
        project.Write("Views/First.axaml", "<UserControl><Button x:Name=\"ActionButton\" /></UserControl>");
        project.Write("Views/Second.axaml", "<UserControl><Button x:Name=\"ActionButton\" /></UserControl>");

        var result = AxamlSourceLocator.Locate(project.Root, "ActionButton", "Button");

        Assert.Null(result);
    }

    [Fact]
    public void Locate_does_not_guess_when_a_type_is_ambiguous()
    {
        using var project = TestProject.Create();
        project.Write("Views/First.axaml", "<UserControl><Button /></UserControl>");
        project.Write("Views/Second.axaml", "<UserControl><Button /></UserControl>");

        var result = AxamlSourceLocator.Locate(project.Root, null, "Button");

        Assert.Null(result);
    }

    [Fact]
    public void FindDesktopProjectRoot_walks_up_from_build_output()
    {
        using var project = TestProject.Create();
        var nested = Path.Combine(project.Root, "bin", "Debug", "net10.0-windows10.0.19041.0");
        Directory.CreateDirectory(nested);

        var result = AxamlSourceLocator.FindDesktopProjectRoot(nested);

        Assert.Equal(project.Root, result);
    }

    private sealed class TestProject : IDisposable
    {
        private TestProject(string root)
        {
            Root = root;
            File.WriteAllText(Path.Combine(root, "Haven.Desktop.csproj"), "<Project />");
        }

        public string Root { get; }

        public static TestProject Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "haven-devtools-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TestProject(root);
        }

        public void Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
