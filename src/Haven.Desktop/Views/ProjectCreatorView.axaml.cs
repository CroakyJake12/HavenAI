using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

/// <summary>
/// Code-behind-only project creator. No Avalonia binding expressions are used.
/// </summary>
public sealed partial class ProjectCreatorView : UserControl
{
    private static IBrush CardBrush => PaletteBrush("HavenPanelBrush", "#FFFFFF");
    private static IBrush BorderBrush => PaletteBrush("HavenLineBrush", "#E2E8E0");
    private static IBrush MutedBrush => PaletteBrush("HavenMutedBrush", "#687076");
    private static IBrush AccentBrush => PaletteBrush("HavenAccentBrush", "#00A7B3");
    private static IBrush AccentTextBrush => PaletteBrush("HavenAccentInkBrush", "#FFFFFF");
    private static IBrush TextBrush => PaletteBrush("HavenTextBrush", "#111111");
    private static IBrush SelectedBrush => PaletteBrush("HavenAccentSoftBrush", "#E7F9FB");
    private static IBrush WarningBrush => PaletteBrush("HavenAccentSoftBrush", "#FFF7E6");

    private readonly Grid _rootHost;
    private readonly TextBox _promptBox;
    private readonly TextBox _templateSearchBox;
    private readonly TextBox _projectNameBox;
    private readonly TextBox _destinationBox;
    private readonly TextBox _packageDescriptionBox;
    private readonly WrapPanel _templatePanel;
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
    private readonly Button _detailsToggleButton;
    private readonly Dictionary<string, Button> _templateButtons = new(StringComparer.Ordinal);
    private Border _detailsCard = null!;

    private ProjectCreatorPageViewModel? _viewModel;
    private ProjectCreationProposal? _renderedProposal;
    private bool _syncing;

    public ProjectCreatorView()
    {
        InitializeComponent();

        _rootHost = this.FindControl<Grid>("CodeBehindHost")
            ?? throw new InvalidOperationException("Project creator host was not initialized.");

        _promptBox = new HavenTextInput
        {
            PlaceholderText = "Describe what you want to build…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 58,
            MaxHeight = 130,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(24)
        };
        _promptBox.PlaceholderText = "Describe Your Project";
        AutomationProperties.SetName(_promptBox, "Project request");

        _templateSearchBox = new HavenTextInput
        {
            PlaceholderText = "Search Templates",
            MinHeight = 58,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(20),
            FontSize = 16
        };
        AutomationProperties.SetName(_templateSearchBox, "Search project templates");

        _projectNameBox = FieldTextBox("Project name");
        _destinationBox = FieldTextBox("Destination folder");

        _packageDescriptionBox = new HavenTextInput
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
        _detailsToggleButton = IconButton("plus", "Project name, destination, and type");

        _templatePanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 300,
            ItemHeight = 222
        };

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

        _rootHost.Background = Brushes.Transparent;
        _rootHost.Children.Add(BuildLayout());

