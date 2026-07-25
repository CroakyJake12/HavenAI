using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

/// <summary>
/// Code-behind-only project creator. No Avalonia binding expressions are used.
/// </summary>
public sealed partial class ProjectCreatorView : UserControl
{
    private static readonly IBrush PageBrush = Brush("#FBFDF7");
    private static readonly IBrush CardBrush = Brush("#FFFFFF");
    private static readonly IBrush BorderBrush = Brush("#E2E8E0");
    private static readonly IBrush MutedBrush = Brush("#687076");
    private static readonly IBrush AccentBrush = Brush("#111111");
    private static readonly IBrush AccentTextBrush = Brush("#FFFFFF");
    private static readonly IBrush SelectedBrush = Brush("#E7F9FB");
    private static readonly IBrush WarningBrush = Brush("#FFF7E6");

    private readonly ScrollViewer _rootScroll;
    private readonly TextBox _promptBox;
    private readonly TextBox _projectNameBox;
    private readonly TextBox _destinationBox;
    private readonly TextBox _packageDescriptionBox;
    private readonly StackPanel _templatePanel;
    private readonly Border _packageDescriptionCard;
    private readonly Border _proposalCard;
    private readonly TextBlock _proposalSummary;
    private readonly TextBlock _proposalTarget;
    private readonly StackPanel _proposalFiles;
    private readonly StackPanel _proposalCommands;
    private readonly TextBlock _statusText;
    private readonly Button _dotNetButton;
    private readonly Button _packageButton;
    private readonly Button _reviewButton;
    private readonly Button _approveButton;
    private readonly Button _chooseDestinationButton;
    private readonly Button _openFolderButton;
    private readonly Button _openProjectFileButton;
    private readonly Dictionary<string, Button> _templateButtons = new(StringComparer.Ordinal);

    private ProjectCreatorPageViewModel? _viewModel;
    private ProjectCreationProposal? _renderedProposal;
    private bool _syncing;

