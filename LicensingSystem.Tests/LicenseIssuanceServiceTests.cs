using System.Security.Cryptography;
using System.Text;
using LicensingAdmin.Licensing;
using LicensingCore.Crypto;
using LicensingCore.Entities;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Deterministic unit tests for <see cref="LicenseIssuanceService"/> — no database, no host.
/// The <see cref="ILicenseStore"/> is faked; the RSA signer is real so the produced
/// signature is verified end to end.
/// </summary>
public class LicenseIssuanceServiceTests
{
    private const string KeyFormat =
        @"^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$";

    private readonly RSA _rsa = RSA.Create(2048);

    /// <summary>Per-test cancellation token (xUnit v3 idiom; keeps analyzer xUnit1051 quiet).</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LicenseSigner Signer() => new(_rsa);

    /// <summary>Public-only RSA instance derived from the shared signing key.</summary>
    private RSA PublicKey()
    {
        var pub = RSA.Create();
        pub.ImportParameters(_rsa.ExportParameters(includePrivateParameters: false));
        return pub;
    }

    private static LicenseIssuanceRequest NewRequest(
        bool useExistingProduct = true,
        string? newProductName = null,
        string? newProductVendor = null,
        string? newProductVersion = null,
        LicenseModel? model = null,
        int maxActivations = 7,
        DateTime? subscriptionExpiryUtc = null,
        string issuedBy = "admin@vendor.test") => new()
    {
        ExistingProductId = useExistingProduct ? Guid.NewGuid() : null,
        NewProductName = newProductName,
        NewProductVendor = newProductVendor,
        NewProductVersion = newProductVersion,
        Model = model ?? (LicenseModel.Machine | LicenseModel.Subscription),
        MaxActivations = maxActivations,
        SubscriptionExpiryUtc =
            subscriptionExpiryUtc ?? new DateTime(2027, 6, 1, 12, 30, 45, DateTimeKind.Utc),
        IssuedBy = issuedBy,
    };

    private LicenseIssuanceService NewService(FakeLicenseStore store) => new(Signer(), store);

    // ---------------------------------------------------------------------
    // Acceptance criterion 1 — LicenseKey matches the canonical format.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task IssueAsync_ProducesLicenseKeyMatchingFormat()
    {
        var store = new FakeLicenseStore();

        var result = await NewService(store).IssueAsync(NewRequest(), Ct);

        Assert.Matches(KeyFormat, result.LicenseKey);
    }

    // ---------------------------------------------------------------------
    // Acceptance criterion 2 — Signature verifies with the issuing public key.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task IssueAsync_SignatureVerifiesWithIssuingPublicKey()
    {
        var store = new FakeLicenseStore();

        var result = await NewService(store).IssueAsync(NewRequest(), Ct);

        Assert.True(Signer().Verify(result, result.Signature, PublicKey()));
    }

    // ---------------------------------------------------------------------
    // Acceptance criterion 3 — retry key generation until the store says it is free.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task IssueAsync_RetriesUntilKeyIsFree()
    {
        var store = new FakeLicenseStore();
        store.ExistsResults.Enqueue(true);
        store.ExistsResults.Enqueue(true);
        store.ExistsResults.Enqueue(false);

        var result = await NewService(store).IssueAsync(NewRequest(), Ct);

        Assert.Equal(3, store.KeysChecked.Count);
        Assert.Equal(store.KeysChecked[^1], result.LicenseKey);
        Assert.Equal(3, store.KeysChecked.Distinct().Count());
    }

    // ---------------------------------------------------------------------
    // Acceptance criterion 4 — model flags and MaxActivations copied verbatim.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task IssueAsync_CopiesModelFlagsAndMaxActivations()
    {
        var store = new FakeLicenseStore();
        var request = NewRequest(
            model: LicenseModel.Machine | LicenseModel.User | LicenseModel.Floating,
            maxActivations: 42);

        var result = await NewService(store).IssueAsync(request, Ct);

        Assert.Equal(request.Model, result.ModelSnapshot);
        Assert.Equal(42, result.MaxActivations);
    }

