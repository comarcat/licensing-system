using LicensingAdmin.Auth;
using LicensingCore.Configuration;
using LicensingCore.Data;
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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
