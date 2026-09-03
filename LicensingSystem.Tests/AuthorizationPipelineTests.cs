using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Boots the real <c>LicensingAdmin</c> pipeline with <see cref="WebApplicationFactory{TEntryPoint}"/>
/// and asserts the cookie + fallback-policy wiring from steps 8–10: the anonymous
/// login page is reachable, and every other route challenges to <c>/Account/Login</c>.
/// No database is touched — an anonymous request is redirected by the fallback policy
/// before any page resolves the <c>DbContextFactory</c>.
/// </summary>
public class AuthorizationPipelineTests : IClassFixture<AuthorizationPipelineTests.PipelineFactory>
{
    private readonly PipelineFactory _factory;

    public AuthorizationPipelineTests(PipelineFactory factory) => _factory = factory;

    private HttpClient AnonymousClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Get_login_without_cookie_returns_200()
    {
        var response = await AnonymousClient().GetAsync("/Account/Login", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_root_without_cookie_redirects_to_login()
    {
        var response = await AnonymousClient().GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("/Account/Login", LocationPathAndQuery(response));
    }

    [Fact]
    public async Task Get_pending_review_without_cookie_redirects_to_login()
    {
        var response = await AnonymousClient().GetAsync("/pending-review", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("/Account/Login", LocationPathAndQuery(response));
    }

    // The cookie handler builds an absolute redirect URI (scheme://host/Account/Login?...);
    // the acceptance criterion is about the path it lands on, so compare on path + query.
    private static string LocationPathAndQuery(HttpResponseMessage response)
    {
        var location = response.Headers.Location!;
        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    /// <summary>
    /// Supplies a syntactically valid but unreachable connection string so
    /// <c>ConnectionStringGuard.Require</c> (which runs in Program.cs before the host is
    /// built) is satisfied. It is set as an environment variable rather than only via
    /// <see cref="IWebHostBuilder.ConfigureAppConfiguration"/> because factory config
    /// callbacks are layered in after <c>WebApplicationBuilder.Configuration</c> is read,
    /// which is too late for that guard; the default environment-variables provider is
    /// read as <c>CreateBuilder</c> runs. The in-memory entry below mirrors it for
    /// anyone reading the fixture. Step 11 extends <see cref="ConfigureWebHost"/> to strip
    /// <c>AddHostedService&lt;AdminSeeder&gt;</c> so host startup still never opens a socket.
    /// </summary>
    public sealed class PipelineFactory : WebApplicationFactory<Program>
    {
        private const string FakeConnectionString =
            "Host=localhost;Port=5432;Database=test;Username=test;Password=test";

        public PipelineFactory() =>
            Environment.SetEnvironmentVariable("ConnectionStrings__LicensingDb", FakeConnectionString);

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:LicensingDb"] = FakeConnectionString }));

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                Environment.SetEnvironmentVariable("ConnectionStrings__LicensingDb", null);
            }
        }
    }
}
