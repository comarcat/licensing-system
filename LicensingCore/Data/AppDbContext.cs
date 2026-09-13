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
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Vendor).HasColumnName("vendor").HasMaxLength(200).IsRequired();
            e.Property(x => x.CurrentVersion).HasColumnName("current_version").HasMaxLength(50);
            e.Property(x => x.DefaultLicenseModel).HasColumnName("default_license_model").HasConversion<int>();
            e.Property(x => x.DefaultMaxActivations).HasColumnName("default_max_activations");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            e.HasIndex(x => x.Name);
        });

        // ---------------- License ----------------
        modelBuilder.Entity<License>(e =>
        {
            e.ToTable("licenses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.LicenseKey).HasColumnName("license_key").HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.LicenseKey).IsUnique();
            e.Property(x => x.ModelSnapshot).HasColumnName("model_snapshot").HasConversion<int>();
            e.Property(x => x.MaxActivations).HasColumnName("max_activations");
            e.Property(x => x.SubscriptionExpiryUtc).HasColumnName("subscription_expiry_utc");
            e.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Signature).HasColumnName("signature").IsRequired();
            e.Property(x => x.CustomerEmail).HasColumnName("customer_email").HasMaxLength(320);
            e.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(200);
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");
            e.Property(x => x.RevokedReason).HasColumnName("revoked_reason");
            e.Property(x => x.IsArchived).HasColumnName("is_archived");

            e.HasOne(x => x.Product)
                .WithMany(p => p.Licenses)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.ProductId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SubscriptionExpiryUtc);
            e.HasIndex(x => x.IsArchived);
        });

        // ---------------- Activation ----------------
        modelBuilder.Entity<Activation>(e =>
        {
            e.ToTable("activations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.LicenseId).HasColumnName("license_id");
            e.Property(x => x.InstallGuid).HasColumnName("install_guid");
            e.Property(x => x.CpuId).HasColumnName("cpu_id").HasMaxLength(200).IsRequired();
            e.Property(x => x.MotherboardSerial).HasColumnName("motherboard_serial").HasMaxLength(200).IsRequired();
            e.Property(x => x.TpmId).HasColumnName("tpm_id").HasMaxLength(200).IsRequired();
            e.Property(x => x.MacAddressPrimary).HasColumnName("mac_address_primary").HasMaxLength(32).IsRequired();
            e.Property(x => x.OsType).HasColumnName("os_type").HasMaxLength(100);
            e.Property(x => x.OsVersion).HasColumnName("os_version").HasMaxLength(50);
            e.Property(x => x.CpuModel).HasColumnName("cpu_model").HasMaxLength(200);
            e.Property(x => x.RamGb).HasColumnName("ram_gb");
            e.Property(x => x.IsVm).HasColumnName("is_vm");
            e.Property(x => x.VmSignals).HasColumnName("vm_signals").HasMaxLength(500);
            e.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FirstActivatedAtUtc).HasColumnName("first_activated_at_utc");
            e.Property(x => x.LastCheckinAtUtc).HasColumnName("last_checkin_at_utc");
            e.Property(x => x.ReviewDeadlineUtc).HasColumnName("review_deadline_utc");
            e.Property(x => x.ApprovedAtUtc).HasColumnName("approved_at_utc");
            e.Property(x => x.RejectedAtUtc).HasColumnName("rejected_at_utc");
            e.Property(x => x.ReviewedBy).HasColumnName("reviewed_by").HasMaxLength(320);
            e.Property(x => x.ReviewNotes).HasColumnName("review_notes");

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
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.Property(x => x.LastLoginAtUtc).HasColumnName("last_login_at_utc");
        });

        // ---------------- AuditLogEntry ----------------
        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_log_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Actor).HasColumnName("actor").HasMaxLength(320).IsRequired();
            e.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(100).IsRequired();
            e.Property(x => x.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
            e.Property(x => x.DetailsJson).HasColumnName("details_json").HasColumnType("jsonb");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.CreatedAtUtc);
        });

        // ---------------- NotificationConfig ----------------
        modelBuilder.Entity<NotificationConfig>(e =>
        {
            e.ToTable("notification_configs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SmtpHost).HasColumnName("smtp_host").HasMaxLength(255).IsRequired();
            e.Property(x => x.SmtpPort).HasColumnName("smtp_port");
            e.Property(x => x.Encryption).HasColumnName("encryption").HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AuthType).HasColumnName("auth_type").HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(320).IsRequired();
            e.Property(x => x.PasswordEncrypted).HasColumnName("password_encrypted").IsRequired();
            e.Property(x => x.FromAddress).HasColumnName("from_address").HasMaxLength(320).IsRequired();
            e.Property(x => x.LastTestStatus).HasColumnName("last_test_status").HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LastTestAtUtc).HasColumnName("last_test_at_utc");
            e.Property(x => x.EventTogglesJson).HasColumnName("event_toggles_json").HasColumnType("jsonb");
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });
    }
}
