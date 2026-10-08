namespace LicensingCore.Entities;

/// <summary>
/// A specific version/edition (e.g., Free, Premium, Enterprise) of a SoftwareProduct.
/// </summary>
public class ProductVersion
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }
    public SoftwareProduct Product { get; set; } = null!;

    public required string Name { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
