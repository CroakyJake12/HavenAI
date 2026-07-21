/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/DeveloperTools/HavenDeveloperToolsSession.cs in the Desktop composition layer.
 * What: Owns one built-in developer-tools session for the main Haven window.
 * How: It creates, toggles, coordinates, and disposes the inspector and element-picker windows.
 * Why: Session lifetime stays isolated from the production workspace shell and commercial diagnostics packages.
 */

using Avalonia;
using Avalonia.Controls;

namespace Haven.Desktop.DeveloperTools;

/// <summary>
/// Owns one built-in developer-tools session for the main Haven window.
/// </summary>
internal sealed class HavenDeveloperToolsSession : IDisposable
{
    private readonly Window _owner;
    private DeveloperToolsWindow? _tools;
    private ElementPickerWindow? _picker;
    private bool _disposed;

    public HavenDeveloperToolsSession(Window owner)
    {
        _owner = owner;
        _owner.Closed += OnOwnerClosed;
    }

    public void Toggle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tools = EnsureTools();
        if (tools.IsVisible)
        {
            tools.Hide();
            return;
        }

        tools.RefreshTree();
        tools.Show(_owner);
        tools.Activate();
    }

    public void BeginInspect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tools = EnsureTools();
        if (!tools.IsVisible)
            tools.Show(_owner);
        tools.Activate();

        ClosePicker();
        var picker = new ElementPickerWindow(_owner);
        _picker = picker;
        picker.ElementPicked += OnElementPicked;
        picker.Closed += OnPickerClosed;
        picker.Show(_owner);
        picker.Activate();
    }

    private DeveloperToolsWindow EnsureTools()
    {
        if (_tools is not null) return _tools;

        var tools = new DeveloperToolsWindow(_owner);
        tools.InspectRequested += (_, _) => BeginInspect();
        tools.Closed += OnToolsClosed;
        _tools = tools;
        return tools;
    }

    private void OnElementPicked(Visual visual)
    {
        var tools = EnsureTools();
        tools.RefreshTree();
        tools.SelectVisual(visual);
        tools.Activate();
    }

    private void OnPickerClosed(object? sender, EventArgs e)
    {
        if (sender is ElementPickerWindow picker)
        {
            picker.ElementPicked -= OnElementPicked;
            picker.Closed -= OnPickerClosed;
        }
        if (ReferenceEquals(_picker, sender)) _picker = null;
    }

    private void OnToolsClosed(object? sender, EventArgs e)
    {
        if (sender is DeveloperToolsWindow tools)
            tools.Closed -= OnToolsClosed;
        if (ReferenceEquals(_tools, sender)) _tools = null;
    }

    private void OnOwnerClosed(object? sender, EventArgs e) => Dispose();

    private void ClosePicker()
    {
        if (_picker is null) return;
        var picker = _picker;
        _picker = null;
        picker.ElementPicked -= OnElementPicked;
        picker.Closed -= OnPickerClosed;
        picker.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Closed -= OnOwnerClosed;
        ClosePicker();
        if (_tools is not null)
        {
            var tools = _tools;
            _tools = null;
            tools.Closed -= OnToolsClosed;
            tools.Close();
        }
    }
}
