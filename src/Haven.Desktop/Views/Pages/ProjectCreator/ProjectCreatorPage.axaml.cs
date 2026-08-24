using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.ProjectCreator;

/// <summary>
/// Project creator page. Scaffolds new .NET projects, NuGet packages, or connects existing folders.
/// </summary>
public sealed partial class ProjectCreatorPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly ProjectCreationService _creator;
    private readonly Func<ContainerDefinition, Task> _completed;

    private ProjectCreationKind _kind = ProjectCreationKind.DotNetProject;
    private string _selectedTemplate = "Console app";
    private bool _isBusy;

    private static readonly (string Name, string Description)[] Templates =
    [
        ("Console app", "A straightforward executable project."),
        ("Class library", "Reusable .NET code without package metadata."),
        ("Web API", "An ASP.NET Core HTTP API."),
        ("Worker service", "A long-running background service.")
    ];

    public ProjectCreatorPage(
        HavenEventBus bus,
        ProjectCreationService creator,
        Func<ContainerDefinition, Task> completed)
    {
        _bus = bus;
        _creator = creator;
        _completed = completed;

        InitializeComponent();
        DestinationBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        BuildTemplateButtons();
        WireEvents();
    }

    private void BuildTemplateButtons()
    {
        foreach (var template in Templates)
        {
            var button = new HavenButton
            {
                Classes = { "choice" },
                Margin = new Avalonia.Thickness(0, 0, 7, 7),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = template.Name, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                        new TextBlock { Text = template.Description, Classes = { "muted" }, FontSize = 9 }
                    }
                }
            };
            button.Click += (_, _) =>
            {
                _selectedTemplate = template.Name;
                SelectedTemplateLabel.Text = $"Selected: {template.Name}";
            };
            TemplateWrapPanel.Children.Add(button);
        }
    }

    private void WireEvents()
    {
        _bus.RegisterElement("ProjectCreator.Actions.DotNet", SelectDotNetButton);
        _bus.WirePointerEvents("ProjectCreator.Actions.DotNet", SelectDotNetButton);
        SelectDotNetButton.Click += (_, _) =>
        {
            _kind = ProjectCreationKind.DotNetProject;
            KindLabel.Text = "New .NET project";
            TemplatePanel.IsVisible = true;
            PackagePanel.IsVisible = false;
            _bus.Fire("ProjectCreator.Actions.DotNet");
        };

        _bus.RegisterElement("ProjectCreator.Actions.Package", SelectPackageButton);
        _bus.WirePointerEvents("ProjectCreator.Actions.Package", SelectPackageButton);
        SelectPackageButton.Click += (_, _) =>
        {
            _kind = ProjectCreationKind.NuGetPackage;
            KindLabel.Text = "NuGet package project";
            TemplatePanel.IsVisible = false;
            PackagePanel.IsVisible = true;
            _bus.Fire("ProjectCreator.Actions.Package");
        };

        _bus.RegisterElement("ProjectCreator.Actions.Browse", BrowseButton);
        _bus.WirePointerEvents("ProjectCreator.Actions.Browse", BrowseButton);
        BrowseButton.Click += async (_, _) =>
        {
            _bus.Fire("ProjectCreator.Actions.Browse");
            var folders = await PickFolderAsync("Choose where Haven should create the project");
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) DestinationBox.Text = path;
        };

        _bus.RegisterElement("ProjectCreator.Actions.OpenProject", OpenProjectFileButton);
        _bus.WirePointerEvents("ProjectCreator.Actions.OpenProject", OpenProjectFileButton);
        OpenProjectFileButton.Click += async (_, _) =>
        {
            _bus.Fire("ProjectCreator.Actions.OpenProject");
            await ConnectProjectAsync();
        };

        _bus.RegisterElement("ProjectCreator.Actions.OpenFolder", OpenFolderButton);
        _bus.WirePointerEvents("ProjectCreator.Actions.OpenFolder", OpenFolderButton);
        OpenFolderButton.Click += async (_, _) =>
        {
            _bus.Fire("ProjectCreator.Actions.OpenFolder");
            var folders = await PickFolderAsync("Open an existing local project or source folder");
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) await ConnectFolderAsync(path);
        };

        _bus.RegisterElement("ProjectCreator.Actions.Create", CreateButton);
        _bus.WirePointerEvents("ProjectCreator.Actions.Create", CreateButton);
        CreateButton.Click += async (_, _) =>
        {
            _bus.Fire("ProjectCreator.Actions.Create");
            await CreateAsync();
        };
    }

    private async Task CreateAsync()
    {
        var name = ProjectNameBox.Text?.Trim();
        var dest = DestinationBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dest) || !Directory.Exists(dest)) return;

        _isBusy = true;
        CreateButton.IsEnabled = false;
        StatusText.Text = _kind == ProjectCreationKind.NuGetPackage
            ? "Creating and packing the NuGet project..."
            : "Creating the local .NET project...";
        try
        {
            var request = new ProjectCreationRequest(_kind, name, dest, _selectedTemplate,
                PackageDescriptionBox.Text?.Trim() ?? string.Empty);
            var result = await _creator.CreateAsync(request, CancellationToken.None);
            StatusText.Text = result.Message;
            await _completed(result.Project);
        }
        catch (Exception ex) { StatusText.Text = "Project creation stopped: " + ex.Message; }
        finally { _isBusy = false; CreateButton.IsEnabled = true; }
    }

    private async Task ConnectProjectAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a local project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Project and solution files") { Patterns = ["*.sln", "*.slnx", "*.csproj", "*.fsproj", "*.vbproj", "*.vcxproj"] },
                FilePickerFileTypes.All
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path)) await ConnectFolderAsync(path);
    }

    private async Task ConnectFolderAsync(string path)
    {
        if (_isBusy) return;
        _isBusy = true;
        StatusText.Text = "Inspecting and connecting the selected local project...";
        try
        {
            var result = await _creator.ConnectAsync(path, CancellationToken.None);
            StatusText.Text = result.Message;
            await _completed(result.Project);
        }
        catch (Exception ex) { StatusText.Text = "Could not connect that project: " + ex.Message; }
        finally { _isBusy = false; }
    }

    private async Task<IReadOnlyList<IStorageFolder>> PickFolderAsync(string title)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        return storage is null
            ? []
            : await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
    }
}