        WireUiEvents();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        Dispatcher.UIThread.Post(AttachCurrentViewModel);
        ActivateHavenScene();
    }

    private Control BuildLayout()
    {
        var typeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _dotNetButton, _packageButton }
        };

        var destinationRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10
        };
        destinationRow.Children.Add(_destinationBox);
        Grid.SetColumn(_chooseDestinationButton, 1);
        destinationRow.Children.Add(_chooseDestinationButton);

        var approvalRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _approveButton }
        };

        var existingActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _openFolderButton, _openProjectFileButton }
        };

        _detailsCard = Card(
            new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    Heading("Project setup", 20),
                    Label("Project type"),
                    typeRow,
                    Label("Project name"),
                    _projectNameBox,
                    Label("Destination"),
                    destinationRow,
                    _packageDescriptionCard,
                    Heading("Open existing work", 16),
                    new TextBlock
                    {
                        Text = "Choose a local project or an existing folder without running creation commands.",
                        Foreground = MutedBrush,
                        TextWrapping = TextWrapping.Wrap
                    },
                    existingActions,
                    approvalRow
                }
            }, padding: 20);
        _detailsCard.IsVisible = false;

        _reviewButton.Content = new HavenIcon
        {
            IconKey = "send",
            Width = 24,
            Height = 24,
            Foreground = AccentTextBrush
        };
        _reviewButton.Width = 62;
        _reviewButton.Height = 62;
        _reviewButton.CornerRadius = new CornerRadius(22);
        _reviewButton.Background = AccentBrush;
        _reviewButton.Foreground = AccentTextBrush;
        AutomationProperties.SetName(_reviewButton, "Review project proposal");

        var composer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            MaxWidth = 1260,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        composer.Children.Add(_detailsToggleButton);
        Grid.SetColumn(_promptBox, 1);
        composer.Children.Add(_promptBox);
        Grid.SetColumn(_reviewButton, 2);
        composer.Children.Add(_reviewButton);

        var installedTemplates = new StackPanel { Spacing = 10 };
        if (IsVisualStudioInstalled())
        {
            installedTemplates.Children.Add(Heading("From an Installed App: Microsoft Visual Studio", 14));
            var packageTile = TemplateTile(
                "NuGet Package",
                "plus",
                "Create a package project using the installed .NET tooling.");
            packageTile.Click += (_, _) =>
            {
                _viewModel?.SelectPackageCommand.Execute(null);
                _detailsCard.IsVisible = true;
            };
            installedTemplates.Children.Add(packageTile);
        }

        var content = new StackPanel
        {
            Width = 1260,
            MaxWidth = 1260,
            Spacing = 18,
            Margin = new Thickness(32, 28, 32, 28),
            Children =
            {
                new TextBlock
                {
                    Text = "Create New Project",
                    FontSize = 46,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                _templateSearchBox,
                Heading("Generic", 14),
                _templatePanel,
                installedTemplates,
                _detailsCard,
                _proposalCard,
            }
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = new Grid
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { content }
                    }
                }
            }
        };
        var footer = new HavenAdaptiveSurface
        {
            Padding = new Thickness(32, 10, 32, 26),
            Child = new StackPanel
            {
                MaxWidth = 1260,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6,
                Children = { _statusText, composer }
            }
        };
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        return root;
    }

    private Border BuildProposalCard()
    {
        var warning = new HavenAdaptiveSurface
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
        _templateSearchBox.TextChanged += (_, _) => ApplyTemplateFilter();
        _detailsToggleButton.Click += (_, _) => _detailsCard.IsVisible = !_detailsCard.IsVisible;
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

    private void ApplyTemplateFilter()
    {
        var query = (_templateSearchBox.Text ?? string.Empty).Trim();
        foreach (var template in _templateButtons)
        {
            var option = _viewModel?.Templates.FirstOrDefault(item => item.Name == template.Key);
            var displayName = DisplayTemplateName(template.Key);
            template.Value.IsVisible = query.Length == 0 ||
                displayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (option?.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    private static bool IsVisualStudioInstalled()
    {
        var installer = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");
        return File.Exists(installer);
    }

    private static string DisplayTemplateName(string templateName) => templateName switch
    {
        "Console app" => "Blank Project",
        "Class library" => "Library",
        "Web API" => "Website",
        "Worker service" => "Worker",
        _ => templateName
    };

    private static string TemplateIcon(string templateName) => templateName switch
    {
        "Console app" => "file",
        "Class library" => "folder",
        "Web API" => "browse",
        "Worker service" => "tasks",
        _ => "file"
    };

    private static Button TemplateTile(string title, string icon, string description)
    {
        var button = new HavenButton
        {
            Width = 290,
            Height = 210,
            Margin = new Thickness(0, 0, 10, 12),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(24),
            Background = SelectedBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new HavenIcon
                    {
                        IconKey = icon,
                        Width = 64,
                        Height = 64,
                        Foreground = TextBrush
                    },
                     Heading(title, 17),
                    new TextBlock
                    {
                        Text = description,
                        FontSize = 10,
                        Foreground = MutedBrush,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        MaxHeight = 28
                    }
                }
            }
        };
        AutomationProperties.SetName(button, $"Use {title} template");
        return button;
    }
}
