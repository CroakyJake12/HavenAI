/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/CodeIntelligenceView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns CodeIntelligenceView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents code intelligence view and keeps its related state and behavior together.
/// </summary>
public sealed partial class CodeIntelligenceView : UserControl
{
    /// <summary>
    /// Stores view model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly CodeIntelligenceViewModel? _viewModel;
    /// <summary>
    /// Stores chat locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ChatPageViewModel? _chat;

    public CodeIntelligenceView()
    {
        InitializeComponent();
        if (App.Services is null) return;
        _viewModel = ActivatorUtilities.CreateInstance<CodeIntelligenceViewModel>(App.Services);
        _viewModel.InsertRequested += InsertIntoComposer;
        DataContext = _viewModel;
        AttachedToVisualTree += (_, _) => AttachChat();
        DetachedFromVisualTree += (_, _) => DetachChat();
    }

    /// <summary>
    /// Performs the attach chat step owned by this component.
    /// </summary>
    private void AttachChat()
    {
        var chat = this.FindAncestorOfType<ChatView>()?.DataContext as ChatPageViewModel;
        if (ReferenceEquals(chat, _chat)) return;
        DetachChat();
        _chat = chat;
        if (_chat is not null) _chat.PropertyChanged += OnChatPropertyChanged;
        _viewModel?.SetChat(_chat);
    }

    /// <summary>
    /// Performs the detach chat step owned by this component.
    /// </summary>
    private void DetachChat()
    {
        if (_chat is not null) _chat.PropertyChanged -= OnChatPropertyChanged;
        _chat = null;
    }

    /// <summary>
    /// Handles the chat property changed event raised by the UI or runtime.
    /// </summary>
    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatPageViewModel.SelectedContainer) or nameof(ChatPageViewModel.Mode))
            _viewModel?.SetChat(_chat);
    }

    /// <summary>
    /// Performs the insert into composer step owned by this component.
    /// </summary>
    private void InsertIntoComposer(string text)
    {
        if (_chat is null) return;
        _chat.Composer = string.IsNullOrWhiteSpace(_chat.Composer)
            ? text.TrimStart()
            : _chat.Composer.TrimEnd() + text;
    }
}
