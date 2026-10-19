using ClosedXML.Excel;
using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicensingAdmin.Reports;

/// <summary>
/// Builds the full licensing report workbook (E3 item 5): every license and every
/// activation, including hardware fingerprint and customer contact fields, so the
/// business side has one file to hand to finance/support without a database query.
/// Two sheets rather than one flat one — a license with N activations would otherwise
/// repeat its own columns N times, which is noise for the "how many licenses do we
/// have" question and only useful for the "which machines are on this license" one.
/// </summary>
public sealed class LicenseExportService(IDbContextFactory<AppDbContext> factory)
{
    /// <summary>
    /// Builds the workbook and returns it as an in-memory .xlsx byte array, ready to
    /// stream back as a file download.
    /// </summary>
    public async Task<byte[]> BuildWorkbookAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var licenses = await db.Licenses
            .Include(l => l.Product)
            .Include(l => l.Activations)
            .OrderBy(l => l.Product.Name).ThenBy(l => l.LicenseKey)
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();

        var licensesSheet = workbook.Worksheets.Add("Licenses");
        string[] licenseHeaders =
        [
            "License Key", "Product", "Model", "Max Activations", "Used Activations",
            "Status", "Subscription Expiry (UTC)", "Days Until Expiry", "Customer Name",
            "Customer Email", "Created (UTC)", "Revoked (UTC)", "Revoked Reason",
        ];
        for (var i = 0; i < licenseHeaders.Length; i++)
        {
            licensesSheet.Cell(1, i + 1).Value = licenseHeaders[i];
        }

        var row = 2;
        var today = DateTime.UtcNow.Date;
        foreach (var l in licenses)
        {
            var used = l.Activations.Count(a => a.Status == ActivationStatus.Approved);
            var daysUntilExpiry = l.SubscriptionExpiryUtc.HasValue
                ? (int?)(l.SubscriptionExpiryUtc.Value.Date - today).TotalDays
                : null;

            licensesSheet.Cell(row, 1).Value = l.LicenseKey;
            licensesSheet.Cell(row, 2).Value = l.Product.Name;
            licensesSheet.Cell(row, 3).Value = l.ModelSnapshot.ToString();
            licensesSheet.Cell(row, 4).Value = l.MaxActivations;
            licensesSheet.Cell(row, 5).Value = used;
            licensesSheet.Cell(row, 6).Value = l.Status.ToString();
            licensesSheet.Cell(row, 7).Value = l.SubscriptionExpiryUtc?.ToString("yyyy-MM-dd") ?? "";
            licensesSheet.Cell(row, 8).Value = daysUntilExpiry;
            licensesSheet.Cell(row, 9).Value = l.CustomerName ?? "";
            licensesSheet.Cell(row, 10).Value = l.CustomerEmail ?? "";
            licensesSheet.Cell(row, 11).Value = l.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm");
            licensesSheet.Cell(row, 12).Value = l.RevokedAtUtc?.ToString("yyyy-MM-dd HH:mm") ?? "";
            licensesSheet.Cell(row, 13).Value = l.RevokedReason ?? "";
            row++;
        }
        licensesSheet.Row(1).Style.Font.Bold = true;
        licensesSheet.Columns().AdjustToContents();

        var activationsSheet = workbook.Worksheets.Add("Activations");
        string[] activationHeaders =
        [
            "License Key", "Product", "Customer Name", "Customer Email", "Install GUID",
            "CPU ID", "Motherboard Serial", "TPM ID", "MAC Address", "OS Type", "OS Version",
            "CPU Model", "RAM (GB)", "Is VM", "VM Signals", "Status", "First Activated (UTC)",
            "Last Check-in (UTC)", "Review Deadline (UTC)", "Approved (UTC)", "Rejected (UTC)",
            "Reviewed By", "Review Notes",
        ];
        for (var i = 0; i < activationHeaders.Length; i++)
        {
            activationsSheet.Cell(1, i + 1).Value = activationHeaders[i];
        }

        row = 2;
        foreach (var l in licenses)
        {
            foreach (var a in l.Activations.OrderBy(a => a.FirstActivatedAtUtc))
            {
                activationsSheet.Cell(row, 1).Value = l.LicenseKey;
                activationsSheet.Cell(row, 2).Value = l.Product.Name;
                activationsSheet.Cell(row, 3).Value = l.CustomerName ?? "";
                activationsSheet.Cell(row, 4).Value = l.CustomerEmail ?? "";
                activationsSheet.Cell(row, 5).Value = a.InstallGuid.ToString();
                activationsSheet.Cell(row, 6).Value = a.CpuId;
                activationsSheet.Cell(row, 7).Value = a.MotherboardSerial;
                activationsSheet.Cell(row, 8).Value = a.TpmId;
                activationsSheet.Cell(row, 9).Value = a.MacAddressPrimary;
                activationsSheet.Cell(row, 10).Value = a.OsType ?? "";
                activationsSheet.Cell(row, 11).Value = a.OsVersion ?? "";
                activationsSheet.Cell(row, 12).Value = a.CpuModel ?? "";
                activationsSheet.Cell(row, 13).Value = a.RamGb;
                activationsSheet.Cell(row, 14).Value = a.IsVm;
                activationsSheet.Cell(row, 15).Value = a.VmSignals ?? "";
                activationsSheet.Cell(row, 16).Value = a.Status.ToString();
                activationsSheet.Cell(row, 17).Value = a.FirstActivatedAtUtc.ToString("yyyy-MM-dd HH:mm");
                activationsSheet.Cell(row, 18).Value = a.LastCheckinAtUtc?.ToString("yyyy-MM-dd HH:mm") ?? "";
                activationsSheet.Cell(row, 19).Value = a.ReviewDeadlineUtc?.ToString("yyyy-MM-dd HH:mm") ?? "";
                activationsSheet.Cell(row, 20).Value = a.ApprovedAtUtc?.ToString("yyyy-MM-dd HH:mm") ?? "";
                activationsSheet.Cell(row, 21).Value = a.RejectedAtUtc?.ToString("yyyy-MM-dd HH:mm") ?? "";
                activationsSheet.Cell(row, 22).Value = a.ReviewedBy ?? "";
                activationsSheet.Cell(row, 23).Value = a.ReviewNotes ?? "";
                row++;
            }
        }
        activationsSheet.Row(1).Style.Font.Bold = true;
        activationsSheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
