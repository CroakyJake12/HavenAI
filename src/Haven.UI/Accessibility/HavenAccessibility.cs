namespace Haven.UI;

public enum HavenAccessibleRole
{
    None,
    Window,
    Group,
    Text,
    Button,
    Input,
    CheckBox,
    Slider,
    List,
    ListItem,
    Tab,
    TabItem,
    Image,
    Link,
    Menu,
    MenuItem,
    Dialog
}

public sealed class HavenAccessibility
{
    public HavenAccessibleRole Role { get; set; }
    public string? AccessibleName { get; set; }
    public string? Description { get; set; }
    public bool Focusable { get; set; }
    public bool Selected { get; set; }
    public bool Checked { get; set; }
    public bool Enabled { get; set; } = true;
    public bool IsPassword { get; set; }
}
