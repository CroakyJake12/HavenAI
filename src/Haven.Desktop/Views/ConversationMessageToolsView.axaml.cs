/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/ConversationMessageToolsView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns ConversationMessageToolsView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents conversation message tools view and keeps its related state and behavior together.
/// </summary>
public sealed partial class ConversationMessageToolsView : UserControl
{
    /// <summary>
    /// Stores view model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Stores branch changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? BranchChanged;
    /// <summary>
    /// Stores regeneration requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event Action<string>? RegenerationRequested;

    /// <summary>
    /// Performs load async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task LoadAsync(Guid conversationId, CancellationToken cancellationToken) =>
        _viewModel?.LoadAsync(conversationId, cancellationToken) ?? Task.CompletedTask;
}
