using Haven.Desktop.Services;

namespace Haven.Desktop.ViewModels;

public sealed class ProjectCreatorPageViewModel : ObservableObject
{
    private readonly ProjectCreationService _creator;
    private readonly Func<Haven.Core.ContainerDefinition, Task> _completed;
    private ProjectCreationKind _kind = ProjectCreationKind.DotNetProject;
    private string _projectName = "MyProject";
    private string _destinationFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private string _selectedTemplate = "Console app";
    private string _packageDescription = string.Empty;
    private string _status = "Choose how this local project should start.";
    private bool _isBusy;

    public ProjectCreatorPageViewModel(ProjectCreationService creator, Func<Haven.Core.ContainerDefinition, Task> completed)
    {
        _creator = creator;
        _completed = completed;
        SelectDotNetCommand = new RelayCommand(() => Kind = ProjectCreationKind.DotNetProject);
        SelectPackageCommand = new RelayCommand(() => Kind = ProjectCreationKind.NuGetPackage);
        SelectTemplateCommand = new RelayCommand<ProjectTemplateOptionViewModel>(item =>
        {
            if (item is not null) SelectedTemplate = item.Name;
        });
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ProjectName) && Directory.Exists(DestinationFolder));
        Templates =
        [
            new("Console app", "A straightforward executable project."),
            new("Class library", "Reusable .NET code without package metadata."),
            new("Web API", "An ASP.NET Core HTTP API."),
            new("Worker service", "A long-running background service.")
        ];
    }

    public IReadOnlyList<ProjectTemplateOptionViewModel> Templates { get; }
    public ProjectCreationKind Kind
    {
        get => _kind;
        private set
        {
            if (!SetProperty(ref _kind, value)) return;
            RaisePropertyChanged(nameof(IsDotNetProject));
            RaisePropertyChanged(nameof(IsNuGetPackage));
            RaisePropertyChanged(nameof(KindLabel));
        }
    }
    public bool IsDotNetProject => Kind == ProjectCreationKind.DotNetProject;
    public bool IsNuGetPackage => Kind == ProjectCreationKind.NuGetPackage;
    public string KindLabel => IsNuGetPackage ? "NuGet package project" : "New .NET project";
    public string ProjectName { get => _projectName; set { if (SetProperty(ref _projectName, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    public string DestinationFolder { get => _destinationFolder; set { if (SetProperty(ref _destinationFolder, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    public string SelectedTemplate { get => _selectedTemplate; private set => SetProperty(ref _selectedTemplate, value); }
    public string PackageDescription { get => _packageDescription; set => SetProperty(ref _packageDescription, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    public RelayCommand SelectDotNetCommand { get; }
    public RelayCommand SelectPackageCommand { get; }
    public RelayCommand<ProjectTemplateOptionViewModel> SelectTemplateCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }

    public void SetDestination(string path) => DestinationFolder = path;

    public async Task ConnectAsync(string path)
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Inspecting and connecting the selected local project…";
        try
        {
            var result = await _creator.ConnectAsync(path, CancellationToken.None);
            Status = result.Message;
            await _completed(result.Project);
        }
        catch (Exception ex) { Status = "Could not connect that project: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task CreateAsync()
    {
        IsBusy = true;
        Status = IsNuGetPackage ? "Creating and packing the NuGet project…" : "Creating the local .NET project…";
        try
        {
            var request = new ProjectCreationRequest(Kind, ProjectName, DestinationFolder, SelectedTemplate, PackageDescription);
            var result = await _creator.CreateAsync(request, CancellationToken.None);
            Status = result.Message;
            await _completed(result.Project);
        }
        catch (Exception ex) { Status = "Project creation stopped: " + ex.Message; }
        finally { IsBusy = false; }
    }
}

public sealed record ProjectTemplateOptionViewModel(string Name, string Description);
