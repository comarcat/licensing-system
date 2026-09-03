using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Slice close-out smoke (step 16): the whole composed <c>LicensingAdmin</c> host —
/// cookie auth, the four authorization policies, the admin seeder, license issuance and
/// the admin-user service all wired in <c>Program.cs</c> — builds its DI graph and starts,
/// and the anonymous entry point responds. Runs with a fake connection string and the
/// seeder stripped, so it never opens a socket. The screen-by-screen behaviour lives in
/// <c>AuthorizationPipelineTests</c> / <c>GateScreensPipelineTests</c> /
/// <c>GenerateLicensePipelineTests</c> / <c>AdminUsersGatePipelineTests</c>; this is the
/// single "it all composes" assertion the epic's close-out step calls for.
/// </summary>
public class SmokeTests : IClassFixture<SmokeTests.SolutionFactory>
{
    private readonly SolutionFactory _factory;

    public SmokeTests(SolutionFactory factory) => _factory = factory;

    [Fact]
    public async Task Composed_host_starts_and_serves_the_anonymous_login_page()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.GetAsync("/Account/Login", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Composed_host_challenges_an_anonymous_request_for_a_protected_route()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var root = await client.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, root.StatusCode);
        var location = root.Headers.Location!;
        Assert.StartsWith("/Account/Login", location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
    }

    public sealed class SolutionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder
                .UseSetting("ConnectionStrings:LicensingDb", "Host=db.invalid;Database=test;Username=test;Password=test")
                .WithoutAdminSeeder();
    }
}
