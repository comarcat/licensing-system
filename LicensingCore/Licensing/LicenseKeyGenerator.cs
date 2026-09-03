using System.Security.Cryptography;

namespace LicensingCore.Licensing;

/// <summary>
/// Generates human-readable license keys backed by a cryptographically secure RNG.
/// </summary>
/// <remarks>
/// Output format: 7 segments of lengths 4-5-4-4-4-4-2 joined by '-'
/// (27 alphanumeric characters + 6 dashes).
/// Every generated key satisfies the regex:
/// ^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$
/// </remarks>
public static class LicenseKeyGenerator
{
    /// <summary>Exact key alphabet: 26 uppercase letters + 10 digits = 36 characters.</summary>
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>Segment lengths, in order: 4-5-4-4-4-4-2.</summary>
    private static readonly int[] SegmentLengths = { 4, 5, 4, 4, 4, 4, 2 };

    /// <summary>
    /// Returns a new license key. Thread-safe: relies solely on the static,
    /// thread-safe members of <see cref="RandomNumberGenerator"/> and holds no
    /// mutable static state.
    /// </summary>
    public static string NewKey()
    {
        var segments = new string[SegmentLengths.Length];
        for (var i = 0; i < SegmentLengths.Length; i++)
        {
            segments[i] = new string(RandomNumberGenerator.GetItems<char>(Alphabet, SegmentLengths[i]));
        }

        return string.Join('-', segments);
    }
}
