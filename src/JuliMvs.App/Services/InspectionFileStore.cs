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
}
