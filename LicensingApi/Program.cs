using LicensingCore.Data;
using LicensingApi.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("LicensingDb")));

builder.Services.AddControllers();

// --- Crypto key loading -------------------------------------------------
// DEV-ONLY approach: reads key material from configuration. For production,
// pull the RSA private key from a proper store (Azure Key Vault, a protected
// PFX on the host, etc.) — never commit real keys to appsettings.json.
var rsaPrivateKeyPem = builder.Configuration["Crypto:RsaPrivateKeyPem"];
var aesKeyBase64 = builder.Configuration["Crypto:AesKeyBase64"];

RSA signingKey;
byte[] aesKey;
if (!string.IsNullOrWhiteSpace(rsaPrivateKeyPem) && !string.IsNullOrWhiteSpace(aesKeyBase64))
{
    signingKey = RSA.Create();
    signingKey.ImportFromPem(rsaPrivateKeyPem);
    aesKey = Convert.FromBase64String(aesKeyBase64);
}
else
{
    // Fallback so the app still runs locally without keys configured yet —
    // replace with real, persisted keys before any real activation happens.
    signingKey = RSA.Create(2048);
    aesKey = RandomNumberGenerator.GetBytes(32);
}

builder.Services.AddSingleton(signingKey);
builder.Services.AddSingleton<ILicenseFileService>(new LicenseFileService(signingKey, aesKey));
builder.Services.AddScoped<IHardwareMatchService, HardwareMatchService>();
builder.Services.AddScoped<ActivationService>();

var app = builder.Build();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

