using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// E2-T4: the three admin screens now carry
/// <c>@attribute [Authorize(Policy = AuthPolicies.…)]</c>. These tests boot the real
/// <c>LicensingAdmin</c> pipeline with <see cref="WebApplicationFactory{TEntryPoint}"/>
/// (no database) and assert:
///
///  1. The added <c>[Authorize]</c> attributes do NOT weaken the anonymous gate that the
///     <c>FallbackPolicy</c> already provided — <c>/</c>, <c>/licenses</c> and
///     <c>/pending-review</c> still 302 to <c>/Account/Login</c> for an anonymous caller.
///  2. With a stubbed auth scheme, an authenticated-but-under-privileged admin hitting
///     <c>/pending-review</c> gets the in-app "no permission" copy from <c>App.razor</c>
///     (HTTP 200, prerendered) rather than a redirect — i.e. the policy really gates.
///
/// Known gap: the positive role paths (a viewer seeing <c>/</c> and <c>/licenses</c>, or
/// support staff seeing <c>/pending-review</c>) can't be asserted here because those
/// components hit the <c>DbContextFactory</c> in <c>OnInitializedAsync</c> during
/// prerender, which fails without a real database. See <c>review/tests-E2-T4.md</c>.
/// </summary>
public class GateScreensPipelineTests
{
    private const string FakeConnectionString =
        "Host=db.invalid;Database=test;Username=test;Password=test";

    private static string LocationPathAndQuery(HttpResponseMessage response)
    {
        var location = response.Headers.Location!;
        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    // ---- 1. anonymous: the @attribute must not weaken the fallback gate --------

    public sealed class AnonymousGate : IClassFixture<AnonymousGate.Factory>
    {
        private readonly Factory _factory;
        public AnonymousGate(Factory factory) => _factory = factory;

        private HttpClient Client() =>
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        [Theory]
        [InlineData("/")]
        [InlineData("/licenses")]
        [InlineData("/pending-review")]
        public async Task Gated_screen_without_cookie_redirects_to_login(string path)
        {
            var response = await Client().GetAsync(path, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.StartsWith("/Account/Login", LocationPathAndQuery(response));
        }

        [Fact]
        public async Task Gated_screen_redirect_carries_the_returnUrl_back()
        {
            var response = await Client().GetAsync("/pending-review", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Contains("returnUrl", LocationPathAndQuery(response), StringComparison.OrdinalIgnoreCase);
        }

        public sealed class Factory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder) =>
                builder
                    .UseSetting("ConnectionStrings:LicensingDb", FakeConnectionString)
                    .WithoutAdminSeeder();
        }
    }

    // ---- 2. authenticated but under-privileged: policy gates to in-app copy ----

    public sealed class UnderPrivilegedReviewer : IClassFixture<UnderPrivilegedReviewer.Factory>
    {
        private readonly Factory _factory;
        public UnderPrivilegedReviewer(Factory factory) => _factory = factory;

        private HttpClient ClientAs(string role)
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add(StubAuthHandler.RoleHeader, role);
            client.DefaultRequestHeaders.Add(StubAuthHandler.NameHeader, "viewer@vendor.test");
            return client;
        }

        [Fact]
        public async Task Viewer_hitting_pending_review_gets_the_in_app_no_permission_copy_not_a_redirect()
        {
            var response = await ClientAs("ReadOnlyViewer")
                .GetAsync("/pending-review", TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            // ReviewAccess = RequireRole(SupportStaff, SuperAdmin); a viewer fails it.
            // App.razor renders the alert inline for an authenticated principal — no 302.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("Account/Login", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No tienes permiso para ver esta página", body);
        }

        [Fact]
        public async Task Viewer_is_not_redirected_to_login_from_pending_review()
        {
            var response = await ClientAs("ReadOnlyViewer")
                .GetAsync("/pending-review", TestContext.Current.CancellationToken);

            Assert.NotEqual(HttpStatusCode.Found, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        }

        public sealed class Factory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseSetting("ConnectionStrings:LicensingDb", FakeConnectionString);
                builder.WithoutAdminSeeder();
                builder.ConfigureTestServices(services =>
                {
                    // Re-running AddAuthentication(scheme) re-registers the options setup
                    // that sets DefaultScheme; ConfigureTestServices runs last, so "Stub"
                    // becomes the default the FallbackPolicy authenticates against.
                    services.AddAuthentication(StubAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, StubAuthHandler>(
                            StubAuthHandler.SchemeName, _ => { });
                });
            }
        }
    }

    /// <summary>
    /// Minimal stand-in auth handler: if the request carries an <c>X-Stub-Role</c> header
    /// it authenticates with that role claim (and an optional <c>X-Stub-Name</c>), else it
    /// returns <see cref="AuthenticateResult.NoResult"/> so the fallback policy still bites.
    /// </summary>
    private sealed class StubAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
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
}