    // ---------------------------------------------------------------------
    // Acceptance criterion 5 — exactly one "Created" / "License" audit entry.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task IssueAsync_WritesExactlyOneCreatedLicenseAuditEntry()
    {
        var store = new FakeLicenseStore();

        var result = await NewService(store).IssueAsync(NewRequest(), Ct);

        Assert.Single(store.Audits);
        Assert.Equal("Created", store.Audits[0].Action);
        Assert.Equal("License", store.Audits[0].EntityType);
        Assert.Equal(result.Id.ToString(), store.Audits[0].EntityId);
        Assert.Equal("admin@vendor.test", store.Audits[0].Actor);
    }

    // ---------------------------------------------------------------------
    // Product resolution — existing product path passes null new product.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task IssueAsync_ExistingProduct_PassesNullNewProductToStore()
    {
        var store = new FakeLicenseStore();
        var request = NewRequest(useExistingProduct: true);

        var result = await NewService(store).IssueAsync(request, Ct);

        Assert.Null(store.AddedProduct);
        Assert.Equal(request.ExistingProductId, result.ProductId);
    }

    // ---------------------------------------------------------------------
    // Product resolution — new product path builds and forwards the product.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task IssueAsync_NewProduct_BuildsAndPassesProduct()
    {
        var store = new FakeLicenseStore();
        var request = NewRequest(
            useExistingProduct: false,
            newProductName: "Acme",
            newProductVendor: "Acme Inc");

        var result = await NewService(store).IssueAsync(request, Ct);

        Assert.NotNull(store.AddedProduct);
        Assert.Equal("Acme", store.AddedProduct!.Name);
        Assert.Equal(store.AddedProduct.Id, result.ProductId);
    }

    // ---------------------------------------------------------------------
    // Gap coverage (E2-T5 review) — snapshot completeness, degenerate inputs,
    // no-retry path and object identity handed to the store.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Criterion 4 only names Model + MaxActivations, but the service also snapshots the
    /// subscription expiry and the customer fields; assert those are copied verbatim and
    /// the signature still verifies.
    /// </summary>
    [Fact]
    public async Task IssueAsync_CopiesSubscriptionExpiryAndCustomerFields()
    {
        var store = new FakeLicenseStore();
        var expiry = new DateTime(2029, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        var request = NewRequest(subscriptionExpiryUtc: expiry) with
        {
            CustomerEmail = "buyer@customer.test",
            CustomerName = "Buyer Ltd",
        };

        var result = await NewService(store).IssueAsync(request, Ct);

        Assert.Equal(expiry, result.SubscriptionExpiryUtc);
        Assert.Equal("buyer@customer.test", result.CustomerEmail);
        Assert.Equal("Buyer Ltd", result.CustomerName);
        Assert.True(Signer().Verify(result, result.Signature, PublicKey()));
    }

    /// <summary>
    /// <see cref="LicenseModel.None"/> (0) is a valid snapshot: it is copied through and the
    /// canonical string carries <c>|0|</c> for the model segment, so the signature verifies.
    /// </summary>
    [Fact]
    public async Task IssueAsync_ModelNone_SnapshotIsNoneAndSignatureVerifies()
    {
        var store = new FakeLicenseStore();
        var request = NewRequest(model: LicenseModel.None);

        var result = await NewService(store).IssueAsync(request, Ct);

        Assert.Equal(LicenseModel.None, result.ModelSnapshot);
        Assert.True(Signer().Verify(result, result.Signature, PublicKey()));
    }

    /// <summary>
    /// A non-subscription license (<see cref="License.SubscriptionExpiryUtc"/> null) signs an
    /// empty expiry segment — the canonical bytes end with the trailing separator — and the
    /// signature verifies.
    /// </summary>
    [Fact]
    public async Task IssueAsync_NullSubscriptionExpiry_SignatureVerifiesAndCanonicalEndsWithSeparator()
    {
        var store = new FakeLicenseStore();
        var request = NewRequest(model: LicenseModel.Machine) with { SubscriptionExpiryUtc = null };

        var result = await NewService(store).IssueAsync(request, Ct);

        Assert.Null(result.SubscriptionExpiryUtc);
        Assert.EndsWith("|", Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(result)));
        Assert.True(Signer().Verify(result, result.Signature, PublicKey()));
    }

