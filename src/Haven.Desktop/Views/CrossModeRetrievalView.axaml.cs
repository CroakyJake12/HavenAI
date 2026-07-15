using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class CrossModeRetrievalView : UserControl
{
    private readonly CrossModeRetrievalViewModel? _viewModel;
    private ChatPageViewModel? _chat;

    public CrossModeRetrievalView()
    {
        InitializeComponent();
        if (App.Services is null) return;
        _viewModel = ActivatorUtilities.CreateInstance<CrossModeRetrievalViewModel>(App.Services);
        _viewModel.InsertRequested += InsertIntoComposer;
        DataContext = _viewModel;
        AttachedToVisualTree += (_, _) => AttachChat();
        DetachedFromVisualTree += (_, _) => DetachChat();
    }

    private void AttachChat()
    {
        var chat = this.FindAncestorOfType<ChatView>()?.DataContext as ChatPageViewModel;
        if (ReferenceEquals(chat, _chat)) return;
        DetachChat();
        _chat = chat;
        if (_chat is not null) _chat.PropertyChanged += OnChatPropertyChanged;
        _viewModel?.SetChat(_chat);
    }

    private void DetachChat()
    {
        if (_chat is not null) _chat.PropertyChanged -= OnChatPropertyChanged;
        _chat = null;
    }

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatPageViewModel.SelectedContainer) or nameof(ChatPageViewModel.SelectedLesson))
            _viewModel?.SetChat(_chat);
    }

    private void InsertIntoComposer(string context)
    {
        if (_chat is null) return;
        _chat.Composer = _chat.Composer.TrimEnd() + context;
    }
}
