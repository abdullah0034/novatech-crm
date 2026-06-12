using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NovaTechCRM.Api.Middleware;
using NovaTechCRM.Infrastructure.BackgroundJobs;
using NovaTechCRM.Infrastructure.Cache;
using NovaTechCRM.Infrastructure.Email;
using NovaTechCRM.Infrastructure.Notifications;
using NovaTechCRM.Infrastructure.Payments;
using NovaTechCRM.Infrastructure.Pdf;
using NovaTechCRM.Infrastructure.Shipping;
using NovaTechCRM.Infrastructure.Sms;
using NovaTechCRM.Infrastructure.Storage;
using NovaTechCRM.Repositories;
using NovaTechCRM.Services;
using NovaTechCRM.Services.Interfaces;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var cfg     = builder.Configuration;

// ── MVC ─────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "NovaTech CRM API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new()
    {
        Name   = "Authorization",
        Type   = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
    });
    o.AddSecurityRequirement(new()
    {
        [new() { Reference = new() { Id = "Bearer", Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme } }] = Array.Empty<string>()
    });
});

builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ── Database ─────────────────────────────────────────────────────────────────
var connString = cfg.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection not configured.");

builder.Services.AddDbContext<NovaTechDbContext>(o =>
    o.UseSqlServer(connString, sql => sql.CommandTimeout(60)));

// ── Cache ────────────────────────────────────────────────────────────────────
var redisConn = cfg["Redis:ConnectionString"];
if (!string.IsNullOrEmpty(redisConn))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        ConnectionMultiplexer.Connect(redisConn));
    builder.Services.AddScoped<ICacheService, RedisCacheService>();
}
else
{
    // fall back to in-memory cache for local dev / single-instance staging
    builder.Services.AddMemoryCache();
    builder.Services.AddScoped<ICacheService, CacheService>();
}

// ── Repositories ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<ICustomerRepository>(sp =>
    new CustomerRepository(
        sp.GetRequiredService<NovaTechDbContext>(),
        connString));
builder.Services.AddScoped<IInvoiceRepository>(sp =>
    new InvoiceRepository(
        sp.GetRequiredService<NovaTechDbContext>(),
        connString));
builder.Services.AddScoped<IAuditRepository>(sp =>
    new AuditRepository(
        sp.GetRequiredService<NovaTechDbContext>(),
        connString));
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IShipmentRepository, ShipmentRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IReportRepository, ReportRepository>();
builder.Services.AddScoped<IDiscountRepository, DiscountRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<IFraudShieldService, FraudShieldService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IShipmentService, ShipmentService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IDiscountService, DiscountService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddScoped<SearchService>();

// ── Email ─────────────────────────────────────────────────────────────────────
builder.Services.Configure<SendGridOptions>(cfg.GetSection("SendGrid"));
builder.Services.Configure<SmtpOptions>(cfg.GetSection("Smtp"));

if (builder.Environment.IsProduction())
{
    builder.Services.AddHttpClient<IEmailSender, SendGridEmailSender>();
}
else
{
    // use SMTP (Papercut / Mailhog) in dev & staging
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
}

// ── SMS ───────────────────────────────────────────────────────────────────────
builder.Services.Configure<TwilioOptions>(cfg.GetSection("Twilio"));
builder.Services.AddHttpClient<ISmsSender, TwilioSmsSender>();

// ── Notifications ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<INotificationService, NotificationService>();

// ── PDF / Storage ─────────────────────────────────────────────────────────────
builder.Services.Configure<PdfOptions>(cfg.GetSection("Pdf"));
builder.Services.Configure<AzureBlobOptions>(cfg.GetSection("AzureBlob"));
builder.Services.AddHttpClient<IStorageService, AzureBlobStorageService>();
builder.Services.AddHttpClient<IPdfGeneratorService, GotenbergPdfGeneratorService>();

// ── Payment Providers ─────────────────────────────────────────────────────────
builder.Services.Configure<StripeOptions>(cfg.GetSection("Stripe"));
builder.Services.Configure<PayPalOptions>(cfg.GetSection("PayPal"));
builder.Services.Configure<BraintreeOptions>(cfg.GetSection("Braintree"));
builder.Services.AddHttpClient<StripePaymentProvider>();
builder.Services.AddHttpClient<PayPalPaymentProvider>();
builder.Services.AddHttpClient<BraintreePaymentProvider>();
builder.Services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();

// ── Shipping Providers ────────────────────────────────────────────────────────
builder.Services.Configure<FedExOptions>(cfg.GetSection("FedEx"));
builder.Services.Configure<UpsOptions>(cfg.GetSection("Ups"));
builder.Services.Configure<DhlOptions>(cfg.GetSection("Dhl"));
builder.Services.AddHttpClient<FedExShippingProvider>();
builder.Services.AddHttpClient<UpsShippingProvider>();
builder.Services.AddHttpClient<DhlShippingProvider>();

// ── Background Jobs ───────────────────────────────────────────────────────────
builder.Services.AddHostedService<AuditFlushJob>();
builder.Services.AddHostedService<InvoiceOverdueJob>();
builder.Services.AddHostedService<InventoryCleanupJob>();
builder.Services.AddHostedService<ReportSchedulerJob>();
builder.Services.AddHostedService<MonthlyBillingJob>();

// ── CORS ──────────────────────────────────────────────────────────────────────
// TODO: lock this down per environment — wildcard origin is fine for now (NOVA-80)
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<AuthMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// keep old legacy orders route working — redirect to new path
// TODO: remove once mobile app v2 ships (NOVA-81)
app.MapGet("/orders/{id}", (Guid id) =>
    Results.Redirect($"/api/orders/{id}", permanent: true));

app.Run();
