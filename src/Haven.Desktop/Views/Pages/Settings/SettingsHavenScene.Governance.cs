using System.Globalization;
using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Settings;

internal sealed partial class SettingsHavenScene
{
    private static readonly string[] PersonalityLabels = ["Very low", "Low", "Moderate", "High", "Very high"];
    private static readonly string[] PersonalityFields =
    [
        nameof(ModelPersonality.Friendliness),
        nameof(ModelPersonality.MemoryReferences),
        nameof(ModelPersonality.Seriousness),
        nameof(ModelPersonality.Verbosity),
        nameof(ModelPersonality.Initiative),
        nameof(ModelPersonality.ExplanationDepth)
    ];
    private const string PersonalityInheritOption = "(use Haven default)";
    private static readonly string[] PermissionTargetLabels =
        ["Exact model", "Model family", "Provider", "Parameter size below", "All local models", "All cloud models"];
    private static readonly string[] PermissionScopeLabels = ["This device", "Across mesh"];
    private static readonly string[] PermissionCapabilityLabels = ["Edit Files", "Run Commands", "Computer Use", "Browser Automation"];

    public Input FallbackOrderInput { get; private set; } = null!;
    public HavenButton SaveFallbackOrderButton { get; private set; } = null!;

    public Dictionary<string, Select> SharedPersonalitySelects { get; } = [];
    public HavenButton SaveSharedPersonalityButton { get; private set; } = null!;

    public Input OverrideModelKeyInput { get; private set; } = null!;
    public Input OverrideNicknameInput { get; private set; } = null!;
    public Dictionary<string, Select> OverridePersonalitySelects { get; } = [];
    public HavenText PersonalisationSummary { get; private set; } = null!;
    public HavenButton SavePersonalisationEntryButton { get; private set; } = null!;
    public HavenButton RemovePersonalisationEntryButton { get; private set; } = null!;

    public Select PermissionTargetKindSelect { get; private set; } = null!;
    public Input PermissionMatchInput { get; private set; } = null!;
    public Input PermissionMaxParametersInput { get; private set; } = null!;
    public Select PermissionScopeSelect { get; private set; } = null!;
    public Toggle DenyEditFilesToggle { get; private set; } = null!;
    public Toggle DenyRunCommandsToggle { get; private set; } = null!;
    public Toggle DenyComputerUseToggle { get; private set; } = null!;
    public Toggle DenyBrowserAutomationToggle { get; private set; } = null!;
    public Select PermissionRulesSelect { get; private set; } = null!;
    public HavenButton AddPermissionRuleButton { get; private set; } = null!;
    public HavenButton RemovePermissionRuleButton { get; private set; } = null!;

    public Dictionary<ProviderCategory, Select> DefaultProviderSelects { get; } = [];

    public HavenText GovernanceStatus { get; private set; } = null!;

    internal void BuildGovernance(Container section)
    {
        section.Add(BuildFallbackOrderCard());
        section.Add(BuildSharedPersonalityCard());
        section.Add(BuildPersonalisationOverridesCard());
        section.Add(BuildModelPermissionsCard());
        section.Add(BuildDefaultAppsCard());
        GovernanceStatus = Muted("Settings.Models.Governance.Status", string.Empty);
        GovernanceStatus.SetValue(HavenProperties.MinHeight, HavenLength.Px(24));
        section.Add(GovernanceStatus);
    }

