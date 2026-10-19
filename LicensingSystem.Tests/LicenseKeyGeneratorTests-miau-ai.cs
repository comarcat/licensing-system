using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using LicensingCore.Licensing;
using Xunit;

namespace LicensingSystem.Tests;

public class LicenseKeyGeneratorTests
{
    // ^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$
    private const string KeyPattern =
        "^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$";

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    [Fact]
    public void NewKey_MatchesRequiredFormatRegex()
    {
        for (var i = 0; i < 100; i++)
        {
            var key = LicenseKeyGenerator.NewKey();
            Assert.True(
                Regex.IsMatch(key, KeyPattern),
                $"Key '{key}' does not match {KeyPattern}");
        }
    }

    [Fact]
    public void NewKey_TenThousandKeys_NoCollisions()
    {
        var keys = new HashSet<string>();
        for (var i = 0; i < 10000; i++)
        {
            keys.Add(LicenseKeyGenerator.NewKey());
        }

        Assert.Equal(10000, keys.Count);
    }

    [Fact]
    public void NewKey_UsesOnlyAllowedCharactersAndSeparators()
    {
        for (var i = 0; i < 100; i++)
        {
            var key = LicenseKeyGenerator.NewKey();

            var segments = key.Split('-');
            Assert.Equal(new[] { 4, 5, 4, 4, 4, 4, 2 }, segments.Select(s => s.Length).ToArray());

            // Exactly 6 dashes, total length 27 alphanumerics + 6 separators.
            Assert.Equal(6, key.Count(c => c == '-'));
            Assert.Equal(33, key.Length);

            // Dashes sit at the expected offsets for segments 4-5-4-4-4-4-2.
            Assert.Equal(new[] { 4, 10, 15, 20, 25, 30 },
                Enumerable.Range(0, key.Length).Where(idx => key[idx] == '-').ToArray());

            foreach (var c in key.Replace("-", string.Empty))
            {
                Assert.Contains(c, Alphabet);
            }
        }
    }

    // Gap covered: the regex and "allowed characters" tests only prove the output is a
    // SUBSET of the alphabet. They would still pass if the generator silently dropped a
    // character (e.g. an off-by-one that used a 35-char alphabet). This test proves the
    // output alphabet is EXACTLY the 36-char set: every character must appear at least
    // once across the sample.
    // Effectively deterministic: P(a given char is absent from 500 keys / 13,500 draws)
    // = ((35/36)^27)^500 ~ 1e-165, far below the birthday-collision odds (~5e-35) that
    // the existing 10,000-key test already tolerates.
    [Fact]
    public void NewKey_ExercisesEveryAlphabetCharacter_AcrossSample()
    {
        var seen = new HashSet<char>();
        for (var i = 0; i < 500; i++)
        {
            foreach (var c in LicenseKeyGenerator.NewKey().Replace("-", string.Empty))
            {
                seen.Add(c);
            }
        }

        var missing = Alphabet.Where(c => !seen.Contains(c)).ToArray();
        Assert.Empty(missing);
        Assert.All(seen, c => Assert.Contains(c, Alphabet));
        Assert.Equal(Alphabet.Length, seen.Count);
    }

    // Gap covered: per-position distribution. Guards against a bug that pins any single
    // character slot to a constant (e.g. a mis-sliced segment or a fixed index).
    // Effectively deterministic: P(one position identical across 200 keys)
    // = 36 * (1/36)^199 ~ 1e-308 per position.
    [Fact]
    public void NewKey_ProducesVaryingCharacters_AtEveryPosition()
    {
        const int sampleSize = 200;
        var stripped = new string[sampleSize];
        for (var i = 0; i < sampleSize; i++)
        {
            stripped[i] = LicenseKeyGenerator.NewKey().Replace("-", string.Empty);
        }

        for (var pos = 0; pos < 27; pos++)
        {
            var distinctAtPos = stripped.Select(k => k[pos]).Distinct().Count();
            Assert.True(
                distinctAtPos > 1,
                $"Position {pos} yielded a single constant character across {sampleSize} keys");
        }
    }

    // Gap covered: the XML docs claim NewKey() is thread-safe. This exercises concurrent
    // generation and asserts every key is well-formed and unique. Deterministic: NewKey
    // holds no mutable static state and delegates to the thread-safe static
    // RandomNumberGenerator.GetItems. Collision odds over 10,000 keys ~5e-35, matching
    // the tolerance of the existing sequential collision test.
    [Fact]
    public void NewKey_IsThreadSafe_UnderConcurrentGeneration()
    {
        const int total = 10000;
        var bag = new ConcurrentBag<string>();

        Parallel.For(0, total, _ => bag.Add(LicenseKeyGenerator.NewKey()));

        Assert.Equal(total, bag.Count);
        Assert.All(bag, key => Assert.Matches(KeyPattern, key));
        Assert.Equal(total, bag.Distinct().Count());
    }
}
