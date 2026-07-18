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
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Controls;

/// <summary>
/// One compact prompt-bar control for model selection, effort, generation options and
/// corrective recovery actions. It reuses the live ChatPageViewModel so Generative UI
/// can move the control without copying or bypassing chat behaviour.
/// </summary>
public sealed class ModelConfigurationControl : UserControl, IDisposable
{
    private static readonly Regex ProviderPrefix = new(
        "^(openai|openrouter|anthropic|gemini|openai-compatible):",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex QwenName = new(
        "(?i)\\bqwen\\s*([0-9]+(?:\\.[0-9]+)?)(?:\\s*(?:vl|vision))?\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ParameterSize = new(
        "(?i)(?:^|\\s)[0-9]+(?:\\.[0-9]+)?b(?:\\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Quantisation = new(
        "(?i)\\bq[2-8](?:\\s+[kms]){0,2}(?:\\s+[a-z0-9]+)?\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ModelFluff = new(
        "(?i)\\b(latest|preview|instruct|instruction|chat|base|thinking|reasoning|fp16|fp32)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RepeatedWhitespace = new(
        "\\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Button _button;
    private readonly TextBlock _summary;
    private readonly StackPanel _capabilities;
    private readonly Flyout _mainFlyout;
    private readonly Slider _effortSlider;
    private readonly TextBlock _effortDescription;
    private ChatPageViewModel? _chat;
    private MainWindowViewModel? _shell;
    private INotifyPropertyChanged? _subscribedSource;
    private INotifyCollectionChanged? _subscribedModels;
    private int _effortPercent = 60;
    private bool _updatingEffort;
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
            Minimum = 20,
            Maximum = 100,
            Value = _effortPercent,
            TickFrequency = 20,
            IsSnapToTickEnabled = true,
            LargeChange = 20,
            SmallChange = 20,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
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
                    Text = "Effort snaps to 20% increments. Higher effort gives the model more time to reason before answering.",
                    Classes = { "muted2" },
                    FontSize = 9,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                }
            }
        };
    }

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
            foreach (var model in chat.Models
                         .Where(model => query.Length == 0
                                         || model.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                         || model.Family.Contains(query, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase))
            {
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
                                    new TextBlock { Text = model.Family, Classes = { "muted" }, FontSize = 9 }
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
                    Text = "Full provider and model names are retained here. The prompt bar uses a shorter display name only.",
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

    private void OnDataContextChanged(object? sender, EventArgs e) => AttachContext();
    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => AttachContext();
    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => DetachContext();
    private void OnFlyoutOpened(object? sender, EventArgs e) => RefreshFromContext();

    private void AttachContext()
    {
        DetachContext();
        _shell = DataContext as MainWindowViewModel;
        _chat = DataContext as ChatPageViewModel ?? _shell?.CurrentChat;
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

    private void DetachContext()
    {
        if (_subscribedSource is not null) _subscribedSource.PropertyChanged -= OnSourcePropertyChanged;
        if (_subscribedModels is not null) _subscribedModels.CollectionChanged -= OnModelsChanged;
        _subscribedSource = null;
        _subscribedModels = null;
        _chat = null;
        _shell = null;
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is MainWindowViewModel && e.PropertyName == nameof(MainWindowViewModel.CurrentChat))
        {
            AttachContext();
            return;
        }
        if (e.PropertyName is not (nameof(ChatPageViewModel.SelectedModel) or nameof(ChatPageViewModel.SelectedEffort)))
            return;
        if (!_updatingEffort && e.PropertyName == nameof(ChatPageViewModel.SelectedEffort))
            _effortPercent = PercentageForEffort(_chat?.SelectedEffort ?? EffortLevel.Medium);
        RefreshSummary();
    }

    private void OnModelsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSummary();

    private void SubscribeToModels()
    {
        if (_subscribedModels is not null) _subscribedModels.CollectionChanged -= OnModelsChanged;
        _subscribedModels = _chat?.Models as INotifyCollectionChanged;
        if (_subscribedModels is not null) _subscribedModels.CollectionChanged += OnModelsChanged;
    }

    private ChatPageViewModel? ResolveChat()
    {
        if (_shell is not null && !ReferenceEquals(_chat, _shell.CurrentChat))
        {
            _chat = _shell.CurrentChat;
            SubscribeToModels();
        }
        return _chat;
    }

    private void RefreshFromContext()
    {
        var chat = ResolveChat();
        if (chat is not null && !_updatingEffort)
            _effortPercent = PercentageForEffort(chat.SelectedEffort);
        _effortSlider.Value = _effortPercent;
        _effortDescription.Text = EffortDescription(_effortPercent);
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var chat = ResolveChat();
        _summary.Text = $"{SimplifyModelName(chat?.SelectedModel?.Name)} • {_effortPercent}%";
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

    private void OnEffortValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        var snapped = Math.Clamp((int)Math.Round(e.NewValue / 20d) * 20, 20, 100);
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

    private static EffortLevel EffortForPercentage(int percentage) => percentage switch
    {
        <= 20 => EffortLevel.Low,
        <= 60 => EffortLevel.Medium,
        <= 80 => EffortLevel.High,
        _ => EffortLevel.Max
    };

    private static int PercentageForEffort(EffortLevel effort) => effort switch
    {
        EffortLevel.Low => 20,
        EffortLevel.Medium => 60,
        EffortLevel.High => 80,
        EffortLevel.Max => 100,
        _ => 60
    };

    private static string EffortDescription(int percentage) => percentage switch
    {
        20 => "Fastest responses, least accurate",
        40 or 60 => "Balanced responses",
        80 => "Slow responses, more accurate",
        100 => "Slowest responses, most accurate",
        _ => "Balanced responses"
    };

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
