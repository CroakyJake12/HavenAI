using Avalonia.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// AXAML-defined add menu flyout control. Replaces the code-generated flyout in AddMenu.
/// </summary>
public sealed partial class AddMenuFlyoutControl : UserControl
{
    public AddMenuFlyoutControl()
    {
        InitializeComponent();
    }

    public event EventHandler<AddMenu.AddMenuAction>? ActionSelected;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FileButton.Click += (_, _) => ActionSelected?.Invoke(this, AddMenu.AddMenuAction.File);
        AgentButton.Click += (_, _) => ActionSelected?.Invoke(this, AddMenu.AddMenuAction.Agent);
        CapabilityButton.Click += (_, _) => ActionSelected?.Invoke(this, AddMenu.AddMenuAction.Capability);
        InstructionButton.Click += (_, _) => ActionSelected?.Invoke(this, AddMenu.AddMenuAction.Instruction);
        AppButton.Click += (_, _) => ActionSelected?.Invoke(this, AddMenu.AddMenuAction.App);
    }

    public void HighlightAction(AddMenu.AddMenuAction action)
    {
        FileButton.Classes.Remove("sidebarActive");
        AgentButton.Classes.Remove("sidebarActive");
        CapabilityButton.Classes.Remove("sidebarActive");
        InstructionButton.Classes.Remove("sidebarActive");
        AppButton.Classes.Remove("sidebarActive");

        var button = action switch
        {
            AddMenu.AddMenuAction.File => FileButton,
            AddMenu.AddMenuAction.Agent => AgentButton,
            AddMenu.AddMenuAction.Capability => CapabilityButton,
            AddMenu.AddMenuAction.Instruction => InstructionButton,
            AddMenu.AddMenuAction.App => AppButton,
            _ => null
        };
        button?.Classes.Add("sidebarActive");
    }
}
