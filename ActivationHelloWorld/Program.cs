using System.Security.Cryptography;
using ActivationHelloWorld;

// A minimal Windows console client for LicensingApi — exercises the same contract a
// real activation DLL would (docs/activation-dll-integration-reference.md): read
// hardware, POST /api/activate, verify the returned license file's signature, then
// periodically POST /api/checkin. Built to drive end-to-end lifecycle testing (key
// creation happens in LicensingAdmin; this app covers activate / reactivate /
// hardware-drift / grace-period / revoke from the client's point of view) rather than
// as a production integration — a real DLL would harden retry/backoff and offline
// grace UX far more than this does.

var baseUrl = ArgOrEnv(args, "--base-url", "ACTIVATION_BASE_URL") ?? "https://licensing-api.miautrix.tech";
Console.WriteLine($"LicensingApi base URL: {baseUrl}");

var publicKey = RSA.Create();
publicKey.ImportFromPem(Keys.LicensingPublicKeyPem);

var state = LocalState.LoadOrNew();
using var client = new ActivationApiClient(baseUrl);

PrintMenu();
while (true)
{
    Console.Write("\n> ");
    var choice = Console.ReadLine()?.Trim();
    try
    {
        switch (choice)
        {
            case "1": await ActivateAsync(); break;
            case "2": await CheckinAsync(hardwareOverride: null); break;
            case "3": ShowStatus(); break;
            case "4": await SimulateDriftAsync(); break;
            case "5": ResetState(); break;
            case "0": return;
            default: PrintMenu(); break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.Message}");
    }
}

void PrintMenu()
{
    Console.WriteLine("""

        1) Activate a license key
        2) Check in (same hardware)
        3) Show current status (verify stored license file's signature)
        4) Simulate hardware drift, then check in
        5) Reset local state (new InstallGuid, forget activation)
        0) Exit
        """);
}

async Task ActivateAsync()
{
    Console.Write($"License key{(state.LicenseKey is null ? "" : $" [{state.LicenseKey}]")}: ");
    var input = Console.ReadLine()?.Trim();
    var key = string.IsNullOrWhiteSpace(input) ? state.LicenseKey : input;
    if (string.IsNullOrWhiteSpace(key))
    {
        Console.WriteLine("No license key provided.");
        return;
    }

    var hardware = HardwareFingerprint.Read();
    PrintHardware(hardware);

    var (status, result) = await client.ActivateAsync(new ActivateRequest
    {
        LicenseKey = key,
        InstallGuid = state.InstallGuid,
        Hardware = hardware,
        AppVersion = "hello-world-1.0",
        ClientTimestampUtc = DateTime.UtcNow,
    }, CancellationToken.None);

    PrintResult("activate", status, result);

    if (result.Success && result.Data is not null)
    {
        state.LicenseKey = key;
        state.ActivationId = result.Data.ActivationId;
        state.LastLicenseFileBase64 = result.Data.LicenseFileBase64;
        state.LastStatus = result.Data.Status;
        state.Save();
        TryShowLicenseFile(result.Data.LicenseFileBase64);
    }
}

async Task CheckinAsync(HardwareInfo? hardwareOverride)
{
    if (state.ActivationId is null)
    {
        Console.WriteLine("No activation on record — activate first.");
        return;
    }

    var hardware = hardwareOverride ?? HardwareFingerprint.Read();
    PrintHardware(hardware);

    var (status, result) = await client.CheckinAsync(new CheckinRequest
    {
        ActivationId = state.ActivationId.Value,
        InstallGuid = state.InstallGuid,
        Hardware = hardware,
        ClientTimestampUtc = DateTime.UtcNow,
    }, CancellationToken.None);

    PrintResult("checkin", status, result);

    if (result.Success && result.Data is not null)
    {
        // Locked responses carry no license file — keep the last known-good one locally
        // rather than overwrite it with null, same as a real client should.
        if (!string.IsNullOrEmpty(result.Data.LicenseFileBase64))
            state.LastLicenseFileBase64 = result.Data.LicenseFileBase64;
        state.LastStatus = result.Data.Status;
        state.Save();
        TryShowLicenseFile(result.Data.LicenseFileBase64);
    }
}

async Task SimulateDriftAsync()
{
    var drifted = HardwareFingerprint.Read();
    drifted.CpuId += "-DRIFTED";
    drifted.MotherboardSerial += "-DRIFTED";
    Console.WriteLine("(hardware fingerprint modified locally to simulate a component swap)");
    await CheckinAsync(drifted);
}

void ShowStatus()
{
    Console.WriteLine($"LicenseKey:    {state.LicenseKey ?? "(none)"}");
    Console.WriteLine($"InstallGuid:   {state.InstallGuid}");
    Console.WriteLine($"ActivationId:  {state.ActivationId?.ToString() ?? "(none)"}");
    Console.WriteLine($"LastStatus:    {state.LastStatus ?? "(none)"}");
    TryShowLicenseFile(state.LastLicenseFileBase64);
}

void TryShowLicenseFile(string? base64)
{
    if (string.IsNullOrEmpty(base64))
    {
        Console.WriteLine("(no license file to show)");
        return;
    }
    var file = LicenseFile.VerifyAndParse(base64, publicKey);
    Console.WriteLine($"  Signature verified: {file.SignatureVerified}");
    Console.WriteLine($"  Status (in file):   {file.Status}");
    Console.WriteLine($"  SubscriptionExpiry: {file.SubscriptionExpiryUtc?.ToString("u") ?? "(perpetual)"}");
    Console.WriteLine($"  IssuedAtUtc:        {file.IssuedAtUtc:u}");
}

void ResetState()
{
    state = new LocalState { InstallGuid = Guid.NewGuid() };
    state.Save();
    Console.WriteLine("Local state reset. New InstallGuid: " + state.InstallGuid);
}

static void PrintHardware(HardwareInfo hw) =>
    Console.WriteLine($"Hardware: cpu={hw.CpuId} mb={hw.MotherboardSerial} tpm={hw.TpmId} mac={hw.MacAddressPrimary}");

static void PrintResult(string op, int httpStatus, ApiResult result)
{
    Console.WriteLine($"{op} -> HTTP {httpStatus}, success={result.Success}, code={result.Code}");
    if (!result.Success) Console.WriteLine($"  message: {result.Message}");
    if (result.Data?.Reason is not null) Console.WriteLine($"  reason: {result.Data.Reason}");
    if (result.Data?.ReviewDeadlineUtc is not null) Console.WriteLine($"  reviewDeadlineUtc: {result.Data.ReviewDeadlineUtc:u}");
}

static string? ArgOrEnv(string[] args, string flag, string envVar)
{
    var idx = Array.IndexOf(args, flag);
    if (idx >= 0 && idx + 1 < args.Length) return args[idx + 1];
    return Environment.GetEnvironmentVariable(envVar);
}
