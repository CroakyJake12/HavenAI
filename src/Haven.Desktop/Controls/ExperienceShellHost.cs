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
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Controls;

/// <summary>
/// Wraps the existing Haven shell without replacing any page or sidebar. The left rail
/// switches between experience families, while the overlay provides a full-window mode
/// library with persisted pins. Home is intentionally outside the configurable pin list.
/// </summary>
public sealed class ExperienceShellHost : Grid, IDisposable
{
    private static readonly HashSet<string> FixedModeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "chat", "teach", "do", "studio", "call", "plan", "browse"
    };

    private readonly Control _existingShell;
    private readonly Border _rail;
    private readonly StackPanel _experienceButtons;
    private readonly StackPanel _pinnedButtons;
    private readonly Button _allModesButton;
    private readonly Grid _overlay;
    private readonly StackPanel _modeCards;
    private readonly TextBox _modeSearch;
    private readonly TextBlock _overlayStatus;
    private readonly IModeRegistry? _modeRegistry;
    private readonly IPinRepository? _pins;
    private MainWindowViewModel? _shell;
    private INotifyPropertyChanged? _shellNotifications;
    private IReadOnlyList<ModeDefinition> _modes = [];
    private List<ModePin> _orderedPins = [];
    private bool _isReorderMode;
    private bool _disposed;

    public ExperienceShellHost(Control existingShell)
    {
        _existingShell = existingShell ?? throw new ArgumentNullException(nameof(existingShell));
        ColumnDefinitions = new ColumnDefinitions("76,*");
        Background = Brushes.Transparent;

        _experienceButtons = new StackPanel { Spacing = 5 };
        _pinnedButtons = new StackPanel { Spacing = 5 };
        _allModesButton = RailButton("▦", "All modes");
        _allModesButton.Click += (_, _) => ShowModeLibrary();

        var settings = RailButton("⚙", "Settings");
        settings.Click += (_, _) => _shell?.NavigateSettingsCommand.Execute(null);

        _rail = new Border
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
                    BuildHomeButton(),
                    WithRow(new ScrollViewer
                    {
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        Content = new StackPanel
                        {
                            Spacing = 9,
                            Children =
                            {
                                _experienceButtons,
                                new Border
                                {
                                    Height = 1,
                                    Margin = new Thickness(8, 3),
                                    Background = ResourceBrush("HavenLineBrush", Color.FromArgb(45, 255, 255, 255))
                                },
                                _pinnedButtons,
                                _allModesButton
                            }
                        }
                    }, 1),
                    WithRow(new StackPanel { Spacing = 5, Children = { settings } }, 2)
                }
            }
        };

        _modeSearch = new TextBox { PlaceholderText = "Search modes", MinWidth = 280 };
        _modeSearch.TextChanged += (_, _) => RebuildModeCards();
        _modeCards = new StackPanel { Spacing = 9 };
        _overlayStatus = new TextBlock
        {
            Classes = { "muted" },
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };
        var closeOverlay = new Button { Content = "Close" };
        closeOverlay.Classes.Add("secondary");
        closeOverlay.Click += (_, _) => HideModeLibrary();

        _overlay = new Grid
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(224, 8, 8, 10)),
            Children =
            {
                new Border
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
                                                Text = "Chat, Teach and Do share the Chat experience menu because they use the same conversation workspace. Home always remains fixed at the top of the rail.",
                                                Classes = { "muted" },
                                                FontSize = 11,
                                                TextWrapping = TextWrapping.Wrap
                                            }
                                        }
                                    },
                                    WithColumn(closeOverlay, 1)
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
        Grid.SetColumnSpan(_overlay, 2);

        Children.Add(_rail);
        Grid.SetColumn(_existingShell, 1);
        Children.Add(_existingShell);
        Children.Add(_overlay);

        if (App.Services is not null)
        {
            _modeRegistry = App.Services.GetService<IModeRegistry>();
            _pins = App.Services.GetService<IPinRepository>();
        }

        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += async (_, _) => await RefreshAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DataContextChanged -= OnDataContextChanged;
        if (_shellNotifications is not null) _shellNotifications.PropertyChanged -= OnShellPropertyChanged;
        _shellNotifications = null;
        _shell = null;
        GC.SuppressFinalize(this);
    }

    private Button BuildHomeButton()
    {
        var button = RailButton("⌂", "Home");
        button.Margin = new Thickness(0, 0, 0, 10);
        button.Click += (_, _) => _shell?.NavigateHomeCommand.Execute(null);
        button.Name = "ExperienceHomeButton";
        return button;
    }

    private async Task RefreshAsync()
    {
        if (_disposed || _modeRegistry is null || _pins is null) return;
        try
        {
            var modesTask = _modeRegistry.GetModesAsync(CancellationToken.None);
            var pinsTask = _pins.GetPinsAsync(CancellationToken.None);
            await Task.WhenAll(modesTask, pinsTask).ConfigureAwait(false);
            _modes = modesTask.Result.Where(mode => mode.IsEnabled).ToArray();
            _orderedPins = pinsTask.Result.OrderBy(pin => pin.SortOrder).Take(6).ToList();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RebuildRail();
                RebuildModeCards();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _overlayStatus.Text = "Mode navigation could not load: " + ex.Message);
        }
    }

    private void RebuildRail()
    {
        _experienceButtons.Children.Clear();
        _pinnedButtons.Children.Clear();

        var pinnedModes = _orderedPins
            .Select(pin => _modes.FirstOrDefault(mode => mode.Id == pin.ModeId))
            .Where(mode => mode is not null)
            .Cast<ModeDefinition>()
            .ToArray();

        _experienceButtons.Children.Add(GroupButton(
            "▰",
            "Chat, Teach and Do",
            BuiltIn("chat", "teach", "do")));
        _experienceButtons.Children.Add(GroupButton(
            "⌘",
            "Studio",
            BuiltIn("studio")));
        _experienceButtons.Children.Add(DirectButton("☎", "Call", () => _shell?.NavigateCallCommand.Execute(null)));
        _experienceButtons.Children.Add(PlanButton());
        _experienceButtons.Children.Add(DirectButton("◉", "Browse", () => _shell?.NavigateBrowserCommand.Execute(null)));

        foreach (var mode in pinnedModes.Where(mode => !FixedModeKeys.Contains(mode.Key)))
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

    private Button PlanButton()
    {
        var button = RailButton("▦", "Plan and automations");
        var panel = new StackPanel { Width = 270, Spacing = 4 };
        panel.Children.Add(FlyoutEntry("Plan", "Tasks, calendars and AI proposals", "plan", () => _shell?.NavigatePlanCommand.Execute(null)));
        panel.Children.Add(FlyoutEntry("Automations", "Scheduled and condition-based work", "automation", () => _shell?.NavigateAutomationsCommand.Execute(null)));
        button.Flyout = new Flyout { Placement = PlacementMode.RightEdgeAlignedTop, Content = panel };
        button.Name = "ExperiencePlanButton";
        return button;
    }

    private Button GroupButton(string glyph, string tooltip, IReadOnlyList<ModeDefinition> modes)
    {
        var button = RailButton(glyph, tooltip);
        var panel = new StackPanel { Width = 300, Spacing = 4 };
        foreach (var mode in modes)
            panel.Children.Add(FlyoutEntry(mode.Name, mode.Description, mode.IconKey, () => _ = OpenModeAsync(mode)));
        button.Flyout = new Flyout { Placement = PlacementMode.RightEdgeAlignedTop, Content = panel };
        button.Name = modes.FirstOrDefault()?.Key switch
        {
            "chat" => "ExperienceChatButton",
            "studio" => "ExperienceStudioButton",
            _ => null
        };
        return button;
    }

    private Button DirectButton(string glyph, string tooltip, Action action)
    {
        var button = RailButton(glyph, tooltip);
        button.Click += (_, _) => action();
        button.Name = tooltip switch
        {
            "Call" => "ExperienceCallButton",
            "Browse" => "ExperienceBrowseButton",
            _ => null
        };
        return button;
    }

    private Button PinnedModeButton(ModeDefinition mode)
    {
        var button = RailButton(ModeGlyph(mode), mode.Name + (_isReorderMode ? " · drag to reorder" : string.Empty));
        button.Name = "PinnedMode_" + mode.Id.ToString("N");
        button.Click += (_, _) =>
        {
            if (!_isReorderMode) _ = OpenModeAsync(mode);
        };
        button.ContextMenu = new ContextMenu
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

    private void ShowModeLibrary()
    {
        _modeSearch.Text = string.Empty;
        RebuildModeCards();
        _overlay.IsVisible = true;
        _modeSearch.Focus();
    }

    private void HideModeLibrary() => _overlay.IsVisible = false;

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

        _overlayStatus.Text = $"{visible.Length} mode{(visible.Length == 1 ? string.Empty : "s")} · {_orderedPins.Count}/6 pinned";
        foreach (var mode in visible)
        {
            var fixedMode = FixedModeKeys.Contains(mode.Key);
            var pinned = pinnedIds.Contains(mode.Id);
            var open = new Button { Content = "Open" };
            open.Classes.Add("accent");
            open.Click += async (_, _) =>
            {
                HideModeLibrary();
                await OpenModeAsync(mode);
            };
            var pin = new Button
            {
                Content = fixedMode ? "Fixed on rail" : pinned ? "Unpin" : "Pin",
                IsEnabled = !fixedMode
            };
            pin.Click += async (_, _) =>
            {
                if (pinned) await UnpinAsync(mode.Id);
                else await PinAsync(mode.Id);
                RebuildModeCards();
            };
            _modeCards.Children.Add(new Border
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
                        new Border
                        {
                            Width = 42,
                            Height = 42,
                            CornerRadius = new CornerRadius(13),
                            Background = ResourceBrush("HavenAccentSoftBrush", Color.FromArgb(55, 0, 120, 212)),
                            Child = new TextBlock
                            {
                                Text = ModeGlyph(mode),
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
            });
        }
    }

    private async Task OpenModeAsync(ModeDefinition mode)
    {
        if (_shell is null) return;
        await _shell.OpenModeDefinitionAsync(mode);
        UpdateActiveState();
    }

    private async Task PinAsync(Guid modeId)
    {
        if (_pins is null || _orderedPins.Any(pin => pin.ModeId == modeId)) return;
        if (_orderedPins.Count >= 6)
        {
            _overlayStatus.Text = "You can pin up to six modes. Unpin one before adding another.";
            return;
        }
        var pin = new ModePin(Guid.NewGuid(), modeId, _orderedPins.Count, DateTimeOffset.UtcNow);
        await _pins.UpsertPinAsync(pin, CancellationToken.None).ConfigureAwait(false);
        _orderedPins.Add(pin);
        await Dispatcher.UIThread.InvokeAsync(RebuildRail);
    }

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

    private IReadOnlyList<ModeDefinition> BuiltIn(params string[] keys) => keys
        .Select(key => _modes.FirstOrDefault(mode => mode.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
        .Where(mode => mode is not null)
        .Cast<ModeDefinition>()
        .ToArray();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_shellNotifications is not null) _shellNotifications.PropertyChanged -= OnShellPropertyChanged;
        _shell = DataContext as MainWindowViewModel;
        _shellNotifications = _shell;
        if (_shellNotifications is not null) _shellNotifications.PropertyChanged += OnShellPropertyChanged;
        UpdateActiveState();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage)
            or nameof(MainWindowViewModel.CurrentChat)
            or nameof(MainWindowViewModel.ProductName))
            UpdateActiveState();
    }

    private void UpdateActiveState()
    {
        if (_shell is null) return;
        foreach (var button in Descendants(this).OfType<Button>())
        {
            var active = button.Name switch
            {
                "ExperienceHomeButton" => _shell.CurrentSurface == HavenSurface.Home,
                "ExperienceChatButton" => _shell.CurrentSurface is HavenSurface.Chat or HavenSurface.Teach or HavenSurface.Do,
                "ExperienceStudioButton" => _shell.CurrentSurface == HavenSurface.Studio,
                "ExperienceCallButton" => _shell.CurrentSurface == HavenSurface.Call,
                "ExperiencePlanButton" => _shell.CurrentSurface == HavenSurface.Plan,
                "ExperienceBrowseButton" => _shell.CurrentSurface == HavenSurface.Browse,
                _ when button.Name?.StartsWith("PinnedMode_", StringComparison.Ordinal) == true
                    => _shell.CurrentChat.ActiveModeDefinition?.Id.ToString("N") == button.Name[11..],
                _ => false
            };
            if (button.Name?.StartsWith("Experience", StringComparison.Ordinal) == true
                || button.Name?.StartsWith("PinnedMode_", StringComparison.Ordinal) == true)
                button.Background = active
                    ? ResourceBrush("HavenAccentSoftBrush", Color.FromArgb(72, 0, 120, 212))
                    : Brushes.Transparent;
        }
    }

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

    private static Button RailButton(string glyph, string tooltip)
    {
        var button = new Button
        {
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

    private static Button FlyoutEntry(string title, string description, string iconKey, Action action)
    {
        var button = new Button
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
        button.Click += (_, _) => action();
        return button;
    }

    private static MenuItem MenuAction(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static string ModeGlyph(ModeDefinition mode) => ModeGlyph(mode.IconKey);

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

    private static bool IsPinTransfer(IDataTransfer transfer) =>
        transfer.TryGetText()?.StartsWith("haven-mode-pin:", StringComparison.Ordinal) == true;

    private static bool TryReadPinTransfer(IDataTransfer transfer, out Guid modeId)
    {
        modeId = Guid.Empty;
        var text = transfer.TryGetText();
        return text is not null
               && text.StartsWith("haven-mode-pin:", StringComparison.Ordinal)
               && Guid.TryParse(text[15..], out modeId);
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

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
