using Avalonia.Controls;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class ConversationMessageToolsView : UserControl
{
    private readonly ConversationMessageToolsViewModel? _viewModel;

    public ConversationMessageToolsView()
    {
        InitializeComponent();
        if (App.Services is null) return;
        _viewModel = ActivatorUtilities.CreateInstance<ConversationMessageToolsViewModel>(App.Services);
        _viewModel.BranchChanged += (_, _) => BranchChanged?.Invoke(this, EventArgs.Empty);
        _viewModel.RegenerationRequested += prompt => RegenerationRequested?.Invoke(prompt);
        DataContext = _viewModel;
    }

    public event EventHandler? BranchChanged;
    public event Action<string>? RegenerationRequested;

    public Task LoadAsync(Guid conversationId, CancellationToken cancellationToken) =>
        _viewModel?.LoadAsync(conversationId, cancellationToken) ?? Task.CompletedTask;
}
