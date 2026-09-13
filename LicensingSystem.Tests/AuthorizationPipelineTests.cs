using System.Net;
using System.Text.RegularExpressions;
using LicensingAdmin.Startup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Shared helper for every <see cref="WebApplicationFactory{TEntryPoint}"/> in the suite:
/// drops the <see cref="AdminSeeder"/> hosted service so the test host never touches the
/// fake connection string at startup (step 11).
/// </summary>
internal static class WebHostBuilderTestExtensions
{
    public static IWebHostBuilder WithoutAdminSeeder(this IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services
                         .Where(d => d.ImplementationType == typeof(AdminSeeder))
                         .ToList())
            {
                services.Remove(descriptor);
            }
        });
}

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
    public async Task Get_root_without_cookie_returns_200_public_landing_page()
    {
        // "/" is the public welcome page ([AllowAnonymous]) — the authenticated app
        // moved to "/dashboard" so there is somewhere anonymous to land a "start session"
        // link on.
        var response = await AnonymousClient().GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_dashboard_without_cookie_redirects_to_login()
    {
        var response = await AnonymousClient().GetAsync("/dashboard", TestContext.Current.CancellationToken);

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

    // ---- criterion 1: AccessDenied is an anonymous, non-redirecting 200 --------

    [Fact]
    public async Task Get_access_denied_without_cookie_returns_200_with_permission_copy()
    {
        var response = await AnonymousClient().GetAsync("/Account/AccessDenied", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sin permiso", body);
        Assert.Contains("No tienes permiso", body);
    }

    // ---- criterion "Logout": document the GET behaviour (render only; POST signs out) --

    [Fact]
    public async Task Get_logout_without_cookie_returns_200_and_renders_a_post_form()
    {
        var response = await AnonymousClient().GetAsync("/Account/Logout", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // GET only renders the confirm button; the actual sign-out is POST-only
        // (LogoutModel.OnPostAsync -> SignOutAsync), so a cross-site GET cannot log an admin out.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("method=\"post\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cerrar sesión", body);
    }

    // ---- optional: the anonymous login GET must not hand out an auth session ----

    [Fact]
    public async Task Get_login_without_cookie_sets_no_authentication_cookie()
    {
        var response = await AnonymousClient().GetAsync("/Account/Login", TestContext.Current.CancellationToken);

        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : Array.Empty<string>();

        // An antiforgery cookie on the GET is fine; a cookie-auth session cookie is not.
        Assert.DoesNotContain(setCookies, c => c.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
    }

    // ---- criteria 2 + 4 end to end: ?returnUrl= is reduced to a safe local path
    //      before it is written into the hidden form field. ------------------------

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("https://evil.com")]
    [InlineData("http:\\evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("   /ok")]
    public async Task Get_login_with_hostile_returnUrl_renders_dashboard_in_hidden_field(string returnUrl)
    {
        var response = await AnonymousClient()
            .GetAsync("/Account/Login?returnUrl=" + Uri.EscapeDataString(returnUrl), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/dashboard", HiddenReturnUrl(body));
    }

    [Theory]
    [InlineData("/pending-review")]
    [InlineData("/dashboard")]
    public async Task Get_login_with_local_returnUrl_keeps_it_in_hidden_field(string returnUrl)
    {
        var response = await AnonymousClient()
            .GetAsync("/Account/Login?returnUrl=" + Uri.EscapeDataString(returnUrl), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(returnUrl, HiddenReturnUrl(body));
    }

    // Pulls the value of <input type="hidden" name="returnUrl" value="..."> from the rendered page.
    private static string HiddenReturnUrl(string html)
    {
        var match = Regex.Match(html, "name=\"returnUrl\"[^>]*value=\"([^\"]*)\"");
        Assert.True(match.Success, "hidden returnUrl field not found in rendered login page");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>
    /// Supplies a syntactically valid but unreachable connection string so
    /// <c>ConnectionStringGuard.Require</c> (which runs in Program.cs, reading
    /// <c>builder.Configuration.GetConnectionString("LicensingDb")</c> before the host is
    /// built) is satisfied. <see cref="IWebHostBuilder.UseSetting"/> writes straight into
    /// that configuration, so no process-wide environment variable is touched.
    /// <see cref="WebHostBuilderTestExtensions.WithoutAdminSeeder"/> drops the
    /// <see cref="AdminSeeder"/> hosted service, so the host never opens a socket: an
    /// anonymous request is redirected by the fallback policy before any page resolves the
    /// <c>DbContextFactory</c>, and <c>db.invalid</c> (RFC 6761) is unresolvable anyway.
    /// </summary>
    public sealed class PipelineFactory : WebApplicationFactory<Program>
    {
        private const string FakeConnectionString =
            "Host=db.invalid;Database=test;Username=test;Password=test";

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder
                .UseSetting("ConnectionStrings:LicensingDb", FakeConnectionString)
                .WithoutAdminSeeder();
    }
}
