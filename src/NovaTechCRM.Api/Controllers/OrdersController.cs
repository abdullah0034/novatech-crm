using Microsoft.AspNetCore.Mvc;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Services;

namespace NovaTechCRM.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;

    public OrdersController(OrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request, CancellationToken ct)
    {
        var order = new Order
        {
            CustomerId = request.CustomerId,
            TotalAmount = request.Items.Sum(i => i.Quantity * i.UnitPrice),
            Items = request.Items.Select(i => new OrderItem
            {
                ProductSku = i.ProductSku,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        var placed = await _orderService.PlaceOrderAsync(order, ct);

        return placed.Status switch
        {
            // Fraud check failed — order will never be fulfilled. Return 422 so
            // clients don't treat this as a successful placement (previously returned
            // 201 for all outcomes, causing the "confirmed but never fulfilled" reports).
            OrderStatus.Rejected =>
                UnprocessableEntity(new { error = "Order could not be accepted.", orderId = placed.Id }),

            // High-risk order: passed initial check, awaiting FraudShield async
            // confirmation before fulfillment. Return 202 so the client knows to
            // poll for status rather than assuming immediate fulfillment.
            OrderStatus.Approved =>
                AcceptedAtAction(nameof(GetOrder), new { id = placed.Id }, placed),

            // Low-risk: synchronous check passed and order is already fulfilled.
            _ => CreatedAtAction(nameof(GetOrder), new { id = placed.Id }, placed)
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken ct)
    {
        var order = await _orderService.GetOrderAsync(id, ct);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet("customer/{customerId}")]
    public async Task<IActionResult> GetCustomerOrders(string customerId, CancellationToken ct)
    {
        var orders = await _orderService.GetCustomerOrdersAsync(customerId, ct);
        return Ok(orders);
    }
}

public record PlaceOrderRequest(
    string CustomerId,
    List<OrderItemRequest> Items
);

public record OrderItemRequest(
    string ProductSku,
    string ProductName,
    int Quantity,
    decimal UnitPrice
);