    /// <summary>
    /// When the very first minted key is free the store is probed exactly once and that key
    /// is the one returned (no retry loop).
    /// </summary>
    [Fact]
    public async Task IssueAsync_FirstKeyFree_NoRetryAndReturnsThatKey()
    {
        var store = new FakeLicenseStore();

        var result = await NewService(store).IssueAsync(NewRequest(), Ct);

        Assert.Single(store.KeysChecked);
        Assert.Equal(store.KeysChecked[0], result.LicenseKey);
    }

    /// <summary>
    /// The instance persisted through <see cref="ILicenseStore.AddAsync"/> is the same
    /// object the caller receives (no copy / re-projection), carrying the resolved product id.
    /// </summary>
    [Fact]
    public async Task IssueAsync_PassesSameLicenseInstanceToStoreAsItReturns()
    {
        var store = new FakeLicenseStore();
        var request = NewRequest(useExistingProduct: true);

        var result = await NewService(store).IssueAsync(request, Ct);

        Assert.Same(result, store.AddedLicense);
        Assert.Equal(request.ExistingProductId, store.AddedLicense!.ProductId);
    }

    /// <summary>
    /// Documents current behaviour: the service performs no range check on
    /// <see cref="LicenseIssuanceRequest.MaxActivations"/>; a non-positive value is snapshot
    /// verbatim and still produces a valid signature. If issuance should reject it, that is a
    /// code change, not a test fix.
    /// </summary>
    [Fact]
    public async Task IssueAsync_NonPositiveMaxActivations_CopiedVerbatimWithoutValidation()
    {
        var store = new FakeLicenseStore();
        var request = NewRequest(maxActivations: -3);

        var result = await NewService(store).IssueAsync(request, Ct);

        Assert.Equal(-3, result.MaxActivations);
        Assert.True(Signer().Verify(result, result.Signature, PublicKey()));
    }

    // ---------------------------------------------------------------------
    // Service-boundary guards (security-auditor M-1 / B-1 / B-3).
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IssueAsync_BlankIssuedBy_Throws(string? issuedBy)
    {
        var store = new FakeLicenseStore();
        var request = NewRequest() with { IssuedBy = issuedBy! };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => NewService(store).IssueAsync(request, Ct));
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task IssueAsync_NewProductWithoutNameOrVendor_Throws()
    {
        var store = new FakeLicenseStore();
        var request = NewRequest(useExistingProduct: false); // NewProductName/Vendor left null

        await Assert.ThrowsAnyAsync<ArgumentException>(() => NewService(store).IssueAsync(request, Ct));
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task IssueAsync_StoreAlwaysReportsKeyExists_ThrowsAfterCappedRetries()
    {
        var store = new FakeLicenseStore { AlwaysExists = true };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewService(store).IssueAsync(NewRequest(), Ct));

        Assert.Contains("unique license key", ex.Message);
        Assert.Empty(store.Audits);
    }

    /// <summary>
    /// In-memory <see cref="ILicenseStore"/>: <see cref="LicenseKeyExistsAsync"/> replays
    /// <see cref="ExistsResults"/> (defaulting to <c>false</c> once drained) and records every
    /// probed key; <see cref="AddAsync"/> captures its arguments for assertions.
    /// </summary>
    private sealed class FakeLicenseStore : ILicenseStore
    {
        public Queue<bool> ExistsResults { get; } = new();

        /// <summary>When true, every key is reported as already taken (drives the retry cap).</summary>
        public bool AlwaysExists { get; init; }

        public List<string> KeysChecked { get; } = new();

        public SoftwareProduct? AddedProduct { get; private set; }

        public License? AddedLicense { get; private set; }

        public List<AuditLogEntry> Audits { get; } = new();

        public Task<bool> LicenseKeyExistsAsync(string licenseKey, CancellationToken ct = default)
        {
            KeysChecked.Add(licenseKey);
            var exists = AlwaysExists || (ExistsResults.Count > 0 && ExistsResults.Dequeue());
            return Task.FromResult(exists);
        }

        public Task AddAsync(SoftwareProduct? newProduct, License license, AuditLogEntry audit, CancellationToken ct = default)
        {
            AddedProduct = newProduct;
            AddedLicense = license;
            Audits.Add(audit);
            return Task.CompletedTask;
        }
    }
}
