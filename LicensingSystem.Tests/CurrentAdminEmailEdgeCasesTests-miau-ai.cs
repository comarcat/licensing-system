using System.Security.Claims;
using LicensingAdmin.Auth;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Boundary behaviour of <see cref="CurrentAdmin.Email"/> (E2-T4). The contract is
/// literally
/// <c>principal?.Identity?.IsAuthenticated == true ? principal.FindFirstValue(ClaimTypes.Name) ?? "" : ""</c>,
/// so these tests pin down what that expression actually does at the edges.
///
/// Cases are tagged in their names:
///  * "contract"        — the behaviour the E2-T4 spec promises (must not regress).
///  * "characterization" — behaviour that falls out of the implementation and the BCL,
///                         not spelled out by the spec; captured so a refactor that
///                         changes it is a conscious decision. See the review note for
///                         the ones that can put an untrustworthy value in an audit row.
/// </summary>
public class CurrentAdminEmailEdgeCasesTests
{
    private const string Scheme = "TestCookie";

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: Scheme));

    // ---- multiple Name claims -------------------------------------------------

    [Fact] // characterization: FindFirstValue walks claims in insertion order, first wins
    public void Email_authenticated_with_two_name_claims_returns_the_first()
    {
        var principal = Authenticated(
            new Claim(ClaimTypes.Name, "first@vendor.test"),
            new Claim(ClaimTypes.Name, "second@vendor.test"));

        Assert.Equal("first@vendor.test", CurrentAdmin.Email(principal));
    }

    // ---- empty / whitespace Name value -------------------------------------------

    [Fact] // characterization: an *authenticated* principal whose Name claim is "" still
           // collapses to "" — the `?? ""` never fires because the value is non-null.
           // Same observable result as "no Name claim at all".
    public void Email_authenticated_with_empty_name_claim_returns_empty_string()
    {
        var principal = Authenticated(new Claim(ClaimTypes.Name, string.Empty));

        Assert.Equal("", CurrentAdmin.Email(principal));
    }

    [Fact] // characterization: the value is returned verbatim — NOT trimmed. A principal
           // carrying a whitespace Name would write "   " into AuditLogEntry.Actor /
           // Activation.ReviewedBy.
    public void Email_authenticated_with_whitespace_name_claim_returns_it_verbatim()
    {
        var principal = Authenticated(new Claim(ClaimTypes.Name, "   "));

        Assert.Equal("   ", CurrentAdmin.Email(principal));
    }

    // ---- multiple identities: Email keys off the PRIMARY identity only ----------

    [Fact] // characterization: ClaimsPrincipal.Identity is the FIRST identity. When it is
           // unauthenticated, Email returns "" even though another identity in the
           // principal is authenticated and carries the Name.
    public void Email_primary_identity_unauthenticated_ignores_authenticated_secondary()
    {
        var anonymousPrimary = new ClaimsIdentity(); // no auth type
        var authenticatedSecondary = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "secondary@vendor.test") },
            authenticationType: Scheme);

        var principal = new ClaimsPrincipal(new[] { anonymousPrimary, authenticatedSecondary });

        Assert.Contains(principal.Identities, i => i.IsAuthenticated);   // there IS an auth identity
        Assert.Equal("", CurrentAdmin.Email(principal));                 // ...but Email still says ""
    }

    [Fact] // characterization + CONTRACT GAP: the guard (`Identity.IsAuthenticated`) looks
           // at the primary identity, but the lookup (`principal.FindFirstValue`) searches
           // ALL identities. So when the primary is authenticated-but-nameless and a
           // secondary carries the Name, Email returns the secondary's Name.
    public void Email_authenticated_primary_without_name_falls_through_to_secondary_identity()
    {
        var authenticatedPrimaryNoName = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, "SupportStaff") },
            authenticationType: Scheme);
        var secondaryWithName = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "other-identity@vendor.test") },
            authenticationType: Scheme);

        var principal = new ClaimsPrincipal(new[] { authenticatedPrimaryNoName, secondaryWithName });

        Assert.Equal("other-identity@vendor.test", CurrentAdmin.Email(principal));
    }

    // ---- identity built with a non-default NameClaimType -----------------------

    [Fact] // characterization + CONTRACT GAP: Email hard-codes ClaimTypes.Name and ignores
           // the identity's configured NameClaimType. An identity whose name lives under a
           // custom claim type resolves via principal.Identity.Name but NOT via Email.
    public void Email_ignores_custom_nameType_and_returns_empty_string()
    {
        const string customNameType = "urn:licensing:admin-email";
        var identity = new ClaimsIdentity(
            new[] { new Claim(customNameType, "custom@vendor.test") },
            authenticationType: Scheme,
            nameType: customNameType,
            roleType: ClaimTypes.Role);

        var principal = new ClaimsPrincipal(identity);

        Assert.Equal("custom@vendor.test", principal.Identity!.Name); // the BCL resolves it
        Assert.Equal("", CurrentAdmin.Email(principal));              // CurrentAdmin does not
    }

    [Fact] // contract: a standard ClaimTypes.Name claim still wins even when the identity
           // also declares a non-default nameType (the claim *type string* is what matters).
    public void Email_returns_standard_name_claim_even_with_custom_nameType_declared()
    {
        const string customNameType = "urn:licensing:admin-email";
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(customNameType, "custom@vendor.test"),
                new Claim(ClaimTypes.Name, "standard@vendor.test"),
            },
            authenticationType: Scheme,
            nameType: customNameType,
            roleType: ClaimTypes.Role);

        Assert.Equal("standard@vendor.test", CurrentAdmin.Email(new ClaimsPrincipal(identity)));
    }
}
