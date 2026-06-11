namespace NovaTechCRM.Services.Interfaces;

public interface IBillingService
{
    // Generates draft invoices for every active customer's fulfilled, un-billed
    // orders in the given billing period. Returns the number of invoices created.
    Task<int> RunMonthlyBillingAsync(int year, int month, CancellationToken ct = default);
}
