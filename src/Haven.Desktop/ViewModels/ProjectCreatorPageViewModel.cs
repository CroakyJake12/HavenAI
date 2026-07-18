/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ProjectCreatorPageViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns ProjectCreatorPageViewModel, ProjectTemplateOptionViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Desktop.Services;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents project creator page view model and keeps its related state and behavior together.
/// </summary>
public sealed class ProjectCreatorPageViewModel : ObservableObject
{
    /// <summary>
    /// Stores creator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ProjectCreationService _creator;
    /// <summary>
    /// Stores completed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<Haven.Core.ContainerDefinition, Task> _completed;
    /// <summary>
    /// Stores kind locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ProjectCreationKind _kind = ProjectCreationKind.DotNetProject;
    /// <summary>
    /// Stores project name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _projectName = "MyProject";
    /// <summary>
    /// Stores destination folder locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _destinationFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    /// <summary>
    /// Stores selected template locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _selectedTemplate = "Console app";
    /// <summary>
    /// Stores package description locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _packageDescription = string.Empty;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Choose how this local project should start.";
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates templates, the bindable or domain state represented by this property.
    /// </summary>
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
    /// <summary>
    /// Reports whether dot net project applies to the current state.
    /// </summary>
    public bool IsDotNetProject => Kind == ProjectCreationKind.DotNetProject;
    /// <summary>
    /// Reports whether nu get package applies to the current state.
    /// </summary>
    public bool IsNuGetPackage => Kind == ProjectCreationKind.NuGetPackage;
    /// <summary>
    /// Gets or updates kind label, the bindable or domain state represented by this property.
    /// </summary>
    public string KindLabel => IsNuGetPackage ? "NuGet package project" : "New .NET project";
    /// <summary>
    /// Gets or updates project name, the bindable or domain state represented by this property.
    /// </summary>
    public string ProjectName { get => _projectName; set { if (SetProperty(ref _projectName, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates destination folder, the bindable or domain state represented by this property.
    /// </summary>
    public string DestinationFolder { get => _destinationFolder; set { if (SetProperty(ref _destinationFolder, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates selected template, the bindable or domain state represented by this property.
    /// </summary>
    public string SelectedTemplate { get => _selectedTemplate; private set => SetProperty(ref _selectedTemplate, value); }
    /// <summary>
    /// Gets or updates package description, the bindable or domain state represented by this property.
    /// </summary>
    public string PackageDescription { get => _packageDescription; set => SetProperty(ref _packageDescription, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Reports whether busy applies to the current state.
    /// </summary>
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates select dot net command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SelectDotNetCommand { get; }
    /// <summary>
    /// Gets or updates select package command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SelectPackageCommand { get; }
    /// <summary>
    /// Gets or updates select template command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<ProjectTemplateOptionViewModel> SelectTemplateCommand { get; }
    /// <summary>
    /// Creates command with the invariants required by its callers.
    /// </summary>
    public AsyncRelayCommand CreateCommand { get; }

    /// <summary>
    /// Performs the set destination step owned by this component.
    /// </summary>
    public void SetDestination(string path) => DestinationFolder = path;

    /// <summary>
    /// Performs connect asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Creates async with the invariants required by its callers.
    /// </summary>
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

/// <summary>
/// Represents project template option view model and keeps its related state and behavior together.
/// </summary>
public sealed record ProjectTemplateOptionViewModel(string Name, string Description);
