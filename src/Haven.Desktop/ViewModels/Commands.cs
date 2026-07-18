/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/Commands.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns RelayCommand, AsyncRelayCommand. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Windows.Input;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents relay command and keeps its related state and behavior together.
/// </summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    /// <summary>
    /// Reports whether can execute changed is true for the current state.
    /// </summary>
    public event EventHandler? CanExecuteChanged;
    /// <summary>
    /// Reports whether can execute is true for the current state.
    /// </summary>
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    /// <summary>
    /// Runs execute while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public void Execute(object? parameter) => execute();
    /// <summary>
    /// Performs the raise can execute changed step owned by this component.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Represents relay command and keeps its related state and behavior together.
/// </summary>
public sealed class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    /// <summary>
    /// Reports whether can execute changed is true for the current state.
    /// </summary>
    public event EventHandler? CanExecuteChanged;
    /// <summary>
    /// Reports whether can execute is true for the current state.
    /// </summary>
    public bool CanExecute(object? parameter) => canExecute?.Invoke((T?)parameter) ?? true;
    /// <summary>
    /// Runs execute while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public void Execute(object? parameter) => execute((T?)parameter);
    /// <summary>
    /// Performs the raise can execute changed step owned by this component.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Represents async relay command and keeps its related state and behavior together.
/// </summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    /// <summary>
    /// Stores running locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _running;
    /// <summary>
    /// Reports whether can execute changed is true for the current state.
    /// </summary>
    public event EventHandler? CanExecuteChanged;
    /// <summary>
    /// Reports whether can execute is true for the current state.
    /// </summary>
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    /// <summary>
    /// Runs execute while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public async void Execute(object? parameter) => await ExecuteAsync().ConfigureAwait(true);

    /// <summary>
    /// Runs execute async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;
        _running = true;
        RaiseCanExecuteChanged();
        try { await execute().ConfigureAwait(true); }
        finally { _running = false; RaiseCanExecuteChanged(); }
    }

    /// <summary>
    /// Performs the raise can execute changed step owned by this component.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Represents async relay command and keeps its related state and behavior together.
/// </summary>
public sealed class AsyncRelayCommand<T>(Func<T?, Task> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    /// <summary>
    /// Stores running locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _running;
    /// <summary>
    /// Reports whether can execute changed is true for the current state.
    /// </summary>
    public event EventHandler? CanExecuteChanged;
    /// <summary>
    /// Reports whether can execute is true for the current state.
    /// </summary>
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke((T?)parameter) ?? true);

    /// <summary>
    /// Runs execute while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public async void Execute(object? parameter) => await ExecuteAsync((T?)parameter).ConfigureAwait(true);

    /// <summary>
    /// Runs execute async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public async Task ExecuteAsync(T? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        RaiseCanExecuteChanged();
        try { await execute(parameter).ConfigureAwait(true); }
        finally { _running = false; RaiseCanExecuteChanged(); }
    }

    /// <summary>
    /// Performs the raise can execute changed step owned by this component.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
