using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Repositories;

namespace NovaTechCRM.Services;

public class OrderService
{
    private readonly IOrderRepository _orderRepo;
    private readonly IFraudShieldService _fraudShield;
    private readonly INotificationService _notifications;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepo,
        IFraudShieldService fraudShield,
        INotificationService notifications,
        ILogger<OrderService> logger)
    {
        _orderRepo     = orderRepo;
        _fraudShield   = fraudShield;
        _notifications = notifications;
        _logger        = logger;
    }

    public async Task<Order> PlaceOrderAsync(Order order, CancellationToken ct = default)
    {
        order.Status = OrderStatus.FraudCheckPending;
        await _orderRepo.SaveAsync(order, ct);

        FraudCheckResult fraudResult;
        try
        {
            // Use a dedicated timeout so a hung FraudShield API doesn't block threads
            // forever, and client disconnects don't cancel the check mid-flight (which
            // left orders permanently stuck in FraudCheckPending at the Kestrel level).
            using var fraudCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            fraudCts.CancelAfter(TimeSpan.FromSeconds(30));
            fraudResult = await _fraudShield.CheckAsync(order, fraudCts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // FraudShield is unreachable — leave order in FraudCheckPending so it
            // can be retried rather than silently dropped or wrongly fulfilled.
            _logger.LogError(ex,
                "FraudShield unavailable for order {OrderId}; leaving in FraudCheckPending",
                order.Id);
            return order;
        }

        if (!fraudResult.Passed)
        {
            order.Status        = OrderStatus.Rejected;
            order.FraudCheckPassed = false;
            await _orderRepo.SaveAsync(order, CancellationToken.None);

            // Notify the customer their order was declined (previously missing —
            // only an internal fraud alert was sent, so customers never heard back).
            await _notifications.SendOrderRejectedAsync(order, CancellationToken.None);
            await _notifications.SendFraudAlertAsync(order, fraudResult, CancellationToken.None);

            _logger.LogWarning(
                "Order {OrderId} rejected by FraudShield: risk={RiskLevel}, reason={Reason}",
                order.Id, fraudResult.RiskLevel, fraudResult.Reason);
            return order;
        }

        order.FraudCheckId     = fraudResult.CheckId;
        order.FraudCheckPassed = true;

        // Low-risk orders (< $500): FraudShield's synchronous verdict is definitive —
        // safe to fulfill immediately.
        //
        // Medium/High/Critical risk (>= $500): the real FraudShield API returns a
        // provisional result and performs deeper async checks, delivering the final
        // verdict via webhook. Fulfilling here — before the webhook arrives — is the
        // race condition that caused confirmed-but-never-fulfilled. Set Approved and
        // let HandleFraudWebhookAsync trigger fulfillment once FraudShield confirms.
        if (fraudResult.RiskLevel == FraudRiskLevel.Low)
        {
            await FulfillOrderAsync(order, ct);
        }
        else
        {
            order.Status = OrderStatus.Approved;
            await _orderRepo.SaveAsync(order, ct);
            _logger.LogInformation(
                "Order {OrderId} approved by FraudShield (risk={RiskLevel}); " +
                "awaiting webhook confirmation before fulfillment",
                order.Id, fraudResult.RiskLevel);
        }

        return order;
    }

    // Called by the FraudShield webhook endpoint with the definitive async verdict.
    public async Task HandleFraudWebhookAsync(
        string checkId, bool approved, CancellationToken ct = default)
    {
        var order = await _orderRepo.GetByFraudCheckIdAsync(checkId, ct);

        if (order is null)
        {
            _logger.LogWarning(
                "FraudShield webhook received for unknown checkId {CheckId}", checkId);
            return;
        }

        if (order.Status != OrderStatus.Approved)
        {
            // Already fulfilled, rejected, or cancelled by another path — ignore.
            _logger.LogWarning(
                "FraudShield webhook for order {OrderId} arrived in unexpected status {Status}; ignoring",
                order.Id, order.Status);
            return;
        }

        if (approved)
        {
            await FulfillOrderAsync(order, ct);
        }
        else
        {
            order.Status = OrderStatus.Rejected;
            await _orderRepo.SaveAsync(order, CancellationToken.None);
            await _notifications.SendOrderRejectedAsync(order, CancellationToken.None);
            _logger.LogWarning(
                "Order {OrderId} rejected via FraudShield async webhook (checkId={CheckId})",
                order.Id, checkId);
        }
    }

    public async Task<Order?> GetOrderAsync(Guid orderId, CancellationToken ct = default)
        => await _orderRepo.GetByIdAsync(orderId, ct);

    public async Task<IReadOnlyList<Order>> GetCustomerOrdersAsync(
        string customerId, CancellationToken ct = default)
        => await _orderRepo.GetByCustomerAsync(customerId, ct);

    private async Task FulfillOrderAsync(Order order, CancellationToken ct = default)
    {
        order.Status      = OrderStatus.Fulfilled;
        order.FulfilledAt = DateTime.UtcNow;
        await _orderRepo.SaveAsync(order, CancellationToken.None);

        await _notifications.SendOrderConfirmationAsync(order, CancellationToken.None);

        _logger.LogInformation(
            "Order {OrderId} fulfilled for customer {CustomerId} (amount: {Amount:C})",
            order.Id, order.CustomerId, order.TotalAmount);
    }
}
