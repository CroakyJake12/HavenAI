using Haven.Application;
using Haven.Desktop.Overlay;
namespace Haven.Desktop.Tests;
public sealed class OverlayVisualContextCaptureFlowTests
{
    [Fact]
    public async Task Capture_persists_frame_and_stops_share()
    {
        var root=Path.Combine(Path.GetTempPath(),"haven-overlay-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var token=TestContext.Current.CancellationToken;
            var share=new FakeShare(); var service=new OverlayVisualContextCaptureService(share,new Paths(root));
            var context=await service.CaptureAsync(token);
            var file=Assert.Single(context.Attachments).Id;
            Assert.True(File.Exists(file)); Assert.Equal(new byte[]{1,2,3},await File.ReadAllBytesAsync(file,token));
            Assert.Equal(1,share.StartCount); Assert.Equal(1,share.StopCount); Assert.Equal(OverlayContextKind.Image,context.Kind);
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
    [Fact]
    public async Task Capture_does_not_take_over_active_share()
    {
        var token=TestContext.Current.CancellationToken;
        var share=new FakeShare { IsSharing=true };
        var service=new OverlayVisualContextCaptureService(share,new Paths(Path.GetTempPath()));
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>service.CaptureAsync(token));
        Assert.Contains("screen share is active",ex.Message); Assert.Equal(0,share.StartCount); Assert.Equal(0,share.StopCount);
    }
    private sealed class FakeShare : IScreenShareService
    {
        public bool IsSupported=>true; public bool IsSharing{get;set;} public string? UnavailableReason=>null; public ScreenShareSource? CurrentSource{get;private set;}
        public int StartCount{get;private set;} public int StopCount{get;private set;}
        public event EventHandler? SourceClosed { add { } remove { } } public event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable { add { } remove { } }
        public Task<ScreenShareSource> StartWithSystemPickerAsync(CancellationToken cancellationToken){StartCount++; IsSharing=true; CurrentSource=new("id","Picked display",ScreenShareSourceKind.Unknown); return Task.FromResult(CurrentSource);}
        public Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken)=>Task.FromResult<ScreenShareSnapshot?>(new(Convert.ToBase64String(new byte[]{1,2,3}),640,480,DateTimeOffset.UtcNow));
        public Task StopAsync(CancellationToken cancellationToken){StopCount++; IsSharing=false; CurrentSource=null; return Task.CompletedTask;}
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory=>root; public string DatabasePath=>Path.Combine(root,"db.sqlite"); public string BrowserProfileDirectory=>root; public string AttachmentsDirectory=>root; public string LogsDirectory=>root; public string LegacyStatePath=>Path.Combine(root,"legacy.json");
    }
}
