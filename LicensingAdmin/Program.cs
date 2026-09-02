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
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("LicensingDb")));

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
