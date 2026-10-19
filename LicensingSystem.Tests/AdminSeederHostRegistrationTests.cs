using System.Net;
using LicensingAdmin.Startup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// E2-T3 — regression around registering <see cref="AdminSeeder"/> as a hosted service in
/// <c>Program.cs</c>. Two guarantees:
///  1. <c>Program.cs</c> registers exactly one <c>IHostedService</c> of type
///     <see cref="AdminSeeder"/> (no accidental double-registration).
///  2. <see cref="WebHostBuilderTestExtensions.WithoutAdminSeeder"/> removes it in every
///     <see cref="WebApplicationFactory{TEntryPoint}"/> in the suite, so no test host
///     opens a socket to the fake <c>db.invalid</c> connection string at startup — the
///     host boots and the anonymous gate still bites.
/// </summary>
public class AdminSeederHostRegistrationTests
{
    private const string FakeConnectionString =
        "Host=db.invalid;Database=test;Username=test;Password=test";

    [Fact]
    public void Program_registers_the_AdminSeeder_hosted_service_exactly_once()
    {
        using var factory = new CountingSeederFactory();

        _ = factory.Services; // forces the host to build (and WithoutAdminSeeder to run)

        Assert.Equal(1, factory.HostedSeederDescriptorCount);
    }

    [Fact]
    public void WithoutAdminSeeder_removes_the_seeder_and_the_host_still_starts()
    {
        using var factory = new AuthorizationPipelineTests.PipelineFactory();

        var hosted = factory.Services.GetServices<IHostedService>();

        Assert.DoesNotContain(hosted, h => h is AdminSeeder);
    }

    [Fact]
    public void WithoutAdminSeeder_is_effective_in_all_three_pipeline_factories()
    {
        // Building each factory's Services would throw at StartAsync if the AdminSeeder
        // were still registered (it would try to resolve the DbContextFactory and dial
        // db.invalid). Reaching the assertions at all proves the seeder is gone.
        using var f1 = new AuthorizationPipelineTests.PipelineFactory();
        using var f2 = new GateScreensPipelineTests.AnonymousGate.Factory();
        using var f3 = new GateScreensPipelineTests.UnderPrivilegedReviewer.Factory();

        foreach (var services in new[] { f1.Services, f2.Services, f3.Services })
        {
            Assert.DoesNotContain(services.GetServices<IHostedService>(), h => h is AdminSeeder);
        }
    }

    [Fact]
    public async Task With_the_seeder_dropped_the_anonymous_gate_still_redirects_to_login()
    {
        using var factory = new AuthorizationPipelineTests.PipelineFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // "/" is the public landing page; "/dashboard" is the protected one the fallback
        // policy gates.
        var response = await client.GetAsync("/dashboard", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!;
        var pathAndQuery = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
        Assert.StartsWith("/Account/Login", pathAndQuery);
    }

    /// <summary>
    /// Counts the <c>IHostedService</c>/<see cref="AdminSeeder"/> descriptors that
    /// <c>Program.cs</c> put in the container, captured in an <see cref="IWebHostBuilder.ConfigureServices"/>
    /// callback (which runs after the entry point's registrations but before
    /// <c>ConfigureTestServices</c>), then still drops the seeder so the host can boot.
    /// </summary>
    private sealed class CountingSeederFactory : WebApplicationFactory<Program>
    {
        public int HostedSeederDescriptorCount { get; private set; } = -1;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:LicensingDb", FakeConnectionString);
            builder.ConfigureServices(services =>
                HostedSeederDescriptorCount = services.Count(d =>
                    d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType == typeof(AdminSeeder)));
            builder.WithoutAdminSeeder();
        }
    }
}
