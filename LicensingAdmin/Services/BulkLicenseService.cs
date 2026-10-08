using ClosedXML.Excel;
using LicensingAdmin.Licensing;
using LicensingCore.Entities;

namespace LicensingAdmin.Services;

public class BulkLicenseService(LicenseIssuanceService issuanceService)
{
    public async Task<(byte[] FileContent, List<License> Licenses)> GenerateAsync(
        Guid productId,
        Guid versionId,
        int quantity,
        string issuedBy,
        IProgress<double> progress,
        CancellationToken ct)
    {
        var generatedLicenses = new List<License>();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Licencias");
        worksheet.Cell(1, 1).Value = "License Key";
        worksheet.Cell(1, 2).Value = "Version ID";

        for (int i = 0; i < quantity; i++)
        {
            var request = new LicenseIssuanceRequest
            {
                ExistingProductId = productId,
                VersionId = versionId,
                Model = LicenseModel.Machine, // Modelo por defecto
                MaxActivations = 5,
                IssuedBy = issuedBy
            };

            var license = await issuanceService.IssueAsync(request, ct);
            generatedLicenses.Add(license);

            worksheet.Cell(i + 2, 1).Value = license.LicenseKey;
            worksheet.Cell(i + 2, 2).Value = license.VersionId.ToString();

            progress.Report((double)(i + 1) / quantity * 100);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), generatedLicenses);
    }
}
