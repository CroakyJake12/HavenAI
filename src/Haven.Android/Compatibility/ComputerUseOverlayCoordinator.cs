using Haven.Application;

namespace Haven.Desktop.Services;

/// <summary>
/// Android compatibility coordinator. The desktop implementation owns top-level
/// Avalonia overlay windows, which are not supported by the Android windowing
/// backend. Android keeps the same DI contract without constructing a Window.
/// </summary>
public sealed class ComputerUseOverlayCoordinator : IDisposable
{
    public ComputerUseOverlayCoordinator(IComputerUseSessionController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
    }

    public void Dispose()
    {
    }
}
