using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Services;

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public async Task SendOrderConfirmationAsync(Order order, CancellationToken ct = default)
    {
        // Simulate email/SMS dispatch latency
        await Task.Delay(150, ct);
        _logger.LogInformation("Order confirmation sent for {OrderId} to customer {CustomerId}",
            order.Id, order.CustomerId);
    }

    public async Task SendOrderRejectedAsync(Order order, CancellationToken ct = default)
    {
        await Task.Delay(100, ct);
        _logger.LogInformation("Order rejection notice sent for {OrderId} to customer {CustomerId}",
            order.Id, order.CustomerId);
    }

    public async Task SendFraudAlertAsync(Order order, FraudCheckResult result, CancellationToken ct = default)
    {
        await Task.Delay(100, ct);
        _logger.LogWarning("Fraud alert dispatched for {OrderId}: risk={RiskLevel}, reason={Reason}",
            order.Id, result.RiskLevel, result.Reason);
    }
}
