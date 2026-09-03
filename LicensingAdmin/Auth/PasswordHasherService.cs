using LicensingCore.Entities;
using Microsoft.AspNetCore.Identity;

namespace LicensingAdmin.Auth;

/// <summary>
/// Wraps ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/> (PBKDF2) to hash and
/// verify admin passwords. Stateless and thread-safe.
/// </summary>
public sealed class PasswordHasherService
{
    private readonly PasswordHasher<AdminUser> _inner = new();

    // The concrete AdminUser instance is irrelevant to PBKDF2 hashing; Identity's
    // PasswordHasher never inspects it. A throwaway instance keeps the call simple.
    private static AdminUser DummyUser() => new() { Email = "", PasswordHash = "" };

    /// <summary>Hashes <paramref name="password"/> with a per-call random salt.</summary>
    /// <param name="password">Plaintext password to hash.</param>
    /// <returns>An opaque, self-describing hash string safe to persist.</returns>
    public string Hash(string password) => _inner.HashPassword(DummyUser(), password);

    /// <summary>
    /// Verifies <paramref name="password"/> against a previously produced <paramref name="hash"/>.
    /// Never throws for malformed input: a hash that is not even valid encoding returns
    /// <c>false</c> rather than propagating a <see cref="FormatException"/>.
    /// </summary>
    /// <param name="hash">Stored hash string.</param>
    /// <param name="password">Plaintext password to check.</param>
    /// <returns><c>true</c> when the password matches (including when a rehash is advisable); otherwise <c>false</c>.</returns>
    public bool Verify(string hash, string password)
    {
        try
        {
            var result = _inner.VerifyHashedPassword(DummyUser(), hash, password);
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
