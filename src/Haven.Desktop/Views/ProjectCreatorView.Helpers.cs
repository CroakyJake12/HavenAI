using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views;

public sealed partial class ProjectCreatorView
{
    private void SetFormEnabled(bool enabled)
    {
        foreach (var control in new Control[]
                {
                    _promptBox,
                    _templateSearchBox,
                    _projectNameBox,
                    _destinationBox,
                    _packageDescriptionBox,
                    _dotNetButton,
                    _packageButton,
                    _reviewButton,
                    _approveButton,
                    _chooseDestinationButton,
                    _openFolderButton,
                    _openProjectFileButton,
                    _detailsToggleButton
                })
        {
            control.IsEnabled = enabled;
        }

        foreach (var templateButton in _templateButtons.Values)
        {
            templateButton.IsEnabled = enabled;
        }
    }

    private static void SetText(TextBox textBox, string value)
    {
        if (!string.Equals(textBox.Text, value, StringComparison.Ordinal))
        {
            textBox.Text = value;
        }
    }

    private static void ApplyChoiceState(Button button, bool isSelected)
    {
        button.Background = isSelected ? SelectedBrush : CardBrush;
        button.BorderBrush = isSelected ? AccentBrush : BorderBrush;
        button.BorderThickness = new Thickness(isSelected ? 2 : 1);
    }

    private static TextBox FieldTextBox(string automationName)
    {
        var box = new TextBox
        {
            MinHeight = 42,
            Padding = new Thickness(12),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(box, automationName);
        return box;
    }

    private static Button ChoiceButton(string text)
    {
        var button = SecondaryButton(text);
        button.MinWidth = 150;
        return button;
    }

    private static Button PrimaryButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Background = AccentBrush,
            Foreground = AccentTextBrush,
            Padding = new Thickness(16, 10),
            MinHeight = 40,
            CornerRadius = new CornerRadius(9)
        };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static Button SecondaryButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Background = CardBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 9),
            MinHeight = 40,
            CornerRadius = new CornerRadius(9)
        };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static Button IconButton(string iconKey, string automationName)
    {
        var button = new Button
        {
            Content = new HavenIcon
            {
                IconKey = iconKey,
                Width = 24,
                Height = 24,
                Foreground = TextBrush
            },
            Width = 62,
            Height = 62,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(31),
            Background = CardBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1)
        };
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    private static Border Card(Control child, double padding = 22) =>
        new()
        {
            Background = CardBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(padding),
            Child = child
        };

    private static TextBlock Heading(string text, double size) =>
        new()
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

    private static TextBlock Label(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 4, 0, -6)
        };

    private static Control DetailRow(string title, string description) =>
        new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,*"),
            ColumnSpacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                DescriptionText(description)
            }
        };

    private static TextBlock DescriptionText(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(block, 1);
        return block;
    }

    private static IBrush Brush(string value) =>
        new SolidColorBrush(Color.Parse(value));

    private static IBrush PaletteBrush(string key, string fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? Brush(fallback);
}
