/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/ExperienceShellHost.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns ExperienceShellHost. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Controls;

/// <summary>
/// Places one persistent experience rail beside the existing Haven shell. The existing
/// page, tab and contextual-sidebar implementation remains authoritative.
/// </summary>
public sealed class ExperienceShellHost : Grid, IDisposable
{
    /// <summary>
    /// Stores maximum pinned modes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    internal const int MaximumPinnedModes = 6;
    /// <summary>
    /// Stores mode profile prefix locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    internal const string ModeProfilePrefix = "App profile · ";

    /// <summary>
    /// Stores fixed mode keys locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> FixedModeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "chat", "study", "tasks", "studio", "plan", "browse"
    };

    /// <summary>
    /// Stores experience buttons locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _experienceButtons = new() { Spacing = 5 };
    /// <summary>
    /// Stores pinned buttons locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _pinnedButtons = new() { Spacing = 5 };
    /// <summary>
    /// Stores mode cards locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _modeCards = new() { Spacing = 9 };
    /// <summary>
    /// Stores mode search locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBox _modeSearch = new() { PlaceholderText = "Search modes", MinWidth = 280 };
    /// <summary>
    /// Stores overlay status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBlock _overlayStatus = new() { Classes = { "muted" }, FontSize = 11 };
    /// <summary>
    /// Stores overlay locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Grid _overlay;
    /// <summary>
    /// Stores mode refresh timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly DispatcherTimer _modeRefreshTimer;
    /// <summary>
    /// Stores mode registry locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeRegistry? _modeRegistry;
    /// <summary>
    /// Stores pins locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPinRepository? _pins;
    /// <summary>
    /// Stores shell locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private MainView? _shell;
    /// <summary>
    /// Stores shell notifications locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private INotifyPropertyChanged? _shellNotifications;
    /// <summary>
    /// Stores modes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private IReadOnlyList<ModeDefinition> _modes = [];
    /// <summary>
    /// Stores ordered pins locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private List<ModePin> _orderedPins = [];
    /// <summary>
    /// Stores is reorder mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isReorderMode;
    /// <summary>
    /// Stores is refreshing locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isRefreshing;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    public ExperienceShellHost(Control existingShell)
    {
        ArgumentNullException.ThrowIfNull(existingShell);
        ColumnDefinitions = new ColumnDefinitions("76,*");
        Background = Brushes.Transparent;

        _modeRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _modeRefreshTimer.Tick += OnModeRefreshTimerTick;

        var home = RailButton("⌂", "Home", "ExperienceHomeButton");
        home.Margin = new Thickness(0, 0, 0, 10);
        home.Click += (_, _) => _shell?.NavigateHomeCommand.Execute(null);

        var allModes = RailButton("▦", "All modes", "ExperienceAllModesButton");
        allModes.Click += async (_, _) => await ShowModeLibraryAsync();

        var settings = RailButton("⚙", "Settings", "ExperienceSettingsButton");
        settings.Click += (_, _) => _shell?.NavigateSettingsCommand.Execute(null);

        var rail = new HavenAdaptiveSurface
        {
            Width = 66,
            Margin = new Thickness(7, 7, 3, 7),
            Padding = new Thickness(7),
            CornerRadius = new CornerRadius(17),
            Background = ResourceBrush("HavenElevatedBrush", Color.FromArgb(232, 20, 20, 20)),
            BorderBrush = ResourceBrush("HavenLineBrush", Colors.Transparent),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children =
                {
                    home,
                    WithRow(new ScrollViewer
                    {
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        Content = new StackPanel
                        {
                            Spacing = 9,
                            Children = { _experienceButtons, Divider(), _pinnedButtons, allModes }
                        }
                    }, 1),
                    WithRow(new StackPanel { Spacing = 5, Children = { settings } }, 2)
                }
            }
        };

        _modeSearch.TextChanged += (_, _) => RebuildModeCards();
        _overlay = BuildModeOverlay();
        Grid.SetColumnSpan(_overlay, 2);

        Children.Add(rail);
        Grid.SetColumn(existingShell, 1);
        Children.Add(existingShell);
        Children.Add(_overlay);

        if (App.Services is not null)
        {
            _modeRegistry = App.Services.GetService<IModeRegistry>();
            _pins = App.Services.GetService<IPinRepository>();
        }

        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DataContextChanged -= OnDataContextChanged;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        _modeRefreshTimer.Stop();
        _modeRefreshTimer.Tick -= OnModeRefreshTimerTick;
        if (_shellNotifications is not null) _shellNotifications.PropertyChanged -= OnShellPropertyChanged;
        _shellNotifications = null;
        _shell = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Builds mode overlay from the currently available inputs.
    /// </summary>
    private Grid BuildModeOverlay()
    {
        var close = new HavenButton { Content = "Close" };
        close.Classes.Add("secondary");
        close.Click += (_, _) => _overlay.IsVisible = false;
        return new Grid
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(224, 8, 8, 10)),
            Children =
            {
                new HavenAdaptiveSurface
                {
                    Margin = new Thickness(28),
                    Padding = new Thickness(24),
                    CornerRadius = new CornerRadius(22),
                    Background = ResourceBrush("HavenElevatedBrush", Color.FromArgb(250, 28, 28, 31)),
                    BorderBrush = ResourceBrush("HavenLineStrongBrush", Color.FromArgb(90, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Child = new Grid
                    {
                        RowDefinitions = new RowDefinitions("Auto,Auto,*"),
                        RowSpacing = 14,
                        Children =
                        {
                            new Grid
                            {
                                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                                Children =
                                {
                                    new StackPanel
                                    {
                                        Spacing = 3,
                                        Children =
                                        {
                                            new TextBlock { Text = "ALL HAVEN MODES", Classes = { "eyebrow" } },
                                            new TextBlock { Text = "Choose an experience or pin up to six modes", FontSize = 24, FontWeight = FontWeight.SemiBold },
                                            new TextBlock
                                            {
                                                Text = "Chat, Study and Tasks live inside one conversation experience. Home always remains fixed above every configurable item.",
                                                Classes = { "muted" },
                                                FontSize = 11,
                                                TextWrapping = TextWrapping.Wrap
                                            }
                                        }
                                    },
                                    WithColumn(close, 1)
                                }
                            },
                            WithRow(new Grid
                            {
                                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                                ColumnSpacing = 12,
                                Children = { _modeSearch, WithColumn(_overlayStatus, 1) }
                            }, 1),
                            WithRow(new ScrollViewer
                            {
                                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                                Content = _modeCards
                            }, 2)
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Handles the attached to visual tree event raised by the UI or runtime.
    /// </summary>
    private async void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        await RefreshAsync();
        if (_modes.Count == 0 && !_disposed) _modeRefreshTimer.Start();
    }

    /// <summary>
    /// Handles the mode refresh timer tick event raised by the UI or runtime.
    /// </summary>
    private async void OnModeRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            _modeRefreshTimer.Stop();
            return;
        }
        await RefreshAsync();
        if (_modes.Count > 0) _modeRefreshTimer.Stop();
    }

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshAsync()
    {
        if (_disposed || _isRefreshing || _modeRegistry is null || _pins is null) return;
        _isRefreshing = true;
        try
        {
            var modesTask = _modeRegistry.GetModesAsync(CancellationToken.None);
            var pinsTask = _pins.GetPinsAsync(CancellationToken.None);
            await Task.WhenAll(modesTask, pinsTask).ConfigureAwait(false);
            _modes = modesTask.Result.Where(mode => mode.IsEnabled).ToArray();
            _orderedPins = pinsTask.Result.OrderBy(pin => pin.SortOrder).Take(MaximumPinnedModes).ToList();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RebuildRail();
                RebuildModeCards();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _overlayStatus.Text = "App navigation could not load: " + ex.Message);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// Performs the rebuild rail step owned by this component.
    /// </summary>
    private void RebuildRail()
    {
        _experienceButtons.Children.Clear();
        _pinnedButtons.Children.Clear();

        _experienceButtons.Children.Add(GroupButton("▰", "Chat", "ExperienceChatButton", BuiltIn("chat", "study", "tasks")));
        _experienceButtons.Children.Add(GroupButton("⌘", "Studio", "ExperienceStudioButton", BuiltIn("studio")));
        _experienceButtons.Children.Add(PlanButton());
        _experienceButtons.Children.Add(DirectButton("◉", "Browse", "ExperienceBrowseButton", () =>
        {
            _shell?.NavigateBrowserCommand.Execute(null);
            return Task.CompletedTask;
        }));

        foreach (var mode in _orderedPins
                     .Select(pin => _modes.FirstOrDefault(item => item.Id == pin.ModeId))
                     .Where(mode => mode is not null && !FixedModeKeys.Contains(mode.Key))
                     .Cast<ModeDefinition>())
            _pinnedButtons.Children.Add(PinnedModeButton(mode));

        if (_pinnedButtons.Children.Count == 0)
        {
            _pinnedButtons.Children.Add(new TextBlock
            {
                Text = "No pins",
                Classes = { "muted2" },
                FontSize = 9,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 3)
            });
        }
        UpdateActiveState();
    }

    /// <summary>
    /// Performs the plan button step owned by this component.
    /// </summary>
    private Button PlanButton()
    {
        var button = RailButton("▦", "Plan and automations", "ExperiencePlanButton");
        var panel = new StackPanel { Width = 280, Spacing = 4 };
        panel.Children.Add(FlyoutEntry("Plan", "Tasks, calendars and reviewed AI proposals", "plan", () =>
        {
            _shell?.NavigatePlanCommand.Execute(null);
            return Task.CompletedTask;
        }));
        panel.Children.Add(FlyoutEntry("Automations", "Scheduled and condition-based work", "automation", () =>
        {
            _shell?.NavigateAutomationsCommand.Execute(null);
            return Task.CompletedTask;
        }));
        button.Flyout = new HavenAdaptivePopup { Placement = PlacementMode.Right, Content = panel };
        return button;
    }

    /// <summary>
    /// Performs the group button step owned by this component.
    /// </summary>
    private Button GroupButton(string glyph, string tooltip, string name, IReadOnlyList<ModeDefinition> modes)
    {
        var button = RailButton(glyph, tooltip, name);
        var panel = new StackPanel { Width = 310, Spacing = 4 };
        foreach (var mode in modes)
            panel.Children.Add(FlyoutEntry(mode.Name, mode.Description, mode.IconKey, () => OpenModeAsync(mode)));
        if (modes.Count == 0)
            panel.Children.Add(new TextBlock { Text = "Apps are still loading…", Classes = { "muted" }, Margin = new Thickness(10) });
        button.Flyout = new HavenAdaptivePopup { Placement = PlacementMode.Right, Content = panel };
        return button;
    }

    /// <summary>
    /// Performs the direct button step owned by this component.
    /// </summary>
    private Button DirectButton(string glyph, string tooltip, string name, Func<Task> action)
    {
        var button = RailButton(glyph, tooltip, name);
        button.Click += async (_, _) => await action();
        return button;
    }

    /// <summary>
    /// Performs the pinned mode button step owned by this component.
    /// </summary>
    private Button PinnedModeButton(ModeDefinition mode)
    {
        var button = RailButton(ModeGlyph(mode.IconKey), mode.Name + (_isReorderMode ? " · drag to reorder" : string.Empty), "PinnedMode_" + mode.Id.ToString("N"));
        button.Click += async (_, _) =>
        {
            if (!_isReorderMode) await OpenModeAsync(mode);
        };
        button.ContextMenu = new HavenContextMenu
        {
            ItemsSource = new object[]
            {
                MenuAction(_isReorderMode ? "Finish re-ordering" : "Re-order pinned modes", () =>
                {
                    _isReorderMode = !_isReorderMode;
                    RebuildRail();
                }),
                MenuAction("Unpin", () => _ = UnpinAsync(mode.Id))
            }
        };

        DragDrop.SetAllowDrop(button, true);
        DragDrop.AddDragOverHandler(button, (_, args) =>
        {
            args.DragEffects = _isReorderMode && IsPinTransfer(args.DataTransfer) ? DragDropEffects.Move : DragDropEffects.None;
            args.Handled = true;
        });
        DragDrop.AddDropHandler(button, async (_, args) =>
        {
            if (!_isReorderMode || !TryReadPinTransfer(args.DataTransfer, out var draggedId)) return;
            args.Handled = true;
            await MovePinAsync(draggedId, mode.Id);
        });
        button.PointerPressed += async (_, args) =>
        {
            if (!_isReorderMode || !args.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText("haven-mode-pin:" + mode.Id.ToString("D")));
            await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Move);
        };
        return button;
    }

    /// <summary>
    /// Performs show mode library asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ShowModeLibraryAsync()
    {
        await RefreshAsync();
        _modeSearch.Text = string.Empty;
        RebuildModeCards();
        _overlay.IsVisible = true;
        _modeSearch.Focus();
    }

    /// <summary>
    /// Performs the rebuild mode cards step owned by this component.
    /// </summary>
    private void RebuildModeCards()
    {
        _modeCards.Children.Clear();
        var query = _modeSearch.Text?.Trim() ?? string.Empty;
        var pinnedIds = _orderedPins.Select(pin => pin.ModeId).ToHashSet();
        var visible = _modes
            .Where(mode => query.Length == 0
                           || mode.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || mode.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || mode.TagsJson.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(mode => mode.Source)
            .ThenBy(mode => mode.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _overlayStatus.Text = $"{visible.Length} mode{(visible.Length == 1 ? string.Empty : "s")} · {_orderedPins.Count}/{MaximumPinnedModes} pinned";

        foreach (var mode in visible) _modeCards.Children.Add(BuildModeCard(mode, pinnedIds.Contains(mode.Id)));
        if (visible.Length == 0)
            _modeCards.Children.Add(new TextBlock { Text = "No matching modes.", Classes = { "muted" }, Margin = new Thickness(0, 8) });
    }

    /// <summary>
    /// Builds mode card from the currently available inputs.
    /// </summary>
    private Control BuildModeCard(ModeDefinition mode, bool isPinned)
    {
        var isFixed = FixedModeKeys.Contains(mode.Key);
        var open = new HavenButton { Content = "Open" };
        open.Classes.Add("accent");
        open.Click += async (_, _) =>
        {
            _overlay.IsVisible = false;
            await OpenModeAsync(mode);
        };
        var pin = new HavenButton
        {
            Content = isFixed ? "Fixed on rail" : isPinned ? "Unpin" : "Pin",
            IsEnabled = !isFixed
        };
        pin.Click += async (_, _) =>
        {
            if (isPinned) await UnpinAsync(mode.Id);
            else await PinAsync(mode.Id);
            RebuildModeCards();
        };

        return new HavenAdaptiveSurface
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(14),
            Background = ResourceBrush("HavenPanel2Brush", Color.FromArgb(215, 35, 35, 39)),
            BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(45, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                ColumnSpacing = 12,
                Children =
                {
                    new HavenAdaptiveSurface
                    {
                        Width = 42,
                        Height = 42,
                        CornerRadius = new CornerRadius(13),
                        Background = ResourceBrush("HavenAccentSoftBrush", Color.FromArgb(55, 0, 120, 212)),
                        Child = new TextBlock
                        {
                            Text = ModeGlyph(mode.IconKey),
                            FontSize = 19,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    },
                    WithColumn(new StackPanel
                    {
                        Spacing = 2,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = mode.Name, FontWeight = FontWeight.SemiBold, FontSize = 15 },
                            new TextBlock { Text = mode.Description, Classes = { "muted" }, FontSize = 10, TextWrapping = TextWrapping.Wrap },
                            new TextBlock { Text = mode.Source + " · " + mode.Version, Classes = { "muted2" }, FontSize = 9 }
                        }
                    }, 1),
                    WithColumn(pin, 2),
                    WithColumn(open, 3)
                }
            }
        };
    }

    /// <summary>
    /// Performs open mode asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task OpenModeAsync(ModeDefinition mode)
    {
        if (_shell is null) return;
        await _shell.OpenModeDefinitionAsync(mode);
        UpdateActiveState();
    }

    /// <summary>
    /// Performs pin asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task PinAsync(Guid modeId)
    {
        if (_pins is null || _orderedPins.Any(pin => pin.ModeId == modeId)) return;
        if (_orderedPins.Count >= MaximumPinnedModes)
        {
            _overlayStatus.Text = "You can pin up to six modes. Unpin one before adding another.";
            return;
        }
        var pin = new ModePin(Guid.NewGuid(), modeId, _orderedPins.Count, DateTimeOffset.UtcNow);
        await _pins.UpsertPinAsync(pin, CancellationToken.None).ConfigureAwait(false);
        _orderedPins.Add(pin);
        await Dispatcher.UIThread.InvokeAsync(RebuildRail);
    }

    /// <summary>
    /// Performs unpin asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task UnpinAsync(Guid modeId)
    {
        if (_pins is null) return;
        await _pins.DeletePinAsync(modeId, CancellationToken.None).ConfigureAwait(false);
        _orderedPins.RemoveAll(pin => pin.ModeId == modeId);
        await PersistPinOrderAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RebuildRail();
            RebuildModeCards();
        });
    }

    /// <summary>
    /// Performs move pin asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task MovePinAsync(Guid draggedModeId, Guid targetModeId)
    {
        var source = _orderedPins.FindIndex(pin => pin.ModeId == draggedModeId);
        var target = _orderedPins.FindIndex(pin => pin.ModeId == targetModeId);
        if (source < 0 || target < 0 || source == target) return;
        var item = _orderedPins[source];
        _orderedPins.RemoveAt(source);
        _orderedPins.Insert(target, item);
        await PersistPinOrderAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(RebuildRail);
    }

    /// <summary>
    /// Performs persist pin order asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task PersistPinOrderAsync()
    {
        if (_pins is null) return;
        for (var index = 0; index < _orderedPins.Count; index++)
        {
            var updated = _orderedPins[index] with { SortOrder = index };
            _orderedPins[index] = updated;
            await _pins.UpsertPinAsync(updated, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs the built in step owned by this component.
    /// </summary>
    private IReadOnlyList<ModeDefinition> BuiltIn(params string[] keys) => keys
        .Select(key => _modes.FirstOrDefault(mode => mode.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
        .Where(mode => mode is not null)
        .Cast<ModeDefinition>()
        .ToArray();

    /// <summary>
    /// Handles the data context changed event raised by the UI or runtime.
    /// </summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_shellNotifications is not null) _shellNotifications.PropertyChanged -= OnShellPropertyChanged;
        _shell = DataContext as MainView;
        _shellNotifications = _shell;
        if (_shellNotifications is not null) _shellNotifications.PropertyChanged += OnShellPropertyChanged;
        UpdateActiveState();
    }

    /// <summary>
    /// Handles the shell property changed event raised by the UI or runtime.
    /// </summary>
    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_modes.Count == 0 && !_modeRefreshTimer.IsEnabled) _modeRefreshTimer.Start();
        if (e.PropertyName is nameof(MainView.CurrentPage)
            or nameof(MainView.CurrentChat)
            or nameof(MainView.ProductName))
            UpdateActiveState();
    }

    /// <summary>
    /// Performs the update active state step owned by this component.
    /// </summary>
    private void UpdateActiveState()
    {
        if (_shell is null) return;
        foreach (var button in Descendants(this).OfType<Button>())
        {
            var active = button.Name switch
            {
                "ExperienceHomeButton" => _shell.CurrentSurface == HavenSurface.Home,
                "ExperienceChatButton" => _shell.CurrentSurface is HavenSurface.Chat or HavenSurface.Study or HavenSurface.Tasks,
                "ExperienceStudioButton" => _shell.CurrentSurface == HavenSurface.Studio,
                "ExperiencePlanButton" => _shell.CurrentSurface is HavenSurface.Plan or HavenSurface.Automations,
                "ExperienceBrowseButton" => _shell.CurrentSurface == HavenSurface.Browse,
                _ when button.Name?.StartsWith("PinnedMode_", StringComparison.Ordinal) == true => IsPinnedModeActive(button.Name[11..]),
                _ => false
            };
            if (button.Name?.StartsWith("Experience", StringComparison.Ordinal) == true
                || button.Name?.StartsWith("PinnedMode_", StringComparison.Ordinal) == true)
                button.Background = active
                    ? ResourceBrush("HavenAccentSoftBrush", Color.FromArgb(72, 0, 120, 212))
                    : Brushes.Transparent;
        }
    }

    /// <summary>
    /// Reports whether pinned mode active applies to the current state.
    /// </summary>
    private bool IsPinnedModeActive(string compactId)
    {
        if (_shell is null || !Guid.TryParseExact(compactId, "N", out var modeId)) return false;
        var mode = _modes.FirstOrDefault(item => item.Id == modeId);
        if (mode is null || SurfaceForMode(mode.BaseMode) != _shell.CurrentSurface) return false;
        return _shell.CurrentChat.Prompts.Any(prompt =>
            prompt.IsActive && prompt.Name.Equals(ModeProfilePrefix + mode.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Performs the surface for mode step owned by this component.
    /// </summary>
    private static HavenSurface SurfaceForMode(HavenMode mode) => mode switch
    {
        HavenMode.Chat => HavenSurface.Chat,
        HavenMode.Study => HavenSurface.Study,
        HavenMode.Tasks => HavenSurface.Tasks,
        HavenMode.Studio => HavenSurface.Studio,
        _ => HavenSurface.Chat
    };

    /// <summary>
    /// Performs the descendants step owned by this component.
    /// </summary>
    private static IEnumerable<Control> Descendants(Control root)
    {
        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
        else if (root is ContentControl { Content: Control content })
        {
            yield return content;
            foreach (var nested in Descendants(content)) yield return nested;
        }
        else if (root is Decorator { Child: Control child })
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    /// <summary>
    /// Performs the rail button step owned by this component.
    /// </summary>
    private static Button RailButton(string glyph, string tooltip, string name)
    {
        var button = new HavenButton
        {
            Name = name,
            Width = 50,
            Height = 48,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new TextBlock { Text = glyph, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center }
        };
        button.Classes.Add("icon");
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    /// <summary>
    /// Performs the flyout entry step owned by this component.
    /// </summary>
    private static Button FlyoutEntry(string title, string description, string iconKey, Func<Task> action)
    {
        var button = new HavenButton
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 10,
                Children =
                {
                    new TextBlock { Text = ModeGlyph(iconKey), FontSize = 17, VerticalAlignment = VerticalAlignment.Center },
                    WithColumn(new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                            new TextBlock { Text = description, Classes = { "muted" }, FontSize = 10, TextWrapping = TextWrapping.Wrap }
                        }
                    }, 1)
                }
            }
        };
        button.Classes.Add("sidebar");
        button.Click += async (_, _) => await action();
        return button;
    }

    /// <summary>
    /// Performs the menu action step owned by this component.
    /// </summary>
    private static MenuItem MenuAction(string header, Action action)
    {
        var item = new HavenMenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// Performs the divider step owned by this component.
    /// </summary>
    private static Border Divider() => new()
    {
        Height = 1,
        Margin = new Thickness(8, 3),
        Background = ResourceBrush("HavenLineBrush", Color.FromArgb(45, 255, 255, 255))
    };

    /// <summary>
    /// Performs the mode glyph step owned by this component.
    /// </summary>
    private static string ModeGlyph(string? key) => key?.ToLowerInvariant() switch
    {
        "home" => "⌂",
        "chat" => "▰",
        "book" => "▤",
        "rocket" or "tasks" => "✓",
        "code" or "studio" => "⌘",
        "phone" or "call" => "☎",
        "calendar" or "plan" => "▦",
        "globe" or "browse" => "◉",
        "target" or "training" => "◎",
        "automation" => "↻",
        _ => "◇"
    };

    /// <summary>
    /// Reports whether pin transfer applies to the current state.
    /// </summary>
    private static bool IsPinTransfer(IDataTransfer transfer) =>
        transfer.TryGetText()?.StartsWith("haven-mode-pin:", StringComparison.Ordinal) == true;

    /// <summary>
    /// Attempts to read pin transfer and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryReadPinTransfer(IDataTransfer transfer, out Guid modeId)
    {
        modeId = Guid.Empty;
        var text = transfer.TryGetText();
        return text is not null
               && text.StartsWith("haven-mode-pin:", StringComparison.Ordinal)
               && Guid.TryParse(text[15..], out modeId);
    }

    /// <summary>
    /// Performs the resource brush step owned by this component.
    /// </summary>
    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    private static T WithRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
