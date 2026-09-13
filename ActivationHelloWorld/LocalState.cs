using System.Text.Json;

namespace ActivationHelloWorld;

/// <summary>What a real client tool would persist locally between runs.</summary>
public class LocalState
{
    public string? LicenseKey { get; set; }
    public Guid InstallGuid { get; set; }
    public Guid? ActivationId { get; set; }
    public string? LastLicenseFileBase64 { get; set; }
    public string? LastStatus { get; set; }

    private static string PathFor() =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "hello-world-state.json");

    public static LocalState LoadOrNew()
    {
        var path = PathFor();
        if (File.Exists(path))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<LocalState>(File.ReadAllText(path));
                if (loaded is not null) return loaded;
            }
            catch
            {
                // Corrupt/old-format state file — start fresh rather than crash.
            }
        }
        return new LocalState { InstallGuid = Guid.NewGuid() };
    }

    public void Save() =>
        File.WriteAllText(PathFor(), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
