using System.Security.Cryptography;
using LicensingAdmin.Auth;
using LicensingCore.Crypto;
using LicensingCore.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Covers <see cref="CryptoRegistration.AddLicenseSigner"/> after the E1-T6 CAMBIOS:
/// the PEM-key path is validated <b>eagerly, at registration time</b>; the in-memory
/// fallback is Development-only and logs exactly one warning; outside Development a
/// missing PEM fails closed. All deterministic — no database, no host startup.
/// </summary>
/// <remarks>
/// The class / method names deliberately avoid the substring <c>LicenseSigner</c>
/// so <c>dotnet test --filter LicenseSigner</c> keeps selecting only
/// <c>LicenseSignerTests</c>, while <c>--filter CryptoRegistration</c> selects this class.
/// </remarks>
public class CryptoRegistrationTests
{
    private static IConfiguration ConfigWithPem(string? pemOrNull) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Crypto:RsaPrivateKeyPem"] = pemOrNull,
            })
            .Build();

    private static IHostEnvironment Dev() =>
        new FakeHostEnvironment { EnvironmentName = Environments.Development };

    private static IHostEnvironment Prod() =>
        new FakeHostEnvironment { EnvironmentName = Environments.Production };

    /// <summary>A well-formed license used for the round-trip assertion.</summary>
    private static License NewLicense() => new()
    {
        LicenseKey = "ABCD-EFGHJ-KLMN-PQRS-TUVW-XYZ2-34",
        ProductId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ModelSnapshot = LicenseModel.Machine | LicenseModel.Subscription,
        MaxActivations = 7,
        SubscriptionExpiryUtc = new DateTime(2027, 6, 1, 12, 30, 45, DateTimeKind.Utc),
        Signature = Array.Empty<byte>(),
    };

    [Fact]
    public void AddSigner_DevWithValidPem_ResolvesWithoutThrowing()
    {
        using var keySource = RSA.Create(2048);
        var pem = keySource.ExportRSAPrivateKeyPem();
        var config = ConfigWithPem(pem);

        var services = new ServiceCollection();
        services.AddLicenseSigner(config, Dev());
        var sp = services.BuildServiceProvider();

        var signer = sp.GetRequiredService<ILicenseSigner>();
        Assert.NotNull(signer);

        // The imported key is usable end to end: sign here, verify with its public half.
        var license = NewLicense();
        var signature = signer.Sign(license);

        using var publicOnly = RSA.Create();
        publicOnly.ImportParameters(keySource.ExportParameters(includePrivateParameters: false));
        Assert.True(signer.Verify(license, signature, publicOnly));
    }

    [Fact]
    public void AddSigner_ProdWithValidPem_ResolvesWithoutThrowing()
    {
        // A well-formed private-key PEM must work in Production too: fail-closed only
        // bites when there is no usable key material.
        using var keySource = RSA.Create(2048);
        var pem = keySource.ExportRSAPrivateKeyPem();
        var config = ConfigWithPem(pem);

        var services = new ServiceCollection();
        services.AddLicenseSigner(config, Prod());
        var sp = services.BuildServiceProvider();

        var signer = sp.GetRequiredService<ILicenseSigner>();
        Assert.NotNull(signer);

        var license = NewLicense();
        var signature = signer.Sign(license);

        using var publicOnly = RSA.Create();
        publicOnly.ImportParameters(keySource.ExportParameters(includePrivateParameters: false));
        Assert.True(signer.Verify(license, signature, publicOnly));
    }

    [Fact]
    public void AddSigner_DevWithNoPem_ResolvesInMemory_AndLogsExactlyOneWarning()
    {
        var config = ConfigWithPem(null);
        var provider = new CapturingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(provider));
        services.AddLicenseSigner(config, Dev());
        var sp = services.BuildServiceProvider();

        var signer = sp.GetRequiredService<ILicenseSigner>();
        Assert.NotNull(signer);
        Assert.Equal(1, provider.Entries.Count(e => e.Level == LogLevel.Warning));

        // Singleton: a second resolution must not re-run the factory nor re-log.
        var again = sp.GetRequiredService<ILicenseSigner>();
        Assert.Same(signer, again);
        Assert.Equal(1, provider.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public void AddSigner_ProdWithNoPem_ThrowsInvalidOperationAtRegistration()
    {
        // Fail-closed: the throw happens in the AddLicenseSigner call itself, before any
        // BuildServiceProvider — i.e. before builder.Build() in the real app.
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddLicenseSigner(ConfigWithPem(null), Prod()));

        Assert.Contains("Crypto:RsaPrivateKeyPem", ex.Message);
    }

    [Fact]
    public void AddSigner_WithMalformedPem_ThrowsInvalidOperationAtRegistration()
    {
        // A present-but-unparseable PEM is rejected eagerly, in the call — not lazily on
        // the first admin request that needs a signer.
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddLicenseSigner(ConfigWithPem("not-a-pem"), Prod()));

        Assert.Contains("Crypto:RsaPrivateKeyPem", ex.Message);
    }

    [Fact]
    public void AddSigner_WithGarbageBodyPem_ThrowsInvalidOperationAtRegistration()
    {
        // Valid PEM framing + valid base64 ("AAAA" = three zero bytes) that is not a key.
        // ImportFromPem gets past label/base64 parsing and fails in key import.
        var config = ConfigWithPem(
            "-----BEGIN RSA PRIVATE KEY-----\nAAAA\n-----END RSA PRIVATE KEY-----");

        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddLicenseSigner(config, Dev()));
    }

    [Fact]
    public void AddSigner_WithPublicKeyOnlyPem_ThrowsAtRegistration()
    {
        // ImportFromPem accepts a public-only PEM, but there is no private material, so the
        // eager check now rejects it at registration instead of letting Sign() blow up later.
        using var keySource = RSA.Create(2048);
        var publicPem = keySource.ExportSubjectPublicKeyInfoPem();

        var ex = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddLicenseSigner(ConfigWithPem(publicPem), Prod()));

        Assert.Contains("Crypto:RsaPrivateKeyPem", ex.Message);
        // The exception message must never echo the PEM (nor its inner exception chain).
        Assert.DoesNotContain(publicPem, FullText(ex));
    }

    [Fact]
    public void AddSigner_WithUndersizedKeyPem_ThrowsAtRegistration()
    {
        // A syntactically valid 1024-bit private key is below the 2048-bit floor.
        using var keySource = RSA.Create(1024);
        var pem = keySource.ExportRSAPrivateKeyPem();

        var ex = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddLicenseSigner(ConfigWithPem(pem), Prod()));

        Assert.Contains("Crypto:RsaPrivateKeyPem", ex.Message);
        Assert.Contains("2048", ex.Message);
        Assert.DoesNotContain(pem, FullText(ex));
    }

    /// <summary>Full exception text including the inner-exception chain.</summary>
    private static string FullText(Exception ex)
    {
        var text = ex.ToString();
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            text += "\n" + inner;
        }
        return text;
    }

    [Fact]
    public void AddSigner_ReturnsSameServiceCollection()
    {
        using var keySource = RSA.Create(2048);
        var config = ConfigWithPem(keySource.ExportRSAPrivateKeyPem());
        var services = new ServiceCollection();

        Assert.Same(services, services.AddLicenseSigner(config, Dev()));
    }

    [Fact]
    public void AddSigner_NoCryptoSectionAtAll_Dev_UsesFallback()
    {
        // Configuration with no "Crypto" section whatsoever: config["Crypto:RsaPrivateKeyPem"]
        // returns null -> same Development fallback as the empty case.
        var config = new ConfigurationBuilder().Build();
        var provider = new CapturingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(provider));
        services.AddLicenseSigner(config, Dev());
        var sp = services.BuildServiceProvider();

        var signer = sp.GetRequiredService<ILicenseSigner>();
        Assert.NotNull(signer);
        Assert.Equal(1, provider.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    /// <summary>
    /// Minimal <see cref="IHostEnvironment"/> whose <see cref="EnvironmentName"/> is
    /// settable so a test can select Development vs. Production behaviour.
    /// </summary>
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } =
            typeof(CryptoRegistrationTests).Assembly.GetName().Name!;

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// Minimal <see cref="ILoggerProvider"/> that records every log call so a test can
    /// assert on the number and level of emitted entries. Thread-safe via a lock.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly object _gate = new();

        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
                NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner._gate)
                {
                    owner.Entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            private NullScope()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
