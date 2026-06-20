using Microsoft.AspNetCore.Mvc;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Api.Controllers;

// Webhook endpoints — no JWT auth; each provider signs the payload instead.
// These must stay on the public path list in AuthMiddleware.
[Route("api/webhooks")]
[ApiController]
public class WebhooksController : ControllerBase
{
    private readonly IPaymentService _payments;
    private readonly OrderService _orderService;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IPaymentService payments,
        OrderService orderService,
        ILogger<WebhooksController> logger)
    {
        _payments     = payments;
        _orderService = orderService;
        _logger       = logger;
    }

    [HttpPost("stripe")]
    public async Task<IActionResult> Stripe(CancellationToken ct)
    {
        var payload   = await ReadBodyAsync();
        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault() ?? "";

        if (string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("Stripe webhook arrived without signature header");
            return BadRequest();
        }

        await _payments.HandleWebhookAsync(PaymentProvider.Stripe, payload, signature, ct);
        return Ok();
    }

    [HttpPost("paypal")]
    public async Task<IActionResult> PayPal(CancellationToken ct)
    {
        var payload   = await ReadBodyAsync();
        var signature = Request.Headers["PAYPAL-TRANSMISSION-SIG"].FirstOrDefault() ?? "";

        await _payments.HandleWebhookAsync(PaymentProvider.PayPal, payload, signature, ct);
        return Ok();
    }

    [HttpPost("braintree")]
    public async Task<IActionResult> Braintree(CancellationToken ct)
    {
        // Braintree sends form-encoded bt_signature + bt_payload
        var btSignature = Request.Form["bt_signature"].ToString();
        var btPayload   = Request.Form["bt_payload"].ToString();

        await _payments.HandleWebhookAsync(
            PaymentProvider.Braintree, btPayload, btSignature, ct);

        return Ok();
    }

    // FraudShield sends this when its async review of a high-risk order is complete.
    // This is the missing handler that caused confirmed-but-never-fulfilled: without it,
    // Approved orders never transitioned to Fulfilled (or Rejected), and ops had to
    // manually intervene via the FraudShield dashboard.
    [HttpPost("fraudshield")]
    public async Task<IActionResult> FraudShield(
        [FromBody] FraudShieldWebhookPayload payload, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(payload.CheckId))
        {
            _logger.LogWarning("FraudShield webhook arrived with missing CheckId");
            return BadRequest();
        }

        _logger.LogInformation(
            "FraudShield webhook received: checkId={CheckId}, status={Status}",
            payload.CheckId, payload.Status);

        await _orderService.HandleFraudWebhookAsync(
            payload.CheckId,
            approved: payload.Status == "approved",
            ct);

        return Ok();
    }

    [HttpPost("fedex")]
    public async Task<IActionResult> FedEx()
    {
        // FedEx tracking webhooks — not fully implemented yet (NOVA-78)
        // Just acknowledge receipt so FedEx stops retrying
        _logger.LogDebug("FedEx tracking webhook received (not processed — NOVA-78)");
        return Ok();
    }

    private async Task<string> ReadBodyAsync()
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body         = await reader.ReadToEndAsync();
        Request.Body.Position = 0;
        return body;
    }
}

// FraudShield posts this JSON body when async review of a high-risk order completes.
// "status" is either "approved" or "rejected".
public record FraudShieldWebhookPayload(string CheckId, string Status);
