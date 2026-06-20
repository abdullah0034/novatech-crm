using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services;

public interface INotificationService
{
    Task SendOrderConfirmationAsync(Order order, CancellationToken ct = default);
    Task SendOrderRejectedAsync(Order order, CancellationToken ct = default);
    Task SendFraudAlertAsync(Order order, FraudCheckResult result, CancellationToken ct = default);
}
