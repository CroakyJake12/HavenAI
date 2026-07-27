/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/WindowsGraphicsCaptureService.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns WindowsGraphicsCaptureService, D3dDriverType. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Haven.Application;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Haven.Desktop.Services;

/// <summary>
/// Consent-based Windows Graphics Capture. Only the latest downsampled JPEG is
/// retained, and it is cleared as soon as sharing stops.
/// </summary>
public sealed class WindowsGraphicsCaptureService : IScreenShareService, IAsyncDisposable
{
    /// <summary>
    /// Stores preview max width locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int PreviewMaxWidth = 960;
    /// <summary>
    /// Stores preview max height locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int PreviewMaxHeight = 540;
    /// <summary>
    /// Stores d3d11 create device bgra support locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const uint D3d11CreateDeviceBgraSupport = 0x20;
    /// <summary>
    /// Stores d3d11 sdk version locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const uint D3d11SdkVersion = 7;
    /// <summary>
    /// Stores snapshot interval locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(350);
    /// <summary>
    /// Stores idxgi device id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Guid IdxgiDeviceId = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    /// <summary>
    /// Stores sync locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly object _sync = new();
    /// <summary>
    /// Stores device locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private IDirect3DDevice? _device;
    /// <summary>
    /// Stores frame pool locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Direct3D11CaptureFramePool? _framePool;
    /// <summary>
    /// Stores session locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private GraphicsCaptureSession? _session;
    /// <summary>
    /// Stores item locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private GraphicsCaptureItem? _item;
    /// <summary>
    /// Stores capture cts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _captureCts;
    /// <summary>
    /// Stores latest snapshot locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ScreenShareSnapshot? _latestSnapshot;
    /// <summary>
    /// Stores current source locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ScreenShareSource? _currentSource;
    /// <summary>
    /// Stores last snapshot at locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset _lastSnapshotAt;
    /// <summary>
    /// Stores processing frame locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _processingFrame;

    /// <summary>
    /// Reports whether supported applies to the current state.
    /// </summary>
    public bool IsSupported =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
        && GraphicsCaptureSession.IsSupported();

    public bool IsSharing
    {
        get { lock (_sync) return _session is not null; }
    }

    /// <summary>
    /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
    /// </summary>
    public string? UnavailableReason => IsSupported
        ? null
        : OperatingSystem.IsWindows()
            ? "Windows Graphics Capture requires Windows 10 version 1803 or newer."
            : "Screen sharing currently requires Windows.";

    public ScreenShareSource? CurrentSource
    {
        get { lock (_sync) return _currentSource; }
    }

    /// <summary>
    /// Stores source closed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? SourceClosed;
    /// <summary>
    /// Stores snapshot available locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable;

    /// <summary>
    /// Performs start with system picker asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ScreenShareSource> StartWithSystemPickerAsync(CancellationToken cancellationToken)
    {
        if (!IsSupported) throw new PlatformNotSupportedException(UnavailableReason);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var operation = Dispatcher.UIThread.InvokeAsync(
                () => StartWithSystemPickerAsync(cancellationToken));
            return await operation.ConfigureAwait(false);
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var windowHandle = GetMainWindowHandle();
        var picker = new GraphicsCapturePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        var item = await picker.PickSingleItemAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (item is null) throw new OperationCanceledException("Screen-share selection was cancelled.", cancellationToken);

        var device = CreateDirect3DDevice();
        var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);
        var session = framePool.CreateCaptureSession(item);
        var captureCts = new CancellationTokenSource();
        var source = new ScreenShareSource(Guid.NewGuid().ToString("N"), item.DisplayName, true);

        lock (_sync)
        {
            _device = device;
            _framePool = framePool;
            _session = session;
            _item = item;
            _captureCts = captureCts;
            _currentSource = source;
            _latestSnapshot = null;
            _lastSnapshotAt = DateTimeOffset.MinValue;
        }

