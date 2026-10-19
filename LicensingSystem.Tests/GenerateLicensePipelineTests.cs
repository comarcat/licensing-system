using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// E2-T6: <c>LicensingAdmin/Pages/Licenses/New.razor</c> now carries
/// <c>@attribute [Authorize(Policy = AuthPolicies.IssueAccess)]</c>, and
/// <c>MainLayout.razor</c> wraps every nav link in
/// <c>&lt;AuthorizeView Policy="..."&gt;</c> plus an AppBar block with the signed-in
/// email and a logout link.
///
/// These tests boot the real <c>LicensingAdmin</c> pipeline with
/// <see cref="WebApplicationFactory{TEntryPoint}"/> (no database) and assert:
///
///  1. Anonymous <c>GET /licenses/new</c> still 302s to <c>/Account/Login</c> with a
///     <c>returnUrl</c> — the explicit <c>[Authorize(IssueAccess)]</c> is not weaker
///     than the app's <c>FallbackPolicy</c>.
///  2. A stubbed, authenticated <c>ReadOnlyViewer</c> hitting <c>/licenses/new</c> gets
///     the in-app "No tienes permiso para ver esta página" copy from <c>App.razor</c>
///     (HTTP 200, prerendered) — not a redirect, not the issuance form. <c>IssueAccess</c>
///     = <c>RequireRole(SupportStaff, SuperAdmin)</c>, which a viewer fails.
///  3. That same 200 response renders inside <c>MainLayout</c>, so it also pins the
///     shell contract: the AppBar shows the viewer's email + a "Cerrar sesión" link to
///     <c>/Account/Logout</c>, and the nav menu shows only the links whose policy the
///     viewer satisfies (Dashboard / Licencias via <c>ViewerAccess</c>) while hiding
///     Pending Review, Generar licencia and Usuarios admin.
///
/// KNOWN GAP — anchored to the E2-T6 verify step 14 "# manual:" check.
/// The happy render of <c>/licenses/new</c> for SupportStaff / SuperAdmin cannot be
/// asserted here: <c>New.razor.OnInitializedAsync</c> resolves the
/// <c>IDbContextFactory&lt;AppDbContext&gt;</c> and queries <c>SoftwareProducts</c>
/// during Blazor prerender, which throws without a live Postgres (same limitation
/// E2-T4 documented for the gated list screens). That leaves the following to the
/// manual pass, NOT covered by an automated test:
///   - the "new product" switch creating a <c>SoftwareProduct</c> then issuing via
///     <c>LicenseIssuanceService</c>, and an existing product being reused;
///   - the success <c>MudPaper</c> showing the key + copy button + link to
///     <c>/licenses</c>;
///   - the <c>MudDatePicker</c> staying <c>Disabled</c> until the <c>Subscription</c>
///     model flag is checked.
/// Adding those would require a real database or EF InMemory + bUnit; per the task
/// scope no NuGet packages are added and no DB-dependent test is introduced.
/// </summary>
public class GenerateLicensePipelineTests
{
    private const string FakeConnectionString =
        "Host=db.invalid;Database=test;Username=test;Password=test";

    private static string LocationPathAndQuery(HttpResponseMessage response)
    {
        var location = response.Headers.Location!;
        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    // ---- 1. anonymous: [Authorize(IssueAccess)] must not weaken the fallback gate ----

    public sealed class AnonymousGate : IClassFixture<AnonymousGate.Factory>
    {
        private readonly Factory _factory;
        public AnonymousGate(Factory factory) => _factory = factory;

        private HttpClient Client() =>
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        [Fact]
        public async Task Anonymous_get_licenses_new_redirects_to_login()
        {
            var response = await Client().GetAsync("/licenses/new", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.StartsWith("/Account/Login", LocationPathAndQuery(response));
        }

        [Fact]
        public async Task Anonymous_get_licenses_new_redirect_carries_a_returnUrl_to_the_page()
        {
            var response = await Client().GetAsync("/licenses/new", TestContext.Current.CancellationToken);
            var location = LocationPathAndQuery(response);

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Contains("returnurl", location, StringComparison.OrdinalIgnoreCase);
            // round-trip target is /licenses/new (percent-encoded in the query string)
            Assert.Contains("licenses", location, StringComparison.OrdinalIgnoreCase);
        }

        public sealed class Factory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder) =>
                builder.UseSetting("ConnectionStrings:LicensingDb", FakeConnectionString);
        }
    }

    // ---- 2 + 3. authenticated ReadOnlyViewer: policy gates to the in-app copy, and
    //             the MainLayout shell around it is role-scoped ---------------------

    public sealed class UnderPrivilegedViewer : IClassFixture<UnderPrivilegedViewer.Factory>
    {
        private const string ViewerEmail = "viewer@vendor.test";

        private readonly Factory _factory;
        public UnderPrivilegedViewer(Factory factory) => _factory = factory;

        private HttpClient ViewerClient()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add(StubAuthHandler.RoleHeader, "ReadOnlyViewer");
            client.DefaultRequestHeaders.Add(StubAuthHandler.NameHeader, ViewerEmail);
            return client;
        }

        private async Task<(HttpResponseMessage Response, string Body)> GetLicensesNew()
        {
            var response = await ViewerClient().GetAsync("/licenses/new", TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            return (response, body);
        }

        [Fact]
        public async Task Viewer_hitting_licenses_new_gets_the_in_app_no_permission_copy_not_a_redirect()
        {
            var (response, body) = await GetLicensesNew();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("Account/Login", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No tienes permiso para ver esta página", body);
        }

        [Fact]
        public async Task Viewer_is_not_redirected_to_login_from_licenses_new()
        {
            var (response, _) = await GetLicensesNew();

            Assert.NotEqual(HttpStatusCode.Found, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        }

        [Fact]
        public async Task Viewer_does_not_see_the_issuance_form_on_licenses_new()
        {
            var (_, body) = await GetLicensesNew();

            // Copy unique to the New.razor form body; absent when NotAuthorized renders.
            Assert.DoesNotContain("Crea el producto (o reutiliza uno)", body);
            Assert.DoesNotContain("Emitir licencia", body);
        }

        [Fact]
        public async Task MainLayout_appbar_shows_the_signed_in_email_and_a_logout_link()
        {
            var (response, body) = await GetLicensesNew();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(ViewerEmail, body);
            Assert.Contains("Cerrar sesión", body);
            Assert.Contains("/Account/Logout", body);
        }

        [Fact]
        public async Task MainLayout_nav_shows_only_the_links_the_viewer_policy_allows()
        {
            var (response, body) = await GetLicensesNew();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // ViewerAccess satisfied -> these two nav links render.
            Assert.Contains("Dashboard", body);
            Assert.Contains("Licencias", body);

            // ReviewAccess / IssueAccess / AdminUserAccess NOT satisfied -> hidden.
            Assert.DoesNotContain("Pending Review", body);
            Assert.DoesNotContain("Generar licencia", body);
            Assert.DoesNotContain("Usuarios admin", body);
        }

        public sealed class Factory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseSetting("ConnectionStrings:LicensingDb", FakeConnectionString);
                builder.ConfigureTestServices(services =>
                {
                    // ConfigureTestServices runs last; re-running AddAuthentication(scheme)
                    // makes "Stub" the default scheme the FallbackPolicy authenticates
                    // against. Mirrors GateScreensPipelineTests.
                    services.AddAuthentication(StubAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, StubAuthHandler>(
                            StubAuthHandler.SchemeName, _ => { });
                });
            }
        }
    }
}
