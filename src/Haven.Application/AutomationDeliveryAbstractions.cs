using Haven.Core;

namespace Haven.Application;

public interface IAutomationDeliveryOutbox
{
    Task EnqueueAsync(AutomationDelivery delivery, CancellationToken cancellationToken);
    Task<IReadOnlyList<AutomationDelivery>> DrainAsync(CancellationToken cancellationToken);
}
