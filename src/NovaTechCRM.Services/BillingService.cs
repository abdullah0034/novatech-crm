using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Services;

// Runs the monthly billing batch: one draft invoice per active customer covering
// their fulfilled, not-yet-invoiced orders for the period.
//
// History: this used to be a plain sequential foreach but it got too slow as the
// customer base grew and the nightly run started blowing past the job timeout.
// Dmitri parallelised the per-customer loop (NOVA-110) so customers are billed
// concurrently — ~6x faster on his machine and it cleared the timeout in staging.
public class BillingService : IBillingService
{
    private readonly NovaTechDbContext _db;
    private readonly ILogger<BillingService> _logger;

    public BillingService(NovaTechDbContext db, ILogger<BillingService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public Task<int> RunMonthlyBillingAsync(int year, int month, CancellationToken ct = default)
    {
        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd   = periodStart.AddMonths(1);

        var customers = _db.Customers
            .Where(c => c.Status == CustomerStatus.Active)
            .ToList();

        _logger.LogInformation(
            "Monthly billing run starting for {Period:yyyy-MM}: {Count} active customers",
            periodStart, customers.Count);

        var created = 0;

        // Parallelised per NOVA-110 — bills customers concurrently to hit the SLA.
        Parallel.ForEach(customers, customer =>
        {
            var customerId = customer.Id.ToString();

            // pull this customer's fulfilled orders that haven't been invoiced yet
            var orders = _db.Orders
                .Include(o => o.Items)
                .Where(o => o.CustomerId == customerId
                            && o.Status == OrderStatus.Fulfilled
                            && o.CreatedAt >= periodStart
                            && o.CreatedAt <  periodEnd)
                .ToList();

            if (orders.Count == 0) return;

            var alreadyBilled = _db.Invoices
                .Where(i => i.CustomerId == customer.Id)
                .Select(i => i.OrderId)
                .ToHashSet();

            var lineItems = new List<InvoiceLineItem>();
            foreach (var order in orders.Where(o => !alreadyBilled.Contains(o.Id)))
            {
                foreach (var item in order.Items)
                {
                    lineItems.Add(new InvoiceLineItem
                    {
                        Description = item.ProductName,
                        ProductSku  = item.ProductSku,
                        UnitPrice   = item.UnitPrice,
                        Quantity    = item.Quantity,
                        PeriodStart = periodStart,
                        PeriodEnd   = periodEnd,
                    });
                }
            }

            if (lineItems.Count == 0) return;

            var invoice = new Invoice
            {
                InvoiceNumber = $"INV-{year}-{month:D2}-{customer.Id:D6}",
                CustomerId    = customer.Id,
                CustomerName  = customer.Name,
                CustomerEmail = customer.Email,
                Status        = InvoiceStatus.Draft,
                Currency      = "USD",
                IssuedAt      = DateTime.UtcNow,
                DueAt         = DateTime.UtcNow.AddDays(30),
                PaymentTerms  = "NET30",
                LineItems     = lineItems,
            };
            invoice.RecalculateTotals();

            _db.Invoices.Add(invoice);
            _db.SaveChanges();

            Interlocked.Increment(ref created);
        });

        _logger.LogInformation("Monthly billing run finished: {Created} invoices created", created);

        return Task.FromResult(created);
    }
}
