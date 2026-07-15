using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class ConversationRetrievalView : UserControl
{
    private readonly ConversationRetrievalViewModel? _viewModel;

    public ConversationRetrievalView()
    {
        InitializeComponent();
        if (App.Services is null) return;
        _viewModel = ActivatorUtilities.CreateInstance<ConversationRetrievalViewModel>(App.Services);
        _viewModel.InsertRequested += InsertIntoComposer;
        DataContext = _viewModel;
    }

    public void Load(Guid conversationId) => _viewModel?.Load(conversationId);

    private void OnUseComposerQueryClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || this.FindAncestorOfType<ChatView>()?.DataContext is not ChatPageViewModel chat) return;
        _viewModel.Query = chat.Composer.Trim();
        if (_viewModel.SearchCommand.CanExecute(null)) _viewModel.SearchCommand.Execute(null);
    }

    private void InsertIntoComposer(string context)
    {
        if (this.FindAncestorOfType<ChatView>()?.DataContext is not ChatPageViewModel chat) return;
        chat.Composer = chat.Composer.TrimEnd() + context;
    }
}
