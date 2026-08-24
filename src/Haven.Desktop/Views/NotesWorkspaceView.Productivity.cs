/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/NotesWorkspaceView.Productivity.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns NotesWorkspaceView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents notes workspace view and keeps its related state and behavior together.
/// </summary>
public sealed partial class NotesWorkspaceView
{
    /// <summary>
    /// Stores notes copy json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions NotesCopyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Stores local find results panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _localFindResultsPanel = new() { Spacing = 4 };
    /// <summary>
    /// Stores language issues panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _languageIssuesPanel = new() { Spacing = 5 };
    /// <summary>
    /// Stores bookmarks panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _bookmarksPanel = new() { Spacing = 5 };
    /// <summary>
    /// Stores fields panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _fieldsPanel = new() { Spacing = 5 };
    /// <summary>
    /// Stores styles panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _stylesPanel = new() { Spacing = 5 };
    /// <summary>
    /// Stores version comparison panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _versionComparisonPanel = new() { Spacing = 3 };
    /// <summary>
    /// Stores local find text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _localFindText = string.Empty;
    /// <summary>
    /// Stores local replace text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _localReplaceText = string.Empty;
    /// <summary>
    /// Stores local find regex locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _localFindRegex;
    /// <summary>
    /// Stores local find match case locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _localFindMatchCase;
    /// <summary>
    /// Stores local find whole word locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _localFindWholeWord;
    /// <summary>
    /// Stores local find scope locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _localFindScope = "Document";
    /// <summary>
    /// Stores local find matches locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private IReadOnlyList<NotesFindMatch> _localFindMatches = [];
    /// <summary>
    /// Stores language issues locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private IReadOnlyList<NotesLanguageIssue> _languageIssues = [];
    /// <summary>
    /// Stores version comparison locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private NotesVersionComparison? _versionComparison;

    /// <summary>
    /// Builds productivity inspector from the currently available inputs.
    /// </summary>
    private void BuildProductivityInspector()
    {
        if (_page.Document is not { } document) return;
        _informationPanel.Children.Add(SectionHeading("DOCUMENT PRODUCTIVITY"));
        _informationPanel.Children.Add(BuildTemplateAndFileTools(document));
        _informationPanel.Children.Add(BuildLocalFindReplace(document));
        _informationPanel.Children.Add(BuildLanguageTools(document));
        _informationPanel.Children.Add(BuildHeaderFooterTools(document));
        _informationPanel.Children.Add(BuildFieldTools(document));
        _informationPanel.Children.Add(BuildBookmarkTools(document));
        _informationPanel.Children.Add(BuildStyleTools(document));
        _informationPanel.Children.Add(BuildExtendedLayoutTools(document));
        _informationPanel.Children.Add(BuildPrivacyTools(document));
        _informationPanel.Children.Add(BuildVersionComparisonTools());
        BuildStudyAndCanvasProductivity(document);
    }

    /// <summary>
    /// Builds template and file tools from the currently available inputs.
    /// </summary>
    private Control BuildTemplateAndFileTools(NotesDocument document)
    {
        var templates = NotesTemplateCatalog.Templates.ToArray();
        var template = new HavenComboBox
        {
            ItemsSource = templates.Select(item => item.Name).ToArray(),
            SelectedIndex = 0
        };
        var state = LoadAdvancedState(document);
        var pin = new HavenCheckBox { Content = "Pin this document", IsChecked = state.View.IsPinned };
        pin.IsCheckedChanged += (_, _) => MutateAdvancedState(
            document,
            value => value.View.IsPinned = pin.IsChecked == true,
            pin.IsChecked == true ? "Pinned document" : "Unpinned document");

        var buttons = new WrapPanel();
        buttons.Children.Add(ActionButton("Create from template", async () =>
        {
            var selected = template.SelectedIndex >= 0 && template.SelectedIndex < templates.Length
                ? templates[template.SelectedIndex]
                : templates[0];
            await ImportManagedDocumentAsync(NotesTemplateCatalog.Create(selected.Id), "Created from template");
        }, "Create a new managed Notes document from the selected native template"));
        buttons.Children.Add(ActionButton("Duplicate", async () =>
        {
            var clone = CloneForDuplicate(document);
            await ImportManagedDocumentAsync(clone, "Duplicated Notes document");
        }, "Create an independent managed copy with a new document ID"));
        buttons.Children.Add(ActionButton("Save copy as", SaveNativeCopyAsync, "Write a native editable copy without changing the current document"));

        return ToolCard("Templates and file copies", new StackPanel
        {
            Spacing = 7,
            Children =
            {
                new TextBlock
                {
                    Text = "Templates create full native documents. Duplicate creates a separately versioned managed document; Save copy as leaves the current identity unchanged.",
                    Classes = { "muted2" },
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 9
                },
                Labeled("Template", template),
                buttons,
                pin
            }
        });
    }

