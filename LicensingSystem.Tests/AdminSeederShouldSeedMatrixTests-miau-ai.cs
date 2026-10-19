using LicensingAdmin.Startup;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// E2-T3 — full vector matrix for <see cref="AdminSeeder.ShouldSeed"/>.
/// Contract: <c>ShouldSeed(tableEmpty, email, password)</c> ==
/// <c>tableEmpty &amp;&amp; !IsNullOrWhiteSpace(email) &amp;&amp; !IsNullOrWhiteSpace(password)</c>.
/// The expected value below is computed with an independent <c>Trim().Length &gt; 0</c>
/// oracle (not <c>string.IsNullOrWhiteSpace</c>) so the assertion is a real cross-check.
/// </summary>
public class AdminSeederShouldSeedMatrixTests
{
    // tableEmpty ∈ {true,false} × email ∈ {null,"","  ","a@b.c"," a@b.c "}
    //                            × password ∈ {null,"","  ","pw"," pw "}  => 50 rows.
    public static TheoryData<bool, string?, string?, bool> Matrix()
    {
        var data = new TheoryData<bool, string?, string?, bool>();
        string?[] emails = { null, "", "  ", "a@b.c", " a@b.c " };
        string?[] passwords = { null, "", "  ", "pw", " pw " };

        static bool NotBlank(string? s) => s is not null && s.Trim().Length > 0;

        foreach (var tableEmpty in new[] { true, false })
        {
            foreach (var email in emails)
            {
                foreach (var password in passwords)
                {
                    var expected = tableEmpty && NotBlank(email) && NotBlank(password);
                    data.Add(tableEmpty, email, password, expected);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void ShouldSeed_is_the_exact_AND_of_tableEmpty_and_nonblank_email_and_nonblank_password(
        bool tableEmpty, string? email, string? password, bool expected)
    {
        Assert.Equal(expected, AdminSeeder.ShouldSeed(tableEmpty, email, password));
    }

    // ---- the interesting "true" corner: padded values with real content ----------
    // IsNullOrWhiteSpace(" a@b.c ") is false and IsNullOrWhiteSpace(" pw ") is false,
    // so an empty table + both present => seed.

    [Theory]
    [InlineData(" a@b.c ", " pw ")]
    [InlineData("  a@b.c  ", "  pw  ")]
    [InlineData("a@b.c", "pw")]
    [InlineData(" a@b.c ", "pw")]
    [InlineData("a@b.c", " pw ")]
    public void ShouldSeed_true_when_table_empty_and_both_values_have_real_content(string email, string password)
    {
        Assert.True(AdminSeeder.ShouldSeed(tableEmpty: true, email, password));
    }

    [Theory]
    [InlineData(" a@b.c ", " pw ")]
    [InlineData("a@b.c", "pw")]
    public void ShouldSeed_false_when_table_not_empty_regardless_of_valid_values(string email, string password)
    {
        Assert.False(AdminSeeder.ShouldSeed(tableEmpty: false, email, password));
    }

    // Whitespace-only strings are blank -> false even on an empty table.
    [Theory]
    [InlineData("   ", " pw ")]
    [InlineData(" a@b.c ", "   ")]
    [InlineData("\t", "pw")]
    [InlineData("a@b.c", "\t")]
    [InlineData("\n", "\r")]
    public void ShouldSeed_false_when_email_or_password_is_whitespace_only(string email, string password)
    {
        Assert.False(AdminSeeder.ShouldSeed(tableEmpty: true, email, password));
    }
}
