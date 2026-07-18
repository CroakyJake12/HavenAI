/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/ConversationUsageView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns ConversationUsageView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents conversation usage view and keeps its related state and behavior together.
/// </summary>
public sealed partial class ConversationUsageView : UserControl
{
    /// <summary>
    /// Stores view model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConversationUsageViewModel? _viewModel;

    public ConversationUsageView()
    {
        InitializeComponent();
        if (App.Services is null) return;
        _viewModel = ActivatorUtilities.CreateInstance<ConversationUsageViewModel>(App.Services);
        DataContext = _viewModel;
    }

    /// <summary>
    /// Performs load asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task LoadAsync(Guid conversationId, CancellationToken cancellationToken) =>
        _viewModel?.LoadAsync(conversationId, cancellationToken) ?? Task.CompletedTask;
}
