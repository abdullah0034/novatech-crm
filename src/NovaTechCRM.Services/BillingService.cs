using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Domain.Models;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Services;

// Runs the monthly billing batch: one draft invoice per active customer covering
// their fulfilled, not-yet-invoiced orders for the period.
//
// WHY NOT Parallel.ForEach:
// EF Core's DbContext is not thread-safe. Concurrent operations on the shared _db
// instance cause "A second operation was started on this context" errors, and the
// shared change tracker lets one thread's SaveChanges() flush entities staged by
// another thread — producing invoices with wrong line items.
//
// The fix is two bulk reads for the whole period, in-memory grouping per customer,
// then one AddRange + SaveChangesAsync. Query count is O(3) regardless of how many
// customers there are, so the run time no longer scales with customer count.
public class BillingService : IBillingService
{
    private readonly NovaTechDbContext _db;
    private readonly ILogger<BillingService> _logger;

    public BillingService(NovaTechDbContext db, ILogger<BillingService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<int> RunMonthlyBillingAsync(int year, int month, CancellationToken ct = default)
    {
        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd   = periodStart.AddMonths(1);

        // Query 1: all active customers.
        var customers = await _db.Customers
            .Where(c => c.Status == CustomerStatus.Active)
            .AsNoTracking()
            .ToListAsync(ct);

        _logger.LogInformation(
            "Monthly billing run starting for {Period:yyyy-MM}: {Count} active customers",
            periodStart, customers.Count);

        if (customers.Count == 0)
            return 0;

        var customerIdStrings = customers.Select(c => c.Id.ToString()).ToHashSet();

        // Query 2: all fulfilled orders for the period across all active customers at once.
        var allOrders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => customerIdStrings.Contains(o.CustomerId)
                     && o.Status == OrderStatus.Fulfilled
                     && o.CreatedAt >= periodStart
                     && o.CreatedAt <  periodEnd)
            .AsNoTracking()
            .ToListAsync(ct);

        // Query 3: invoice numbers already issued for this period (for idempotent re-runs).
        // Dedup by InvoiceNumber, not Invoice.OrderId — billing invoices cover multiple
        // orders and never set OrderId, so an OrderId-based check would always pass.
        var periodPrefix = $"INV-{year}-{month:D2}-";
        var alreadyBilledNumbers = (await _db.Invoices
            .Where(i => i.InvoiceNumber.StartsWith(periodPrefix))
            .Select(i => i.InvoiceNumber)
            .ToListAsync(ct))
            .ToHashSet();

        // All remaining work is in memory — no further DB calls until the final save.
        var ordersByCustomer = allOrders
            .GroupBy(o => o.CustomerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var invoicesToCreate = new List<Invoice>();
        var now = DateTime.UtcNow;

        foreach (var customer in customers)
        {
            var invoiceNumber = $"INV-{year}-{month:D2}-{customer.Id:D6}";

            if (alreadyBilledNumbers.Contains(invoiceNumber))
                continue;

            if (!ordersByCustomer.TryGetValue(customer.Id.ToString(), out var orders))
                continue;

            var lineItems = orders
                .SelectMany(o => o.Items.Select(item => new InvoiceLineItem
                {
                    Description = item.ProductName,
                    ProductSku  = item.ProductSku,
                    UnitPrice   = item.UnitPrice,
                    Quantity    = item.Quantity,
                    PeriodStart = periodStart,
                    PeriodEnd   = periodEnd,
                }))
                .ToList();

            if (lineItems.Count == 0)
                continue;

            var invoice = new Invoice
            {
                InvoiceNumber = invoiceNumber,
                CustomerId    = customer.Id,
                CustomerName  = customer.Name,
                CustomerEmail = customer.Email,
                Status        = InvoiceStatus.Draft,
                Currency      = "USD",
                IssuedAt      = now,
                DueAt         = now.AddDays(30),
                PaymentTerms  = "NET30",
                LineItems     = lineItems,
            };
            invoice.RecalculateTotals();

            invoicesToCreate.Add(invoice);
        }

        if (invoicesToCreate.Count == 0)
        {
            _logger.LogInformation("Monthly billing run finished: no new invoices to create");
            return 0;
        }

        _db.Invoices.AddRange(invoicesToCreate);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Monthly billing run finished: {Created} invoices created for {Period:yyyy-MM}",
            invoicesToCreate.Count, periodStart);

        return invoicesToCreate.Count;
    }
}
