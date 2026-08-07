/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/GenerativeUiSlot.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns GenerativeUiSlot. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell;
using Haven.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents generative ui slot and keeps its related state and behavior together.
/// </summary>
public sealed class GenerativeUiSlot : StackPanel
{
    /// <summary>
    /// Stores region property locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly StyledProperty<string> RegionProperty =
        AvaloniaProperty.Register<GenerativeUiSlot, string>(nameof(Region), string.Empty);

    /// <summary>
    /// Stores runtime locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private GenerativeUiThemeRuntime? _runtime;
    /// <summary>
    /// Stores subscribed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Handles the property changed event raised by the UI or runtime.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RegionProperty) Rebuild();
    }

    /// <summary>
    /// Performs the attach runtime step owned by this component.
    /// </summary>
    private void AttachRuntime()
    {
        if (_subscribed) return;
        _runtime = App.Services?.GetService<GenerativeUiThemeRuntime>();
        if (_runtime is null) return;
        _runtime.ThemeChanged += OnThemeChanged;
        _subscribed = true;
        Rebuild();
    }

    /// <summary>
    /// Performs the detach runtime step owned by this component.
    /// </summary>
    private void DetachRuntime()
    {
        if (!_subscribed || _runtime is null) return;
        _runtime.ThemeChanged -= OnThemeChanged;
        _subscribed = false;
        DisposeChildren();
    }

    /// <summary>
    /// Handles the theme changed event raised by the UI or runtime.
    /// </summary>
    private void OnThemeChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(Rebuild, DispatcherPriority.Background);

    /// <summary>
    /// Performs the rebuild step owned by this component.
    /// </summary>
    private void Rebuild()
    {
        DisposeChildren();
        Children.Clear();
        var runtime = _runtime;
        var activeTheme = runtime?.ActiveTheme;
        if (runtime is null || activeTheme is null || string.IsNullOrWhiteSpace(Region)) return;
        Spacing = 6 * activeTheme.Shape.SpacingScale;
        foreach (var placement in runtime.GetPlacements(Region))
        {
            var control = CreateItem(placement.ItemId, placement.Presentation);
            if (control is not null) Children.Add(control);
        }
        if (Region.Equals(GenerativeUiCatalog.ShellHeaderRight, StringComparison.OrdinalIgnoreCase)
            && runtime.GetPages().Count > 0)
            Children.Add(CreateGeneratedPagesLauncher());
    }

    /// <summary>
    /// Performs the dispose children step owned by this component.
    /// </summary>
    private void DisposeChildren()
    {
        foreach (var disposable in Children.OfType<IDisposable>().ToArray()) disposable.Dispose();
    }

    /// <summary>
    /// Creates item with the invariants required by its callers.
    /// </summary>
    private Control? CreateItem(string itemId, string presentation) => itemId switch
    {
        "chat.temporary" => CreateTemporary(presentation),
        "chat.model" => new ModelConfigurationControl(presentation),
        // Effort is intentionally rendered inside the unified model control. Existing
        // theme manifests may retain this legacy placement without creating a duplicate.
        "chat.effort" => null,
        "chat.context" => CreateContext(presentation),
        _ => null
    };

    /// <summary>
    /// Creates temporary with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Creates context with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Creates generated pages launcher with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Performs the open generated page step owned by this component.
    /// </summary>
    private void OpenGeneratedPage(GeneratedPageDefinition definition)
    {
        if (DataContext is not MainView shell) return;
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

    /// <summary>
    /// Runs execute shell command async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private static Task ExecuteShellCommandAsync(MainView shell, string commandId)
    {
        System.Windows.Input.ICommand? command = commandId.ToLowerInvariant() switch
        {
            "home" => shell.NavigateHomeCommand,
            "new-chat" => shell.NewChatCommand,
            "chat" => shell.NavigateChatCommand,
            "study" => shell.NavigateStudyCommand,
            "tasks" => shell.NavigateTasksCommand,
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

    /// <summary>
    /// Performs the binding for step owned by this component.
    /// </summary>
    private Binding BindingFor(string chatPath, BindingMode mode = BindingMode.OneWay) => new()
    {
        Path = Region.Equals(GenerativeUiCatalog.ShellHeaderRight, StringComparison.OrdinalIgnoreCase)
            ? "CurrentChat." + chatPath
            : chatPath,
        Mode = mode
    };
}
