using ClosedXML.Excel;
using LicensingAdmin.Licensing;
using LicensingAdmin.Products;
using LicensingCore.Entities;

namespace LicensingAdmin.Services;

public class BulkLicenseService(LicenseIssuanceService issuanceService, IProductStore productStore)
{
    public async Task<(byte[] FileContent, List<License> Licenses)> GenerateAsync(
        Guid productId,
        Guid versionId,
        int quantity,
        LicenseStatus status,
        string issuedBy,
        IProgress<double> progress,
        CancellationToken ct)
    {
        var product = await productStore.FindByIdAsync(productId, ct)
            ?? throw new InvalidOperationException($"No product with id '{productId}'.");
        var version = (await productStore.ListVersionsAsync(productId, ct)).First(v => v.Id == versionId);
        var generatedLicenses = new List<License>();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Licencias");
        worksheet.Cell(1, 1).Value = "License Key";
        worksheet.Cell(1, 2).Value = "Product";
        worksheet.Cell(1, 3).Value = "Version";

        for (int i = 0; i < quantity; i++)
        {
            var request = new LicenseIssuanceRequest
            {
                ExistingProductId = productId,
                VersionId = versionId,
                Model = LicenseModel.Machine, // Modelo por defecto
                MaxActivations = 5,
                Status = status,
                IssuedBy = issuedBy
            };

            var license = await issuanceService.IssueAsync(request, ct);
            generatedLicenses.Add(license);

            worksheet.Cell(i + 2, 1).Value = license.LicenseKey;
            worksheet.Cell(i + 2, 2).Value = product.Name;
            worksheet.Cell(i + 2, 3).Value = version.Name;

            progress.Report((double)(i + 1) / quantity * 100);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), generatedLicenses);
    }
}
