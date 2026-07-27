using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Haven.Desktop.Views.Shell.NativePresentation;

internal sealed partial class NativeProjectsPage : ContentControl, IDisposable
{
    private static readonly IBrush PageBrush = Brush("#FBFDF7");
    private static readonly IBrush CardBrush = Brush("#FFFFFF");
    private static readonly IBrush MutedBrush = Brush("#687076");
    private new static readonly IBrush BorderBrush = Brush("#E4E9E1");
    private static readonly IBrush AccentBrush = Brush("#111111");
    private static readonly IBrush CyanBrush = Brush("#62DDE7");
    private static readonly IBrush UnreadBrush = Brush("#E7F9FB");

    private readonly object _source;
    private readonly Func<IEnumerable<object>> _fallbackProjects;
    private readonly Func<Task> _openCreator;
    private readonly Func<object, Task> _openProjectFallback;
    private readonly Func<object, Task> _archiveProjectFallback;
    private readonly NativeProjectUiStateStore _stateStore;
    private readonly CancellationTokenSource _lifetime = new();

    private readonly TextBox _searchBox;
    private readonly WrapPanel _pinnedPanel;
    private readonly WrapPanel _unreadPanel;
    private readonly WrapPanel _projectPanel;
    private readonly TextBlock _pinnedHeading;
    private readonly TextBlock _unreadHeading;
    private readonly TextBlock _projectHeading;
    private readonly Border _emptyState;
    private readonly TextBlock _status;
    private readonly Button _refreshButton;
    private readonly Button _newProjectButton;

    private INotifyPropertyChanged? _notifySource;
    private INotifyCollectionChanged? _notifyCollection;
    private IEnumerable<object>? _observedCollection;
    private bool _disposed;
    private bool _refreshing;

    public NativeProjectsPage(
        object legacySurface,
        Func<IEnumerable<object>> fallbackProjects,
        Func<Task> openCreator,
        Func<object, Task> openProjectFallback,
        Func<object, Task> archiveProjectFallback,
        NativeProjectUiStateStore? stateStore = null)
    {
        ArgumentNullException.ThrowIfNull(legacySurface);
        _source = NativePresentationReflection.Get(legacySurface, "DataContext") ?? legacySurface;
        _fallbackProjects = fallbackProjects ?? throw new ArgumentNullException(nameof(fallbackProjects));
        _openCreator = openCreator ?? throw new ArgumentNullException(nameof(openCreator));
        _openProjectFallback = openProjectFallback ?? throw new ArgumentNullException(nameof(openProjectFallback));
        _archiveProjectFallback = archiveProjectFallback ?? throw new ArgumentNullException(nameof(archiveProjectFallback));
        _stateStore = stateStore ?? new NativeProjectUiStateStore();

        _searchBox = new TextBox
        {
            PlaceholderText = "Search Projects",
            MinWidth = 640,
            MaxWidth = 920,
            Height = 64,
            Padding = new Thickness(54, 12, 18, 12),
            CornerRadius = new CornerRadius(18),
            BorderBrush = BorderBrush,
            Background = Brush("#FCFCFC"),
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _searchBox.Classes.Add("native-project-search");
        _searchBox.TextChanged += OnSearchChanged;
        AutomationProperties.SetName(_searchBox, "Search projects");

        _refreshButton = IconButton("refresh", "Refresh projects");
        _refreshButton.Click += OnRefreshClicked;
        AutomationProperties.SetName(_refreshButton, "Refresh projects");

        _newProjectButton = Button("Create New Project", false);
        _newProjectButton.MinWidth = 620;
        _newProjectButton.MinHeight = 58;
        _newProjectButton.FontSize = 17;
        _newProjectButton.FontWeight = FontWeight.SemiBold;
        _newProjectButton.Click += OnNewProjectClicked;
        AutomationProperties.SetName(_newProjectButton, "Create a new project");

        _pinnedHeading = Heading("Pinned Projects", 16);
        _pinnedPanel = ProjectTilePanel();

        _unreadHeading = Heading("Unread Changes", 16);
        _unreadPanel = ProjectTilePanel();

        _projectHeading = Heading("All Projects", 16);
        _projectPanel = ProjectTilePanel();

        _emptyState = BuildEmptyState();
        _status = new TextBlock
        {
            Foreground = MutedBrush,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        Content = BuildLayout();
        Background = PageBrush;

        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    public event EventHandler<object>? ProjectOpened;

    public event EventHandler? ProjectCreatorOpened;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AttachedToVisualTree -= OnAttached;
        DetachedFromVisualTree -= OnDetached;
        _lifetime.Cancel();
        DetachNotifications();
        _lifetime.Dispose();
        _searchBox.TextChanged -= OnSearchChanged;
        _refreshButton.Click -= OnRefreshClicked;
        _newProjectButton.Click -= OnNewProjectClicked;
    }
}
