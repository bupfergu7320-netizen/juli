using System.IO;
using OpenCvSharp;

namespace JuliMvs.App.Services;

internal sealed class InspectionFileStore
{
    private readonly string _baseDirectory;

    public InspectionFileStore(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    public string SaveDiagnosticImage(Mat diagnostic, string batchNo, string partNo)
    {
        var directory = Path.Combine(_baseDirectory, "Data", "Inspections", DateTime.Now.ToString("yyyyMMdd"), batchNo, "result");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{partNo}.bmp");
        Cv2.ImWrite(path, diagnostic);
        return path;
    }

    public string SaveInspectionRawImage(Mat image, string batchNo, string partNo)
    {
        var directory = Path.Combine(_baseDirectory, "Data", "Inspections", DateTime.Now.ToString("yyyyMMdd"), batchNo, "raw");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{partNo}.bmp");
        Cv2.ImWrite(path, image);
        return path;
    }

    public string SaveTemplateImage(Mat image, string productName, string batchNo, Guid templateId)
    {
        var directory = Path.Combine(
            _baseDirectory,
            "Data",
            "Templates",
            SanitizePathSegment(productName),
            SanitizePathSegment(batchNo));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMddHHmmssfff}-{templateId:N}.bmp");
        Cv2.ImWrite(path, image);
        return path;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "template" : sanitized;
    }
}
