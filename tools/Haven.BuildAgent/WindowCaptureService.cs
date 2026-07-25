using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Options;

namespace Haven.BuildAgent;

public sealed class WindowCaptureService
{
    private const uint RenderFullContent = 2;

    private readonly BuildAgentOptions _options;
    private readonly AppProcessService _runs;

    public WindowCaptureService(IOptions<BuildAgentOptions> options, AppProcessService runs)
    {
        _options = options.Value;
        _runs = runs;
    }

    public async Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        Process process = _runs.GetRunningProcess(request.RunId);
        int waitSeconds = Math.Clamp(request.WaitSeconds, 1, 60);
        nint windowHandle = nint.Zero;
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(waitSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.HasExited)
            {
                throw new InvalidOperationException("Haven exited before its window could be captured.");
            }

            windowHandle = process.MainWindowHandle;
            if (windowHandle != nint.Zero)
            {
                break;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        if (windowHandle == nint.Zero)
        {
            throw new TimeoutException($"No main Haven window appeared within {waitSeconds} seconds.");
        }

        if (NativeMethods.GetWindowRect(windowHandle, out NativeRect rectangle) == 0)
        {
            throw new InvalidOperationException("Windows could not read the Haven window bounds.");
        }

        int width = rectangle.Right - rectangle.Left;
        int height = rectangle.Bottom - rectangle.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"Haven returned invalid window dimensions: {width}x{height}.");
        }

        string captureDirectory = _options.CreateArtifactDirectory("captures", Guid.NewGuid());
        string capturePath = Path.Combine(captureDirectory, $"haven-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.png");

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        nint deviceContext = graphics.GetHdc();
        bool printed;
        try
        {
            printed = NativeMethods.PrintWindow(windowHandle, deviceContext, RenderFullContent) != 0;
        }
        finally
        {
            graphics.ReleaseHdc(deviceContext);
        }

        if (!printed)
        {
            graphics.CopyFromScreen(
                rectangle.Left,
                rectangle.Top,
                0,
                0,
                new Size(width, height),
                CopyPixelOperation.SourceCopy);
        }

        bitmap.Save(capturePath, ImageFormat.Png);
        return new CaptureResult(
            request.RunId,
            width,
            height,
            DateTimeOffset.UtcNow,
            _options.ToArtifactUrl(capturePath),
            capturePath);
    }
}
