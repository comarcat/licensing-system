using LicensingCore.Configuration;
using LicensingCore.Data;
using LicensingApi.Dtos;
using LicensingApi.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Fail loudly at startup (in Main, before the host is built) if the connection
// string is missing — not lazily on the first request that touches the DbContext.
var licensingDb = ConnectionStringGuard.Require(builder.Configuration.GetConnectionString("LicensingDb"));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(licensingDb));

builder.Services.AddControllers();

// --- Crypto key loading -------------------------------------------------
// DEV-ONLY approach: reads key material from configuration. For production,
// pull the RSA private key from a proper store (Azure Key Vault, a protected
// PFX on the host, etc.) — never commit real keys to appsettings.json.
// Note: license files are signed only (no AES encryption) as of 2026-09-13 — see
// the comment on LicenseFileService for why. There is no symmetric key to configure.
var rsaPrivateKeyPem = builder.Configuration["Crypto:RsaPrivateKeyPem"];

RSA signingKey;
if (!string.IsNullOrWhiteSpace(rsaPrivateKeyPem))
{
    signingKey = RSA.Create();
    signingKey.ImportFromPem(rsaPrivateKeyPem);
}
else
{
    // Fallback so the app still runs locally without a key configured yet —
    // replace with a real, persisted key before any real activation happens.
    signingKey = RSA.Create(2048);
}

builder.Services.AddSingleton(signingKey);
builder.Services.AddSingleton<ILicenseFileService>(new LicenseFileService(signingKey));
builder.Services.AddScoped<IHardwareMatchService, HardwareMatchService>();
builder.Services.AddScoped<ActivationService>();

// Per-client-IP throttle on /api/activate and /api/checkin (the only endpoints that
// take unauthenticated, attacker-reachable input). Relies on ForwardedHeaders below to
// see the real client IP through nginx/Cloudflare rather than the proxy's own address.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("activation", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(ApiResult.Fail(
            ResultCode.RateLimited, "Too many requests. Please retry after a short delay."));
        await context.HttpContext.Response.WriteAsync(body, ct);
    };
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseRateLimiter();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/", () => Results.Content(LicensingApi.RootPage.Html, "text/html"));

app.Run();

