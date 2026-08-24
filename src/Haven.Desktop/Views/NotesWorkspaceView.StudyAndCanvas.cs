/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/NotesWorkspaceView.StudyAndCanvas.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns NotesWorkspaceView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents notes workspace view and keeps its related state and behavior together.
/// </summary>
public sealed partial class NotesWorkspaceView
{
    /// <summary>
    /// Stores equation library panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _equationLibraryPanel = new() { Spacing = 4 };
    /// <summary>
    /// Stores canvas bookmarks panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _canvasBookmarksPanel = new() { Spacing = 4 };
    /// <summary>
    /// Stores study attempts panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _studyAttemptsPanel = new() { Spacing = 4 };
    /// <summary>
    /// Stores cross references panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _crossReferencesPanel = new() { Spacing = 4 };
    /// <summary>
    /// Stores conflicts panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _conflictsPanel = new() { Spacing = 5 };
    /// <summary>
    /// Stores equation symbol query locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _equationSymbolQuery = string.Empty;

    /// <summary>
    /// Builds study and canvas productivity from the currently available inputs.
    /// </summary>
    private void BuildStudyAndCanvasProductivity(NotesDocument document)
    {
        ApplyDocumentLayoutSurface(document);
        _informationPanel.Children.Add(BuildCrossReferenceTools(document));
        _informationPanel.Children.Add(BuildEquationProductivity(document));
        _informationPanel.Children.Add(BuildCanvasProductivity(document));
        _informationPanel.Children.Add(BuildStudyProductivity(document));
        _informationPanel.Children.Add(BuildCollaborationTools(document));
    }

    /// <summary>
    /// Performs the apply document layout surface step owned by this component.
    /// </summary>
    private void ApplyDocumentLayoutSurface(NotesDocument document)
    {
        if (_page.CurrentPage is null || _page.CurrentSection is null) return;
        _blocksPanel.Width = double.NaN;
        _blocksPanel.MaxWidth = double.PositiveInfinity;
        _blocksPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        _blocksPanel.Background = Brushes.Transparent;
        if (document.LayoutMode == NotesLayoutMode.Continuous) return;

        foreach (var preview in _blocksPanel.GetVisualDescendants().OfType<NotesHtmlPreviewControl>())
            preview.Dispose();
        _blocksPanel.Children.Clear();
        var advanced = LoadAdvancedState(document);
        var surface = document.LayoutMode switch
        {
            NotesLayoutMode.Paginated => NotesDocumentLayoutSurface.BuildPaginated(
                _page,
                advanced,
                BeginEditingAsync,
                EndEditingAsync,
                QueueRefresh,
                ImportMediaAsync),
            NotesLayoutMode.Freeform => NotesDocumentLayoutSurface.BuildFreeform(
                _page,
                infinite: false,
                BeginEditingAsync,
                EndEditingAsync,
                QueueRefresh,
                ImportMediaAsync),
            NotesLayoutMode.InfiniteCanvas => NotesDocumentLayoutSurface.BuildFreeform(
                _page,
                infinite: true,
                BeginEditingAsync,
                EndEditingAsync,
                QueueRefresh,
                ImportMediaAsync),
            _ => throw new ArgumentOutOfRangeException(nameof(document.LayoutMode))
        };
        _blocksPanel.Children.Add(surface);
    }

