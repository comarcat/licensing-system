using LicensingCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicensingCore.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SoftwareProduct> SoftwareProducts => Set<SoftwareProduct>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<Activation> Activations => Set<Activation>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<NotificationConfig> NotificationConfigs => Set<NotificationConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ---------------- SoftwareProduct ----------------
        modelBuilder.Entity<SoftwareProduct>(e =>
        {
            e.ToTable("software_products");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Vendor).HasMaxLength(200).IsRequired();
            e.Property(x => x.CurrentVersion).HasMaxLength(50);
            e.Property(x => x.DefaultLicenseModel).HasConversion<int>();
            e.HasIndex(x => x.Name);
        });

        // ---------------- License ----------------
        modelBuilder.Entity<License>(e =>
        {
            e.ToTable("licenses");
            e.HasKey(x => x.Id);
            e.Property(x => x.LicenseKey).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.LicenseKey).IsUnique();
            e.Property(x => x.ModelSnapshot).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CustomerEmail).HasMaxLength(320);
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.Signature).IsRequired();

            e.HasOne(x => x.Product)
                .WithMany(p => p.Licenses)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.ProductId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SubscriptionExpiryUtc);
        });

        // ---------------- Activation ----------------
        modelBuilder.Entity<Activation>(e =>
        {
            e.ToTable("activations");
            e.HasKey(x => x.Id);
            e.Property(x => x.CpuId).HasMaxLength(200).IsRequired();
            e.Property(x => x.MotherboardSerial).HasMaxLength(200).IsRequired();
            e.Property(x => x.TpmId).HasMaxLength(200).IsRequired();
            e.Property(x => x.MacAddressPrimary).HasMaxLength(32).IsRequired();
            e.Property(x => x.OsType).HasMaxLength(100);
            e.Property(x => x.OsVersion).HasMaxLength(50);
            e.Property(x => x.CpuModel).HasMaxLength(200);
            e.Property(x => x.VmSignals).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ReviewedBy).HasMaxLength(320);

            e.HasOne(x => x.License)
                .WithMany(l => l.Activations)
                .HasForeignKey(x => x.LicenseId)
                .OnDelete(DeleteBehavior.Cascade);

            // A given install (by GUID) should map to one activation row per license.
            e.HasIndex(x => new { x.LicenseId, x.InstallGuid }).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ReviewDeadlineUtc);
            // Same-machine lookups filter by these four fields together often.
            e.HasIndex(x => new { x.LicenseId, x.CpuId, x.MotherboardSerial, x.TpmId, x.MacAddressPrimary });
        });

        // ---------------- AdminUser ----------------
        modelBuilder.Entity<AdminUser>(e =>
        {
            e.ToTable("admin_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        });

        // ---------------- AuditLogEntry ----------------
        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_log_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Actor).HasMaxLength(320).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.DetailsJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.CreatedAtUtc);
        });

        // ---------------- NotificationConfig ----------------
        modelBuilder.Entity<NotificationConfig>(e =>
        {
            e.ToTable("notification_configs");
            e.HasKey(x => x.Id);
            e.Property(x => x.SmtpHost).HasMaxLength(255).IsRequired();
            e.Property(x => x.Encryption).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AuthType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Username).HasMaxLength(320).IsRequired();
            e.Property(x => x.PasswordEncrypted).IsRequired();
            e.Property(x => x.FromAddress).HasMaxLength(320).IsRequired();
            e.Property(x => x.LastTestStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EventTogglesJson).HasColumnType("jsonb");
        });
    }
}
