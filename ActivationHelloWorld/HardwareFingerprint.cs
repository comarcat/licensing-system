using System.Management;
using System.Net.NetworkInformation;

namespace ActivationHelloWorld;

/// <summary>
/// Reads the same 4 identifiers <c>LicensingApi</c> treats as "this machine" (see
/// docs/activation-dll-integration-reference.md §1) via WMI. Best-effort: a field that
/// can't be read on this box falls back to a clearly-labeled stable placeholder rather
/// than throwing, since the point of this app is to exercise the activation API, not to
/// be a production-grade fingerprinting library.
/// </summary>
public static class HardwareFingerprint
{
    public static HardwareInfo Read()
    {
        return new HardwareInfo
        {
            CpuId = QuerySingle("Win32_Processor", "ProcessorId") ?? "CPU-UNAVAILABLE",
            MotherboardSerial = QuerySingle("Win32_BaseBoard", "SerialNumber") ?? "MB-UNAVAILABLE",
            TpmId = ReadTpmId(),
            MacAddressPrimary = ReadPrimaryMac(),
            OsType = "Windows",
            OsVersion = Environment.OSVersion.VersionString,
            CpuModel = QuerySingle("Win32_Processor", "Name"),
            RamGb = ReadRamGb(),
        };
    }

    private static string? QuerySingle(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var value = obj[property]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }
        catch
        {
            // WMI can be unavailable (locked-down policy, non-admin, virtualized edge
            // cases) — fall through to the caller's placeholder rather than crash.
        }
        return null;
    }

    private static string ReadTpmId()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2\Security\MicrosoftTpm", "SELECT ManufacturerId, PhysicalPresenceVersionInfo FROM Win32_Tpm");
            foreach (ManagementObject obj in searcher.Get())
            {
                var manufacturer = obj["ManufacturerId"]?.ToString();
                if (!string.IsNullOrWhiteSpace(manufacturer))
                    return $"TPM-{manufacturer}";
            }
        }
        catch
        {
            // No TPM, no access to the MicrosoftTpm namespace, or running non-elevated —
            // all common and all fine for a Hello World harness.
        }

        // Stable-per-machine placeholder — consistently empty/same on this box across
        // runs, per the integration doc's guidance for fields that can't be read reliably.
        return $"TPM-FALLBACK-{Environment.MachineName}";
    }

    private static string ReadPrimaryMac()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && n.GetPhysicalAddress().GetAddressBytes().Length == 6)
            .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
            .FirstOrDefault();

        var bytes = nic?.GetPhysicalAddress().GetAddressBytes();
        return bytes is null ? "MAC-UNAVAILABLE" : string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    private static int? ReadRamGb()
    {
        var raw = QuerySingle("Win32_ComputerSystem", "TotalPhysicalMemory");
        return long.TryParse(raw, out var bytes) ? (int)(bytes / 1024 / 1024 / 1024) : null;
    }
}
