/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ActivatablePage.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns IActivatablePage. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Defines the activatable page contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IActivatablePage
{
    Task ActivateAsync(CancellationToken cancellationToken);
    void Deactivate();
}

