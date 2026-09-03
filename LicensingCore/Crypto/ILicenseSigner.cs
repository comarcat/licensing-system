using System.Security.Cryptography;
using LicensingCore.Entities;

namespace LicensingCore.Crypto;

/// <summary>
/// Signs and verifies the canonical, security-relevant fields of a <see cref="License"/>
/// record (RSA-SHA256/PKCS#1 v1.5), so a client DLL can prove a key's authenticity
/// offline before ever calling the API.
/// </summary>
public interface ILicenseSigner
{
    /// <summary>Produces an RSA-SHA256 signature over the license's canonical bytes.</summary>
    byte[] Sign(License license);

    /// <summary>
    /// Verifies <paramref name="signature"/> against the license's canonical bytes using
    /// the supplied <paramref name="publicKey"/>.
    /// </summary>
    /// <returns>
    /// <c>false</c> (without throwing) when the signature was produced by a different key,
    /// when <paramref name="signature"/> is <c>null</c>, when it has length 0, or when it is
    /// otherwise malformed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="license"/> or <paramref name="publicKey"/> is <c>null</c>.
    /// </exception>
    bool Verify(License license, byte[] signature, RSA publicKey);
}
