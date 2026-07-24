using System.ComponentModel;
using Avalonia.Threading;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class ProjectCreatorView
{
    private void OnAttachedToVisualTree(
        object? sender,
        Avalonia.VisualTree.VisualTreeAttachmentEventArgs e)
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
        Avalonia.VisualTree.VisualTreeAttachmentEventArgs e)
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
        var vm = _viewModel;
        _syncing = true;
        try
        {
            if (vm is null)
            {
                SetFormEnabled(false);
                _statusText.Text = "Project creator is unavailable.";
                _proposalCard.IsVisible = false;
                return;
            }

            SetText(_promptBox, vm.Prompt);
            SetText(_projectNameBox, vm.ProjectName);
            SetText(_destinationBox, vm.DestinationFolder);
            SetText(_packageDescriptionBox, vm.PackageDescription);
            _statusText.Text = vm.Status;

            _dotNetButton.IsEnabled = vm.SelectDotNetCommand.CanExecute(null);
            _packageButton.IsEnabled = vm.SelectPackageCommand.CanExecute(null);
            _reviewButton.IsEnabled = vm.PrepareProposalCommand.CanExecute(null);
            _approveButton.IsEnabled = vm.CreateCommand.CanExecute(null);
            _chooseDestinationButton.IsEnabled = !vm.IsBusy;
            _openFolderButton.IsEnabled = !vm.IsBusy;
            _openProjectFileButton.IsEnabled = !vm.IsBusy;
            _promptBox.IsEnabled = !vm.IsBusy;
            _projectNameBox.IsEnabled = !vm.IsBusy;
            _destinationBox.IsEnabled = !vm.IsBusy;
            _packageDescriptionBox.IsEnabled = !vm.IsBusy;

            ApplyChoiceState(_dotNetButton, vm.IsDotNetProject);
            ApplyChoiceState(_packageButton, vm.IsNuGetPackage);

            _packageDescriptionCard.IsVisible = vm.IsNuGetPackage;
            RebuildTemplateButtons(vm);
            RenderProposal(vm.Proposal);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RebuildTemplateButtons(ProjectCreatorPageViewModel vm)
    {
        if (_templateButtons.Count == 0)
        {
            foreach (var template in vm.Templates)
            {
                var button = new Avalonia.Controls.Button
                {
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    Padding = new Avalonia.Thickness(12),
                    Content = new Avalonia.Controls.StackPanel
                    {
                        Spacing = 3,
                        Children =
                        {
                            new Avalonia.Controls.TextBlock
                            {
                                Text = template.Name,
                                FontWeight = Avalonia.Media.FontWeight.SemiBold
                            },
                            new Avalonia.Controls.TextBlock
                            {
                                Text = template.Description,
                                Foreground = MutedBrush,
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            }
                        }
                    }
                };
                AutomationProperties.SetName(button, $Use {template.Name} template");
                button.Click += (_, _) =>
                {
                    var current = _viewModel;
                    var selected = current?.Templates.FirstOrDefault(
                        item => string.Equals(
                            item.Name,
                            template.Name,
                            StringComparison.Ordinal));
                    if (selected is not null)
                    {
                        current!.SelectTemplateCommand.Execute(selected);
                    }
                };
                _templateButtons.Add(template.Name, button);
                _templatePanel.Children.Add(button);
            }
        }

        foreach (var pair in _templateButtons)
        {
            pair.Value.IsEnabled =
                vm.IsDotNetProject &&
                vm.SelectTemplateCommand.CanExecute(
                    vm.Templates.First(item => item.Name == pair.Key));
            ApplyChoiceState(pair.Value,
                vm.IsDotNetProject &&
                string.Equals(vm.SelectedTemplate, pair.Key, StringComparison.Ordinal));
        }

        _templatePanel.IsVisible = vm.IsDotNetProject;
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
        _proposalSummary.Text = proposal.Summary;
        _proposalTarget.Text = $Target: {proposal.TargetFolder}\nTemplate: {proposal.TemplateName};

        _proposalFiles.Children.Clear();
        foreach (var file in proposal.Files)
        {
            _proposalFiles.Children.Add(DetailRow(file.RelativePath, file.Purpose));
        }

        _proposalCommands.Children.Clear();
        foreach (var command in proposal.Commands)
        {
            _proposalCommands.Children.Add(
                new Avalonia.Controls.Border
                {
                    Background = Brush("#F5F7F4"),
                    BorderBrush = BorderBrush,
                    BorderThickness = new Avalonia.Thickness(1),
                    CornerRadius = new Avalonia.CornerRadius(8),
                    Padding = new Avalonia.Thickness(12),
                    Child = new Avalonia.Controls.StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            new Avalonia.Controls.SelectableTextBlock
                            {
                                Text = command.DisplayText,
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            },
                            new Avalonia.Controls.TextBlock
                            {
                                Text = $Working directory: {command.WorkingDirectory},
                                Foreground = MutedBrush,
                                FontSize = 12,
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            }
                        }
                    }
                });
        }
    }
}
