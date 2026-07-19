using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Domain.Exceptions;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Services;

public class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly ICustomerRepository _customerRepo;
    private readonly INotificationService _notifications;
    private readonly IPdfGeneratorService _pdfGenerator;
    private readonly IAuditService _audit;
    private readonly ILogger<InvoiceService> _logger;

    // invoice number counter — persisted in DB but cached here (risky across instances)
    // TODO: move to DB sequence or Redis counter (NOVA-51)
    private static int _invoiceSequence = 0;

    public InvoiceService(
        IInvoiceRepository invoiceRepo,
        ICustomerRepository customerRepo,
        INotificationService notifications,
        IPdfGeneratorService pdfGenerator,
        IAuditService audit,
        ILogger<InvoiceService> logger)
    {
        _invoiceRepo   = invoiceRepo;
        _customerRepo  = customerRepo;
        _notifications = notifications;
        _pdfGenerator  = pdfGenerator;
        _audit         = audit;
        _logger        = logger;
    }

    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _invoiceRepo.GetByIdAsync(id, ct);

    public async Task<Invoice?> GetByNumberAsync(string number, CancellationToken ct = default) =>
        await _invoiceRepo.GetByNumberAsync(number, ct);

    public async Task<IReadOnlyList<Invoice>> GetByCustomerAsync(
        int customerId, CancellationToken ct = default) =>
        await _invoiceRepo.GetByCustomerAsync(customerId, ct);

    public async Task<IReadOnlyList<InvoiceSummary>> GetOverdueAsync(
        CancellationToken ct = default) =>
        await _invoiceRepo.GetOverdueAsync(ct);

    public async Task<Invoice> CreateFromOrderAsync(Order order, CancellationToken ct = default)
    {
        var customer = await _customerRepo.GetByIdAsync(int.Parse(order.CustomerId), ct)
            ?? throw new InvoiceException($"Customer {order.CustomerId} not found.");

        var invoice = new Invoice
        {
            CustomerId    = customer.Id,
            CustomerName  = customer.Name,
            CustomerEmail = customer.Email,
            OrderId       = order.Id,
            Status        = InvoiceStatus.Draft,
            Currency      = "USD",
            IssuedAt      = DateTime.UtcNow,
            DueAt         = DateTime.UtcNow.AddDays(30), // NET30
            PaymentTerms  = "NET30",
            LineItems     = order.Items.Select(i => new InvoiceLineItem
            {
                Description = i.ProductName,
                ProductSku  = i.ProductSku,
                UnitPrice   = i.UnitPrice,
                Quantity    = i.Quantity,
                SortOrder   = order.Items.IndexOf(i)
            }).ToList()
        };

        invoice.BillingAddressLine1 = customer.BillingAddressLine1;
        invoice.BillingCity         = customer.BillingCity;
        invoice.BillingState        = customer.BillingState;
        invoice.BillingPostalCode   = customer.BillingPostalCode;
        invoice.BillingCountry      = customer.BillingCountry;

        invoice.RecalculateTotals();
        invoice.InvoiceNumber = await GenerateInvoiceNumberAsync(ct);

        var created = await _invoiceRepo.CreateAsync(invoice, ct);

        await _audit.LogAsync(AuditAction.Created, "Invoice", created.Id.ToString(),
            null, newValues: new { created.InvoiceNumber, created.TotalAmount }, ct: ct);

        _logger.LogInformation("Invoice {Number} created for order {OrderId}: {Total:C}",
            created.InvoiceNumber, order.Id, created.TotalAmount);

        return created;
    }

    public async Task<Invoice> CreateManualAsync(Invoice invoice, CancellationToken ct = default)
    {
        if (!invoice.LineItems.Any())
            throw new InvoiceException("Invoice must have at least one line item.");

        invoice.InvoiceNumber = await GenerateInvoiceNumberAsync(ct);
        invoice.CreatedAt     = DateTime.UtcNow;
        invoice.Status        = InvoiceStatus.Draft;
        invoice.RecalculateTotals();

        return await _invoiceRepo.CreateAsync(invoice, ct);
    }

    public async Task<Invoice> IssueAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(invoiceId, ct)
            ?? throw new InvoiceException($"Invoice {invoiceId} not found.");

        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvoiceException($"Cannot issue invoice in status {invoice.Status}.");

        invoice.Status   = InvoiceStatus.Issued;
        invoice.IssuedAt = DateTime.UtcNow;

        var updated = await _invoiceRepo.UpdateAsync(invoice, ct);

        _logger.LogInformation("Invoice {Number} issued: {Total:C}", invoice.InvoiceNumber, invoice.TotalAmount);

        return updated;
    }

    public async Task<Invoice> RecordPaymentAsync(
        Guid invoiceId, decimal amount, Guid paymentId, CancellationToken ct = default)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(invoiceId, ct)
            ?? throw new InvoiceException($"Invoice {invoiceId} not found.");

        if (invoice.Status == InvoiceStatus.Voided)
            throw new InvoiceException("Cannot record payment on a voided invoice.");

        invoice.AmountPaid += amount;

        if (invoice.AmountPaid >= invoice.TotalAmount)
        {
            invoice.Status  = InvoiceStatus.Paid;
            invoice.PaidAt  = DateTime.UtcNow;
        }
        else if (invoice.AmountPaid > 0)
        {
            invoice.Status = InvoiceStatus.PartiallyPaid;
        }

        invoice.UpdatedAt = DateTime.UtcNow;

        await _invoiceRepo.AddPaymentRecordAsync(new InvoicePaymentRecord
        {
            InvoiceId     = invoiceId,
            PaymentId     = paymentId,
            AmountApplied = amount,
            AppliedAt     = DateTime.UtcNow
        }, ct);

        return await _invoiceRepo.UpdateAsync(invoice, ct);
    }

    public async Task<Invoice> VoidAsync(
        Guid invoiceId, string reason, CancellationToken ct = default)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(invoiceId, ct)
            ?? throw new InvoiceException($"Invoice {invoiceId} not found.");

        invoice.Void(reason);

        var updated = await _invoiceRepo.UpdateAsync(invoice, ct);

        await _audit.LogAsync(AuditAction.StatusChanged, "Invoice", invoiceId.ToString(),
            null, oldValues: new { Status = "Issued" },
            newValues: new { Status = "Voided", Reason = reason }, ct: ct);

        return updated;
    }

    public async Task<string> GeneratePdfAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(invoiceId, ct)
            ?? throw new InvoiceException($"Invoice {invoiceId} not found.");

        var pdfUrl = await _pdfGenerator.GenerateInvoicePdfAsync(invoice, ct);

        invoice.PdfUrl    = pdfUrl;
        invoice.UpdatedAt = DateTime.UtcNow;
        await _invoiceRepo.UpdateAsync(invoice, ct);

        return pdfUrl;
    }

    public async Task SendAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(invoiceId, ct)
            ?? throw new InvoiceException($"Invoice {invoiceId} not found.");

        if (invoice.Status == InvoiceStatus.Draft)
            invoice = await IssueAsync(invoiceId, ct);

        // generate PDF if not yet done
        if (string.IsNullOrWhiteSpace(invoice.PdfUrl))
            await GeneratePdfAsync(invoiceId, ct);

        await _notifications.SendInvoiceAsync(invoice, ct);

        invoice.SentAt    = DateTime.UtcNow;
        invoice.Status    = InvoiceStatus.Sent;
        invoice.UpdatedAt = DateTime.UtcNow;

        await _invoiceRepo.UpdateAsync(invoice, ct);

        _logger.LogInformation("Invoice {Number} sent to {Email}",
            invoice.InvoiceNumber, invoice.CustomerEmail);
    }

    public async Task ProcessOverdueAsync(CancellationToken ct = default)
    {
        var overdue = await _invoiceRepo.GetByStatusAsync(InvoiceStatus.Issued, ct);
        var now     = DateTime.UtcNow;

        foreach (var invoice in overdue.Where(i => i.DueAt < now && i.AmountDue > 0))
        {
            try
            {
                // Send first, flip status second. This method only ever re-scans invoices
                // that are still Issued, so if the process dies (e.g. mid-deploy restart)
                // before the notification completes, the invoice stays Issued and tomorrow's
                // run retries both the send and the status change. Persisting Overdue first
                // is what silently dropped notifications for invoices caught by a restart
                // between the two steps (NOVA-123).
                // TODO: add configurable grace period (NOVA-64)
                await _notifications.SendInvoiceOverdueAsync(invoice, ct);

                invoice.Status    = InvoiceStatus.Overdue;
                invoice.UpdatedAt = now;
                await _invoiceRepo.UpdateAsync(invoice, ct);

                _logger.LogWarning("Invoice {Number} marked overdue ({Days} days late)",
                    invoice.InvoiceNumber, (int)(now - invoice.DueAt).TotalDays);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to send overdue notification for invoice {Number}; leaving status as Issued for retry",
                    invoice.InvoiceNumber);
            }
        }
    }

    private async Task<string> GenerateInvoiceNumberAsync(CancellationToken ct)
    {
        var year     = DateTime.UtcNow.Year;
        var sequence = await _invoiceRepo.GetNextSequenceAsync(year, ct);
        return $"INV-{year}-{sequence:D5}";
    }
}

// marker interface — implementation lives in Infrastructure
public interface IPdfGeneratorService
{
    Task<string> GenerateInvoicePdfAsync(Invoice invoice, CancellationToken ct);
}
