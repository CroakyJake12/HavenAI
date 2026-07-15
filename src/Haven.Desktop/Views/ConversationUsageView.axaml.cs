using Avalonia.Controls;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class ConversationUsageView : UserControl
{
    private readonly ConversationUsageViewModel? _viewModel;

    public ConversationUsageView()
    {
        InitializeComponent();
        if (App.Services is null) return;
        _viewModel = ActivatorUtilities.CreateInstance<ConversationUsageViewModel>(App.Services);
        DataContext = _viewModel;
    }

    public Task LoadAsync(Guid conversationId, CancellationToken cancellationToken) =>
        _viewModel?.LoadAsync(conversationId, cancellationToken) ?? Task.CompletedTask;
}
