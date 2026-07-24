using System.Runtime.CompilerServices;
using Haven.Desktop.Services;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Owns project-creator state. Creation is deliberately split into review and explicit approval.
/// </summary>
public sealed class ProjectCreatorPageViewModel : ObservableObject
{
    private readonly ProjectCreationService _creator;
    private readonly Func<Haven.Core.ContainerDefinition, Task> _completed;
    private ProjectCreationKind _kind = ProjectCreationKind.DotNetProject;
    private string _prompt = string.Empty;
    private string _projectName = "MyProject";
    private string _destinationFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private string _selectedTemplate = "Console app";
    private string _packageDescription = string.Empty;
    private string _status = "Describe the project, then review the proposed files and commands.";
    private bool _isBusy;
    private ProjectCreationProposal? _proposal;

    public ProjectCreatorPageViewModel(
        ProjectCreationService creator,
        Func<Haven.Core.ContainerDefinition, Task> completed)
    {
        ArgumentNullException.ThrowIfNull(creator);
        ArgumentNullException.ThrowIfNull(completed);

        _creator = creator;
        _completed = completed;
        Templates =
        [
            new("Console app", "A straightforward executable project."),
            new("Class library", "Reusable .NET code without package metadata."),
            new("Web API", "An ASP.NET Core HTTP API."),
            new("Worker service", "A long-running background service.")
        ];

        SelectDotNetCommand = new RelayCommand(
            () => Kind = ProjectCreationKind.DotNetProject,
            () => !IsBusy);
        SelectPackageCommand = new RelayCommand(
            () => Kind = ProjectCreationKind.NuGetPackage,
            () => !IsBusy);
        SelectTemplateCommand = new RelayCommand<ProjectTemplateOptionViewModel>(
            item =>
            {
                if (item is not null)
                {
                    SelectedTemplate = item.Name;
                }
            },
            _ => !IsBusy);
        PrepareProposalCommand = new RelayCommand(
            PrepareProposal,
            () => !IsBusy);
        CreateCommand = new AsyncRelayCommand(
            CreateApprovedAsync,
            CanCreateApproved);
    }

    public IReadOnlyList<ProjectTemplateOptionViewModel> Templates { get; }

    public ProjectCreationKind Kind
    {
        get => _kind;
        private set
        {
            if (!SetProperty(ref _kind, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(IsDotNetProject));
            RaisePropertyChanged(nameof(IsNuGetPackage));
            RaisePropertyChanged(nameof(KindLabel));
            InvalidateProposal();
            RefreshCommandStates();
        }
    }

    public bool IsDotNetProject => Kind == ProjectCreationKind.DotNetProject;

    public bool IsNuGetPackage => Kind == ProjectCreationKind.NuGetPackage;

    public string KindLabel =>
        IsNuGetPackage ? "NuGet‏ package project" : "New .NET project";

    public string Prompt
    {
        get => _prompt;
        set => SetInput(ref _prompt, value);
    }

    public string ProjectName
    {
        get => _projectName;
        set => SetInput(ref _projectName, value);
    }

    public string DestinationFolder
    {
        get => _destinationFolder;
        set => SetInput(ref _destinationFolder, value);
    }

    public string SelectedTemplate
    {
        get => _selectedTemplate;
        private set => SetInput(ref _selectedTemplate, value);
    }

    public string PackageDescription
    {
        get => _packageDescription;
        set => SetInput(ref _packageDescription, value);
    }

    public ProjectCreationProposal? Proposal
    {
        get => _proposal;
        private set
        {
            if (!SetProperty(ref _proposal, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(HasProposal));
            RaisePropertyChanged(nameof(CanApproveProposal));
            CreateCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasProposal => Proposal is not null;

    public bool CanApproveProposal => CanCreateApproved();

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(CanApproveProposal));
            RefreshCommandStates();
        }
    }

    public RelayCommand SelectDotNetCommand { get; }

    public RelayCommand SelectPackageCommand { get; }

    public RelayCommand<ProjectTemplateOptionViewModel> SelectTemplateCommand { get; }

    public RelayCommand PrepareProposalCommand { get; }

    /// <summary>
    /// Executes only the currently reviewed proposal. Editing any input invalidates it.
    /// </summary>
    public AsyncRelayCommand CreateCommand { get; }

    public AsyncRelayCommand ApproveAndCreateCommand => CreateCommand;

    public void SetDestination(string path) =>
        DestinationFolder = path ?? string.Empty;

    public async Task ConnectAsync(string path)
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "Choose an existing folder or project file first.";
            return;
        }

        IsBusy = true;
        Status = "Inspecting and connecting the selected local project…";
        try
        {
            var result = await _creator.ConnectAsync(
                path,
                CancellationToken.None).ConfigureAwait(true);
            Status = result.Message;
            await _completed(result.Project).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = "Could not connect that project: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PrepareProposal()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            Proposal = ProjectCreationProposalPlanner.Build(
                Kind,
                Prompt,
                ProjectName,
                DestinationFolder,
                SelectedTemplate,
                PackageDescription);
            Status =
                "Proposal ready. Review every file and command, then choose Approve and create.";
        }
        catch (Exception ex)
        {
            Proposal = null;
            Status = "The proposal could not be prepared: " + ex.Message;
        }
    }

    private bool CanCreateApproved()
    {
        var proposal = Proposal;
        return !IsBusy &&
               proposal is not null &&
               proposal.Matches(
                    Kind,
                    Prompt,
                    ProjectName,
                    DestinationFolder,
                    SelectedTemplate,
                    PackageDescription);
    }

    private async Task CreateApprovedAsync()
    {
        var proposal = Proposal;
        if (proposal is null ||
            !proposal.Matches(
                Kind,
                Prompt,
                ProjectName,
                DestinationFolder,
                SelectedTemplate,
                PackageDescription))
        {
            Status = "Review the current proposal before creating the project.";
            InvalidateProposal();
            return;
        }

        IsBusy = true;
        Status = proposal.Kind == ProjectCreationKind.NuGetPackage
            ? "Creating and packing the approved NuGet‏ project…"
            : "Creating the approved local .NET project…";

        try
        {
            var request = new ProjectCreationRequest(
                proposal.Kind,
                proposal.ProjectName,
                proposal.ParentFolder,
                proposal.TemplateName,
                proposal.PackageDescription);
            var result = await _creator.CreateAsync(
                request,
                CancellationToken.None).ConfigureAwait(true);
            Status = result.Message;
            await _completed(result.Project).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = "Project creation stopped: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetInput(
        ref string field,
        string? value,
        [CallerMemberName] string? propertyName = null)
    {
        var normalized = value ?? string.Empty;
        if (!SetProperty(ref field, normalized, propertyName))
        {
            return;
        }

        InvalidateProposal();
        RefreshCommandStates();
    }

    private void InvalidateProposal()
    {
        if (Proposal is null)
        {
            return;
        }

        Proposal = null;
        Status = "Inputs changed. Review the proposal again before creating.";
    }

    private void RefreshCommandStates()
    {
        SelectDotNetCommand.RaiseCanExecuteChanged();
        SelectPackageCommand.RaiseCanExecuteChanged();
        SelectTemplateCommand.RaiseCanExecuteChanged();
        PrepareProposalCommand.RaiseCanExecuteChanged();
        CreateCommand.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(CanApproveProposal));
    }
}

public sealed record ProjectTemplateOptionViewModel(
    string Name,
    string Description);
