using LicensingAdmin.Auth;
using LicensingAdmin.Licensing;
using LicensingAdmin.Notifications;
using LicensingAdmin.Startup;
using LicensingCore.Configuration;
using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();

// Talks to the same PostgreSQL database as the API — this admin app reads/writes
// via EF Core directly rather than going through the API's HTTP surface, matching
// the "shared data layer" link in the architecture diagram. If you'd rather have
// it call the API's admin endpoints instead, swap AppDbContext usage in the Pages
// for a typed HttpClient — nothing else in this project depends on that choice.
// Fail loudly at startup (in Main, before the host is built) if the connection
// string is missing — not lazily on the first request that resolves the factory.
var licensingDb = ConnectionStringGuard.Require(builder.Configuration.GetConnectionString("LicensingDb"));
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(licensingDb));

// Registers ILicenseSigner (singleton). Uses Crypto:RsaPrivateKeyPem when set,
// otherwise a dev-only in-memory RSA key (logs one warning). See CryptoRegistration.
builder.Services.AddLicenseSigner(builder.Configuration, builder.Environment);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        // Not /Account/Login: an authenticated user with an insufficient role must not
        // bounce back to a login page that immediately re-redirects them (loop). E2-T2
        // adds the [AllowAnonymous] /Account/AccessDenied page.
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.Cookie.HttpOnly = true;
        // The session cookie must never leave over plaintext HTTP in a real deployment.
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.SlidingExpiration = true;
        // Session times out after 8h of inactivity (sliding renews it on use). The hard
        // account check is OnValidatePrincipal below, which re-verifies against the DB
        // that the account is still active and its role unchanged on every request.
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async ctx =>
            {
                var email = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
                var role = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                if (string.IsNullOrEmpty(email))
                {
                    ctx.RejectPrincipal();
                    return;
                }

                var lookup = ctx.HttpContext.RequestServices.GetRequiredService<IAdminUserLookup>();
                var admin = await lookup.FindByEmailAsync(email, ctx.HttpContext.RequestAborted);
                if (admin is not { IsActive: true } || admin.Role.ToString() != role)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(
                        Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
                }
            },
        };
    });

builder.Services.AddAdminAuthorization();

// PBKDF2-HMAC-SHA512 @ >= 210_000 iteraciones (OWASP 2024). El PasswordHasherService
// del paso 7 recibe este PasswordHasher<AdminUser> por DI.
builder.Services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000);
builder.Services.AddSingleton<PasswordHasher<AdminUser>>();
builder.Services.AddSingleton<PasswordHasherService>();

// Email/password validation + cookie principal building, backed by an EF lookup.
// Scoped: EfAdminUserLookup resolves the scoped IDbContextFactory consumer chain and
// the credential service is only used per sign-in request / per Blazor circuit.
builder.Services.AddScoped<IAdminUserLookup, EfAdminUserLookup>();
builder.Services.AddScoped<AdminCredentialService>();
// Admin-user management screen (E2-T7): create admins + toggle IsActive, audited.
builder.Services.AddScoped<IAdminUserStore, EfAdminUserStore>();
builder.Services.AddScoped<AdminUserService>();
// Server-side Blazor: revalidate the circuit's principal against the DB every 30 min.
builder.Services.AddScoped<AuthenticationStateProvider, AdminAuthStateProvider>();

// License issuance (E2-T5): the /licenses/new screen resolves LicenseIssuanceService,
// which needs the EF-backed store. Scoped — one short-lived context per issuance.
builder.Services.AddScoped<ILicenseStore, EfLicenseStore>();
builder.Services.AddScoped<LicenseIssuanceService>();

// Seeds the first SuperAdmin on startup from Admin:BootstrapEmail / Admin:BootstrapPassword
// when admin_users is empty; a no-op once a real admin exists or the config is absent.
builder.Services.AddHostedService<AdminSeeder>();

// Notification Settings screen (E3): SMTP config is admin-editable data, not static
// app config, so it lives in the DB (NotificationConfig) with the password encrypted
// via Data Protection rather than in appsettings/user-secrets.
builder.Services.AddScoped<INotificationConfigStore, EfNotificationConfigStore>();
builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();
builder.Services.AddScoped<NotificationConfigService>();

var app = builder.Build();

// Must run before anything that inspects Request.Scheme/IsHttps (HSTS redirect, the
// cookie's CookieSecurePolicy.Always check, absolute-URL generation) — nginx terminates
// TLS and proxies to Kestrel over plain loopback HTTP, so without this the app sees every
// request as HTTP and the auth cookie (Secure in Production) never gets accepted by the
// browser. Default KnownProxies/KnownNetworks already trust loopback, which is exactly
// where nginx proxies from here.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    // With HSTS on we assume the prod front is HTTPS; make the process reject plaintext.
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

// Exposes the implicit Program class to the test project so
// WebApplicationFactory<Program> can boot the real pipeline
// (AuthorizationPipelineTests) without InternalsVisibleTo.
public partial class Program { }
