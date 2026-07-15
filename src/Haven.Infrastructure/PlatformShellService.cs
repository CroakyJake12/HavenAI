using Haven.Application;

namespace Haven.Infrastructure;

public sealed class PlatformShellService : IPlatformShellService
{
    public Task OpenExternalAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
                Verb = "open"
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
        return Task.CompletedTask;
    }

    public Task<string> GetClipboardTextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(string.Empty);

    public Task SetClipboardTextAsync(string text, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
