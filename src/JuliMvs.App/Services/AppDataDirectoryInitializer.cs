using System.IO;

namespace JuliMvs.App.Services;

internal static class AppDataDirectoryInitializer
{
    private static readonly string[] DataSubdirectories =
    [
        "Config",
        "Database",
        "Inspections",
        "Templates",
        "Camera",
        "Calibration",
        "CalibrationReports",
        "ChangeoverTemplateSelfChecks",
        "Diagnostics",
        "InspectionReports",
        "PassivePlcVerificationReports",
        "Logs"
    ];

    public static void EnsureDataDirectories(string baseDirectory)
    {
        foreach (var subdirectory in DataSubdirectories)
        {
            Directory.CreateDirectory(Path.Combine(baseDirectory, "Data", subdirectory));
        }
    }
}
