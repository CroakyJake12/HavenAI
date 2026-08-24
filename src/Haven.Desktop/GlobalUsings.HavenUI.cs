// Haven-owned code-behind composes UI exclusively from the canonical HavenUI
// vocabulary. Keeping the namespace global prevents each view from silently
// falling back to raw Avalonia controls merely because an import was omitted.
global using Haven.Desktop.HavenUI.Components;