    public ProjectCreatorView()
    {
        InitializeComponent();

        _rootScroll = this.FindControl<ScrollViewer>("RootScroll")
            ?? throw new InvalidOperationException("Project creator scroll host was not initialized.");

        _promptBox = new TextBox
        {
            PlaceholderText = "Describe what you want to build…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 104,
            MaxHeight = 180,
            Padding = new Thickness(14)
        };
        AutomationProperties.SetName(_promptBox, "Project request");

        _projectNameBox = FieldTextBox("Project name");
        _destinationBox = FieldTextBox("Destination folder");

        _packageDescriptionBox = new TextBox
        {
            PlaceholderText = "Short package description",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            Padding = new Thickness(12)
        };
        AutomationProperties.SetName(_packageDescriptionBox, "Package description");

        _dotNetButton = ChoiceButton(".NET project");
        _packageButton = ChoiceButton("NuGet package");
        _reviewButton = PrimaryButton("Review proposal");
        _approveButton = PrimaryButton("Approve and create");
        _chooseDestinationButton = SecondaryButton("Choose folder");
        _openFolderButton = SecondaryButton("Open existing folder");
        _openProjectFileButton = SecondaryButton("Open project file");

        _templatePanel = new StackPanel { Spacing = 8 };

        _packageDescriptionCard = Card(
            new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Heading("Package description", 16),
                    new TextBlock
                    {
                        Text = "Included in package metadata after approval.",
                        Foreground = MutedBrush,
                        TextWrapping = TextWrapping.Wrap
                    },
                    _packageDescriptionBox
                }
            },
            padding: 16);

        _proposalSummary = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        _proposalTarget = new TextBlock
        {
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap
        };
        _proposalFiles = new StackPanel { Spacing = 6 };
        _proposalCommands = new StackPanel { Spacing = 8 };
        _proposalCard = BuildProposalCard();

        _statusText = new TextBlock
        {
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 22
        };
        AutomationProperties.SetName(_statusText, "Project creation status");

        _rootScroll.Background = PageBrush;
        _rootScroll.Content = BuildLayout();

        WireUiEvents();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        Dispatcher.UIThread.Post(AttachCurrentViewModel);
    }

    private Control BuildLayout()
    {
        var typeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _dotNetButton, _packageButton }
        };

        var destinationRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _destinationBox, _chooseDestinationButton }
        };

        var form = Card(
            new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    Heading("Project details", 20),
                    Label("What should Haven create?"),
                    _promptBox,
                    Label("Project type"),
                    typeRow,
                    Label("Project name"),
                    _projectNameBox,
                    Label("Destination"),
                    destinationRow,
                    Label("Template"),
                    _templatePanel,
                    _packageDescriptionCard
                }
            });

        var existingActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _openFolderButton, _openProjectFileButton }
        };
        var existing = Card(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Heading("Open existing work", 18),
                    new TextBlock
                    {
                        Text = "Connecting an existing project does not run creation commands.",
                        Foreground = MutedBrush,
                        TextWrapping = TextWrapping.Wrap
                    },
                    existingActions
                }
            });

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { _reviewButton, _approveButton }
        };

        var content = new StackPanel
        {
            Width = 960,
            MaxWidth = 960,
            Spacing = 20,
            Margin = new Thickness(32, 28, 32, 48),
            Children =
            {
                new TextBlock
                {
                    Text = "New project",
                    FontSize = 32,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = "Describe the result, review the exact local changes, then approve creation.",
                    Foreground = MutedBrush,
                    FontSize = 15,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, -12, 0, 0)
                },
                form,
                _proposalCard,
                existing,
                _statusText,
                actionRow
            }
        };

        return new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { content }
        };
    }

    private Border BuildProposalCard()
    {
        var warning = new Border
        {
            Background = WarningBrush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text =
                    "Nothing below runs until you choose Approve and create.\n" +
                    "Editing any input invalidates this proposal.",
                TextWrapping = TextWrapping.Wrap
            }
        };

        return Card(
            new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    Heading("Review proposal", 22),
                    _proposalSummary,
                    _proposalTarget,
                    warning,
                    Heading("Proposed files", 16),
                    _proposalFiles,
                    Heading("Proposed commands", 16),
                    _proposalCommands
                }
            });
    }

    private void WireUiEvents()
    {
        _promptBox.TextChanged += (_, _) =>
        {
            if (!_syncing && _viewModel is not null)
            {
                _viewModel.Prompt = _promptBox.Text ?? string.Empty;
            }
        };
        _projectNameBox.TextChanged += (_, _) =>
        {
            if (!_syncing && _viewModel is not null)
            {
                _viewModel.ProjectName = _projectNameBox.Text ?? string.Empty;
            }
        };
        _destinationBox.TextChanged += (_, _) =>
        {
            if (!_syncing && _viewModel is not null)
            {
                _viewModel.SetDestination(_destinationBox.Text ?? string.Empty);
            }
        };
        _packageDescriptionBox.TextChanged += (_, _) =>
        {
            if (!_syncing && _viewModel is not null)
            {
                _viewModel.PackageDescription = _packageDescriptionBox.Text ?? string.Empty;
            }
        };

        _dotNetButton.Click += (_, _) => _viewModel?.SelectDotNetCommand.Execute(null);
        _packageButton.Click += (_, _) => _viewModel?.SelectPackageCommand.Execute(null);
        _reviewButton.Click += (_, _) => _viewModel?.PrepareProposalCommand.Execute(null);
        _approveButton.Click += async (_, _) =>
        {
            if (_viewModel is not null)
            {
                await _viewModel.CreateCommand.ExecuteAsync();
            }
        };
        _chooseDestinationButton.Click += OnChooseDestinationClicked;
        _openFolderButton.Click += OnOpenFolderClicked;
        _openProjectFileButton.Click += OnOpenProjectFileClicked;
    }
}
