using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Infrastructure.BackgroundJobs;

// Runs once a day; on the 1st of the month it bills the previous month.
// Same polling-loop style as InvoiceOverdueJob — no Hangfire/Quartz yet (NOVA-73).
public class MonthlyBillingJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MonthlyBillingJob> _logger;

    public MonthlyBillingJob(IServiceProvider services, ILogger<MonthlyBillingJob> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MonthlyBillingJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now          = DateTime.UtcNow;
            var nextMidnight = now.Date.AddDays(1);
            await Task.Delay(nextMidnight - now, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;
            if (DateTime.UtcNow.Day != 1) continue; // only bill on the 1st

            try
            {
                // One scope → one scoped NovaTechDbContext shared by the whole run.
                using var scope = _services.CreateScope();
                var billing = scope.ServiceProvider.GetRequiredService<IBillingService>();

                var lastMonth = DateTime.UtcNow.AddMonths(-1);
                var count = await billing.RunMonthlyBillingAsync(
                    lastMonth.Year, lastMonth.Month, stoppingToken);

                _logger.LogInformation("MonthlyBillingJob created {Count} invoices", count);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MonthlyBillingJob failed");
            }
        }
    }
}