    /// <summary>
    /// Builds cross reference tools from the currently available inputs.
    /// </summary>
    private Control BuildCrossReferenceTools(NotesDocument document)
    {
        var blocks = document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).ToArray();
        var target = new HavenComboBox { ItemsSource = blocks.Select(BlockLabel).ToArray(), SelectedIndex = blocks.Length > 1 ? 1 : 0 };
        var kind = new HavenComboBox
        {
            ItemsSource = new[] { "Reference", "Equation", "Table", "Figure", "Heading", "Bookmark" },
            SelectedIndex = 0
        };
        var label = new HavenTextInput { PlaceholderText = "Displayed label" };
        var add = ActionButton("Add cross-reference", () =>
        {
            if (_page.SelectedBlock is not { } source
                || target.SelectedIndex < 0
                || target.SelectedIndex >= blocks.Length)
                return Task.CompletedTask;
            var destination = blocks[target.SelectedIndex];
            if (source.Id == destination.Id)
            {
                _status.Text = "Choose a different target block.";
                return Task.CompletedTask;
            }
            MutateAdvancedState(document, state => state.CrossReferences.Add(new NotesCrossReference
            {
                SourceBlockId = source.Id,
                TargetBlockId = destination.Id,
                Kind = kind.SelectedItem as string ?? "Reference",
                Label = string.IsNullOrWhiteSpace(label.Text) ? BlockLabel(destination) : label.Text.Trim()
            }), "Added document cross-reference");
            RefreshAll();
            return Task.CompletedTask;
        }, "Create a durable reference from the selected block to another block");
        RefreshCrossReferences(document);
        return ToolCard("Cross-references", new StackPanel
        {
            Spacing = 6,
            Children = { Labeled("Target", target), Labeled("Kind", kind), label, add, _crossReferencesPanel }
        });
    }

    /// <summary>
    /// Builds equation productivity from the currently available inputs.
    /// </summary>
    private Control BuildEquationProductivity(NotesDocument document)
    {
        var selectedBlock = _page.SelectedBlock;
        var equation = selectedBlock?.Equation;
        var templates = NotesEquationTools.Templates.ToArray();
        var template = new HavenComboBox
        {
            ItemsSource = templates.Select(item => item.Name + " · " + item.Category).ToArray(),
            SelectedIndex = 0,
            IsEnabled = equation is not null
        };
        var symbolSearch = new HavenTextInput { Text = _equationSymbolQuery, PlaceholderText = "Search symbols or LaTeX commands" };
        var symbols = new WrapPanel();
        void RebuildSymbols()
        {
            symbols.Children.Clear();
            foreach (var symbol in NotesEquationTools.SearchSymbols(_equationSymbolQuery).Take(40))
            {
                var value = symbol;
                symbols.Children.Add(ActionButton(value.Glyph + " " + value.Command, async () =>
                {
                    if (selectedBlock?.Equation is not { } selectedEquation) return;
                    await _page.UpdateEquationAsync(
                        selectedBlock,
                        selectedEquation.Source + value.Command,
                        selectedEquation.ViewMode,
                        selectedEquation.AccessibleAlternative);
                    RefreshAll();
                }, value.Name + " · " + value.Category));
            }
        }
        symbolSearch.TextChanged += (_, _) =>
        {
            _equationSymbolQuery = symbolSearch.Text ?? string.Empty;
            RebuildSymbols();
        };
        RebuildSymbols();

        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Insert template", async () =>
        {
            if (selectedBlock?.Equation is not { } selectedEquation
                || template.SelectedIndex < 0
                || template.SelectedIndex >= templates.Length)
                return;
            var selected = templates[template.SelectedIndex];
            var source = string.IsNullOrWhiteSpace(selectedEquation.Source)
                ? selected.Latex
                : selectedEquation.Source + " " + selected.Latex;
            await _page.UpdateEquationAsync(selectedBlock, source, selectedEquation.ViewMode, selectedEquation.AccessibleAlternative);
            RefreshAll();
        }, "Insert a source-preserving equation template"));
        actions.Children.Add(ActionButton("Expand input", async () =>
        {
            if (selectedBlock?.Equation is not { } selectedEquation) return;
            await _page.UpdateEquationAsync(
                selectedBlock,
                NotesEquationTools.ExpandIntelligentInput(selectedEquation.Source),
                selectedEquation.ViewMode,
                selectedEquation.AccessibleAlternative);
            RefreshAll();
        }, "Expand exact intelligent-input shortcuts such as sqrt, sum, int and alpha"));
        actions.Children.Add(ActionButton("Save to library", () =>
        {
            if (selectedBlock?.Equation is not { } selectedEquation) return Task.CompletedTask;
            MutateAdvancedState(document, state => state.EquationLibrary.Add(new NotesEquationLibraryEntry
            {
                Name = string.IsNullOrWhiteSpace(selectedEquation.Label)
                    ? "Equation " + (state.EquationLibrary.Count + 1)
                    : selectedEquation.Label,
                Description = selectedEquation.AccessibleAlternative,
                Latex = selectedEquation.Source,
                Category = "Document"
            }), "Saved equation to document library");
            RefreshAll();
            return Task.CompletedTask;
        }, "Save the selected editable equation in this document's library"));
        actions.Children.Add(ActionButton("Export equation", ExportSelectedEquationAsync, "Export selected equation as LaTeX, MathML, SVG or text"));

        var macroName = new HavenTextInput { PlaceholderText = @"Macro name, e.g. \R", IsEnabled = equation is not null };
        var macroValue = new HavenTextInput { PlaceholderText = @"Replacement, e.g. \mathbb{R}", IsEnabled = equation is not null };
        var addMacro = ActionButton("Add macro", async () =>
        {
            if (selectedBlock?.Equation is not { } selectedEquation) return;
            var name = macroName.Text?.Trim() ?? string.Empty;
            var candidate = new Dictionary<string, string>(selectedEquation.Macros, StringComparer.Ordinal)
            {
                [name] = macroValue.Text ?? string.Empty
            };
            var errors = NotesEquationTools.ValidateMacros(candidate);
            if (errors.Count > 0)
            {
                _status.Text = string.Join(" ", errors);
                return;
            }
            await _page.BeginBlockEditAsync(selectedBlock);
            selectedEquation.Macros = candidate;
            await _page.CommitBlockEditAsync(selectedBlock, "Added equation macro " + name);
            RefreshAll();
        }, "Add a validated equation macro");
        addMacro.IsEnabled = equation is not null;
        RefreshEquationLibrary(document);
        return ToolCard("Equation tools", new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = equation is null
                        ? "Select an equation block to use templates, symbols, macros or exports."
                        : "Source remains authoritative. Visual helpers insert reviewable LaTeX and never silently replace valid source.",
                    Classes = { "muted2" },
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 9
                },
                Labeled("Template", template),
                actions,
                symbolSearch,
                symbols,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    ColumnSpacing = 5,
                    Children = { macroName, WithColumn(macroValue, 1) }
                },
                addMacro,
                _equationLibraryPanel
            }
        });
    }

    /// <summary>
    /// Builds canvas productivity from the currently available inputs.
    /// </summary>
    private Control BuildCanvasProductivity(NotesDocument document)
    {
        var selectedBlock = _page.SelectedBlock;
        var canvas = selectedBlock?.Canvas;
        if (canvas is null)
        {
            return ToolCard("Canvas geometry", new TextBlock
            {
                Text = "Select a canvas block to manage geometry, connectors and spatial bookmarks.",
                Classes = { "muted2" },
                TextWrapping = TextWrapping.Wrap,
                FontSize = 9
            });
        }

        var objects = canvas.Objects.ToArray();
        var names = objects.Select(ObjectLabel).ToArray();
        var selected = new HavenComboBox { ItemsSource = names, SelectedIndex = names.Length > 0 ? 0 : -1 };
        var x = new HavenNumericInput { Minimum = -1_000_000, Maximum = 1_000_000 };
        var y = new HavenNumericInput { Minimum = -1_000_000, Maximum = 1_000_000 };
        var width = new HavenNumericInput { Minimum = 8, Maximum = 1_000_000 };
        var height = new HavenNumericInput { Minimum = 8, Maximum = 1_000_000 };
        var rotation = new HavenNumericInput { Minimum = -360_000, Maximum = 360_000 };
        var locked = new HavenCheckBox { Content = "Locked" };
        var grid = new HavenNumericInput { Minimum = 0, Maximum = 1000, Value = 10 };
        var ready = false;
        void LoadSelection()
        {
            ready = false;
            if (selected.SelectedIndex >= 0 && selected.SelectedIndex < objects.Length)
            {
                var value = objects[selected.SelectedIndex];
                x.Value = (decimal)value.X;
                y.Value = (decimal)value.Y;
                width.Value = (decimal)value.Width;
                height.Value = (decimal)value.Height;
                rotation.Value = (decimal)value.Rotation;
                locked.IsChecked = value.Locked;
            }
            ready = true;
        }
        selected.SelectionChanged += (_, _) => LoadSelection();
        LoadSelection();
        var apply = ActionButton("Apply geometry", async () =>
        {
            if (!ready || selected.SelectedIndex < 0 || selected.SelectedIndex >= objects.Length || selectedBlock is null)
                return;
            var value = objects[selected.SelectedIndex];
            await _page.BeginBlockEditAsync(selectedBlock);
            var wasLocked = value.Locked;
            value.Locked = false;
            NotesCanvasOperations.Move(value, (double)(x.Value ?? 0), (double)(y.Value ?? 0), (double)(grid.Value ?? 0));
            NotesCanvasOperations.Resize(value, (double)(width.Value ?? 160), (double)(height.Value ?? 100), (double)(grid.Value ?? 0));
            NotesCanvasOperations.Rotate(value, (double)(rotation.Value ?? 0));
            value.Locked = locked.IsChecked == true || wasLocked && locked.IsChecked != false;
            await _page.CommitBlockEditAsync(selectedBlock, "Changed canvas object geometry");
            RefreshAll();
        }, "Apply snapped position, size, rotation and lock state");

        var from = new HavenComboBox { ItemsSource = names, SelectedIndex = names.Length > 0 ? 0 : -1 };
        var to = new HavenComboBox { ItemsSource = names, SelectedIndex = names.Length > 1 ? 1 : -1 };
        var connectorLabel = new HavenTextInput { PlaceholderText = "Connector label" };
        var connect = ActionButton("Connect objects", async () =>
        {
            if (selectedBlock is null
                || from.SelectedIndex < 0 || to.SelectedIndex < 0
                || from.SelectedIndex >= objects.Length || to.SelectedIndex >= objects.Length)
                return;
            var first = objects[from.SelectedIndex];
            var second = objects[to.SelectedIndex];
            if (first.Id == second.Id)
            {
                _status.Text = "A canvas object cannot connect to itself.";
                return;
            }
            await _page.BeginBlockEditAsync(selectedBlock);
            canvas.Objects.Add(CreateSafeConnector(first, second, connectorLabel.Text ?? string.Empty, canvas.Objects.Count));
            await _page.CommitBlockEditAsync(selectedBlock, "Connected canvas objects");
            RefreshAll();
        }, "Create a validator-safe editable connector");

        var bookmarkName = new HavenTextInput { PlaceholderText = "Spatial bookmark name" };
        var addBookmark = ActionButton("Add canvas bookmark", () =>
        {
            MutateAdvancedState(document, state => state.CanvasBookmarks.Add(new NotesCanvasBookmarkEntry
            {
                PageId = _page.CurrentPage?.Id ?? Guid.Empty,
                Name = string.IsNullOrWhiteSpace(bookmarkName.Text)
                    ? "Canvas view " + (state.CanvasBookmarks.Count + 1)
                    : bookmarkName.Text.Trim(),
                X = canvas.OffsetX,
                Y = canvas.OffsetY,
                Zoom = canvas.Zoom
            }), "Added canvas spatial bookmark");
            RefreshAll();
            return Task.CompletedTask;
        }, "Save current pan and zoom in the native document");
        var grouping = new WrapPanel();
        grouping.Children.Add(ActionButton("Group unlocked", async () =>
        {
            if (selectedBlock is null) return;
            await _page.BeginBlockEditAsync(selectedBlock);
            NotesCanvasOperations.Group(canvas.Objects.Where(value => !value.Locked));
            await _page.CommitBlockEditAsync(selectedBlock, "Grouped canvas objects");
            RefreshAll();
        }, "Group unlocked canvas objects"));
        grouping.Children.Add(ActionButton("Ungroup all", async () =>
        {
            if (selectedBlock is null) return;
            await _page.BeginBlockEditAsync(selectedBlock);
            NotesCanvasOperations.Ungroup(canvas.Objects);
            await _page.CommitBlockEditAsync(selectedBlock, "Ungrouped canvas objects");
            RefreshAll();
        }, "Remove group IDs from unlocked objects"));
        var bounds = NotesCanvasOperations.Bounds(canvas.Objects);
        RefreshCanvasBookmarks(document, canvas);
        return ToolCard("Canvas geometry", new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = $"{canvas.Objects.Count} objects · {canvas.Strokes.Count} strokes · bounds {bounds.Width:0}×{bounds.Height:0}",
                    Classes = { "muted" },
                    FontSize = 9
                },
                Labeled("Object", selected),
                new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 5, Children = { Labeled("X", x), WithColumn(Labeled("Y", y), 1) } },
                new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 5, Children = { Labeled("Width", width), WithColumn(Labeled("Height", height), 1) } },
                Labeled("Rotation", rotation),
                Labeled("Snap grid", grid),
                locked,
                apply,
                grouping,
                new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 5, Children = { Labeled("From", from), WithColumn(Labeled("To", to), 1) } },
                connectorLabel,
                connect,
                bookmarkName,
                addBookmark,
                _canvasBookmarksPanel
            }
        });
    }

    /// <summary>
    /// Builds study productivity from the currently available inputs.
    /// </summary>
    private Control BuildStudyProductivity(NotesDocument document)
    {
        var selectedBlock = _page.SelectedBlock;
        var card = selectedBlock?.Flashcard;
        var state = LoadAdvancedState(document);
        var dailyTarget = new HavenNumericInput { Minimum = 1, Maximum = 10_000, Value = state.Study.DailyTarget };
        var newLimit = new HavenNumericInput { Minimum = 0, Maximum = 10_000, Value = state.Study.NewCardLimit };
        var sessionLimit = new HavenNumericInput { Minimum = 1, Maximum = 10_000, Value = state.Study.MaximumCardsPerSession };
        var shuffle = new HavenCheckBox { Content = "Shuffle study order", IsChecked = state.Study.Shuffle };
        var mistakes = new HavenCheckBox { Content = "Review mistakes only", IsChecked = state.Study.ReviewMistakesOnly };
        var cram = new HavenCheckBox { Content = "Cram mode", IsChecked = state.Study.CramMode };
        var ready = false;
        void CommitPreferences()
        {
            if (!ready) return;
            MutateAdvancedState(document, value =>
            {
                value.Study.DailyTarget = (int)(dailyTarget.Value ?? 20);
                value.Study.NewCardLimit = (int)(newLimit.Value ?? 10);
                value.Study.MaximumCardsPerSession = (int)(sessionLimit.Value ?? 50);
                value.Study.Shuffle = shuffle.IsChecked == true;
                value.Study.ReviewMistakesOnly = mistakes.IsChecked == true;
                value.Study.CramMode = cram.IsChecked == true;
            }, "Changed study preferences");
        }
        dailyTarget.ValueChanged += (_, _) => CommitPreferences();
        newLimit.ValueChanged += (_, _) => CommitPreferences();
        sessionLimit.ValueChanged += (_, _) => CommitPreferences();
        shuffle.IsCheckedChanged += (_, _) => CommitPreferences();
        mistakes.IsCheckedChanged += (_, _) => CommitPreferences();
        cram.IsCheckedChanged += (_, _) => CommitPreferences();
        ready = true;
        var panel = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                    ColumnSpacing = 5,
                    Children =
                    {
                        Labeled("Daily target", dailyTarget),
                        WithColumn(Labeled("New cards", newLimit), 1),
                        WithColumn(Labeled("Session maximum", sessionLimit), 2)
                    }
                },
                shuffle,
                mistakes,
                cram
            }
        };
        if (card is not null && selectedBlock is not null)
        {
            panel.Children.Insert(0, new TextBlock
            {
                Text = NotesStudyTools.ExplainDueReason(card, DateTimeOffset.Now),
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
                FontSize = 9
            });
            var answer = new HavenTextInput { PlaceholderText = "Answer before revealing", AcceptsReturn = true, MinHeight = 70, TextWrapping = TextWrapping.Wrap };
            var confidence = new HavenSlider { Minimum = 0, Maximum = 1, Value = 0.5, TickFrequency = 0.1 };
            var hints = new HavenNumericInput { Minimum = 0, Maximum = 100, Value = 0 };
            var mark = new HavenComboBox { ItemsSource = new[] { "Correct", "Partly correct", "Incorrect" }, SelectedIndex = 0 };
            panel.Children.Add(new TextBlock { Text = "SELF-MARKING", Classes = { "eyebrow" }, Margin = new Thickness(0, 6, 0, 0) });
            panel.Children.Add(answer);
            panel.Children.Add(Labeled("Confidence", confidence));
            panel.Children.Add(Labeled("Hints used", hints));
            panel.Children.Add(Labeled("Mark after reveal", mark));
            panel.Children.Add(ActionButton("Record attempt", () =>
            {
                MutateAdvancedState(document, value =>
                {
                    var attempt = NotesStudyTools.BeginAttempt(value, card, selectedBlock.Id, confidence.Value);
                    NotesStudyTools.CompleteAttempt(
                        attempt,
                        answer.Text ?? string.Empty,
                        mark.SelectedItem as string ?? "Unmarked",
                        (int)(hints.Value ?? 0),
                        DateTimeOffset.UtcNow);
                }, "Recorded self-marked study attempt");
                RefreshAll();
                return Task.CompletedTask;
            }, "Store answer, confidence, hints, correctness and response time"));
        }
        else
        {
            panel.Children.Insert(0, new TextBlock
            {
                Text = "Select a flashcard block to record and self-mark an answer.",
                Classes = { "muted2" },
                TextWrapping = TextWrapping.Wrap,
                FontSize = 9
            });
        }
        RefreshStudyAttempts(document, card?.CardId);
        panel.Children.Add(_studyAttemptsPanel);
        return ToolCard("Study and self-marking", panel);
    }

    /// <summary>
    /// Builds collaboration tools from the currently available inputs.
    /// </summary>
    private Control BuildCollaborationTools(NotesDocument document)
    {
        RefreshConflicts(document);
        return ToolCard("Collaboration conflicts", new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = "Conflict metadata is local-first and reviewable. No remote value is applied before a resolution is chosen.",
                    Classes = { "muted2" },
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 9
                },
                _conflictsPanel
            }
        });
    }

    /// <summary>
    /// Performs the refresh cross references step owned by this component.
    /// </summary>
    private void RefreshCrossReferences(NotesDocument document)
    {
        _crossReferencesPanel.Children.Clear();
        var state = LoadAdvancedState(document);
        var blockIds = document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Select(block => block.Id).ToHashSet();
        foreach (var reference in state.CrossReferences)
        {
            reference.IsBroken = !blockIds.Contains(reference.SourceBlockId) || !blockIds.Contains(reference.TargetBlockId);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 5 };
            row.Children.Add(new TextBlock
            {
                Text = reference.Label + (reference.IsBroken ? " · broken" : " · " + reference.Kind),
                Classes = { reference.IsBroken ? "muted2" : "muted" },
                TextWrapping = TextWrapping.Wrap,
                FontSize = 9
            });
            row.Children.Add(WithColumn(ActionButton("Open", () =>
            {
                NavigateToBlock(reference.TargetBlockId);
                return Task.CompletedTask;
            }, "Open reference target"), 1));
            row.Children.Add(WithColumn(ActionButton("×", () =>
            {
                MutateAdvancedState(document, value =>
                {
                    var target = value.CrossReferences.FirstOrDefault(item => item.Id == reference.Id);
                    if (target is not null) value.CrossReferences.Remove(target);
                }, "Removed cross-reference");
                RefreshAll();
                return Task.CompletedTask;
            }, "Remove cross-reference", danger: true), 2));
            _crossReferencesPanel.Children.Add(row);
        }
        if (state.CrossReferences.Count == 0)
            _crossReferencesPanel.Children.Add(new TextBlock { Text = "No cross-references.", Classes = { "muted2" }, FontSize = 9 });
    }

    /// <summary>
    /// Performs the refresh equation library step owned by this component.
    /// </summary>
    private void RefreshEquationLibrary(NotesDocument document)
    {
        _equationLibraryPanel.Children.Clear();
        var state = LoadAdvancedState(document);
        foreach (var entry in state.EquationLibrary.OrderByDescending(value => value.IsFavourite).ThenBy(value => value.Name))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 5 };
            row.Children.Add(new TextBlock
            {
                Text = (entry.IsFavourite ? "★ " : string.Empty) + entry.Name + " · " + entry.Latex,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Classes = { "muted" },
                FontSize = 9
            });
            row.Children.Add(WithColumn(ActionButton("Insert", async () =>
            {
                var block = _page.SelectedBlock;
                if (block?.Equation is not { } equation) return;
                await _page.UpdateEquationAsync(block, entry.Latex, equation.ViewMode, equation.AccessibleAlternative);
                RefreshAll();
            }, "Insert this saved equation"), 1));
            row.Children.Add(WithColumn(ActionButton("×", () =>
            {
                MutateAdvancedState(document, value =>
                {
                    var target = value.EquationLibrary.FirstOrDefault(item => item.Id == entry.Id);
                    if (target is not null) value.EquationLibrary.Remove(target);
                }, "Removed equation library entry");
                RefreshAll();
                return Task.CompletedTask;
            }, "Remove equation from library", danger: true), 2));
            _equationLibraryPanel.Children.Add(row);
        }
        if (state.EquationLibrary.Count == 0)
            _equationLibraryPanel.Children.Add(new TextBlock { Text = "No saved equations in this document.", Classes = { "muted2" }, FontSize = 9 });
    }

    /// <summary>
    /// Performs the refresh canvas bookmarks step owned by this component.
    /// </summary>
    private void RefreshCanvasBookmarks(NotesDocument document, NotesCanvasData canvas)
    {
        _canvasBookmarksPanel.Children.Clear();
        var pageId = _page.CurrentPage?.Id;
        var state = LoadAdvancedState(document);
        foreach (var bookmark in state.CanvasBookmarks.Where(value => value.PageId == pageId))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 5 };
            row.Children.Add(new TextBlock { Text = bookmark.Name + $" · {bookmark.Zoom:P0}", Classes = { "muted" }, FontSize = 9 });
            row.Children.Add(WithColumn(ActionButton("Open", async () =>
            {
                var block = _page.SelectedBlock;
                if (block?.Canvas is null) return;
                await _page.BeginBlockEditAsync(block);
                canvas.OffsetX = bookmark.X;
                canvas.OffsetY = bookmark.Y;
                canvas.Zoom = bookmark.Zoom;
                await _page.CommitBlockEditAsync(block, "Opened canvas bookmark");
                RefreshAll();
            }, "Restore this pan and zoom position"), 1));
            row.Children.Add(WithColumn(ActionButton("×", () =>
            {
                MutateAdvancedState(document, value =>
                {
                    var target = value.CanvasBookmarks.FirstOrDefault(item => item.Id == bookmark.Id);
                    if (target is not null) value.CanvasBookmarks.Remove(target);
                }, "Removed canvas bookmark");
                RefreshAll();
                return Task.CompletedTask;
            }, "Remove canvas bookmark", danger: true), 2));
            _canvasBookmarksPanel.Children.Add(row);
        }
        if (_canvasBookmarksPanel.Children.Count == 0)
            _canvasBookmarksPanel.Children.Add(new TextBlock { Text = "No spatial bookmarks for this page.", Classes = { "muted2" }, FontSize = 9 });
    }

    /// <summary>
    /// Performs the refresh study attempts step owned by this component.
    /// </summary>
    private void RefreshStudyAttempts(NotesDocument document, Guid? cardId)
    {
        _studyAttemptsPanel.Children.Clear();
        var state = LoadAdvancedState(document);
        var attempts = state.StudyAttempts
            .Where(attempt => cardId is null || attempt.CardId == cardId)
            .OrderByDescending(attempt => attempt.StartedAt)
            .Take(20)
            .ToArray();
        foreach (var attempt in attempts)
        {
            _studyAttemptsPanel.Children.Add(new TextBlock
            {
                Text = $"{attempt.StartedAt.LocalDateTime:g} · {attempt.Correctness} · confidence {attempt.Confidence:P0} · {attempt.ResponseTime.TotalSeconds:0.#}s · {attempt.HintsUsed} hint{(attempt.HintsUsed == 1 ? string.Empty : "s")}\n{attempt.AttemptText}",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
                FontSize = 9
            });
        }
        if (attempts.Length == 0)
            _studyAttemptsPanel.Children.Add(new TextBlock { Text = "No recorded study attempts in this scope.", Classes = { "muted2" }, FontSize = 9 });
    }

    /// <summary>
    /// Performs the refresh conflicts step owned by this component.
    /// </summary>
    private void RefreshConflicts(NotesDocument document)
    {
        _conflictsPanel.Children.Clear();
        foreach (var conflict in document.Collaboration.Conflicts.OrderByDescending(value => value.DetectedAt))
        {
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = conflict.ResolvedAt is null ? "Open conflict" : "Resolved conflict", FontWeight = FontWeight.SemiBold });
            panel.Children.Add(new TextBlock { Text = "Local: " + conflict.LocalValue, TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 9 });
            panel.Children.Add(new TextBlock { Text = "Remote: " + conflict.RemoteValue, TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 9 });
            if (conflict.ResolvedAt is null)
            {
                var actions = new WrapPanel();
                actions.Children.Add(ActionButton("Keep local", () => ResolveConflict(document, conflict, "local"), "Use the local value"));
                actions.Children.Add(ActionButton("Keep remote", () => ResolveConflict(document, conflict, "remote"), "Use the remote value"));
                panel.Children.Add(actions);
            }
            else
            {
                panel.Children.Add(new TextBlock { Text = "Resolution: " + conflict.Resolution, Classes = { "muted2" }, FontSize = 9 });
            }
            _conflictsPanel.Children.Add(Card(panel));
        }
        if (document.Collaboration.Conflicts.Count == 0)
            _conflictsPanel.Children.Add(new TextBlock { Text = "No collaboration conflicts.", Classes = { "muted2" }, FontSize = 9 });
    }

    /// <summary>
    /// Performs the resolve conflict step owned by this component.
    /// </summary>
    private Task ResolveConflict(NotesDocument document, NotesConflict conflict, string resolution)
    {
        var anchor = BeginWholeDocumentEdit();
        if (anchor is null) return Task.CompletedTask;
        var revisionsBefore = document.Revisions.Count;
        NotesCollaborationTools.ResolveConflict(document, conflict, resolution);
        RemoveServiceRevisions(document, revisionsBefore);
        CompleteWholeDocumentEdit(anchor, changed: true, "Resolved collaboration conflict");
        RefreshAll();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs export selected equation asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ExportSelectedEquationAsync()
    {
        if (_page.SelectedBlock?.Equation is not { } equation) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export equation",
            SuggestedFileName = "equation.tex",
            FileTypeChoices =
            [
                new FilePickerFileType("LaTeX source") { Patterns = ["*.tex"] },
                new FilePickerFileType("MathML") { Patterns = ["*.mathml", "*.xml"] },
                new FilePickerFileType("Accessible SVG") { Patterns = ["*.svg"] },
                new FilePickerFileType("Plain text") { Patterns = ["*.txt"] }
            ]
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var content = extension switch
        {
            ".mathml" or ".xml" => NotesEquationTools.ToMathMl(equation),
            ".svg" => NotesEquationTools.ToSvg(equation),
            ".txt" => string.IsNullOrWhiteSpace(equation.AccessibleAlternative) ? equation.RenderedText : equation.AccessibleAlternative,
            _ => equation.Source
        };
        await File.WriteAllTextAsync(path, content);
        _status.Text = "Exported equation content.";
    }

    /// <summary>
    /// Creates safe connector with the invariants required by its callers.
    /// </summary>
    private static NotesCanvasObject CreateSafeConnector(
        NotesCanvasObject from,
        NotesCanvasObject to,
        string label,
        int zIndex)
    {
        var fromX = from.X + from.Width / 2;
        var fromY = from.Y + from.Height / 2;
        var toX = to.X + to.Width / 2;
        var toY = to.Y + to.Height / 2;
        return new NotesCanvasObject
        {
            Kind = NotesCanvasObjectKind.Connector,
            FromObjectId = from.Id,
            ToObjectId = to.Id,
            Text = label,
            X = Math.Min(fromX, toX),
            Y = Math.Min(fromY, toY),
            Width = Math.Max(8, Math.Abs(toX - fromX)),
            Height = Math.Max(8, Math.Abs(toY - fromY)),
            ZIndex = zIndex
        };
    }

    /// <summary>
    /// Performs the block label step owned by this component.
    /// </summary>
    private static string BlockLabel(NotesBlock block)
    {
        var text = block.PlainText;
        if (string.IsNullOrWhiteSpace(text)) text = block.Equation?.Label;
        if (string.IsNullOrWhiteSpace(text)) text = block.Flashcard?.Front;
        if (string.IsNullOrWhiteSpace(text)) text = block.Media?.Caption;
        text = string.IsNullOrWhiteSpace(text) ? block.Kind.ToString() : text.ReplaceLineEndings(" ").Trim();
        return block.Kind + " · " + text[..Math.Min(text.Length, 48)];
    }

    /// <summary>
    /// Performs the object label step owned by this component.
    /// </summary>
    private static string ObjectLabel(NotesCanvasObject value)
    {
        var text = string.IsNullOrWhiteSpace(value.Text) ? value.Kind.ToString() : value.Text.ReplaceLineEndings(" ").Trim();
        return text[..Math.Min(text.Length, 56)];
    }
}