        framePool.FrameArrived += OnFrameArrived;
        item.Closed += OnItemClosed;
        session.StartCapture();
        return source;
    }

    /// <summary>
    /// Retrieves latest snapshot async for the current operation.
    /// </summary>
    public Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult(_latestSnapshot);
    }

    /// <summary>
    /// Performs stop asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Direct3D11CaptureFramePool? framePool;
        GraphicsCaptureSession? session;
        GraphicsCaptureItem? item;
        IDirect3DDevice? device;
        CancellationTokenSource? captureCts;
        lock (_sync)
        {
            framePool = _framePool;
            session = _session;
            item = _item;
            device = _device;
            captureCts = _captureCts;
            _framePool = null;
            _session = null;
            _item = null;
            _device = null;
            _captureCts = null;
            _currentSource = null;
            _latestSnapshot = null;
        }

        captureCts?.Cancel();
        if (framePool is not null) framePool.FrameArrived -= OnFrameArrived;
        if (item is not null) item.Closed -= OnItemClosed;
        session?.Dispose();
        framePool?.Dispose();
        device?.Dispose();
        captureCts?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles the frame arrived event raised by the UI or runtime.
    /// </summary>
    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (Interlocked.Exchange(ref _processingFrame, 1) != 0) return;
        try
        {
            CancellationToken cancellationToken;
            lock (_sync)
            {
                if (_captureCts is null || _device is null) return;
                cancellationToken = _captureCts.Token;
                if (DateTimeOffset.UtcNow - _lastSnapshotAt < SnapshotInterval) return;
                _lastSnapshotAt = DateTimeOffset.UtcNow;
            }

            using var frame = sender.TryGetNextFrame();
            if (frame is null || frame.ContentSize.Width <= 0 || frame.ContentSize.Height <= 0) return;
            var scale = Math.Min(
                1d,
                Math.Min(
                    (double)PreviewMaxWidth / frame.ContentSize.Width,
                    (double)PreviewMaxHeight / frame.ContentSize.Height));
            var width = Math.Max(1, (int)Math.Round(frame.ContentSize.Width * scale));
            var height = Math.Max(1, (int)Math.Round(frame.ContentSize.Height * scale));
            using var sourceBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                frame.Surface,
                BitmapAlphaMode.Ignore);
            using var randomAccessStream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, randomAccessStream);
            encoder.SetSoftwareBitmap(sourceBitmap);
            encoder.BitmapTransform.ScaledWidth = checked((uint)width);
            encoder.BitmapTransform.ScaledHeight = checked((uint)height);
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
            await encoder.FlushAsync();
            cancellationToken.ThrowIfCancellationRequested();
            randomAccessStream.Seek(0);
            var length = checked((uint)randomAccessStream.Size);
            using var reader = new DataReader(randomAccessStream.GetInputStreamAt(0));
            await reader.LoadAsync(length);
            var bytes = new byte[checked((int)length)];
            reader.ReadBytes(bytes);
            var snapshot = new ScreenShareSnapshot(
                Convert.ToBase64String(bytes),
                width,
                height,
                DateTimeOffset.UtcNow);

            lock (_sync)
            {
                if (_captureCts is null || _captureCts.IsCancellationRequested) return;
                _latestSnapshot = snapshot;
            }
            SnapshotAvailable?.Invoke(this, new(snapshot));
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception)
        {
            // A dropped frame must not end the call. The next frame can recover;
            // GetLatestSnapshotAsync continues serving the last good snapshot.
        }
        finally
        {
            Interlocked.Exchange(ref _processingFrame, 0);
        }
    }

    /// <summary>
    /// Handles the item closed event raised by the UI or runtime.
    /// </summary>
    private async void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        SourceClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Retrieves main window handle for the current operation.
    /// </summary>
    private static nint GetMainWindowHandle()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.TryGetPlatformHandle() is { } handle
            && handle.Handle != IntPtr.Zero)
            return handle.Handle;
        throw new InvalidOperationException("Haven's main window is not ready for the Windows screen picker.");
    }

    /// <summary>
    /// Creates direct3 d device with the invariants required by its callers.
    /// </summary>
    private static IDirect3DDevice CreateDirect3DDevice()
    {
        nint d3dDevice = 0;
        nint d3dContext = 0;
        nint dxgiDevice = 0;
        nint inspectableDevice = 0;
        try
        {
            var result = D3D11CreateDevice(
                0,
                D3dDriverType.Hardware,
                0,
                D3d11CreateDeviceBgraSupport,
                0,
                0,
                D3d11SdkVersion,
                out d3dDevice,
                out _,
                out d3dContext);
            if (result < 0)
            {
                ReleaseComPointer(ref d3dContext);
                ReleaseComPointer(ref d3dDevice);
                result = D3D11CreateDevice(
                    0,
                    D3dDriverType.Warp,
                    0,
                    D3d11CreateDeviceBgraSupport,
                    0,
                    0,
                    D3d11SdkVersion,
                    out d3dDevice,
                    out _,
                    out d3dContext);
            }
            Marshal.ThrowExceptionForHR(result);

            var iid = IdxgiDeviceId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in iid, out dxgiDevice));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDxgiDevice(dxgiDevice, out inspectableDevice));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectableDevice);
        }
        finally
        {
            ReleaseComPointer(ref inspectableDevice);
            ReleaseComPointer(ref dxgiDevice);
            ReleaseComPointer(ref d3dContext);
            ReleaseComPointer(ref d3dDevice);
        }
    }

    /// <summary>
    /// Performs the release com pointer step owned by this component.
    /// </summary>
    private static void ReleaseComPointer(ref nint pointer)
    {
        if (pointer == 0) return;
        Marshal.Release(pointer);
        pointer = 0;
    }

    /// <summary>
    /// Lists the supported d3d driver type values used to make state explicit and type-safe.
    /// </summary>
    private enum D3dDriverType : uint
    {
        Hardware = 1,
        Warp = 5
    }

    /// <summary>
    /// Performs the d3 d11 create device step owned by this component.
    /// </summary>
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        nint adapter,
        D3dDriverType driverType,
        nint software,
        uint flags,
        nint featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out nint device,
        out uint featureLevel,
        out nint immediateContext);

    /// <summary>
    /// Creates direct3 d11 device from dxgi device with the invariants required by its callers.
    /// </summary>
    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDxgiDevice(
        nint dxgiDevice,
        out nint graphicsDevice);

    /// <summary>
    /// Performs dispose asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async ValueTask DisposeAsync() =>
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
}
