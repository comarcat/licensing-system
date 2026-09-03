using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// E2-T7: <c>Pages/Admin/Users.razor</c> (<c>@page "/admin/users"</c>) now carries
/// <c>@attribute [Authorize(Policy = AuthPolicies.AdminUserAccess)]</c>, and
/// <c>AdminUserAccess = RequireRole(SuperAdmin)</c>. These tests boot the real
/// <c>LicensingAdmin</c> pipeline with <see cref="WebApplicationFactory{TEntryPoint}"/>
/// (no database) and assert the gate end to end:
///
///  1. Anonymous <c>GET /admin/users</c> => 302 to <c>/Account/Login</c> (with a
///     <c>returnUrl</c>), same as every other gated screen — the new attribute does not
///     weaken the fallback policy.
///  2. An authenticated but under-privileged admin (<c>SupportStaff</c> or
///     <c>ReadOnlyViewer</c>) => HTTP 200 with the in-app "no permission" copy from
///     <c>App.razor</c>, NOT a 302 and NOT the create form. The policy really gates:
///     only a <c>SuperAdmin</c> gets through.
///
/// Reuse note: this file reuses <c>GateScreensPipelineTests.StubAuthHandler</c> (widened
/// from <c>private</c> to <c>internal</c> on this branch) rather than re-declaring the
/// scheme or introducing <c>TestAuthStub.cs</c> — E2-T6 does that extraction and this
/// branch (stacked on E2-T3) should not race it. The factories call
/// <c>WithoutAdminSeeder()</c> (from <c>AuthorizationPipelineTests.cs</c>) because E2-T3's
/// <c>AdminSeeder</c> hosted service is present on this branch and would otherwise dial
/// the fake connection string at host start.
///
/// Known gap: the happy path — a <c>SuperAdmin</c> rendering <c>/admin/users</c> — cannot
/// be asserted here. <c>Users.razor.OnInitializedAsync</c> calls
/// <c>AdminUserService.ListAsync</c>, which resolves the <c>DbContextFactory</c> during
/// prerender and throws without Postgres. That belongs to a manual/integration check
/// (E2-T8 / epic step 14: <c># manual:</c>). No database and no bUnit are added here.
/// </summary>
public class AdminUsersGatePipelineTests
{
    private const string FakeConnectionString =
        "Host=db.invalid;Database=test;Username=test;Password=test";

    private static string LocationPathAndQuery(HttpResponseMessage response)
    {
        var location = response.Headers.Location!;
        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    // ---- 1. anonymous: fallback policy still redirects -----------------------

    public sealed class AnonymousGate : IClassFixture<AnonymousGate.Factory>
    {
        private readonly Factory _factory;
        public AnonymousGate(Factory factory) => _factory = factory;

        private HttpClient Client() =>
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        [Fact]
        public async Task Admin_users_without_cookie_redirects_to_login()
        {
            var response = await Client().GetAsync("/admin/users", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.StartsWith("/Account/Login", LocationPathAndQuery(response));
        }

        [Fact]
        public async Task Admin_users_redirect_carries_the_returnUrl_back()
        {
            var response = await Client().GetAsync("/admin/users", TestContext.Current.CancellationToken);

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

    // ---- 2. authenticated but not SuperAdmin: 200 "no permission" ----------

    public sealed class UnderPrivilegedAdmin : IClassFixture<UnderPrivilegedAdmin.Factory>
    {
        private readonly Factory _factory;
        public UnderPrivilegedAdmin(Factory factory) => _factory = factory;

        private HttpClient ClientAs(string role)
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add(GateScreensPipelineTests.StubAuthHandler.RoleHeader, role);
            client.DefaultRequestHeaders.Add(GateScreensPipelineTests.StubAuthHandler.NameHeader, "staff@vendor.test");
            return client;
        }

        [Theory]
        [InlineData("SupportStaff")]
        [InlineData("ReadOnlyViewer")]
        public async Task Non_superadmin_hitting_admin_users_gets_the_in_app_no_permission_copy(string role)
        {
            var response = await ClientAs(role).GetAsync("/admin/users", TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            // AdminUserAccess = RequireRole(SuperAdmin); SupportStaff / ReadOnlyViewer fail it.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("Account/Login", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No tienes permiso para ver esta página", body);
            // ...and definitely not the management form.
            Assert.DoesNotContain("Nuevo administrador", body);
        }

        [Theory]
        [InlineData("SupportStaff")]
        [InlineData("ReadOnlyViewer")]
        public async Task Non_superadmin_is_not_redirected_from_admin_users(string role)
        {
            var response = await ClientAs(role).GetAsync("/admin/users", TestContext.Current.CancellationToken);

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
                    // ConfigureTestServices runs last, so re-registering AddAuthentication
                    // makes "Stub" the default scheme the FallbackPolicy authenticates against.
                    services.AddAuthentication(GateScreensPipelineTests.StubAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, GateScreensPipelineTests.StubAuthHandler>(
                            GateScreensPipelineTests.StubAuthHandler.SchemeName, _ => { });
                });
            }
        }
    }
}
