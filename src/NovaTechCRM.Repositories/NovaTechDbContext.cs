using Microsoft.EntityFrameworkCore;
using NovaTechCRM.Domain.Models;

namespace NovaTechCRM.Repositories;

// Grown organically over 3 years — started with just Orders, now covers most of the domain.
// WARNING: prod DB is still on migration v18, dev is on v24. Do NOT run dotnet ef migrations add
//          without checking with the infra team first. Eliav handles all prod migrations manually.
// TODO: split into smaller bounded-context DbContexts (NOVA-55)
public class NovaTechDbContext : DbContext
{
    public NovaTechDbContext(DbContextOptions<NovaTechDbContext> options) : base(options) { }

    public DbSet<Customer>             Customers             => Set<Customer>();
    public DbSet<Order>                Orders                => Set<Order>();
    public DbSet<OrderItem>            OrderItems            => Set<OrderItem>();
    public DbSet<Invoice>              Invoices              => Set<Invoice>();
    public DbSet<InvoiceLineItem>      InvoiceLineItems      => Set<InvoiceLineItem>();
    public DbSet<InvoicePaymentRecord> InvoicePayments       => Set<InvoicePaymentRecord>();
    public DbSet<Payment>              Payments              => Set<Payment>();
    public DbSet<PaymentRefund>        PaymentRefunds        => Set<PaymentRefund>();
    public DbSet<PaymentMethod>        PaymentMethods        => Set<PaymentMethod>();
    public DbSet<Product>              Products              => Set<Product>();
    public DbSet<ProductVariant>       ProductVariants       => Set<ProductVariant>();
    public DbSet<Inventory>            Inventory             => Set<Inventory>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<Shipment>             Shipments             => Set<Shipment>();
    public DbSet<ShipmentEvent>        ShipmentEvents        => Set<ShipmentEvent>();
    public DbSet<Discount>             Discounts             => Set<Discount>();
    public DbSet<Notification>         Notifications         => Set<Notification>();
    public DbSet<Report>               Reports               => Set<Report>();
    public DbSet<ReportSchedule>       ReportSchedules       => Set<ReportSchedule>();
    public DbSet<AuditLog>             AuditLogs             => Set<AuditLog>();
    public DbSet<Address>              Addresses             => Set<Address>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Customer>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.Property(c => c.Email).HasMaxLength(200).IsRequired();
            e.HasIndex(c => c.Email).IsUnique();
            // TODO: add index on (Status, Tier) for dashboard queries (NOVA-56)
        });

        mb.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.TotalAmount).HasPrecision(18, 2);
            e.HasMany(o => o.Items)
             .WithOne()
             .HasForeignKey("OrderId")
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<OrderItem>(e =>
        {
            e.Property(i => i.UnitPrice).HasPrecision(18, 2);
        });

        mb.Entity<Invoice>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.InvoiceNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(i => i.InvoiceNumber).IsUnique();
            e.HasIndex(i => new { i.CustomerId, i.Status });
            e.HasIndex(i => i.DueAt);
            e.Property(i => i.SubTotal).HasPrecision(18, 2);
            e.Property(i => i.TaxAmount).HasPrecision(18, 2);
            e.Property(i => i.TotalAmount).HasPrecision(18, 2);
            e.Property(i => i.AmountPaid).HasPrecision(18, 2);
            e.HasMany(i => i.LineItems)
             .WithOne()
             .HasForeignKey(li => li.InvoiceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<InvoiceLineItem>(e =>
        {
            e.Property(li => li.UnitPrice).HasPrecision(18, 2);
        });

        mb.Entity<InvoicePaymentRecord>(e =>
        {
            e.Property(r => r.AmountApplied).HasPrecision(18, 2);
        });

        mb.Entity<Payment>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Amount).HasPrecision(18, 2);
            e.Property(p => p.RefundedAmount).HasPrecision(18, 2);
            e.Property(p => p.Currency).HasMaxLength(3);
            e.HasIndex(p => new { p.CustomerId, p.Status });
            e.HasMany<PaymentRefund>()
             .WithOne()
             .HasForeignKey(r => r.PaymentId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<PaymentRefund>(e =>
        {
            e.Property(r => r.Amount).HasPrecision(18, 2);
        });

        mb.Entity<PaymentMethod>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.CustomerId, m.IsDefault });
        });

        mb.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Sku).HasMaxLength(100).IsRequired();
            e.HasIndex(p => p.Sku).IsUnique();
            e.Property(p => p.BasePrice).HasPrecision(18, 2);
            e.Property(p => p.CostPrice).HasPrecision(18, 2);
            e.HasMany(p => p.Variants)
             .WithOne()
             .HasForeignKey(v => v.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Inventory>(e =>
        {
            e.HasKey(i => i.Id);
            // composite unique so we can't double-reserve the same product+variant+warehouse
            e.HasIndex(i => new { i.ProductId, i.VariantId, i.WarehouseId }).IsUnique();
        });

        mb.Entity<InventoryReservation>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.OrderId);
            e.HasIndex(r => r.ExpiresAt);
        });

        mb.Entity<Shipment>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.OrderId);
            e.HasIndex(s => s.TrackingNumber);
            e.HasMany(s => s.Events)
             .WithOne()
             .HasForeignKey(ev => ev.ShipmentId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Discount>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Value).HasPrecision(18, 2);
            e.Property(d => d.MinimumOrderAmount).HasPrecision(18, 2);
            e.Property(d => d.MaxDiscountAmount).HasPrecision(18, 2);
            // filtered index so NULL codes (tier-based discounts) don't conflict
            e.HasIndex(d => d.Code).IsUnique().HasFilter("[Code] IS NOT NULL");
        });

        mb.Entity<Notification>(e =>
        {
            e.HasKey(n => n.Id);
            e.HasIndex(n => new { n.CustomerId, n.Status });
        });

        mb.Entity<Report>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => new { r.RequestedByUserId, r.RequestedAt });
        });

        mb.Entity<ReportSchedule>(e =>
        {
            e.HasKey(rs => rs.Id);
            e.HasIndex(rs => rs.NextRunAt);
        });

        mb.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            e.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
            e.HasIndex(a => new { a.EntityType, a.EntityId });
            e.HasIndex(a => new { a.UserId, a.OccurredAt });
            // PartitionMonth used for table partitioning in prod (SQL Server range partition)
            e.HasIndex(a => a.PartitionMonth);
        });

        mb.Entity<Address>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.CustomerId);
        });
    }
}
