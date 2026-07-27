using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Haven.Desktop.Views.Pages.ProjectCreator;

public sealed partial class ProjectCreatorPage
{
    private void OnChooseDotNetFromVisualStudioClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SelectDotNetButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    private void OnChooseExistingFromVisualStudioClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenProjectFileButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    private async void OnOpenGitHubClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            StatusText.Text = "A window is required to clone a repository.";
            return;
        }

        var urlBox = new TextBox
        {
            PlaceholderText = "https://github.com/owner/repository.git"
        };
        var destinationBox = new TextBox
        {
            Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        var status = new TextBlock
        {
            Opacity = 0.68,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var browse = new Button { Content = "Browse" };
        browse.Click += async (_, _) =>
        {
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Choose where the repository should be cloned",
                    AllowMultiple = false
                });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                destinationBox.Text = path;
            }
        };

        var destinationRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        destinationRow.Children.Add(destinationBox);
        Grid.SetColumn(browse, 1);
        destinationRow.Children.Add(browse);

        var cloneButton = new Button
        {
            Content = "Clone and connect",
            MinWidth = 150
        };
        cloneButton.Classes.Add("accent");

        var cancelButton = new Button { Content = "Cancel" };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, cloneButton }
        };

        var content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Create Project from GitHub",
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold
                },
                new TextBlock { Text = "Repository URL", FontSize = 11, Opacity = 0.66 },
                urlBox,
                new TextBlock { Text = "Destination", FontSize = 11, Opacity = 0.66 },
                destinationRow,
                status,
                actions
            }
        };

        var dialog = new Window
        {
            Title = "Create Project from GitHub",
            Width = 560,
            Height = 360,
            CanResize = false,
            Content = content,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        cancelButton.Click += (_, _) => dialog.Close();

        cloneButton.Click += async (_, _) =>
        {
            cloneButton.IsEnabled = false;
            try
            {
                var repositoryUrl = urlBox.Text?.Trim();
                var destination = destinationBox.Text?.Trim();

                if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) ||
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                {
                    status.Text = "Enter an HTTPS GitHub repository URL.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
                {
                    status.Text = "Choose an existing destination folder.";
                    return;
                }

                var repositoryName = Path.GetFileNameWithoutExtension(uri.AbsolutePath.TrimEnd('/'));
                if (string.IsNullOrWhiteSpace(repositoryName))
                {
                    status.Text = "The repository name could not be determined.";
                    return;
                }

                var targetPath = Path.Combine(destination, repositoryName);
                if (Directory.Exists(targetPath) &&
                    Directory.EnumerateFileSystemEntries(targetPath).Any())
                {
                    status.Text = "The destination already contains a non-empty folder with that name.";
                    return;
                }

                status.Text = "Cloning repository…";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("clone");
                startInfo.ArgumentList.Add("--");
                startInfo.ArgumentList.Add(uri.AbsoluteUri);
                startInfo.ArgumentList.Add(targetPath);

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    status.Text = "Git could not be started.";
                    return;
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(CancellationToken.None);
                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode != 0)
                {
                    status.Text = string.IsNullOrWhiteSpace(error)
                        ? $"Git exited with code {process.ExitCode}."
                        : error.Trim();
                    return;
                }

                status.Text = string.IsNullOrWhiteSpace(output)
                    ? "Connecting cloned project…"
                    : output.Trim();

                var result = await _creator.ConnectAsync(targetPath, CancellationToken.None);
                StatusText.Text = result.Message;
                await _completed(result.Project);
                dialog.Close();
            }
            catch (Exception exception)
            {
                status.Text = $"Clone failed: {exception.Message}";
            }
            finally
            {
                cloneButton.IsEnabled = true;
            }
        };

        await dialog.ShowDialog(owner);
    }
}
