using Android.Content;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async void OnMobileTopRailModelRequested(object? sender, EventArgs e)
    {
        await ShowModelSelectorAsync();
        AddAndroidModelManagementActions();
    }

    private void AddAndroidModelManagementActions()
    {
        if (_modelSelectorFlyout?.Content is not Border flyoutBorder
            || flyoutBorder.Child is not ScrollViewer scroller
            || scroller.Content is not StackPanel rows)
        {
            return;
        }

        var availableWidth = Bounds.Width > 0 ? Bounds.Width - 24 : 336;
        flyoutBorder.Width = Math.Clamp(availableWidth, 280, 360);

        if (rows.Children.OfType<Border>()
            .Any(item => string.Equals(item.Name, "AndroidModelLibrarySection", StringComparison.Ordinal)))
        {
            return;
        }

        rows.Children.Add(new Separator { Margin = new Thickness(4, 8) });

        var section = new Border
        {
            Name = "AndroidModelLibrarySection",
            Padding = new Thickness(8, 4, 8, 2),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Model library",
                        FontWeight = FontWeight.ExtraBold,
                        FontSize = 14
                    },
                    new TextBlock
                    {
                        Text = "Browse Hugging Face GGUF models, use recommendations for this device, download models, or import local GGUF files and folders.",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 11,
                        Foreground = Avalonia.Application.Current?.Resources["HavenTextSoftBrush"] as IBrush
                    },
                    ModelLibraryButton(
                        "Browse Hugging Face & recommendations",
                        "Search the Hugging Face API and download a GGUF selected for this device.",
                        "browse",
                        () =>
                        {
                            _modelSelectorFlyout?.Hide();
                            LaunchAndroidModelImporter();
                        }),
                    ModelLibraryButton(
                        "Import GGUF files or a folder",
                        "Choose individual GGUF files or recursively import a folder.",
                        "folder",
                        () =>
                        {
                            _modelSelectorFlyout?.Hide();
                            LaunchAndroidModelImportPicker();
                        })
                }
            }
        };

        rows.Children.Add(section);
    }

    private static Button ModelLibraryButton(
        string title,
        string detail,
        string iconKey,
        Action action)
    {
        var text = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.Bold,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = detail,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Avalonia.Application.Current?.Resources["HavenTextSoftBrush"] as IBrush
                }
            }
        };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10
        };
        content.Children.Add(new HavenIcon
        {
            IconKey = iconKey,
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(text, 1);
        content.Children.Add(text);

        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 8),
            Content = content
        };
        button.Classes.Add("sidebar");
        button.Click += (_, _) => action();
        return button;
    }

    private static void LaunchAndroidModelImportPicker()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(Haven.Android.ModelImportActivity));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
