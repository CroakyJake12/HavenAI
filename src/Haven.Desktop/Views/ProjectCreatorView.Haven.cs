using Avalonia.Threading;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class ProjectCreatorView
{
    private readonly ProjectCreatorHavenScene _havenScene = new();
    private HavenSceneControl? _havenHost;
    private bool _havenSceneWired;

    private void ActivateHavenScene()
    {
        if (_havenHost is null)
        {
            _havenHost = new HavenSceneControl { Root = _havenScene.Root };
            WireHavenScene();
        }

        Content = _havenHost;
        _havenScene.SetViewportWidth(Bounds.Width > 0 ? Bounds.Width : 1200);
        SizeChanged += (_, e) => _havenScene.SetViewportWidth(e.NewSize.Width);
        SyncHavenScene(_viewModel);
    }

    private void WireHavenScene()
    {
        if (_havenSceneWired) return;
        _havenSceneWired = true;

        _havenScene.ProjectNameChanged += value => { if (_viewModel is not null) _viewModel.ProjectName = value; };
        _havenScene.DestinationChanged += value => _viewModel?.SetDestination(value);
        _havenScene.PromptChanged += value => { if (_viewModel is not null) _viewModel.Prompt = value; };
        _havenScene.PackageDescriptionChanged += value => { if (_viewModel is not null) _viewModel.PackageDescription = value; };
        _havenScene.DotNetRequested += (_, _) => _viewModel?.SelectDotNetCommand.Execute(null);
        _havenScene.PackageRequested += (_, _) => _viewModel?.SelectPackageCommand.Execute(null);
        _havenScene.TemplateRequested += name =>
        {
            if (_viewModel is not { } vm) return;
            var option = vm.Templates.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
            if (option is null) return;
            if (vm.SelectDotNetCommand.CanExecute(null)) vm.SelectDotNetCommand.Execute(null);
            if (vm.SelectTemplateCommand.CanExecute(option)) vm.SelectTemplateCommand.Execute(option);
        };
        _havenScene.ReviewRequested += (_, _) =>
        {
            if (_viewModel?.PrepareProposalCommand.CanExecute(null) == true) _viewModel.PrepareProposalCommand.Execute(null);
        };
        _havenScene.ApproveRequested += async (_, _) =>
        {
            if (_viewModel?.CreateCommand.CanExecute(null) == true) await _viewModel.CreateCommand.ExecuteAsync();
        };
        _havenScene.ChooseDestinationRequested += (_, _) => OnChooseDestinationClicked(null, null!);
        _havenScene.OpenFolderRequested += (_, _) => OnOpenFolderClicked(null, null!);
        _havenScene.OpenProjectFileRequested += (_, _) => OnOpenProjectFileClicked(null, null!);
    }

    private void SyncHavenScene(ProjectCreatorPageViewModel? viewModel)
    {
        if (_havenHost is null) return;
        if (viewModel is null)
        {
            _havenScene.SetUnavailable();
            return;
        }

        _havenScene.Sync(new ProjectCreatorSceneState(
            viewModel.ProjectName,
            viewModel.DestinationFolder,
            viewModel.Prompt,
            viewModel.PackageDescription,
            viewModel.SelectedTemplate,
            viewModel.IsDotNetProject,
            viewModel.IsNuGetPackage,
            viewModel.Status,
            viewModel.IsBusy,
            viewModel.PrepareProposalCommand.CanExecute(null),
            viewModel.CreateCommand.CanExecute(null),
            viewModel.Proposal));
    }
}
