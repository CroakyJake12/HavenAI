using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class ProjectCreatorView
{
    private void OnAttachedToVisualTree(
        object? sender,
        Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Dispatcher.UIThread.Post(AttachCurrentViewModel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(AttachCurrentViewModel);
    }

    private void AttachCurrentViewModel()
    {
        if (ReferenceEquals(_viewModel, DataContext))
        {
            RefreshFromViewModel();
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as ProjectCreatorPageViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RefreshFromViewModel();
    }

    private void OnDetachedFromVisualTree(
        object? sender,
        Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshFromViewModel);
            return;
        }

        RefreshFromViewModel();
    }

    private void RefreshFromViewModel()
    {
        var viewModel = _viewModel;
        _syncing = true;
        try
        {
            if (viewModel is null)
            {
                SetFormEnabled(false);
                _statusText.Text = "Project creator is unavailable.";
                _proposalCard.IsVisible = false;
                return;
            }

            SetText(_promptBox, viewModel.Prompt);
            SetText(_projectNameBox, viewModel.ProjectName);
            SetText(_destinationBox, viewModel.DestinationFolder);
            SetText(_packageDescriptionBox, viewModel.PackageDescription);
            _statusText.Text = viewModel.Status;

            _dotNetButton.IsEnabled = viewModel.SelectDotNetCommand.CanExecute(null);
            _packageButton.IsEnabled = viewModel.SelectPackageCommand.CanExecute(null);
            _reviewButton.IsEnabled = viewModel.PrepareProposalCommand.CanExecute(null);
            _approveButton.IsEnabled = viewModel.CreateCommand.CanExecute(null);
            _chooseDestinationButton.IsEnabled = !viewModel.IsBusy;
            _openFolderButton.IsEnabled = !viewModel.IsBusy;
            _openProjectFileButton.IsEnabled = !viewModel.IsBusy;
            _promptBox.IsEnabled = !viewModel.IsBusy;
            _projectNameBox.IsEnabled = !viewModel.IsBusy;
            _destinationBox.IsEnabled = !viewModel.IsBusy;
            _packageDescriptionBox.IsEnabled = !viewModel.IsBusy;

            ApplyChoiceState(_dotNetButton, viewModel.IsDotNetProject);
            ApplyChoiceState(_packageButton, viewModel.IsNuGetPackage);

            _packageDescriptionCard.IsVisible = viewModel.IsNuGetPackage;
            RebuildTemplateButtons(viewModel);
            RenderProposal(viewModel.Proposal);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RebuildTemplateButtons(ProjectCreatorPageViewModel viewModel)
    {
        if (_templateButtons.Count == 0)
        {
            foreach (var template in viewModel.Templates)
            {
                var capturedTemplate = template;
                var button = TemplateTile(
                    DisplayTemplateName(capturedTemplate.Name),
                    TemplateIcon(capturedTemplate.Name),
                    capturedTemplate.Description);
                button.Click += (_, _) =>
                {
                    var current = _viewModel;
                    if (current is not null &&
                        current.SelectTemplateCommand.CanExecute(capturedTemplate))
                    {
                        current.SelectDotNetCommand.Execute(null);
                        current.SelectTemplateCommand.Execute(capturedTemplate);
                    }
                };

                _templateButtons.Add(capturedTemplate.Name, button);
                _templatePanel.Children.Add(button);
            }

            ApplyTemplateFilter();
        }

        foreach (var template in viewModel.Templates)
        {
            if (!_templateButtons.TryGetValue(template.Name, out var button))
            {
                continue;
            }

            button.IsEnabled = viewModel.SelectTemplateCommand.CanExecute(template);
            ApplyChoiceState(
                button,
                viewModel.IsDotNetProject &&
                string.Equals(
                    viewModel.SelectedTemplate,
                    template.Name,
                    StringComparison.Ordinal));
        }

        _templatePanel.IsVisible = true;
    }

    private void RenderProposal(ProjectCreationProposal? proposal)
    {
        _proposalCard.IsVisible = proposal is not null;
        if (proposal is null)
        {
            _renderedProposal = null;
            _proposalFiles.Children.Clear();
            _proposalCommands.Children.Clear();
            return;
        }

        if (ReferenceEquals(_renderedProposal, proposal))
        {
            return;
        }

        _renderedProposal = proposal;
        _detailsCard.IsVisible = true;
        _proposalSummary.Text = proposal.Summary;
        _proposalTarget.Text =
            $"Target: {proposal.TargetFolder}\nTemplate: {proposal.TemplateName}";

        _proposalFiles.Children.Clear();
        foreach (var file in proposal.Files)
        {
            _proposalFiles.Children.Add(DetailRow(file.RelativePath, file.Purpose));
        }

        _proposalCommands.Children.Clear();
        foreach (var command in proposal.Commands)
        {
            _proposalCommands.Children.Add(
                new Border
                {
                    Background = Brush("#F5F7F4"),
                    BorderBrush = BorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    Child = new StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            new SelectableTextBlock
                            {
                                Text = command.DisplayText,
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = $"Working directory: {command.WorkingDirectory}",
                                Foreground = MutedBrush,
                                FontSize = 12,
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    }
                });
        }
    }
}
