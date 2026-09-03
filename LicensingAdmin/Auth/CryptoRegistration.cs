using System.Security.Cryptography;
using LicensingCore.Crypto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LicensingAdmin.Auth;

/// <summary>
/// Wires <see cref="ILicenseSigner"/> into DI for the admin app. The RSA key material is
/// validated <b>eagerly</b>, inside <see cref="AddLicenseSigner"/> (i.e. before
/// <c>builder.Build()</c>) rather than lazily on first resolution, and the wiring is
/// <b>fail-closed outside the Development environment</b>:
/// <list type="bullet">
///   <item><description>
///     <c>Crypto:RsaPrivateKeyPem</c> set → imported now via
///     <see cref="RSA.ImportFromPem(ReadOnlySpan{char})"/> and checked for private key
///     material and a key size of at least 2048 bits. Any failure throws
///     <see cref="InvalidOperationException"/> at registration; the PEM value is never
///     echoed into an exception message.
///   </description></item>
///   <item><description>
///     no PEM and <see cref="HostEnvironmentEnvExtensions.IsDevelopment(IHostEnvironment)"/>
///     → an ephemeral in-memory 2048-bit key, with a single warning logged on first
///     resolution.
///   </description></item>
///   <item><description>
///     no PEM and not Development → <see cref="InvalidOperationException"/> at
///     registration. An ephemeral key that dies on every restart is an integrity
///     fail-open for an app that issues signed licenses, so it is refused.
///   </description></item>
/// </list>
/// </summary>
public static class CryptoRegistration
{
    /// <summary>Configuration key holding the PEM-encoded RSA private key.</summary>
    private const string PemConfigKey = "Crypto:RsaPrivateKeyPem";

    /// <summary>
    /// Emitted exactly once, on first resolution, when no PEM key is configured and the
    /// Development-only in-memory fallback is used.
    /// </summary>
    private const string InMemoryKeyWarning =
        "Crypto:RsaPrivateKeyPem no configurado — usando clave RSA en memoria " +
        "(solo dev; las licencias firmadas no sobreviven a un reinicio).";

    /// <summary>
    /// Registers <see cref="ILicenseSigner"/> as a singleton, validating the RSA key
    /// material eagerly (before the host is built). See the type-level summary for the
    /// per-environment behaviour.
    /// </summary>
    /// <param name="services">The DI service collection to add the registration to.</param>
    /// <param name="config">Configuration root read for the PEM key material.</param>
    /// <param name="env">Host environment; the in-memory fallback is Development-only.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at registration when <c>Crypto:RsaPrivateKeyPem</c> is present but cannot be
    /// used as an RSA private key of at least 2048 bits, or when it is absent outside the
    /// Development environment.
    /// </exception>
    public static IServiceCollection AddLicenseSigner(
        this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(env);

        var pem = config[PemConfigKey];

        if (!string.IsNullOrWhiteSpace(pem))
        {
            var rsa = RSA.Create();

            try
            {
                rsa.ImportFromPem(pem);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                rsa.Dispose();
                throw new InvalidOperationException(
                    "Crypto:RsaPrivateKeyPem está configurado pero no se pudo interpretar " +
                    "como PEM de clave privada RSA.", ex);
            }

            try
            {
                _ = rsa.ExportParameters(includePrivateParameters: true);
            }
            catch (CryptographicException ex)
            {
                rsa.Dispose();
                throw new InvalidOperationException(
                    "Crypto:RsaPrivateKeyPem no contiene una clave privada RSA " +
                    "(¿es solo la clave pública?).", ex);
            }

            if (rsa.KeySize < 2048)
            {
                var bits = rsa.KeySize;
                rsa.Dispose();
                throw new InvalidOperationException(
                    $"Crypto:RsaPrivateKeyPem tiene una clave RSA de {bits} bits; " +
                    "se requieren al menos 2048.");
            }

            // Deterministic: hand DI an already-constructed instance, no lazy factory.
            services.AddSingleton<ILicenseSigner>(new LicenseSigner(rsa));
            return services;
        }

        if (env.IsDevelopment())
        {
            // DEV-ONLY fallback: keeps the admin app runnable before real key material is
            // provisioned. Signatures made with this key are lost on restart. The lazy
            // factory lives ONLY here so the warning is emitted once, on first resolution.
            services.AddSingleton<ILicenseSigner>(sp =>
            {
                sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(CryptoRegistration).FullName!)
                    .LogWarning(InMemoryKeyWarning);
                return new LicenseSigner(RSA.Create(2048));
            });
            return services;
        }

        throw new InvalidOperationException(
            "Crypto:RsaPrivateKeyPem no está configurado. La clave RSA en memoria solo " +
            "se permite en el entorno Development.");
    }
}
