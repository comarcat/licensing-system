using LicensingCore.Crypto;
using LicensingCore.Entities;
using LicensingCore.Licensing;

namespace LicensingAdmin.Licensing;

/// <summary>
/// Issues a signed <see cref="License"/> from a <see cref="LicenseIssuanceRequest"/>:
/// resolves (or builds) the product, mints a unique key, signs the canonical fields and
/// hands the product/license/audit tuple to the <see cref="ILicenseStore"/> for one
/// atomic write.
/// </summary>
public sealed class LicenseIssuanceService(ILicenseSigner signer, ILicenseStore store)
{
    /// <summary>Upper bound on key-minting retries; a full store would otherwise spin forever.</summary>
    private const int MaxKeyMintAttempts = 8;

    /// <summary>
    /// Produces and persists a new license.
    /// </summary>
    /// <param name="request">
    /// Issuance parameters. <see cref="LicenseIssuanceRequest.IssuedBy"/> is recorded as the
    /// audit actor. When <see cref="LicenseIssuanceRequest.ExistingProductId"/> is <c>null</c>
    /// the <c>NewProduct*</c> fields are used to build a fresh product.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The persisted <see cref="License"/> with a populated <see cref="License.LicenseKey"/>
    /// and <see cref="License.Signature"/>.
    /// </returns>
    public async Task<License> IssueAsync(LicenseIssuanceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The service is the issuance invariant boundary — nothing builds a License by hand.
        // An empty actor breaks audit non-repudiation (CurrentAdmin.Email returns "" for an
        // anonymous principal), so it is refused here, not just at the form.
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IssuedBy);

        // 1. Resolve the product: reuse an existing one, or build a new one to persist.
        SoftwareProduct? newProduct;
        Guid productId;
        if (request.ExistingProductId is { } pid)
        {
            newProduct = null;
            productId = pid;
        }
        else
        {
            // NewProduct* are mandatory when no existing product is chosen; catch the
            // misuse here with a clear message instead of a NOT NULL violation at SaveChanges.
            ArgumentException.ThrowIfNullOrWhiteSpace(request.NewProductName, nameof(request.NewProductName));
            ArgumentException.ThrowIfNullOrWhiteSpace(request.NewProductVendor, nameof(request.NewProductVendor));
            newProduct = new SoftwareProduct
            {
                Id = Guid.NewGuid(),
                Name = request.NewProductName,
                Vendor = request.NewProductVendor,
                CurrentVersion = request.NewProductVersion,
                DefaultLicenseModel = request.Model,
                DefaultMaxActivations = request.MaxActivations,
            };
            productId = newProduct.Id;
        }

        // 2. Mint a key, retrying until the store confirms it is free. A full store (or a
        //    broken uniqueness query) must fail loudly, not spin forever.
        string key;
        var attempt = 0;
        do
        {
            if (++attempt > MaxKeyMintAttempts)
            {
                throw new InvalidOperationException(
                    $"Could not mint a unique license key after {MaxKeyMintAttempts} attempts.");
            }

            key = LicenseKeyGenerator.NewKey();
        }
        while (await store.LicenseKeyExistsAsync(key, ct));

        // 3. Build the license and sign its canonical fields (Signature is not part of them).
        var license = new License
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            LicenseKey = key,
            ModelSnapshot = request.Model,
            MaxActivations = request.MaxActivations,
            SubscriptionExpiryUtc = request.SubscriptionExpiryUtc,
            CustomerEmail = request.CustomerEmail,
            CustomerName = request.CustomerName,
            Signature = Array.Empty<byte>(),
        };
        license.Signature = signer.Sign(license);

        // 4. Audit trail for the issuance.
        var audit = new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            Actor = request.IssuedBy,
            EntityType = "License",
            EntityId = license.Id.ToString(),
            Action = "Created",
        };

        // 5. One atomic write of product (optional) + license + audit.
        await store.AddAsync(newProduct, license, audit, ct);

        // 6. Hand the caller the signed, persisted license.
        return license;
    }
}
