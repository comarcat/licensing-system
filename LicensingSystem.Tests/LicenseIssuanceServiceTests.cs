using System.Security.Cryptography;
using System.Text;
using LicensingAdmin.Licensing;
using LicensingAdmin.Notifications;
using LicensingAdmin.Products;
using LicensingCore.Crypto;
using LicensingCore.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
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
        Guid? versionId = null,
        string issuedBy = "admin@vendor.test") => new()
    {
        ExistingProductId = useExistingProduct ? Guid.NewGuid() : null,
        NewProductName = newProductName,
        NewProductVendor = newProductVendor,
        NewProductVersion = newProductVersion,
        // When useExistingProduct is true and caller didn't pass a versionId, auto-generate one.
        // Tests that want to test null/empty override with 'with { VersionId = ... }'.
        VersionId = versionId ?? (useExistingProduct ? Guid.NewGuid() : null),
        Model = model ?? (LicenseModel.Machine | LicenseModel.Subscription),
        MaxActivations = maxActivations,
        SubscriptionExpiryUtc =
            subscriptionExpiryUtc ?? new DateTime(2027, 6, 1, 12, 30, 45, DateTimeKind.Utc),
        IssuedBy = issuedBy,
    };

    private static readonly IProductStore FakeProductStore = new NoopProductStore();
    private static readonly IEmailSender FakeEmailSender = new NoopEmailSender();
    private static readonly INotificationConfigStore FakeConfigStore = new NoopConfigStore();
    private static readonly IDataProtectionProvider FakeDataProtection = new NoopDataProtectionProvider();
    private static readonly ILogger<LicenseIssuanceService> Logger = new NoopLogger<LicenseIssuanceService>();

    private LicenseIssuanceService NewService(FakeLicenseStore store) =>
        new(Signer(), store, FakeProductStore, FakeEmailSender, FakeConfigStore, FakeDataProtection, Logger);

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
    // Product version (v2.0) — mandatory for existing products, auto-created
    // for new products.
    // ---------------------------------------------------------------------

    /// <summary>
    /// When issuing against an existing product a non-null, non-empty VersionId
    /// is required; omitting it (null or <see cref="Guid.Empty"/>) is refused.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("empty")]
    public async Task IssueAsync_ExistingProductWithoutVersionId_Throws(string? caseName)
    {
        var store = new FakeLicenseStore();
        var request = NewRequest(useExistingProduct: true) with
        {
            VersionId = caseName == "empty" ? Guid.Empty : null,
        };

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => NewService(store).IssueAsync(request, Ct));
        Assert.Empty(store.Audits);
    }

    /// <summary>
    /// When issuing against an existing product the VersionId from the request
    /// is copied verbatim onto the license.
    /// </summary>
    [Fact]
    public async Task IssueAsync_ExistingProduct_CopiesVersionId()
    {
        var store = new FakeLicenseStore();
        var versionId = Guid.NewGuid();
        var request = NewRequest(useExistingProduct: true, versionId: versionId);

        var result = await NewService(store).IssueAsync(request, Ct);

        Assert.Equal(versionId, result.VersionId);
    }

    /// <summary>
    /// When creating a new product a default <see cref="ProductVersion"/> is
    /// built and forwarded to the store alongside the new product.
    /// </summary>
    [Fact]
    public async Task IssueAsync_NewProduct_CreatesDefaultVersion()
    {
        var store = new FakeLicenseStore();
        var request = NewRequest(
            useExistingProduct: false,
            newProductName: "Test",
            newProductVendor: "Test Inc");

        var result = await NewService(store).IssueAsync(request, Ct);

        Assert.NotNull(store.AddedVersion);
        Assert.Equal("Default", store.AddedVersion!.Name);
        Assert.Equal(result.ProductId, store.AddedVersion.ProductId);
        Assert.Equal(result.VersionId, store.AddedVersion.Id);
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
    /// When issuing an existing product, the license Status defaults to Active.
    /// </summary>
    [Fact]
    public async Task IssueAsync_DefaultStatusIsActive()
    {
        var store = new FakeLicenseStore();
        var result = await NewService(store).IssueAsync(NewRequest(), Ct);

        Assert.Equal(LicenseStatus.Active, result.Status);
        Assert.Equal(LicenseStatus.Active, store.AddedLicense!.Status);
    }

    /// <summary>
    /// When Test status is requested, the license stores Test.
    /// </summary>
    [Fact]
    public async Task IssueAsync_TestStatusPropagated()
    {
        var store = new FakeLicenseStore();
        var request = NewRequest() with { Status = LicenseStatus.Test };

        var result = await NewService(store).IssueAsync(request, Ct);

        Assert.Equal(LicenseStatus.Test, result.Status);
        Assert.Equal(LicenseStatus.Test, store.AddedLicense!.Status);
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

        public Task AddAsync(SoftwareProduct? newProduct, ProductVersion? newVersion, License license, AuditLogEntry audit, CancellationToken ct = default)
        {
            AddedProduct = newProduct;
            AddedVersion = newVersion;
            AddedLicense = license;
            Audits.Add(audit);
            return Task.CompletedTask;
        }

        public Task AddAsync(SoftwareProduct product, AuditLogEntry auditProduct, IReadOnlyList<ProductVersion> versions, IReadOnlyList<AuditLogEntry> auditVersions, CancellationToken ct = default)
        {
            return AddAsync(product, versions.FirstOrDefault(), new License { LicenseKey = "", Signature = Array.Empty<byte>() }, auditProduct, ct);
        }

        public ProductVersion? AddedVersion { get; private set; }
    }

    private sealed class NoopProductStore : IProductStore
    {
        public Task<IReadOnlyList<SoftwareProduct>> ListAsync(bool includeArchived = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SoftwareProduct>>(Array.Empty<SoftwareProduct>());

        public Task<SoftwareProduct?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SoftwareProduct?>(null);

        public Task AddAsync(SoftwareProduct product, AuditLogEntry auditProduct, IReadOnlyList<ProductVersion> versions, IReadOnlyList<AuditLogEntry> auditVersions, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task AddAsync(SoftwareProduct product, AuditLogEntry auditProduct, ProductVersion version, AuditLogEntry auditVersion, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(SoftwareProduct product, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<int> CountLicensesAsync(Guid productId, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task ArchiveAsync(Guid productId, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid productId, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ProductVersion>> ListVersionsAsync(Guid productId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProductVersion>>(Array.Empty<ProductVersion>());

        public Task AddVersionAsync(ProductVersion version, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpdateVersionAsync(ProductVersion version, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteVersionAsync(Guid versionId, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(NotificationConfig config, string? plainPassword, string toAddress, string subject, string body, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopConfigStore : INotificationConfigStore
    {
        public Task<NotificationConfig?> GetAsync(CancellationToken ct = default) =>
            Task.FromResult<NotificationConfig?>(null);

        public Task SaveAsync(NotificationConfig config, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new NoopDataProtector();
    }

    private sealed class NoopDataProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }

    private sealed class NoopLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
