namespace LicensingCore.Entities;

/// <summary>
/// A software product that can be licensed (called "Program" in the design doc;
/// named SoftwareProduct here to avoid colliding with the .NET entry-point type).
/// </summary>
public class SoftwareProduct
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Vendor { get; set; }

    public string? CurrentVersion { get; set; }

    /// <summary>Default license model(s) offered for this product; combinable flags.</summary>
    public LicenseModel DefaultLicenseModel { get; set; } = LicenseModel.Machine;

    public int DefaultMaxActivations { get; set; } = 5;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<License> Licenses { get; set; } = new List<License>();
}
