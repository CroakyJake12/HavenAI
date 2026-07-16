using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Controls;

public sealed class GenerativeUiSlot : StackPanel
{
    public static readonly StyledProperty<string> RegionProperty =
        AvaloniaProperty.Register<GenerativeUiSlot, string>(nameof(Region), string.Empty);

    private GenerativeUiThemeRuntime? _runtime;
    private bool _subscribed;

    public GenerativeUiSlot()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 6;
        VerticalAlignment = VerticalAlignment.Center;
        DataContextChanged += (_, _) => Rebuild();
        AttachedToVisualTree += (_, _) => AttachRuntime();
        DetachedFromVisualTree += (_, _) => DetachRuntime();
    }

    public string Region
    {
        get => GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RegionProperty) Rebuild();
    }

    private void AttachRuntime()
    {
        if (_subscribed) return;
        _runtime = App.Services?.GetService<GenerativeUiThemeRuntime>();
        if (_runtime is null) return;
        _runtime.ThemeChanged += OnThemeChanged;
        _subscribed = true;
        Rebuild();
    }

    private void DetachRuntime()
    {
        if (!_subscribed || _runtime is null) return;
        _runtime.ThemeChanged -= OnThemeChanged;
        _subscribed = false;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        if (_runtime is null || string.IsNullOrWhiteSpace(Region)) return;
        foreach (var placement in _runtime.GetPlacements(Region))
        {
            var control = CreateItem(placement.ItemId, placement.Presentation);
            if (control is not null) Children.Add(control);
        }
        if (Region.Equals(GenerativeUiCatalog.ShellHeaderRight, StringComparison.OrdinalIgnoreCase)
            && _runtime.GetPages().Count > 0)
            Children.Add(CreateGeneratedPagesLauncher());
    }

    private Control? CreateItem(string itemId, string presentation) => itemId switch
    {
        "chat.temporary" => CreateTemporary(presentation),
        "chat.model" => CreateModel(presentation),
        "chat.effort" => CreateEffort(presentation),
        "chat.context" => CreateContext(presentation),
        _ => null
    };

    private Button CreateTemporary(string presentation)
    {
        var button = new Button
        {
            Classes = { presentation == "compact" ? "chip" : "chrome" },
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Bind(ContentControl.ContentProperty, BindingFor("TemporaryLabel"));
        button.Bind(Button.CommandProperty, BindingFor("ToggleTemporaryCommand"));
        ToolTip.SetTip(button, "Toggle temporary chat");
        return button;
    }

    private Button CreateModel(string presentation)
    {
        var text = new TextBlock
        {
            MaxWidth = presentation == "compact" ? 110 : 180,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Bind(TextBlock.TextProperty, BindingFor("SelectedModel.Name"));
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { text, new TextBlock { Text = "⌄", VerticalAlignment = VerticalAlignment.Center } }
        };
        var button = new Button
        {
            Content = panel,
            Classes = { presentation == "compact" ? "chip" : "ghost" },
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Bind(Button.CommandProperty, BindingFor("OpenModelPickerCommand"));
        ToolTip.SetTip(button, "Choose model");
        return button;
    }

    private ComboBox CreateEffort(string presentation)
    {
        var combo = new ComboBox
        {
            MinWidth = presentation == "compact" ? 74 : 88,
            VerticalAlignment = VerticalAlignment.Center
        };
        combo.Bind(ItemsControl.ItemsSourceProperty, BindingFor("EffortLevels"));
        combo.Bind(SelectingItemsControl.SelectedItemProperty, BindingFor("SelectedEffort", BindingMode.TwoWay));
        ToolTip.SetTip(combo, "Reasoning effort");
        return combo;
    }

    private Button CreateContext(string presentation)
    {
        var label = new TextBlock
        {
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var labelBinding = BindingFor("ContextPercent");
        labelBinding.StringFormat = "{}{0}%";
        label.Bind(TextBlock.TextProperty, labelBinding);

        var button = new Button
        {
            Width = presentation == "labelled" ? 94 : 42,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(0),
            Content = label,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, "Context usage");

        var contextLabel = new TextBlock();
        contextLabel.Classes.Add("muted");
        contextLabel.Bind(TextBlock.TextProperty, BindingFor("ContextLabel"));
        var progress = new ProgressBar { Maximum = 100 };
        progress.Bind(RangeBase.ValueProperty, BindingFor("ContextPercent"));
        var compact = new Button { Content = "Compact now" };
        compact.Bind(Button.CommandProperty, BindingFor("CompactContextCommand"));
        button.Flyout = new Flyout
        {
            Content = new StackPanel
            {
                Width = 250,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Context", FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    contextLabel,
                    progress,
                    compact
                }
            }
        };
        return button;
    }

    private Button CreateGeneratedPagesLauncher()
    {
        var stack = new StackPanel { Width = 310, Spacing = 5 };
        stack.Children.Add(new TextBlock { Text = "GENERATED PAGES", FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 11 });
        foreach (var page in _runtime?.GetPages() ?? [])
        {
            var pageButton = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = page.Title, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                        new TextBlock { Text = page.Description, FontSize = 10, Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                    }
                }
            };
            pageButton.Classes.Add("sidebar");
            pageButton.Click += (_, _) => OpenGeneratedPage(page);
            stack.Children.Add(pageButton);
        }
        var launcher = new Button
        {
            Content = "Pages",
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = new Flyout { Content = stack }
        };
        launcher.Classes.Add("status");
        ToolTip.SetTip(launcher, "Open pages created with Theme Studio");
        return launcher;
    }

    private void OpenGeneratedPage(GeneratedPageDefinition definition)
    {
        if (DataContext is not MainWindowViewModel shell) return;
        var key = "generated-page-" + definition.Id;
        var existing = shell.OpenTabs.FirstOrDefault(tab => tab.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            shell.SelectedTab = existing;
            return;
        }
        var pageViewModel = new GeneratedPageViewModel(definition, commandId => ExecuteShellCommandAsync(shell, commandId));
        var view = new GeneratedPageView { DataContext = pageViewModel };
        var tab = new WorkspaceTabViewModel(key, definition.Title, view, true, HavenSurface.Home);
        shell.OpenTabs.Add(tab);
        shell.SelectedTab = tab;
    }

    private static Task ExecuteShellCommandAsync(MainWindowViewModel shell, string commandId)
    {
        System.Windows.Input.ICommand? command = commandId.ToLowerInvariant() switch
        {
            "home" => shell.NavigateHomeCommand,
            "new-chat" => shell.NewChatCommand,
            "chat" => shell.NavigateChatCommand,
            "teach" => shell.NavigateTeachCommand,
            "call" => shell.NavigateCallCommand,
            "do" => shell.NavigateDoCommand,
            "studio" => shell.NavigateStudioCommand,
            "browse" => shell.NavigateBrowserCommand,
            "plan" => shell.NavigatePlanCommand,
            "automations" => shell.NavigateAutomationsCommand,
            "settings" => shell.NavigateSettingsCommand,
            _ => null
        };
        if (command?.CanExecute(null) == true) command.Execute(null);
        return Task.CompletedTask;
    }

    private Binding BindingFor(string chatPath, BindingMode mode = BindingMode.OneWay) => new()
    {
        Path = Region.Equals(GenerativeUiCatalog.ShellHeaderRight, StringComparison.OrdinalIgnoreCase)
            ? "CurrentChat." + chatPath
            : chatPath,
        Mode = mode
    };
}