    /// <summary>
    /// Builds local find replace from the currently available inputs.
    /// </summary>
    private Control BuildLocalFindReplace(NotesDocument document)
    {
        var find = new HavenTextInput { Text = _localFindText, PlaceholderText = "Find in this document" };
        var replace = new HavenTextInput { Text = _localReplaceText, PlaceholderText = "Replacement" };
        var regex = new HavenCheckBox { Content = "Regular expression", IsChecked = _localFindRegex };
        var matchCase = new HavenCheckBox { Content = "Match case", IsChecked = _localFindMatchCase };
        var wholeWord = new HavenCheckBox { Content = "Whole word", IsChecked = _localFindWholeWord };
        var scope = new HavenComboBox
        {
            ItemsSource = new[] { "Document", "Current section", "Current page", "Selected block" },
            SelectedItem = _localFindScope
        };
        find.TextChanged += (_, _) => _localFindText = find.Text ?? string.Empty;
        replace.TextChanged += (_, _) => _localReplaceText = replace.Text ?? string.Empty;
        regex.IsCheckedChanged += (_, _) => _localFindRegex = regex.IsChecked == true;
        matchCase.IsCheckedChanged += (_, _) => _localFindMatchCase = matchCase.IsChecked == true;
        wholeWord.IsCheckedChanged += (_, _) => _localFindWholeWord = wholeWord.IsChecked == true;
        scope.SelectionChanged += (_, _) => _localFindScope = scope.SelectedItem as string ?? "Document";

        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Find", () =>
        {
            try
            {
                _localFindMatches = NotesDocumentSearch.Find(document, _localFindText, CurrentFindOptions());
                _status.Text = _localFindMatches.Count == 0
                    ? "No matches in the selected Notes scope."
                    : $"Found {_localFindMatches.Count} match{(_localFindMatches.Count == 1 ? string.Empty : "es")}.";
                RefreshLocalFindResults();
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or RegexMatchTimeoutException)
            {
                _status.Text = "Find failed: " + ex.Message;
            }
            return Task.CompletedTask;
        }, "Find literal text or a bounded regular expression in the selected scope"));
        actions.Children.Add(ActionButton("Replace all", () =>
        {
            var anchor = BeginWholeDocumentEdit();
            if (anchor is null) return Task.CompletedTask;
            var revisionsBefore = document.Revisions.Count;
            try
            {
                var result = NotesDocumentSearch.Replace(document, _localFindText, _localReplaceText, CurrentFindOptions());
                RemoveServiceRevisions(document, revisionsBefore);
                CompleteWholeDocumentEdit(anchor, result.Replacements > 0, $"Replaced {result.Replacements} search matches");
                _status.Text = result.Replacements == 0
                    ? "No matching content was changed."
                    : $"Replaced {result.Replacements} match{(result.Replacements == 1 ? string.Empty : "es")} across {result.BlocksChanged} block{(result.BlocksChanged == 1 ? string.Empty : "s")}.";
                _localFindMatches = NotesDocumentSearch.Find(document, _localFindText, CurrentFindOptions());
                RefreshAll();
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or RegexMatchTimeoutException)
            {
                CompleteWholeDocumentEdit(anchor, changed: false, "Search replace cancelled");
                _status.Text = "Replace failed: " + ex.Message;
            }
            return Task.CompletedTask;
        }, "Replace every match in the selected scope as one undoable document edit"));
        actions.Children.Add(ActionButton("Clear results", () =>
        {
            _localFindMatches = [];
            RefreshLocalFindResults();
            return Task.CompletedTask;
        }, "Clear local find results"));

        RefreshLocalFindResults();
        return ToolCard("Find and replace", new StackPanel
        {
            Spacing = 6,
            Children =
            {
                find,
                replace,
                new WrapPanel { Children = { regex, matchCase, wholeWord } },
                Labeled("Scope", scope),
                actions,
                new HavenAdaptiveSurface
                {
                    MaxHeight = 180,
                    Child = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = _localFindResultsPanel
                    }
                }
            }
        });
    }

    /// <summary>
    /// Builds language tools from the currently available inputs.
    /// </summary>
    private Control BuildLanguageTools(NotesDocument document)
    {
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Check language", () =>
        {
            _languageIssues = NotesLanguageChecks.Check(document);
            RefreshLanguageIssues();
            _status.Text = _languageIssues.Count == 0
                ? "No deterministic language issues were found."
                : $"Found {_languageIssues.Count} language issue{(_languageIssues.Count == 1 ? string.Empty : "s")} for review.";
            return Task.CompletedTask;
        }, "Check repeated words, punctuation spacing and sentence capitalisation without sending document content anywhere"));
        actions.Children.Add(ActionButton("Clear", () =>
        {
            _languageIssues = [];
            RefreshLanguageIssues();
            return Task.CompletedTask;
        }, "Clear language review results"));
        RefreshLanguageIssues();
        return ToolCard("Language review", new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = "These checks run locally and report exact ranges. Suggestions never apply until selected.",
                    Classes = { "muted2" },
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 9
                },
                actions,
                new HavenAdaptiveSurface
                {
                    MaxHeight = 220,
                    Child = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = _languageIssuesPanel
                    }
                }
            }
        });
    }

    /// <summary>
    /// Builds header footer tools from the currently available inputs.
    /// </summary>
    private Control BuildHeaderFooterTools(NotesDocument document)
    {
        var section = _page.CurrentSection;
        if (section is null) return ToolCard("Headers and footers", new TextBlock { Text = "Select a section.", Classes = { "muted" } });
        var advanced = LoadAdvancedState(document);
        var variants = advanced.SectionHeaders.TryGetValue(section.Id, out var saved)
            ? saved
            : new NotesSectionHeaderFooterState();
        var header = MetadataBox(section.Header, "Section header");
        var footer = MetadataBox(section.Footer, "Section footer");
        var firstHeader = MetadataBox(variants.FirstPageHeader, "First-page header");
        var firstFooter = MetadataBox(variants.FirstPageFooter, "First-page footer");
        var oddHeader = MetadataBox(variants.OddPageHeader, "Odd-page header");
        var evenHeader = MetadataBox(variants.EvenPageHeader, "Even-page header");
        var restart = new HavenNumericInput
        {
            Minimum = 1,
            Maximum = 1_000_000,
            Value = variants.RestartPageNumberAt,
            PlaceholderText = "Continue numbering"
        };
        var ready = false;
        void Commit()
        {
            if (!ready) return;
            var anchor = BeginWholeDocumentEdit();
            if (anchor is null) return;
            section.Header = header.Text ?? string.Empty;
            section.Footer = footer.Text ?? string.Empty;
            var state = LoadAdvancedState(document);
            var value = state.SectionHeaders.TryGetValue(section.Id, out var existing)
                ? existing
                : new NotesSectionHeaderFooterState();
            value.FirstPageHeader = firstHeader.Text ?? string.Empty;
            value.FirstPageFooter = firstFooter.Text ?? string.Empty;
            value.OddPageHeader = oddHeader.Text ?? string.Empty;
            value.EvenPageHeader = evenHeader.Text ?? string.Empty;
            value.RestartPageNumberAt = restart.Value is { } number ? (int)number : null;
            state.SectionHeaders[section.Id] = value;
            NotesAdvancedStateStore.Save(document, state);
            CompleteWholeDocumentEdit(anchor, changed: true, "Changed section headers and footers");
        }
        foreach (var box in new[] { header, footer, firstHeader, firstFooter, oddHeader, evenHeader })
            box.LostFocus += (_, _) => Commit();
        restart.ValueChanged += (_, _) => Commit();
        ready = true;
        return ToolCard("Headers and footers", new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = section.Title, FontWeight = FontWeight.SemiBold },
                Labeled("Default header", header),
                Labeled("Default footer", footer),
                Labeled("First-page header", firstHeader),
                Labeled("First-page footer", firstFooter),
                Labeled("Odd-page header", oddHeader),
                Labeled("Even-page header", evenHeader),
                Labeled("Restart page numbering at", restart)
            }
        });
    }

    /// <summary>
    /// Builds field tools from the currently available inputs.
    /// </summary>
    private Control BuildFieldTools(NotesDocument document)
    {
        var names = new[]
        {
            "title", "author", "date", "time", "page-count", "word-count", "character-count", "file-name"
        };
        var name = new HavenComboBox { ItemsSource = names, SelectedIndex = 0 };
        var format = new HavenTextInput { PlaceholderText = "Optional .NET date/time format" };
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Add field", () =>
        {
            var anchor = BeginWholeDocumentEdit();
            if (anchor is null) return Task.CompletedTask;
            document.Fields.Add(new NotesField
            {
                Name = name.SelectedItem as string ?? "title",
                Format = format.Text?.Trim() ?? string.Empty,
                IsComputed = true
            });
            NotesFieldEvaluator.Refresh(document, DateTimeOffset.Now);
            CompleteWholeDocumentEdit(anchor, changed: true, "Added computed document field");
            RefreshAll();
            return Task.CompletedTask;
        }, "Add a computed field stored in the native document"));
        actions.Children.Add(ActionButton("Refresh fields", () =>
        {
            var anchor = BeginWholeDocumentEdit();
            if (anchor is null) return Task.CompletedTask;
            NotesFieldEvaluator.Refresh(document, DateTimeOffset.Now);
            CompleteWholeDocumentEdit(anchor, changed: true, "Refreshed computed document fields");
            RefreshAll();
            return Task.CompletedTask;
        }, "Refresh date, title, page and document-statistic fields"));
        RefreshFieldsPanel(document);
        return ToolCard("Document fields", new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Labeled("Field", name),
                format,
                actions,
                _fieldsPanel
            }
        });
    }

    /// <summary>
    /// Builds bookmark tools from the currently available inputs.
    /// </summary>
    private Control BuildBookmarkTools(NotesDocument document)
    {
        var name = new HavenTextInput { PlaceholderText = "Bookmark name" };
        var add = ActionButton("Add bookmark", () =>
        {
            if (_page.SelectedBlock is not { } block) return Task.CompletedTask;
            var anchor = BeginWholeDocumentEdit();
            if (anchor is null) return Task.CompletedTask;
            document.Bookmarks.Add(new NotesBookmark
            {
                Name = string.IsNullOrWhiteSpace(name.Text) ? "Bookmark " + (document.Bookmarks.Count + 1) : name.Text.Trim(),
                BlockId = block.Id,
                Offset = 0
            });
            CompleteWholeDocumentEdit(anchor, changed: true, "Added document bookmark");
            RefreshAll();
            return Task.CompletedTask;
        }, "Bookmark the selected block for keyboard-accessible navigation");
        RefreshBookmarksPanel(document);
        return ToolCard("Bookmarks", new StackPanel { Spacing = 6, Children = { name, add, _bookmarksPanel } });
    }

    /// <summary>
    /// Builds style tools from the currently available inputs.
    /// </summary>
    private Control BuildStyleTools(NotesDocument document)
    {
        var name = new HavenTextInput { PlaceholderText = "New style name" };
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Create from selection", () =>
        {
            if (_page.SelectedBlock is not { } block) return Task.CompletedTask;
            var anchor = BeginWholeDocumentEdit();
            if (anchor is null) return Task.CompletedTask;
            var displayName = string.IsNullOrWhiteSpace(name.Text) ? "Custom style " + (document.Styles.Count + 1) : name.Text.Trim();
            var id = UniqueStyleId(document, displayName);
            var character = block.Runs.FirstOrDefault() is { } run
                ? Clone(run)
                : new NotesTextRun();
            document.Styles.Add(new NotesNamedStyle
            {
                Id = id,
                Name = displayName,
                BasedOn = block.StyleId,
                Character = character,
                Paragraph = Clone(block.Paragraph)
            });
            block.StyleId = id;
            CompleteWholeDocumentEdit(anchor, changed: true, "Created custom style " + displayName);
            RefreshAll();
            return Task.CompletedTask;
        }, "Create a reusable native style from the selected block"));
        actions.Children.Add(ActionButton("Export styles", () => ExportStyleSetAsync(document), "Export the current style set as reviewable JSON"));
        actions.Children.Add(ActionButton("Import styles", () => ImportStyleSetAsync(document), "Import a validated style set"));
        RefreshStylesPanel(document);
        return ToolCard("Styles", new StackPanel { Spacing = 6, Children = { name, actions, _stylesPanel } });
    }

    /// <summary>
    /// Builds extended layout tools from the currently available inputs.
    /// </summary>
    private Control BuildExtendedLayoutTools(NotesDocument document)
    {
        var state = LoadAdvancedState(document);
        var layout = state.PageLayout;
        var columns = new HavenNumericInput { Minimum = 1, Maximum = 12, Value = layout.Columns };
        var gutter = new HavenNumericInput { Minimum = 0, Maximum = 1000, Value = (decimal)layout.GutterPoints };
        var spacing = new HavenNumericInput { Minimum = 0, Maximum = 1000, Value = (decimal)layout.ColumnSpacingPoints };
        var watermark = new HavenTextInput { Text = layout.Watermark, PlaceholderText = "Optional watermark" };
        var pageNumberFormat = new HavenComboBox
        {
            ItemsSource = new[] { "1, 2, 3", "i, ii, iii", "I, II, III", "a, b, c", "A, B, C" },
            SelectedItem = layout.PageNumberFormat
        };
        var mirror = new HavenCheckBox { Content = "Mirror margins", IsChecked = layout.MirrorMargins };
        var lineNumbers = new HavenCheckBox { Content = "Line numbering", IsChecked = layout.LineNumbering };
        var hyphenation = new HavenCheckBox { Content = "Automatic hyphenation", IsChecked = layout.Hyphenation };
        var firstPage = new HavenCheckBox { Content = "Different first page", IsChecked = layout.DifferentFirstPage };
        var oddEven = new HavenCheckBox { Content = "Different odd and even pages", IsChecked = layout.DifferentOddEvenPages };
        var ready = false;
        void Commit()
        {
            if (!ready) return;
            MutateAdvancedState(document, value =>
            {
                value.PageLayout.Columns = (int)(columns.Value ?? 1);
                value.PageLayout.GutterPoints = (double)(gutter.Value ?? 0);
                value.PageLayout.ColumnSpacingPoints = (double)(spacing.Value ?? 18);
                value.PageLayout.Watermark = watermark.Text ?? string.Empty;
                value.PageLayout.PageNumberFormat = pageNumberFormat.SelectedItem as string ?? "1, 2, 3";
                value.PageLayout.MirrorMargins = mirror.IsChecked == true;
                value.PageLayout.LineNumbering = lineNumbers.IsChecked == true;
                value.PageLayout.Hyphenation = hyphenation.IsChecked == true;
                value.PageLayout.DifferentFirstPage = firstPage.IsChecked == true;
                value.PageLayout.DifferentOddEvenPages = oddEven.IsChecked == true;
            }, "Changed extended page layout");
        }
        columns.ValueChanged += (_, _) => Commit();
        gutter.ValueChanged += (_, _) => Commit();
        spacing.ValueChanged += (_, _) => Commit();
        watermark.LostFocus += (_, _) => Commit();
        pageNumberFormat.SelectionChanged += (_, _) => Commit();
        mirror.IsCheckedChanged += (_, _) => Commit();
        lineNumbers.IsCheckedChanged += (_, _) => Commit();
        hyphenation.IsCheckedChanged += (_, _) => Commit();
        firstPage.IsCheckedChanged += (_, _) => Commit();
        oddEven.IsCheckedChanged += (_, _) => Commit();
        ready = true;
        return ToolCard("Extended page layout", new StackPanel
        {
            Spacing = 5,
            Children =
            {
                Labeled("Columns", columns),
                Labeled("Column spacing (pt)", spacing),
                Labeled("Gutter (pt)", gutter),
                Labeled("Watermark", watermark),
                Labeled("Page-number format", pageNumberFormat),
                mirror,
                lineNumbers,
                hyphenation,
                firstPage,
                oddEven
            }
        });
    }

    /// <summary>
    /// Builds privacy tools from the currently available inputs.
    /// </summary>
    private Control BuildPrivacyTools(NotesDocument document)
    {
        var state = LoadAdvancedState(document);
        var ai = new HavenCheckBox { Content = "Enable Notes AI", IsChecked = state.Privacy.AiEnabled };
        var external = new HavenCheckBox { Content = "Allow explicitly configured external providers", IsChecked = state.Privacy.AllowExternalProviders };
        var context = new HavenCheckBox { Content = "Allow full-document AI context", IsChecked = state.Privacy.AllowDocumentContext };
        var workspace = new HavenCheckBox { Content = "Allow selected workspace context", IsChecked = state.Privacy.AllowWorkspaceContext };
        var web = new HavenCheckBox { Content = "Allow user-approved web research", IsChecked = state.Privacy.AllowWebResearch };
        var ready = false;
        void Commit()
        {
            if (!ready) return;
            MutateAdvancedState(document, value =>
            {
                value.Privacy.AiEnabled = ai.IsChecked == true;
                value.Privacy.AllowExternalProviders = external.IsChecked == true;
                value.Privacy.AllowDocumentContext = context.IsChecked == true;
                value.Privacy.AllowWorkspaceContext = workspace.IsChecked == true;
                value.Privacy.AllowWebResearch = web.IsChecked == true;
            }, "Changed Notes privacy permissions");
            _page.AllowDocumentContext = context.IsChecked == true;
        }
        ai.IsCheckedChanged += (_, _) => Commit();
        external.IsCheckedChanged += (_, _) => Commit();
        context.IsCheckedChanged += (_, _) => Commit();
        workspace.IsCheckedChanged += (_, _) => Commit();
        web.IsCheckedChanged += (_, _) => Commit();
        ready = true;
        return ToolCard("Privacy and AI permissions", new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock
                {
                    Text = "Local Ollama remains available without external-provider permission. Document, workspace and web context are separate explicit grants.",
                    Classes = { "muted2" },
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 9
                },
                ai,
                external,
                context,
                workspace,
                web
            }
        });
    }

    /// <summary>
    /// Builds version comparison tools from the currently available inputs.
    /// </summary>
    private Control BuildVersionComparisonTools()
    {
        var compare = ActionButton("Compare selected version", async () =>
        {
            if (_page.Document is not { } current || _page.SelectedVersion is not { } selected)
            {
                _status.Text = "Select a saved version first.";
                return;
            }
            var repository = App.Services?.GetService<INotesRepository>();
            if (repository is null)
            {
                _status.Text = "The production Notes repository is unavailable for comparison.";
                return;
            }
            var previous = await repository.LoadVersionAsync(current.Id, selected.VersionId, CancellationToken.None);
            if (previous is null)
            {
                _status.Text = "The selected version could not be loaded.";
                return;
            }
            _versionComparison = NotesVersionComparer.Compare(current, previous);
            RefreshVersionComparison();
        }, "Show a line-level current-versus-selected-version comparison without restoring it");
        RefreshVersionComparison();
        return ToolCard("Version comparison", new StackPanel
        {
            Spacing = 6,
            Children =
            {
                compare,
                new HavenAdaptiveSurface
                {
                    MaxHeight = 260,
                    Child = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = _versionComparisonPanel
                    }
                }
            }
        });
    }

    /// <summary>
    /// Performs the refresh local find results step owned by this component.
    /// </summary>
    private void RefreshLocalFindResults()
    {
        _localFindResultsPanel.Children.Clear();
        foreach (var match in _localFindMatches.Take(200))
        {
            var button = new HavenButton
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock { Text = match.BlockKind + $" · {match.Start}", FontWeight = FontWeight.SemiBold, FontSize = 9 },
                        new TextBlock { Text = match.Context, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 9 }
                    }
                }
            };
            button.Classes.Add("sidebar");
            button.Click += (_, _) => NavigateToBlock(match.SectionId, match.PageId, match.BlockId);
            _localFindResultsPanel.Children.Add(button);
        }
        if (_localFindMatches.Count == 0)
            _localFindResultsPanel.Children.Add(new TextBlock { Text = "Local results appear here.", Classes = { "muted2" }, FontSize = 9 });
        else if (_localFindMatches.Count > 200)
            _localFindResultsPanel.Children.Add(new TextBlock { Text = $"Showing 200 of {_localFindMatches.Count} matches.", Classes = { "muted2" }, FontSize = 9 });
    }

    /// <summary>
    /// Performs the refresh language issues step owned by this component.
    /// </summary>
    private void RefreshLanguageIssues()
    {
        _languageIssuesPanel.Children.Clear();
        foreach (var issue in _languageIssues.Take(200))
        {
            var panel = new StackPanel { Spacing = 3 };
            panel.Children.Add(new TextBlock { Text = issue.Kind, FontWeight = FontWeight.SemiBold });
            panel.Children.Add(new TextBlock { Text = issue.Message, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 9 });
            var actions = new WrapPanel();
            actions.Children.Add(ActionButton("Go to", () =>
            {
                NavigateToBlock(issue.BlockId);
                return Task.CompletedTask;
            }, "Navigate to the block containing this issue"));
            for (var index = 0; index < issue.Suggestions.Count; index++)
            {
                var suggestionIndex = index;
                var suggestion = issue.Suggestions[index];
                actions.Children.Add(ActionButton("Apply “" + suggestion + "”", () =>
                {
                    if (_page.Document is not { } document) return Task.CompletedTask;
                    var anchor = BeginWholeDocumentEdit();
                    if (anchor is null) return Task.CompletedTask;
                    var revisionsBefore = document.Revisions.Count;
                    var changed = NotesLanguageSuggestionService.Apply(document, issue, suggestionIndex);
                    RemoveServiceRevisions(document, revisionsBefore);
                    CompleteWholeDocumentEdit(anchor, changed, "Applied language suggestion: " + issue.Kind);
                    if (changed)
                    {
                        _languageIssues = NotesLanguageChecks.Check(document);
                        RefreshAll();
                    }
                    return Task.CompletedTask;
                }, "Apply this exact local suggestion as an undoable edit"));
            }
            panel.Children.Add(actions);
            _languageIssuesPanel.Children.Add(Card(panel));
        }
        if (_languageIssues.Count == 0)
            _languageIssuesPanel.Children.Add(new TextBlock { Text = "Run a local language check to review suggestions.", Classes = { "muted2" }, FontSize = 9 });
    }

    /// <summary>
    /// Performs the refresh fields panel step owned by this component.
    /// </summary>
    private void RefreshFieldsPanel(NotesDocument document)
    {
        _fieldsPanel.Children.Clear();
        foreach (var field in document.Fields)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5 };
            row.Children.Add(new TextBlock
            {
                Text = field.Name + ": " + field.Value,
                TextWrapping = TextWrapping.Wrap,
                Classes = { field.IsComputed ? "muted" : "muted2" },
                FontSize = 9
            });
            row.Children.Add(WithColumn(ActionButton("Remove", () =>
            {
                var anchor = BeginWholeDocumentEdit();
                if (anchor is null) return Task.CompletedTask;
                var changed = document.Fields.Remove(field);
                CompleteWholeDocumentEdit(anchor, changed, "Removed document field " + field.Name);
                RefreshAll();
                return Task.CompletedTask;
            }, "Remove this field", danger: true), 1));
            _fieldsPanel.Children.Add(row);
        }
        if (document.Fields.Count == 0)
            _fieldsPanel.Children.Add(new TextBlock { Text = "No document fields.", Classes = { "muted2" }, FontSize = 9 });
    }

    /// <summary>
    /// Performs the refresh bookmarks panel step owned by this component.
    /// </summary>
    private void RefreshBookmarksPanel(NotesDocument document)
    {
        _bookmarksPanel.Children.Clear();
        foreach (var bookmark in document.Bookmarks)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5 };
            var open = new HavenButton { Content = bookmark.Name, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            open.Classes.Add("sidebar");
            open.Click += (_, _) => NavigateToBlock(bookmark.BlockId);
            row.Children.Add(open);
            row.Children.Add(WithColumn(ActionButton("×", () =>
            {
                var anchor = BeginWholeDocumentEdit();
                if (anchor is null) return Task.CompletedTask;
                var changed = document.Bookmarks.Remove(bookmark);
                CompleteWholeDocumentEdit(anchor, changed, "Removed bookmark " + bookmark.Name);
                RefreshAll();
                return Task.CompletedTask;
            }, "Remove bookmark", danger: true), 1));
            _bookmarksPanel.Children.Add(row);
        }
        if (document.Bookmarks.Count == 0)
            _bookmarksPanel.Children.Add(new TextBlock { Text = "No bookmarks.", Classes = { "muted2" }, FontSize = 9 });
    }

    /// <summary>
    /// Performs the refresh styles panel step owned by this component.
    /// </summary>
    private void RefreshStylesPanel(NotesDocument document)
    {
        _stylesPanel.Children.Clear();
        var builtIn = NotesNamedStyle.CreateDefaults().Select(style => style.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var style in document.Styles)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 5 };
            row.Children.Add(new TextBlock
            {
                Text = style.Name + $" · {style.Character.FontFamily} {style.Character.FontSize:0.#}",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = style.Character.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = style.Character.Italic ? FontStyle.Italic : FontStyle.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(WithColumn(ActionButton("Apply", async () =>
            {
                if (_page.SelectedBlock is not { } block) return;
                await _page.BeginBlockEditAsync(block);
                block.StyleId = style.Id;
                block.Paragraph = Clone(style.Paragraph);
                if (block.Runs.Count == 0) block.Runs.Add(new NotesTextRun { Text = block.PlainText });
                foreach (var run in block.Runs) ApplyCharacterStyle(run, style.Character);
                await _page.CommitBlockEditAsync(block, "Applied style " + style.Name);
                RefreshAll();
            }, "Apply this style to the selected block"), 1));
            var remove = ActionButton("×", () =>
            {
                if (builtIn.Contains(style.Id)) return Task.CompletedTask;
                var anchor = BeginWholeDocumentEdit();
                if (anchor is null) return Task.CompletedTask;
                var changed = document.Styles.Remove(style);
                foreach (var block in document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Where(block => block.StyleId == style.Id))
                    block.StyleId = "normal";
                CompleteWholeDocumentEdit(anchor, changed, "Deleted custom style " + style.Name);
                RefreshAll();
                return Task.CompletedTask;
            }, builtIn.Contains(style.Id) ? "Built-in styles cannot be deleted" : "Delete custom style", danger: !builtIn.Contains(style.Id));
            remove.IsEnabled = !builtIn.Contains(style.Id);
            row.Children.Add(WithColumn(remove, 2));
            _stylesPanel.Children.Add(row);
        }
    }

    /// <summary>
    /// Performs the refresh version comparison step owned by this component.
    /// </summary>
    private void RefreshVersionComparison()
    {
        _versionComparisonPanel.Children.Clear();
        if (_versionComparison is null)
        {
            _versionComparisonPanel.Children.Add(new TextBlock { Text = "Select a version in the Versions tab, then compare it here.", Classes = { "muted2" }, FontSize = 9, TextWrapping = TextWrapping.Wrap });
            return;
        }
        _versionComparisonPanel.Children.Add(new TextBlock
        {
            Text = $"v{_versionComparison.ComparedVersion} → v{_versionComparison.CurrentVersion} · +{_versionComparison.Added} −{_versionComparison.Removed}",
            FontWeight = FontWeight.SemiBold
        });
        foreach (var line in _versionComparison.Lines.Take(500))
        {
            var prefix = line.Kind switch
            {
                NotesDiffKind.Added => "+ ",
                NotesDiffKind.Removed => "− ",
                _ => "  "
            };
            _versionComparisonPanel.Children.Add(new SelectableTextBlock
            {
                Text = prefix + line.Text,
                FontFamily = new FontFamily("Cascadia Mono"),
                FontSize = 9,
                TextWrapping = TextWrapping.NoWrap,
                Opacity = line.Kind == NotesDiffKind.Unchanged ? 0.58 : 1
            });
        }
        if (_versionComparison.Lines.Count > 500)
            _versionComparisonPanel.Children.Add(new TextBlock { Text = $"Showing 500 of {_versionComparison.Lines.Count} diff lines.", Classes = { "muted2" }, FontSize = 9 });
    }

    /// <summary>
    /// Performs the current find options step owned by this component.
    /// </summary>
    private NotesFindOptions CurrentFindOptions() => _localFindScope switch
    {
        "Current section" => new NotesFindOptions(_localFindRegex, _localFindMatchCase, _localFindWholeWord, SectionId: _page.CurrentSection?.Id),
        "Current page" => new NotesFindOptions(_localFindRegex, _localFindMatchCase, _localFindWholeWord, PageId: _page.CurrentPage?.Id),
        "Selected block" => new NotesFindOptions(_localFindRegex, _localFindMatchCase, _localFindWholeWord, BlockId: _page.SelectedBlock?.Id),
        _ => new NotesFindOptions(_localFindRegex, _localFindMatchCase, _localFindWholeWord)
    };

    /// <summary>
    /// Performs import managed document asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ImportManagedDocumentAsync(NotesDocument document, string reason)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "haven-notes-import-" + Guid.NewGuid().ToString("N") + ".haven-notes.json");
        try
        {
            document.Version = 0;
            document.Recovery = new NotesRecoveryState();
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, NotesCopyJsonOptions, CancellationToken.None);
                await stream.FlushAsync(CancellationToken.None);
                stream.Flush(flushToDisk: true);
            }
            await _page.ImportDocumentAsync(temporary, CancellationToken.None);
            _status.Text = reason + ".";
            RefreshAll();
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Performs the clone for duplicate step owned by this component.
    /// </summary>
    private static NotesDocument CloneForDuplicate(NotesDocument source)
    {
        var json = JsonSerializer.Serialize(source, NotesCopyJsonOptions);
        var clone = JsonSerializer.Deserialize<NotesDocument>(json, NotesCopyJsonOptions)
                    ?? throw new InvalidDataException("The Notes document copy could not be created.");
        var now = DateTimeOffset.UtcNow;
        clone.Id = Guid.NewGuid();
        clone.Title = "Copy of " + source.Title;
        clone.Version = 0;
        clone.CreatedAt = now;
        clone.UpdatedAt = now;
        clone.Recovery = new NotesRecoveryState();
        clone.Collaboration.OwnerId = Environment.UserName;
        clone.Collaboration.SyncRevision = string.Empty;
        clone.Collaboration.RemoteEtag = string.Empty;
        clone.Collaboration.LastSyncedAt = null;
        clone.Collaboration.ConflictState = NotesConflictState.None;
        clone.Collaboration.Conflicts.Clear();
        clone.Revisions.Add(new NotesRevision
        {
            Kind = NotesRevisionKind.Created,
            Summary = "Created as an independent duplicate of " + source.Title,
            Author = Environment.UserName,
            CreatedAt = now
        });
        return clone;
    }

    /// <summary>
    /// Performs save native copy asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SaveNativeCopyAsync()
    {
        if (_page.Document is null) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save editable Haven Notes copy",
            SuggestedFileName = SafeFileName(_page.Document.Title) + " copy.haven-notes.json",
            FileTypeChoices = [new FilePickerFileType("Haven Notes") { Patterns = ["*.haven-notes.json"] }]
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        await _page.ExportDocumentAsync(path, CancellationToken.None);
        RefreshStatusOnly();
    }

    /// <summary>
    /// Performs export style set asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ExportStyleSetAsync(NotesDocument document)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Haven Notes styles",
            SuggestedFileName = SafeFileName(document.Title) + ".haven-styles.json",
            FileTypeChoices = [new FilePickerFileType("Haven Notes style set") { Patterns = ["*.haven-styles.json", "*.json"] }]
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        await File.WriteAllTextAsync(path, NotesStyleSetService.Export(document.Styles));
        _status.Text = "Exported Notes style set.";
    }

    /// <summary>
    /// Performs import style set asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ImportStyleSetAsync(NotesDocument document)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Haven Notes styles",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Haven Notes style set") { Patterns = ["*.haven-styles.json", "*.json"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var imported = NotesStyleSetService.Import(await File.ReadAllTextAsync(path));
            var anchor = BeginWholeDocumentEdit();
            if (anchor is null) return;
            var byId = document.Styles.ToDictionary(style => style.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var style in imported) byId[style.Id] = style;
            document.Styles = byId.Values.OrderBy(style => style.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            CompleteWholeDocumentEdit(anchor, changed: true, "Imported Notes style set");
            RefreshAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _status.Text = "Style import failed: " + ex.Message;
        }
    }

    /// <summary>
    /// Performs the navigate to block step owned by this component.
    /// </summary>
    private void NavigateToBlock(Guid sectionId, Guid pageId, Guid blockId)
    {
        if (_page.Document is not { } document) return;
        var section = document.Sections.FirstOrDefault(value => value.Id == sectionId);
        var page = section?.Pages.FirstOrDefault(value => value.Id == pageId);
        var block = page?.Blocks.FirstOrDefault(value => value.Id == blockId);
        if (section is null || page is null || block is null) return;
        _page.CurrentSection = section;
        _page.CurrentPage = page;
        _page.SelectedBlock = block;
        QueueRefresh();
    }

    /// <summary>
    /// Performs the navigate to block step owned by this component.
    /// </summary>
    private void NavigateToBlock(Guid blockId)
    {
        if (_page.Document is not { } document) return;
        foreach (var section in document.Sections)
        foreach (var page in section.Pages)
        {
            var block = page.Blocks.FirstOrDefault(value => value.Id == blockId);
            if (block is null) continue;
            NavigateToBlock(section.Id, page.Id, block.Id);
            return;
        }
    }

    /// <summary>
    /// Performs the begin whole document edit step owned by this component.
    /// </summary>
    private NotesBlock? BeginWholeDocumentEdit()
    {
        var anchor = _page.SelectedBlock ?? _page.Blocks.FirstOrDefault();
        if (anchor is not null) _ = BeginEditingAsync(anchor);
        return anchor;
    }

    /// <summary>
    /// Performs the complete whole document edit step owned by this component.
    /// </summary>
    private void CompleteWholeDocumentEdit(NotesBlock anchor, bool changed, string summary)
    {
        if (changed) _ = EndEditingAsync(anchor, summary);
        else
        {
            _page.CancelBlockEdit(anchor);
            if (_activeEditBlockId == anchor.Id) _activeEditBlockId = null;
        }
    }

    /// <summary>
    /// Performs the remove service revisions step owned by this component.
    /// </summary>
    private static void RemoveServiceRevisions(NotesDocument document, int countBefore)
    {
        if (document.Revisions.Count > countBefore)
            document.Revisions.RemoveRange(countBefore, document.Revisions.Count - countBefore);
    }

    /// <summary>
    /// Performs the mutate advanced state step owned by this component.
    /// </summary>
    private void MutateAdvancedState(
        NotesDocument document,
        Action<NotesAdvancedDocumentState> mutation,
        string summary)
    {
        var anchor = BeginWholeDocumentEdit();
        if (anchor is null) return;
        try
        {
            var state = LoadAdvancedState(document);
            mutation(state);
            NotesAdvancedStateStore.Save(document, state);
            CompleteWholeDocumentEdit(anchor, changed: true, summary);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
        {
            CompleteWholeDocumentEdit(anchor, changed: false, summary);
            _status.Text = "Notes settings could not be saved: " + ex.Message;
        }
    }

    /// <summary>
    /// Performs the load advanced state step owned by this component.
    /// </summary>
    private static NotesAdvancedDocumentState LoadAdvancedState(NotesDocument document)
    {
        try { return NotesAdvancedStateStore.Load(document); }
        catch (InvalidDataException)
        {
            document.Metadata.Remove(NotesAdvancedStateStore.MetadataKey);
            return new NotesAdvancedDocumentState();
        }
    }

    /// <summary>
    /// Performs the metadata box step owned by this component.
    /// </summary>
    private static TextBox MetadataBox(string value, string watermark) => new()
    {
        Text = value,
        PlaceholderText = watermark,
        AcceptsReturn = true,
        MinHeight = 46,
        TextWrapping = TextWrapping.Wrap
    };

    /// <summary>
    /// Performs the section heading step owned by this component.
    /// </summary>
    private static Control SectionHeading(string text) => new TextBlock
    {
        Text = text,
        Classes = { "eyebrow" },
        Margin = new Thickness(0, 10, 0, 0)
    };

    /// <summary>
    /// Performs the tool card step owned by this component.
    /// </summary>
    private static Control ToolCard(string title, Control content) => Card(new StackPanel
    {
        Spacing = 6,
        Children =
        {
            new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
            content
        }
    });

    /// <summary>
    /// Performs the unique style id step owned by this component.
    /// </summary>
    private static string UniqueStyleId(NotesDocument document, string name)
    {
        var baseId = new string(name.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(baseId)) baseId = "custom-style";
        var id = baseId;
        var suffix = 2;
        while (document.Styles.Any(style => style.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) id = baseId + "-" + suffix++;
        return id;
    }

    private static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, NotesCopyJsonOptions);
        return JsonSerializer.Deserialize<T>(json, NotesCopyJsonOptions)
               ?? throw new InvalidDataException("A Notes formatting value could not be copied.");
    }

    /// <summary>
    /// Performs the apply character style step owned by this component.
    /// </summary>
    private static void ApplyCharacterStyle(NotesTextRun target, NotesTextRun source)
    {
        target.FontFamily = source.FontFamily;
        target.FontSize = source.FontSize;
        target.Bold = source.Bold;
        target.Italic = source.Italic;
        target.Underline = source.Underline;
        target.StrikeThrough = source.StrikeThrough;
        target.Foreground = source.Foreground;
        target.Background = source.Background;
    }
}
