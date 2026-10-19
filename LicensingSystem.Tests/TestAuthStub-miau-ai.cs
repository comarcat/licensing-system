using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LicensingSystem.Tests;

/// <summary>
/// Minimal stand-in auth handler shared by the pipeline tests. If the request carries
/// an <c>X-Stub-Role</c> header it authenticates with that role claim (and an optional
/// <c>X-Stub-Name</c>), else it returns <see cref="AuthenticateResult.NoResult"/> so the
/// app's fallback policy still bites.
///
/// Extracted for E2-T6 from <c>GateScreensPipelineTests.cs</c> (E2-T4), where it started
/// life as a <c>private sealed</c> nested type. <c>GenerateLicensePipelineTests</c> needs
/// the same stub, so it now lives here as an assembly-internal top-level type; the E2-T4
/// tests are unchanged beyond the removal of the nested copy.
/// </summary>
internal sealed class StubAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Stub";
    public const string RoleHeader = "X-Stub-Role";
    public const string NameHeader = "X-Stub-Name";

    public StubAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RoleHeader, out var role) || string.IsNullOrEmpty(role))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var name = Request.Headers.TryGetValue(NameHeader, out var n) && !string.IsNullOrEmpty(n)
            ? n.ToString()
            : "stub@vendor.test";

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.Role, role.ToString()),
            },
            authenticationType: SchemeName);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
