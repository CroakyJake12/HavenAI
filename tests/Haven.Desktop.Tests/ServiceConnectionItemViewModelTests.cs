using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Tests;

public sealed class ServiceConnectionItemViewModelTests
{
    [Fact]
    public void UnconfiguredProviderCannotConnect()
    {
        var item = new ServiceConnectionItemViewModel(CalendarProviderKind.Google);
        item.Apply(new FakeProvider(CalendarProviderKind.Google, false, "Google Calendar OAuth client is not configured."), null);

        Assert.False(item.IsConfigured);
        Assert.False(item.IsConnected);
        Assert.False(item.CanConnect);
        Assert.Equal("Not configured", item.ConnectionLabel);
        Assert.Contains("not configured", item.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadyAccountProjectsConnectedState()
    {
        var now = DateTimeOffset.UtcNow;
        var account = new CalendarAccount(Guid.NewGuid(), CalendarProviderKind.Google, "Jacob", "jacob@example.com",
            CalendarSyncStatus.Ready, null, now, now, now);
        var item = new ServiceConnectionItemViewModel(CalendarProviderKind.Google);

        item.Apply(new FakeProvider(CalendarProviderKind.Google, true, "Google Calendar is ready to connect."), account);

        Assert.True(item.IsConnected);
        Assert.True(item.CanDisconnect);
        Assert.False(item.CanConnect);
        Assert.Equal("Connected", item.ConnectionLabel);
        Assert.Contains("jacob@example.com", item.AccountLabel);
        Assert.StartsWith("Last synced", item.LastSyncLabel);
    }

    [Fact]
    public void ErrorAccountRemainsAStoredConnectionAndSurfacesError()
    {
        var now = DateTimeOffset.UtcNow;
        var account = new CalendarAccount(Guid.NewGuid(), CalendarProviderKind.Microsoft, "Microsoft account", "user@example.com",
            CalendarSyncStatus.Error, "Token refresh failed.", null, now, now);
        var item = new ServiceConnectionItemViewModel(CalendarProviderKind.Microsoft);

        item.Apply(new FakeProvider(CalendarProviderKind.Microsoft, true, "Microsoft Calendar is ready to connect."), account);

        Assert.True(item.IsConnected);
        Assert.True(item.CanDisconnect);
        Assert.Equal("Error", item.ConnectionLabel);
        Assert.Equal("Token refresh failed.", item.Status);
    }

    private sealed class FakeProvider(CalendarProviderKind kind, bool configured, string status) : ICalendarSyncProvider
    {
        public CalendarProviderKind Kind => kind;
        public bool IsConfigured => configured;
        public string ConfigurationStatus => status;
        public Task<CalendarAuthorizationResult> ConnectAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CalendarSyncResult> SyncAsync(CalendarSyncRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DisconnectAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
