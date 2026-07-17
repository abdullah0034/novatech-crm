using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly NovaTechDbContext _db;
    private readonly string _connectionString;

    public InvoiceRepository(NovaTechDbContext db, string connectionString)
    {
        _db               = db;
        _connectionString = connectionString;
    }

    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Invoices
            .Include(i => i.LineItems)
            .Include(i => i.PaymentRecords)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<Invoice?> GetByNumberAsync(string invoiceNumber, CancellationToken ct = default)
        => await _db.Invoices
            .Include(i => i.LineItems)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber, ct);

    public async Task<IReadOnlyList<Invoice>> GetByCustomerAsync(
        int customerId, CancellationToken ct = default)
        => await _db.Invoices
            .Include(i => i.LineItems)
            .AsNoTracking()
            .Where(i => i.CustomerId == customerId)
            .OrderByDescending(i => i.IssuedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InvoiceSummary>> GetOverdueAsync(CancellationToken ct = default)
        => await _db.Invoices
            .AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Overdue)
            .OrderBy(i => i.DueAt)
            .Select(i => new InvoiceSummary
            {
                Id            = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                CustomerName  = i.CustomerName,
                TotalAmount   = i.TotalAmount,
                AmountDue     = i.AmountDue,
                DueAt         = i.DueAt,
                Status        = i.Status,
                DaysOverdue   = (int)(DateTime.UtcNow - i.DueAt).TotalDays
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Invoice>> GetByStatusAsync(
        InvoiceStatus status, CancellationToken ct = default)
        => await _db.Invoices
            .AsNoTracking()
            .Where(i => i.Status == status)
            .OrderByDescending(i => i.IssuedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Invoice>> SearchAsync(string query, CancellationToken ct = default)
        => await _db.Invoices
            .AsNoTracking()
            .Where(i => i.InvoiceNumber.Contains(query) ||
                        i.CustomerName.Contains(query) ||
                        i.CustomerEmail.Contains(query))
            .Take(50)
            .ToListAsync(ct);

    public async Task<Invoice> CreateAsync(Invoice invoice, CancellationToken ct = default)
    {
        invoice.CreatedAt = DateTime.UtcNow;
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(ct);
        return invoice;
    }

    public async Task<Invoice> UpdateAsync(Invoice invoice, CancellationToken ct = default)
    {
        invoice.UpdatedAt = DateTime.UtcNow;
        _db.Invoices.Update(invoice);
        await _db.SaveChangesAsync(ct);
        return invoice;
    }

    public async Task AddPaymentRecordAsync(
        InvoicePaymentRecord record, CancellationToken ct = default)
    {
        _db.InvoicePayments.Add(record);
        await _db.SaveChangesAsync(ct);
    }

    // The invoice sequence comes from a stored procedure, not EF Core.
    // See src/NovaTechCRM.Infrastructure/Database/StoredProcedures/usp_GetNextInvoiceSequence.sql
    public async Task<int> GetNextSequenceAsync(int year, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(
            "EXEC dbo.usp_GetNextInvoiceSequence @Year", conn);
        cmd.Parameters.AddWithValue("@Year", year);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }
}
