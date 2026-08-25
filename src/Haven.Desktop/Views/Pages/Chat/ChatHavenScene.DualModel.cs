/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/Pages/Chat/ChatHavenScene.DualModel.cs, the dual-model partial of the
 *       canonical Haven-native Chat scene.
 * What: Owns the small session-only dual-model affordance beside the composer: a "Dual" toggle and,
 *       while active, a second-model picker button rendered in the scene Footer above the chatbox.
 * How: EnsureDualModelBar inserts the bar once by temporarily removing and re-appending the chatbox so
 *      the affordance row sits directly above the composer while the composer keeps its bottom-anchored
 *      position. The page owns all app state: this partial only projects state via Set methods
 *      (SetDualActive, SetDualSecondModel) and raises semantic intent events back to the page.
 * Why: Dual comparison must stay discoverable yet visually quiet, reusing existing popup/ghost-button
 *      patterns instead of introducing a parallel toolbar or prefab.
 * Maintenance: Keep this surface session-only; persistence, model resolution and run execution stay in
 *              NewChatPage/DualModelChatController. Preserve accessible names when changing labels.
 */

using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    private HavenButton? _dualToggle;
    private HavenButton? _dualModelPicker;

    public event EventHandler? DualToggleRequested;
    public event EventHandler? DualModelPickerRequested;
    public event EventHandler<string>? DualSecondModelChosen;

    /// <summary>
    /// Adds the dual-model affordance row to the Footer once; safe to call repeatedly.
    /// </summary>
    public void EnsureDualModelBar()
    {
        if (_dualToggle is not null) return;
        var footer = Root.DescendantsAndSelf().OfType<Container>().Single(element => element.Name == "Footer");
        var bar = new Container { Layout = HavenLayout.Horizontal, Name = "DualModelBar" };
        bar.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        bar.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        bar.SetValue(HavenProperties.Gap, HavenLength.Px(6));

        _dualToggle = new HavenButton { Content = "Dual: Off", Variant = ButtonVariant.Ghost };
        _dualToggle.SetValue(HavenProperties.MinHeight, HavenLength.Px(30));
        _dualToggle.Accessibility.AccessibleName = "Toggle side-by-side dual-model comparison";
        _dualToggle.Invoked += (_, _) => DualToggleRequested?.Invoke(this, EventArgs.Empty);

        _dualModelPicker = new HavenButton { Content = "Model B: choose…", Variant = ButtonVariant.Ghost };
        _dualModelPicker.SetValue(HavenProperties.MinHeight, HavenLength.Px(30));
        _dualModelPicker.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _dualModelPicker.Accessibility.AccessibleName = "Choose the second dual model";
        _dualModelPicker.Invoked += (_, _) => DualModelPickerRequested?.Invoke(this, EventArgs.Empty);

        bar.Add(_dualToggle);
        bar.Add(_dualModelPicker);

        // Re-append the chatbox so the affordance sits directly beside/above the composer controls
        // while the chatbox keeps its bottom-anchored viewport position.
        footer.Remove(Chatbox);
        footer.Add(bar);
        footer.Add(Chatbox);

        SetDualActive(false);
        SetDualSecondModel(null);
    }

    /// <summary>
    /// Reflects whether dual mode is on; shows or hides the second-model picker accordingly.
    /// </summary>
    public void SetDualActive(bool active)
    {
        if (_dualToggle is null) return;
        _dualToggle.Content = active ? "Dual: On" : "Dual: Off";
        _dualToggle.Accessibility.AccessibleName = active
            ? "Dual-model comparison is on. Turn off"
            : "Turn on side-by-side dual-model comparison";
        _dualModelPicker!.SetValue(
            HavenProperties.Visibility,
            active ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    /// <summary>Reflects the chosen second model key on the picker button.</summary>
    public void SetDualSecondModel(string? modelKey)
    {
        if (_dualModelPicker is null) return;
        var label = string.IsNullOrWhiteSpace(modelKey) ? "Model B: choose…" : $"Model B: {modelKey}";
        _dualModelPicker.Content = label;
        _dualModelPicker.Accessibility.AccessibleName = string.IsNullOrWhiteSpace(modelKey)
            ? "Choose the second dual model"
            : $"Second dual model is {modelKey}. Choose another";
    }

    /// <summary>
    /// Shows the installed-model choice menu anchored to the picker button; the page receives the
    /// selection through <see cref="DualSecondModelChosen"/>.
    /// </summary>
    public void ShowDualModelChoices(IReadOnlyList<string> modelKeys)
    {
        ArgumentNullException.ThrowIfNull(modelKeys);
        if (_dualModelPicker is null || modelKeys.Count == 0) return;
        IReadOnlyList<(string Label, Action Action)> choices = modelKeys
            .Select(key => ((string Label, Action Action))($"Model B — {key}", () => DualSecondModelChosen?.Invoke(this, key)))
            .ToArray();
        ShowAnchoredChoiceMenu(_dualModelPicker, "Choose the second model", choices, 300d);
    }
}
