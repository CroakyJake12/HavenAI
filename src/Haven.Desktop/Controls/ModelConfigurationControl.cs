/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Controls/ModelConfigurationControl.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns ModelConfigurationControl. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Controls;

/// <summary>
/// One compact instruction-bar control for model selection, effort, generation options and
/// corrective recovery actions. It reuses the live ChatPageViewModel so Generative UI
/// can move the control without copying or bypassing chat behaviour.
/// </summary>
public sealed class ModelConfigurationControl : UserControl, IDisposable
{
    /// <summary>
    /// Stores provider prefix locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex ProviderPrefix = new(
        "^(openai|openrouter|anthropic|gemini|openai-compatible):",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    /// <summary>
    /// Stores qwen name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex QwenName = new(
        "(?i)\\bqwen\\s*([0-9]+(?:\\.[0-9]+)?)(?:\\s*(?:vl|vision))?\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// Stores parameter size locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex ParameterSize = new(
        "(?i)(?:^|\\s)[0-9]+(?:\\.[0-9]+)?b(?:\\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// Stores quantisation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex Quantisation = new(
        "(?i)\\bq[2-8](?:\\s+[kms]){0,2}(?:\\s+[a-z0-9]+)?\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// Stores model fluff locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex ModelFluff = new(
        "(?i)\\b(latest|preview|instruct|instruction|chat|base|thinking|reasoning|fp16|fp32)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// Stores repeated whitespace locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex RepeatedWhitespace = new(
        "\\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Stores button locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Button _button;
    /// <summary>
    /// Stores summary locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBlock _summary;
    /// <summary>
    /// Stores capabilities locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _capabilities;
    /// <summary>
    /// Stores main flyout locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Flyout _mainFlyout;
    /// <summary>
    /// Stores effort slider locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Slider _effortSlider;
    /// <summary>
    /// Stores effort description locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBlock _effortDescription;
    /// <summary>Tracks up to three model selections for session-local quick access.</summary>
    private readonly LinkedList<string> _recentModelNames = [];
    /// <summary>
    /// Stores chat locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ChatPage? _chat;
    /// <summary>
    /// Stores shell locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private MainView? _shell;
    /// <summary>
    /// Stores subscribed source locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private INotifyPropertyChanged? _subscribedSource;
    /// <summary>
    /// Stores subscribed models locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private INotifyCollectionChanged? _subscribedModels;
    /// <summary>
    /// Stores effort percent locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _effortPercent = 50;
    /// <summary>
    /// Stores updating effort locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _updatingEffort;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    public ModelConfigurationControl(string presentation = "default")
    {
        _summary = new TextBlock
        {
            Text = "Choose model",
            MaxWidth = presentation == "compact" ? 126 : 205,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold
        };
        _capabilities = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center
        };
        var switchGlyph = new TextBlock
        {
            Text = "⇄",
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(switchGlyph, "Model and effort settings");

        _button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _summary, _capabilities, switchGlyph }
            },
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinWidth = presentation == "compact" ? 170 : 235
        };
        _button.Classes.Add(presentation == "compact" ? "chip" : "ghost");
        ToolTip.SetTip(_button, "Model, effort and advanced configurations");

        _effortDescription = new TextBlock
        {
            Text = EffortDescription(_effortPercent),
            Classes = { "muted" },
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };
        _effortSlider = new Slider
        {
            Minimum = ReasoningScale.MinimumPercentage,
            Maximum = ReasoningScale.MaximumPercentage,
            Value = _effortPercent,
            TickFrequency = ReasoningScale.StepSize,
            IsSnapToTickEnabled = true,
            LargeChange = ReasoningScale.StepSize,
            SmallChange = ReasoningScale.StepSize,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 14,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#FFD84D"), 0),
                    new GradientStop(Color.Parse("#FF8A33"), 0.58),
                    new GradientStop(Color.Parse("#E63B3B"), 1)
                }
            }
        };
        _effortSlider.Classes.Add("effort");
        _effortSlider.ValueChanged += OnEffortValueChanged;

        _mainFlyout = new Flyout
        {
            Placement = PlacementMode.Top,
            Content = BuildMainPanel()
        };
        _mainFlyout.Opened += OnFlyoutOpened;
        _button.Flyout = _mainFlyout;
        Content = _button;

        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
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
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        _mainFlyout.Opened -= OnFlyoutOpened;
        _effortSlider.ValueChanged -= OnEffortValueChanged;
        DetachContext();
        _mainFlyout.Hide();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs the simplify model name step owned by this component.
    /// </summary>
    internal static string SimplifyModelName(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return "Choose model";
        var value = ProviderPrefix.Replace(modelName.Trim(), string.Empty)
            .Replace(':', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ');
        value = QwenName.Replace(value, "Qwen $1");
        value = Regex.Replace(value, "(?i)\\bllama\\s*([0-9]+(?:\\.[0-9]+)?)\\b", "Llama $1");
        value = Regex.Replace(value, "(?i)\\bgemma\\s*([0-9]+(?:\\.[0-9]+)?)\\b", "Gemma $1");
        value = Regex.Replace(value, "(?i)\\bmistral\\s*([0-9]+(?:\\.[0-9]+)?)\\b", "Mistral $1");
        value = ParameterSize.Replace(value, " ");
        value = Quantisation.Replace(value, " ");
        value = ModelFluff.Replace(value, " ");
        value = RepeatedWhitespace.Replace(value, " ").Trim(' ', '.', '-', '_');
        return string.IsNullOrWhiteSpace(value) ? modelName.Trim() : value;
    }

    /// <summary>
    /// Builds main panel from the currently available inputs.
    /// </summary>
    private Control BuildMainPanel()
    {
        var advanced = CreateSubmenuButton(
            "⚙",
            "Advanced configurations",
            BuildAdvancedPanel,
            "Temperature, context and action limits");
        var recovery = CreateSubmenuButton(
            "⚑",
            "Resolve errors",
            BuildRecoveryPanel,
            "Interrupt and correct problematic model behaviour");
        var models = CreateSubmenuButton(
            "🤖",
            "Model",
            BuildModelPanel,
            "Choose from the full configured model catalogue");

        return new StackPanel
        {
            Width = 360,
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "MODEL AND EFFORT", FontWeight = FontWeight.SemiBold, FontSize = 11 },
                advanced,
                recovery,
                models,
                new Separator(),
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "⚡", FontSize = 16, VerticalAlignment = VerticalAlignment.Center },
                        WithColumn(_effortSlider, 1),
                        WithColumn(new TextBlock { Text = "🔥", FontSize = 16, VerticalAlignment = VerticalAlignment.Center }, 2)
                    }
                },
                _effortDescription,
                new TextBlock
                {
                    Text = "Reasoning has four levels: 25%, 50%, 75%, and 100%. The accuracy-preserving large-model runtime activates at 100%.",
                    Classes = { "muted2" },
                    FontSize = 9,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                }
            }
        };
    }

    /// <summary>
    /// Creates submenu button with the invariants required by its callers.
    /// </summary>
    private static Button CreateSubmenuButton(
        string glyph,
        string title,
        Func<Control> contentFactory,
        string tooltip)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 9,
                Children =
                {
                    new TextBlock { Text = glyph, FontSize = 15, VerticalAlignment = VerticalAlignment.Center },
                    WithColumn(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, 1),
                    WithColumn(new TextBlock { Text = "›", FontSize = 18, VerticalAlignment = VerticalAlignment.Center }, 2)
                }
            },
            Flyout = new Flyout { Placement = PlacementMode.Right, Content = contentFactory() }
        };
        button.Classes.Add("sidebar");
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    /// <summary>
    /// Builds advanced panel from the currently available inputs.
    /// </summary>
    private Control BuildAdvancedPanel()
    {
        var preferences = App.Services?.GetService<UserPreferencesService>();
        var options = preferences?.GenerationOptions ?? new Haven.Application.GenerationOptions(0.7, 32768, 20);
        var temperature = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 2,
            Increment = 0.1m,
            Value = (decimal)options.Temperature,
            FormatString = "0.0"
        };
        var context = new NumericUpDown
        {
            Minimum = 2048,
            Maximum = 262144,
            Increment = 1024,
            Value = options.ContextLimit
        };
        var actions = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 100,
            Increment = 1,
            Value = options.ActionLimit
        };
        var status = new TextBlock { Classes = { "muted" }, FontSize = 10, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = "Save advanced configurations" };
        save.Classes.Add("accent");
        save.Click += (_, _) =>
        {
            if (preferences is null)
            {
                status.Text = "Preferences are not available in this process.";
                return;
            }
            preferences.SetAdvancedModelOptions(
                (double)(temperature.Value ?? 0.7m),
                (int)(context.Value ?? 32768),
                (int)(actions.Value ?? 20));
            status.Text = "Advanced model configurations saved.";
        };

        return new StackPanel
        {
            Width = 310,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "ADVANCED CONFIGURATIONS", FontWeight = FontWeight.SemiBold, FontSize = 11 },
                Labelled("Temperature", temperature, "Creativity and variation from 0.0 to 2.0."),
                Labelled("Context limit", context, "Maximum context budget before compaction."),
                Labelled("Tool action limit", actions, "Maximum model tool actions in one turn."),
                save,
                status
            }
        };
    }

    /// <summary>
    /// Builds recovery panel from the currently available inputs.
    /// </summary>
    private Control BuildRecoveryPanel()
    {
        var panel = new StackPanel { Width = 330, Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = "RESOLVE MODEL BEHAVIOUR",
            FontWeight = FontWeight.SemiBold,
            FontSize = 11
        });
        panel.Children.Add(new TextBlock
        {
            Text = "These actions interrupt the current response, add a focused corrective message, and let the selected model reassess.",
            Classes = { "muted" },
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(RecoveryButton(
            "↻",
            "Looping",
            "Stop repeating or restarting the same reasoning. Identify the loop, preserve only confirmed progress, then continue once from the next unresolved step without repeating prior text."));
        panel.Children.Add(RecoveryButton(
            "◉",
            "Hallucinating",
            "Pause and audit your previous claims against the conversation and available tool evidence. Retract anything unsupported, clearly mark uncertainty, and continue using only verifiable information."));
        panel.Children.Add(RecoveryButton(
            "≡",
            "Ignoring instructions",
            "Re-read the latest user request and applicable constraints. State the missed requirement briefly, then continue while following it exactly and without discarding valid completed work."));
        panel.Children.Add(RecoveryButton(
            "◇",
            "Overcomplicating",
            "Reduce the approach to the smallest complete solution that satisfies the request. Remove unnecessary branches and explain only the decisions needed to proceed."));
        return panel;
    }

    /// <summary>
    /// Performs the recovery button step owned by this component.
    /// </summary>
    private Button RecoveryButton(string glyph, string title, string instruction)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 9,
                Children =
                {
                    new TextBlock { Text = glyph, FontSize = 16 },
                    new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }
                }
            }
        };
        button.Classes.Add("sidebar");
        button.Click += async (_, _) => await RecoverBehaviourAsync(instruction);
        return button;
    }

    /// <summary>
    /// Builds model panel from the currently available inputs.
    /// </summary>
    private Control BuildModelPanel()
    {
        var search = new TextBox { PlaceholderText = "Search configured models" };
        var modelButtons = new StackPanel { Spacing = 4 };

        void Rebuild()
        {
            modelButtons.Children.Clear();
            var chat = ResolveChat();
            if (chat is null) return;
            var query = search.Text?.Trim() ?? string.Empty;
            var matching = chat.Models
                .Where(model => query.Length == 0
                                || model.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                || model.Family.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (query.Length == 0 && chat.SelectedModel is { } current) RecordRecentModel(current.Name);
            var recent = query.Length == 0
                ? _recentModelNames
                    .Select(name => matching.FirstOrDefault(model => model.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    .OfType<ModelDescriptor>()
                    .Take(3)
                    .ToArray()
                : [];
            var recommended = query.Length == 0
                ? matching.Except(recent)
                    .OrderByDescending(ModelRecommendationScore)
                    .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToArray()
                : matching.OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var displayed = recent.Concat(recommended).ToArray();
            if (recent.Length > 0)
                modelButtons.Children.Add(new TextBlock { Text = "RECENTLY USED", Classes = { "eyebrow" }, Margin = new Thickness(4) });
            for (var index = 0; index < displayed.Length; index++)
            {
                if (query.Length == 0 && index == recent.Length && recommended.Length > 0)
                    modelButtons.Children.Add(new TextBlock { Text = "RECOMMENDED", Classes = { "eyebrow" }, Margin = new Thickness(4, 9, 4, 4) });
                var model = displayed[index];
                var selected = ReferenceEquals(model, chat.SelectedModel);
                var button = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        ColumnSpacing = 8,
                        Children =
                        {
                            new StackPanel
                            {
                                Spacing = 2,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = model.Name,
                                        FontWeight = FontWeight.SemiBold,
                                        TextWrapping = TextWrapping.Wrap
                                    },
                                    new TextBlock { Text = model.Family + " · " + CapabilityLabel(model), Classes = { "muted" }, FontSize = 9 }
                                }
                            },
                            WithColumn(new TextBlock
                            {
                                Text = selected ? "✓" : string.Empty,
                                VerticalAlignment = VerticalAlignment.Center
                            }, 1)
                        }
                    }
                };
                button.Classes.Add("sidebar");
                button.Click += (_, _) =>
                {
                    chat.SelectedModel = model;
                    RecordRecentModel(model.Name);
                    RefreshSummary();
                    _mainFlyout.Hide();
                };
                modelButtons.Children.Add(button);
            }

            if (modelButtons.Children.Count == 0)
                modelButtons.Children.Add(new TextBlock
                {
                    Text = "No matching configured models.",
                    Classes = { "muted" }
                });
        }

        search.TextChanged += (_, _) => Rebuild();
        Rebuild();
        return new StackPanel
        {
            Width = 440,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "MODEL", FontWeight = FontWeight.SemiBold, FontSize = 11 },
                new TextBlock
                {
                    Text = "Full provider and model names are retained here. The instruction bar uses a shorter display name only.",
                    Classes = { "muted" },
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap
                },
                search,
                new ScrollViewer
                {
                    MaxHeight = 430,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = modelButtons
                }
            }
        };
    }

    private void RecordRecentModel(string name)
    {
        var existing = _recentModelNames.FirstOrDefault(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) _recentModelNames.Remove(existing);
        _recentModelNames.AddFirst(name);
        while (_recentModelNames.Count > 3) _recentModelNames.RemoveLast();
    }

    private static int ModelRecommendationScore(ModelDescriptor model)
    {
        var capabilities = Enum.GetValues<ToolCapability>().Count(model.Supports);
        var sizePenalty = (int)Math.Min(99, model.SizeBytes / 1024L / 1024L / 1024L);
        return capabilities * 100 - sizePenalty;
    }

    private static string CapabilityLabel(ModelDescriptor model)
    {
        var labels = new List<string>();
        if (model.Supports(ToolCapability.Vision)) labels.Add("Vision");
        if (model.Supports(ToolCapability.Tools)) labels.Add("Tools");
        if (model.Supports(ToolCapability.Browser)) labels.Add("Web");
        if (model.Supports(ToolCapability.AudioInput) || model.Supports(ToolCapability.AudioOutput)) labels.Add("Audio");
        return labels.Count == 0 ? "Chat" : string.Join(" · ", labels);
    }

    /// <summary>
    /// Performs recover behaviour asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RecoverBehaviourAsync(string instruction)
    {
        var chat = ResolveChat();
        if (chat is null) return;
        if (chat.IsSending && chat.StopCommand.CanExecute(null))
        {
            chat.StopCommand.Execute(null);
            await Task.Delay(175).ConfigureAwait(false);
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            chat.UsePrompt(instruction);
            if (chat.SendCommand.CanExecute(null)) chat.SendCommand.Execute(null);
        });
        _mainFlyout.Hide();
    }

    /// <summary>
    /// Handles the data context changed event raised by the UI or runtime.
    /// </summary>
    private void OnDataContextChanged(object? sender, EventArgs e) => AttachContext();
    /// <summary>
    /// Handles the attached to visual tree event raised by the UI or runtime.
    /// </summary>
    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => AttachContext();
    /// <summary>
    /// Handles the detached from visual tree event raised by the UI or runtime.
    /// </summary>
    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => DetachContext();
    /// <summary>
    /// Handles the flyout opened event raised by the UI or runtime.
    /// </summary>
    private void OnFlyoutOpened(object? sender, EventArgs e) => RefreshFromContext();

    /// <summary>
    /// Performs the attach context step owned by this component.
    /// </summary>
    private void AttachContext()
    {
        DetachContext();
        _shell = DataContext as MainView;
        _chat = DataContext as ChatPage ?? _shell?.CurrentChat;
        if (_shell is INotifyPropertyChanged shellNotifications)
        {
            _subscribedSource = shellNotifications;
            shellNotifications.PropertyChanged += OnSourcePropertyChanged;
        }
        else if (_chat is INotifyPropertyChanged chatNotifications)
        {
            _subscribedSource = chatNotifications;
            chatNotifications.PropertyChanged += OnSourcePropertyChanged;
        }
        SubscribeToModels();
        RefreshFromContext();
    }

    /// <summary>
    /// Performs the detach context step owned by this component.
    /// </summary>
    private void DetachContext()
    {
        if (_subscribedSource is not null) _subscribedSource.PropertyChanged -= OnSourcePropertyChanged;
        if (_subscribedModels is not null) _subscribedModels.CollectionChanged -= OnModelsChanged;
        _subscribedSource = null;
        _subscribedModels = null;
        _chat = null;
        _shell = null;
    }

    /// <summary>
    /// Handles the source property changed event raised by the UI or runtime.
    /// </summary>
    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is MainView && e.PropertyName == nameof(MainView.CurrentChat))
        {
            AttachContext();
            return;
        }
        if (e.PropertyName is not (nameof(ChatPage.SelectedModel) or nameof(ChatPage.SelectedEffort)))
            return;
        if (e.PropertyName == nameof(ChatPage.SelectedModel) && _chat?.SelectedModel is { } selected)
            RecordRecentModel(selected.Name);
        if (!_updatingEffort && e.PropertyName == nameof(ChatPage.SelectedEffort))
            _effortPercent = PercentageForEffort(_chat?.SelectedEffort ?? EffortLevel.Medium);
        RefreshSummary();
    }

    /// <summary>
    /// Handles the models changed event raised by the UI or runtime.
    /// </summary>
    private void OnModelsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSummary();

    /// <summary>
    /// Performs the subscribe to models step owned by this component.
    /// </summary>
    private void SubscribeToModels()
    {
        if (_subscribedModels is not null) _subscribedModels.CollectionChanged -= OnModelsChanged;
        _subscribedModels = _chat?.Models as INotifyCollectionChanged;
        if (_subscribedModels is not null) _subscribedModels.CollectionChanged += OnModelsChanged;
    }

    /// <summary>
    /// Performs the resolve chat step owned by this component.
    /// </summary>
    private ChatPage? ResolveChat()
    {
        if (_shell is not null && !ReferenceEquals(_chat, _shell.CurrentChat))
        {
            _chat = _shell.CurrentChat;
            SubscribeToModels();
        }
        return _chat;
    }

    /// <summary>
    /// Performs the refresh from context step owned by this component.
    /// </summary>
    private void RefreshFromContext()
    {
        var chat = ResolveChat();
        if (chat is not null && !_updatingEffort)
            _effortPercent = PercentageForEffort(chat.SelectedEffort);
        _effortSlider.Value = _effortPercent;
        _effortDescription.Text = EffortDescription(_effortPercent);
        RefreshSummary();
    }

    /// <summary>
    /// Performs the refresh summary step owned by this component.
    /// </summary>
    private void RefreshSummary()
    {
        var chat = ResolveChat();
        _summary.Text = chat?.SelectedModel is null
            ? "Choose model"
            : $"{SimplifyModelName(chat.SelectedModel.Name)} • {_effortPercent}%";
        _capabilities.Children.Clear();
        if (chat?.SelectedModel is not { } model) return;
        AddCapability(model.Supports(ToolCapability.Vision), "◉", "Vision");
        AddCapability(model.Supports(ToolCapability.Tools), "⌘", "Tools");
        AddCapability(model.Supports(ToolCapability.Browser), "◎", "Browser");
        AddCapability(
            model.Supports(ToolCapability.AudioInput) || model.Supports(ToolCapability.AudioOutput),
            "◖",
            "Audio");
    }

    /// <summary>
    /// Performs the add capability step owned by this component.
    /// </summary>
    private void AddCapability(bool supported, string glyph, string label)
    {
        if (!supported) return;
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(icon, label + " supported");
        _capabilities.Children.Add(icon);
    }

    /// <summary>
    /// Handles the effort value changed event raised by the UI or runtime.
    /// </summary>
    private void OnEffortValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        var snapped = ReasoningScale.SnapPercentage(e.NewValue);
        if (_effortPercent == snapped && Math.Abs(_effortSlider.Value - snapped) < 0.01) return;
        _effortPercent = snapped;
        if (Math.Abs(_effortSlider.Value - snapped) > 0.01) _effortSlider.Value = snapped;
        _effortDescription.Text = EffortDescription(snapped);
        var chat = ResolveChat();
        if (chat is not null)
        {
            _updatingEffort = true;
            try { chat.SelectedEffort = EffortForPercentage(snapped); }
            finally { _updatingEffort = false; }
        }
        RefreshSummary();
    }

    /// <summary>
    /// Performs the effort for percentage step owned by this component.
    /// </summary>
    private static EffortLevel EffortForPercentage(int percentage) =>
        ReasoningScale.FromPercentage(percentage);

    /// <summary>
    /// Performs the percentage for effort step owned by this component.
    /// </summary>
    private static int PercentageForEffort(EffortLevel effort) =>
        ReasoningScale.ToPercentage(effort);

    /// <summary>
    /// Performs the effort description step owned by this component.
    /// </summary>
    private static string EffortDescription(int percentage) => percentage switch
    {
        25 => "Fastest responses with a small bounded context",
        50 => "Balanced speed and reasoning",
        75 => "Deeper reasoning with a larger bounded context",
        100 => "Maximum reasoning with accuracy-preserving large-model runtime",
        _ => "Balanced speed and reasoning"
    };

    /// <summary>
    /// Performs the labelled step owned by this component.
    /// </summary>
    private static StackPanel Labelled(string label, Control control, string description) => new()
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, FontSize = 10 },
            control,
            new TextBlock
            {
                Text = description,
                Classes = { "muted2" },
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap
            }
        }
    };

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
