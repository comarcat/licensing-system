using LicensingCore.Entities;
using Microsoft.AspNetCore.Identity;

namespace LicensingAdmin.Auth;

/// <summary>
/// Wraps ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/> (PBKDF2) to hash and
/// verify admin passwords. Stateless and thread-safe.
/// </summary>
public sealed class PasswordHasherService
{
    private readonly PasswordHasher<AdminUser> _inner;

    // The concrete AdminUser instance is irrelevant to PBKDF2 hashing; Identity's
    // PasswordHasher never inspects it. A single throwaway instance keeps calls simple.
    private static readonly AdminUser _dummy = new() { Email = "", PasswordHash = "" };

    /// <summary>
    /// Creates the service around an injected <see cref="PasswordHasher{TUser}"/>.
    /// </summary>
    /// <param name="inner">
    /// The Identity password hasher to delegate to. In DI this instance is built from
    /// <c>IOptions&lt;PasswordHasherOptions&gt;</c> (see E1-T8, which sets
    /// <c>IterationCount = 210_000</c> per OWASP 2024), so the hashing cost is configurable
    /// by <c>LicensingAdmin</c> rather than fixed by a field initializer here.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <c>null</c>.</exception>
    public PasswordHasherService(PasswordHasher<AdminUser> inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>Hashes <paramref name="password"/> with a per-call random salt.</summary>
    /// <param name="password">Plaintext password to hash.</param>
    /// <returns>An opaque, self-describing hash string safe to persist.</returns>
    public string Hash(string password) => _inner.HashPassword(_dummy, password);

    /// <summary>
    /// Verifies <paramref name="password"/> against a previously produced <paramref name="hash"/>.
    /// Never throws for bad input: a <c>null</c>/empty <paramref name="hash"/>, a <c>null</c>
    /// <paramref name="password"/>, or a hash that is not even valid encoding all return
    /// <c>false</c> rather than propagating an exception (fail closed).
    /// </summary>
    /// <param name="hash">Stored hash string.</param>
    /// <param name="password">Plaintext password to check.</param>
    /// <returns><c>true</c> when the password matches (including when a rehash is advisable); otherwise <c>false</c>.</returns>
    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || password is null)
        {
            return false;
        }

        try
        {
            var result = _inner.VerifyHashedPassword(_dummy, hash, password);
            return result is PasswordVerificationResult.Success
                or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            // hash is not decodable (not Base64 / truncated / garbage) -> treat as no match.
            return false;
        }
    }
}