    private Container BuildFallbackOrderCard()
    {
        var card = Card("Settings.Models.Fallback");
        card.Add(Heading("Settings.Models.Fallback.Heading", "Priority & fallback order", 18));
        card.Add(Muted("Settings.Models.Fallback.Description",
            "List one model key per line. The first line is your most preferred model and Haven walks down the list when a higher entry is unavailable."));
        FallbackOrderInput = new Input { Name = "Settings.Models.Fallback.Order", Multiline = true };
        FallbackOrderInput.Accessibility.AccessibleName = "Model priority order, one model key per line";
        FallbackOrderInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        FallbackOrderInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(110));
        card.Add(FallbackOrderInput);
        SaveFallbackOrderButton = new HavenButton
        {
            Name = "Settings.Models.Fallback.Save",
            Content = "Save priority order",
            Variant = ButtonVariant.Primary
        };
        SaveFallbackOrderButton.Accessibility.AccessibleName = "Save model priority order";
        card.Add(SaveFallbackOrderButton);
        return card;
    }

    private Container BuildSharedPersonalityCard()
    {
        var card = Card("Settings.Models.Personality");
        card.Add(Heading("Settings.Models.Personality.Heading", "Shared model personality", 18));
        card.Add(Muted("Settings.Models.Personality.Description",
            "Defaults applied to every model's responses. Per-model overrides below replace individual traits."));
        foreach (var field in PersonalityFields)
        {
            var select = NewSelect($"Settings.Models.Personality.Shared.{field}", PersonalityLabels);
            select.SelectedIndex = (int)Level(ModelPersonality.Defaults, field);
            select.Accessibility.AccessibleName = $"Shared personality: {PersonalityTitle(field)}";
            SharedPersonalitySelects[field] = select;
            card.Add(SettingRow(PersonalityTitle(field), PersonalityDescription(field), select));
        }
        SaveSharedPersonalityButton = new HavenButton
        {
            Name = "Settings.Models.Personality.Save",
            Content = "Save shared personality",
            Variant = ButtonVariant.Primary
        };
        SaveSharedPersonalityButton.Accessibility.AccessibleName = "Save shared model personality";
        card.Add(SaveSharedPersonalityButton);
        return card;
    }

    private Container BuildPersonalisationOverridesCard()
    {
        var card = Card("Settings.Models.Personalise");
        card.Add(Heading("Settings.Models.Personalise.Heading", "Per-model overrides", 18));
        card.Add(Muted("Settings.Models.Personalise.Description",
            "Give one model a nickname and its own personality. Anything left at '(use Haven default)' follows the shared defaults above."));
        OverrideModelKeyInput = new Input { Name = "Settings.Models.Personalise.ModelKey", Placeholder = "Model key, e.g. qwen3:8b" };
        OverrideModelKeyInput.Accessibility.AccessibleName = "Model key to personalise";
        OverrideModelKeyInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        OverrideNicknameInput = new Input { Name = "Settings.Models.Personalise.Nickname", Placeholder = "Nickname (optional)" };
        OverrideNicknameInput.Accessibility.AccessibleName = "Nickname for this model";
        OverrideNicknameInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        card.Add(OverrideModelKeyInput);
        card.Add(OverrideNicknameInput);
        var options = new List<string>(PersonalityLabels.Length + 1) { PersonalityInheritOption };
        options.AddRange(PersonalityLabels);
        foreach (var field in PersonalityFields)
        {
            var select = NewSelect($"Settings.Models.Personalise.Override.{field}", options);
            select.SelectedIndex = 0;
            select.Accessibility.AccessibleName = $"Override {PersonalityTitle(field)}";
            OverridePersonalitySelects[field] = select;
            card.Add(SettingRow(PersonalityTitle(field), $"{PersonalityTitle(field)} override for this model.", select));
        }
        PersonalisationSummary = Muted("Settings.Models.Personalise.Summary", "No per-model overrides stored.");
        card.Add(PersonalisationSummary);
        SavePersonalisationEntryButton = new HavenButton
        {
            Name = "Settings.Models.Personalise.Save",
            Content = "Save override",
            Variant = ButtonVariant.Primary
        };
        SavePersonalisationEntryButton.Accessibility.AccessibleName = "Save per-model override";
        RemovePersonalisationEntryButton = new HavenButton
        {
            Name = "Settings.Models.Personalise.Remove",
            Content = "Remove override",
            Variant = ButtonVariant.Danger
        };
        RemovePersonalisationEntryButton.Accessibility.AccessibleName = "Remove per-model override";
        card.Add(SavePersonalisationEntryButton);
        card.Add(RemovePersonalisationEntryButton);
        return card;
    }

    private Container BuildModelPermissionsCard()
    {
        var card = Card("Settings.Models.Permissions");
        card.Add(Heading("Settings.Models.Permissions.Heading", "Model permissions", 18));
        card.Add(Muted("Settings.Models.Permissions.Description",
            "Deny-rules restrict what matched models may do. More specific rules win over broader ones; a capability without a matching deny stays allowed."));
        PermissionTargetKindSelect = NewSelect("Settings.Models.Permissions.Target", PermissionTargetLabels);
        PermissionTargetKindSelect.SelectedIndex = 0;
        PermissionTargetKindSelect.Accessibility.AccessibleName = "Permission rule target kind";
        PermissionTargetKindSelect.SelectionChanged += (_, _) => UpdatePermissionRuleFields();
        PermissionMatchInput = new Input { Name = "Settings.Models.Permissions.Match", Placeholder = "Model, family or provider id to match" };
        PermissionMatchInput.Accessibility.AccessibleName = "Permission rule match text";
        PermissionMatchInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        PermissionMaxParametersInput = new Input { Name = "Settings.Models.Permissions.MaxParameters", Placeholder = "Maximum size in billions, e.g. 27" };
        PermissionMaxParametersInput.Accessibility.AccessibleName = "Maximum parameter size in billions";
        PermissionMaxParametersInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        PermissionScopeSelect = NewSelect("Settings.Models.Permissions.Scope", PermissionScopeLabels);
        PermissionScopeSelect.SelectedIndex = 0;
        PermissionScopeSelect.Accessibility.AccessibleName = "Where this permission rule applies";
        DenyEditFilesToggle = NewToggle("Settings.Models.Permissions.DenyEditFiles");
        DenyRunCommandsToggle = NewToggle("Settings.Models.Permissions.DenyRunCommands");
        DenyComputerUseToggle = NewToggle("Settings.Models.Permissions.DenyComputerUse");
        DenyBrowserAutomationToggle = NewToggle("Settings.Models.Permissions.DenyBrowserAutomation");
        DenyEditFilesToggle.Accessibility.AccessibleName = "Deny edit files capability";
        DenyRunCommandsToggle.Accessibility.AccessibleName = "Deny run commands capability";
        DenyComputerUseToggle.Accessibility.AccessibleName = "Deny computer use capability";
        DenyBrowserAutomationToggle.Accessibility.AccessibleName = "Deny browser automation capability";
        card.Add(SettingRow("Rule target", "What kind of models this rule matches.", PermissionTargetKindSelect));
        card.Add(PermissionMatchInput);
        card.Add(PermissionMaxParametersInput);
        card.Add(SettingRow("Applies to", "Keep the rule on this device or apply it across your mesh.", PermissionScopeSelect));
        card.Add(SettingRow("Edit files", "Blocks matched models from creating or changing files.", DenyEditFilesToggle));
        card.Add(SettingRow("Run commands", "Blocks matched models from running local commands.", DenyRunCommandsToggle));
        card.Add(SettingRow("Computer use", "Blocks matched models from device-control actions.", DenyComputerUseToggle));
        card.Add(SettingRow("Browser automation", "Blocks matched models from browser actions.", DenyBrowserAutomationToggle));
        PermissionRulesSelect = NewSelect("Settings.Models.Permissions.Rules", []);
        PermissionRulesSelect.Accessibility.AccessibleName = "Saved permission rules";
        AddPermissionRuleButton = new HavenButton
        {
            Name = "Settings.Models.Permissions.Add",
            Content = "Add rule",
            Variant = ButtonVariant.Primary
        };
        AddPermissionRuleButton.Accessibility.AccessibleName = "Add permission rule";
        RemovePermissionRuleButton = new HavenButton
        {
            Name = "Settings.Models.Permissions.RemoveSelected",
            Content = "Remove selected rule",
            Variant = ButtonVariant.Danger
        };
        RemovePermissionRuleButton.Accessibility.AccessibleName = "Remove selected permission rule";
        RemovePermissionRuleButton.SetValue(HavenProperties.Enabled, false);
        card.Add(PermissionRulesSelect);
        card.Add(AddPermissionRuleButton);
        card.Add(RemovePermissionRuleButton);
        UpdatePermissionRuleFields();
        return card;
    }

    private Container BuildDefaultAppsCard()
    {
        var card = Card("Settings.Models.DefaultApps");
        card.Add(Heading("Settings.Models.DefaultApps.Heading", "Default Apps", 18));
        card.Add(Muted("Settings.Models.DefaultApps.Description",
            "Choose which Haven App normally performs each action category, or Always Ask to decide per request. Catalogued Apps stay listed even when not yet connected; availability is resolved when the action runs."));
        foreach (var category in Enum.GetValues<ProviderCategory>())
        {
            var name = ProviderCategoryNames.For(category);
            var options = new List<string> { "Always Ask" };
            options.AddRange(DefaultCategoryCatalog.ProvidersFor(category));
            var select = NewSelect($"Settings.Models.DefaultApps.{category}", options);
            select.SelectedIndex = 0;
            select.Accessibility.AccessibleName = $"Default App for {name}";
            DefaultProviderSelects[category] = select;
            card.Add(SettingRow(name, $"Which App normally performs {name} actions.", select));
        }
        return card;
    }

    private void UpdatePermissionRuleFields()
    {
        var index = PermissionTargetKindSelect.SelectedIndex;
        PermissionMatchInput.SetValue(HavenProperties.Enabled, index is >= 0 and <= 2);
        PermissionMaxParametersInput.SetValue(HavenProperties.Enabled, index == 3);
    }

    public void SetFallbackOrder(IReadOnlyList<string> order)
        => FallbackOrderInput.Text = string.Join("\n", order);

    public void SetSharedPersonality(ModelPersonality personality)
    {
        foreach (var field in PersonalityFields)
            SharedPersonalitySelects[field].SelectedIndex = (int)Level(personality, field);
    }

    public void SetPersonalisationEntries(IReadOnlyList<ModelPersonalisationEntry> entries)
        => PersonalisationSummary.Content = entries.Count == 0
            ? "No per-model overrides stored."
            : string.Join("\n", entries.Select(DescribePersonalisationEntry));

    private static string DescribePersonalisationEntry(ModelPersonalisationEntry entry)
    {
        var nickname = string.IsNullOrWhiteSpace(entry.Nickname) ? string.Empty : $" “{entry.Nickname}”";
        if (entry.Personality is not { } personality) return $"{entry.ModelKey}{nickname} — uses Haven defaults";
        var changed = PersonalityFields
            .Where(field => Level(personality, field) != Level(ModelPersonality.Defaults, field))
            .Select(field => $"{PersonalityTitle(field)} {PersonalityLabels[(int)Level(personality, field)]}")
            .ToList();
        return changed.Count == 0
            ? $"{entry.ModelKey}{nickname} — matches Haven defaults"
            : $"{entry.ModelKey}{nickname} — {string.Join(", ", changed)}";
    }

    public void SetPermissionRules(IReadOnlyList<string> lines)
    {
        PermissionRulesSelect.Items = lines;
        PermissionRulesSelect.SelectedIndex = lines.Count == 0
            ? -1
            : Math.Clamp(PermissionRulesSelect.SelectedIndex, 0, lines.Count - 1);
        RemovePermissionRuleButton.SetValue(HavenProperties.Enabled, lines.Count > 0);
    }

    internal static string DescribePermissionRule(ModelPermissionRule rule)
    {
        var denied = rule.Denied.Count == 0
            ? "nothing"
            : string.Join(", ", Enum.GetValues<RestrictedModelCapability>()
                .Where(rule.Denied.Contains)
                .Select(capability => PermissionCapabilityLabels[(int)capability]));
        var target = rule.Target switch
        {
            ModelPermissionTargetKind.ExactModel => $"Model '{rule.Match}'",
            ModelPermissionTargetKind.ModelFamily => $"Model family '{rule.Match}'",
            ModelPermissionTargetKind.Provider => $"Provider '{rule.Match}'",
            ModelPermissionTargetKind.ParameterSizeBelow => $"Models below {(rule.MaxParameterBillion?.ToString("0.#", CultureInfo.InvariantCulture) ?? "?")}B",
            ModelPermissionTargetKind.LocalModels => "All local models",
            _ => "All cloud models"
        };
        return $"{target} — deny {denied} ({PermissionScopeLabels[(int)rule.Scope]})";
    }

    public void SetDefaultProviderAssignment(ProviderCategory category, string? assignment)
    {
        if (!DefaultProviderSelects.TryGetValue(category, out var select)) return;
        var index = 0;
        if (!string.IsNullOrWhiteSpace(assignment)
            && !assignment.Equals(DefaultProviderAssignments.AlwaysAsk, StringComparison.OrdinalIgnoreCase))
        {
            var candidates = DefaultCategoryCatalog.ProvidersFor(category);
            var found = candidates.ToList().FindIndex(item => item.Equals(assignment, StringComparison.OrdinalIgnoreCase));
            index = found >= 0 ? found + 1 : 0;
        }
        select.SelectedIndex = index;
    }

    internal ModelPersonality ReadSharedPersonality() => new(
        ReadSharedLevel(nameof(ModelPersonality.Friendliness)),
        ReadSharedLevel(nameof(ModelPersonality.MemoryReferences)),
        ReadSharedLevel(nameof(ModelPersonality.Seriousness)),
        ReadSharedLevel(nameof(ModelPersonality.Verbosity)),
        ReadSharedLevel(nameof(ModelPersonality.Initiative)),
        ReadSharedLevel(nameof(ModelPersonality.ExplanationDepth)));

    private PersonalityLevel ReadSharedLevel(string field)
        => (PersonalityLevel)Math.Clamp(SharedPersonalitySelects[field].SelectedIndex, 0, PersonalityLabels.Length - 1);

    /// <summary>Returns the override personality when any trait differs from inheritance, otherwise null.</summary>
    internal ModelPersonality? ReadOverridePersonality(ModelPersonality shared)
    {
        PersonalityLevel? Read(string field)
        {
            var index = Math.Clamp(OverridePersonalitySelects[field].SelectedIndex, 0, PersonalityLabels.Length);
            return index <= 0 ? null : (PersonalityLevel)(index - 1);
        }
        var friendliness = Read(nameof(ModelPersonality.Friendliness));
        var memory = Read(nameof(ModelPersonality.MemoryReferences));
        var seriousness = Read(nameof(ModelPersonality.Seriousness));
        var verbosity = Read(nameof(ModelPersonality.Verbosity));
        var initiative = Read(nameof(ModelPersonality.Initiative));
        var depth = Read(nameof(ModelPersonality.ExplanationDepth));
        if (friendliness is null && memory is null && seriousness is null && verbosity is null && initiative is null && depth is null)
            return null;
        return new ModelPersonality(
            friendliness ?? shared.Friendliness,
            memory ?? shared.MemoryReferences,
            seriousness ?? shared.Seriousness,
            verbosity ?? shared.Verbosity,
            initiative ?? shared.Initiative,
            depth ?? shared.ExplanationDepth);
    }

    internal ModelPermissionTargetKind? ReadPermissionTarget()
    {
        var index = PermissionTargetKindSelect.SelectedIndex;
        return index >= 0 && index < PermissionTargetLabels.Length
            ? (ModelPermissionTargetKind)index
            : null;
    }

    internal bool TryReadMaxParameterBillions(out double billions)
        => double.TryParse(PermissionMaxParametersInput.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out billions)
           && billions > 0;

    internal ModelPermissionScope? ReadPermissionScope()
        => PermissionScopeSelect.SelectedIndex switch
        {
            0 => ModelPermissionScope.ThisDevice,
            1 => ModelPermissionScope.AcrossMesh,
            _ => null
        };

    internal IReadOnlyList<RestrictedModelCapability> ReadDeniedCapabilities()
    {
        var denied = new List<RestrictedModelCapability>(4);
        if (DenyEditFilesToggle.IsChecked) denied.Add(RestrictedModelCapability.EditFiles);
        if (DenyRunCommandsToggle.IsChecked) denied.Add(RestrictedModelCapability.RunCommands);
        if (DenyComputerUseToggle.IsChecked) denied.Add(RestrictedModelCapability.ComputerUse);
        if (DenyBrowserAutomationToggle.IsChecked) denied.Add(RestrictedModelCapability.BrowserAutomation);
        return denied;
    }

    internal void ResetPermissionDraft()
    {
        PermissionMatchInput.Text = string.Empty;
        PermissionMaxParametersInput.Text = string.Empty;
        DenyEditFilesToggle.IsChecked = false;
        DenyRunCommandsToggle.IsChecked = false;
        DenyComputerUseToggle.IsChecked = false;
        DenyBrowserAutomationToggle.IsChecked = false;
    }

    private static PersonalityLevel Level(ModelPersonality personality, string field) => field switch
    {
        nameof(ModelPersonality.Friendliness) => personality.Friendliness,
        nameof(ModelPersonality.MemoryReferences) => personality.MemoryReferences,
        nameof(ModelPersonality.Seriousness) => personality.Seriousness,
        nameof(ModelPersonality.Verbosity) => personality.Verbosity,
        nameof(ModelPersonality.Initiative) => personality.Initiative,
        _ => personality.ExplanationDepth
    };

    private static string PersonalityTitle(string field) => field switch
    {
        nameof(ModelPersonality.MemoryReferences) => "Memory references",
        nameof(ModelPersonality.ExplanationDepth) => "Explanation depth",
        _ => field
    };

    private static string PersonalityDescription(string field) => field switch
    {
        nameof(ModelPersonality.Friendliness) => "How warm or reserved responses feel.",
        nameof(ModelPersonality.MemoryReferences) => "How often replies draw on what Haven remembers.",
        nameof(ModelPersonality.Seriousness) => "From playful and casual to formal and serious.",
        nameof(ModelPersonality.Verbosity) => "From extremely concise to highly detailed answers.",
        nameof(ModelPersonality.Initiative) => "From strictly reactive to proactively suggesting next steps.",
        _ => "From minimal notes to explanations that teach the underlying ideas."
    };
}
