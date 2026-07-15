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
    private const int PreviewMaxWidth = 960;
    private const int PreviewMaxHeight = 540;
    private const uint D3d11CreateDeviceBgraSupport = 0x20;
    private const uint D3d11SdkVersion = 7;
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(350);
    private static readonly Guid IdxgiDeviceId = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    private readonly object _sync = new();
    private IDirect3DDevice? _device;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private CancellationTokenSource? _captureCts;
    private ScreenShareSnapshot? _latestSnapshot;
    private ScreenShareSource? _currentSource;
    private DateTimeOffset _lastSnapshotAt;
    private int _processingFrame;

    public bool IsSupported =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
        && GraphicsCaptureSession.IsSupported();

    public bool IsSharing
    {
        get { lock (_sync) return _session is not null; }
    }

    public string? UnavailableReason => IsSupported
        ? null
        : OperatingSystem.IsWindows()
            ? "Windows Graphics Capture requires Windows 10 version 1803 or newer."
            : "Screen sharing currently requires Windows.";

    public ScreenShareSource? CurrentSource
    {
        get { lock (_sync) return _currentSource; }
    }

    public event EventHandler? SourceClosed;
    public event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable;

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

    public Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult(_latestSnapshot);
    }

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

    private async void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        SourceClosed?.Invoke(this, EventArgs.Empty);
    }

    private static nint GetMainWindowHandle()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.TryGetPlatformHandle() is { } handle
            && handle.Handle != IntPtr.Zero)
            return handle.Handle;
        throw new InvalidOperationException("Haven's main window is not ready for the Windows screen picker.");
    }

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

    private static void ReleaseComPointer(ref nint pointer)
    {
        if (pointer == 0) return;
        Marshal.Release(pointer);
        pointer = 0;
    }

    private enum D3dDriverType : uint
    {
        Hardware = 1,
        Warp = 5
    }

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

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDxgiDevice(
        nint dxgiDevice,
        out nint graphicsDevice);

    public async ValueTask DisposeAsync() =>
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
}
