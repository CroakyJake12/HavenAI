using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Haven.Application;
using Haven.Automations;
using Haven.Browser;
using Haven.Desktop.ViewModels;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Controls;

internal static class ResilientChatSessionBootstrap
{
    private static bool _scheduled;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (_scheduled) return;
        _scheduled = true;
        Dispatcher.UIThread.Post(async () =>
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                if (App.Services is { } services
                    && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel shell })
                {
                    Install(services, shell);
                    return;
                }
                await Task.Delay(100);
            }
        }, DispatcherPriority.Background);
    }

    private static void Install(IServiceProvider services, MainWindowViewModel shell)
    {
        try
        {
            var resilient = new ResilientProviderRoutingModelClient(
                services.GetRequiredService<ProviderRoutingModelClient>(),
                services.GetRequiredService<IModelProviderRegistry>(),
                services.GetRequiredService<IProviderConfigurationStore>());
            var session = new ChatSessionService(
                services.GetRequiredService<IConversationRepository>(),
                resilient,
                services.GetRequiredService<CapabilityPreflightService>(),
                services.GetRequiredService<WorkspaceToolRuntime>(),
                services.GetRequiredService<ComputerToolRuntime>(),
                services.GetRequiredService<BrowserToolRuntime>(),
                services.GetRequiredService<AutomationToolRuntime>());

            SetSession(shell, session);
            foreach (var chat in shell.OpenTabs.Select(item => item.Page).OfType<ChatPageViewModel>().Append(shell.CurrentChat).Distinct(ReferenceComparer.Instance))
                SetSession(chat, session);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FieldAccessException or TargetException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine("Resilient chat session bootstrap failed: " + ex.Message);
        }
    }

    private static void SetSession(object target, ChatSessionService session)
    {
        var field = target.GetType().GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"{target.GetType().Name} no longer exposes the expected chat session field.");
        field.SetValue(target, session);
    }

    private sealed class ReferenceComparer : IEqualityComparer<ChatPageViewModel>
    {
        public static ReferenceComparer Instance { get; } = new();
        public bool Equals(ChatPageViewModel? x, ChatPageViewModel? y) => ReferenceEquals(x, y);
        public int GetHashCode(ChatPageViewModel obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
