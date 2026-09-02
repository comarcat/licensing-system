namespace LicensingCore.Entities;

public class AdminUser
{
    public Guid Id { get; set; }

    public required string Email { get; set; }

    /// <summary>Password hash (e.g. ASP.NET Core Identity / BCrypt) — never plaintext.</summary>
    public required string PasswordHash { get; set; }

    public AdminRole Role { get; set; } = AdminRole.ReadOnlyViewer;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }
}
