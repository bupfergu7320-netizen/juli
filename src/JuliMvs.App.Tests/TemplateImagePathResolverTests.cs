using JuliMvs.App.Services;
using JuliMvs.Core.Camera;
using JuliMvs.Core.Inspection;
using System.Text.Json;

VerifyOldPublishedDataPathResolvesUnderCurrentBaseDirectory();
VerifyExistingPathIsPreserved();
VerifyUnmatchedMissingPathIsPreserved();
VerifyLocalSettingsKeepsCurrentProductName();
VerifyLegacyLocalSettingsDefaultsCurrentProductName();
VerifyProductionOkDoesNotSaveImages();
VerifyProductionNgSavesOnlyDiagnosticImage();
VerifyManualInspectionKeepsExistingImageBehavior();

Console.WriteLine("App services keep template images portable, local settings backward-compatible, and production image saving limited.");

static void VerifyOldPublishedDataPathResolvesUnderCurrentBaseDirectory()
{
    var testRoot = CreateTempDirectory();
    var currentBaseDirectory = Path.Combine(testRoot, "JuliMvs_App_20260520_165347");
    var currentTemplatePath = Path.Combine(
        currentBaseDirectory,
        "Data",
        "Camera",
        "20260520",
        "camera-160941629.bmp");
    Directory.CreateDirectory(Path.GetDirectoryName(currentTemplatePath)!);
    File.WriteAllText(currentTemplatePath, "template image placeholder");

    var oldPublishedPath = Path.Combine(
        testRoot,
        "JuliMvs_App_20260520_155148",
        "Data",
        "Camera",
        "20260520",
        "camera-160941629.bmp");

    var resolver = new TemplateImagePathResolver(currentBaseDirectory);
    var resolved = resolver.ResolvePath(oldPublishedPath);

    AssertEqual(currentTemplatePath, resolved, "old published Data path");
}

static void VerifyExistingPathIsPreserved()
{
    var testRoot = CreateTempDirectory();
    var existingPath = Path.Combine(testRoot, "Data", "Camera", "image.bmp");
    Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
    File.WriteAllText(existingPath, "template image placeholder");

    var resolver = new TemplateImagePathResolver(testRoot);
    var resolved = resolver.ResolvePath(existingPath);

    AssertEqual(existingPath, resolved, "existing path");
}

static void VerifyUnmatchedMissingPathIsPreserved()
{
    var testRoot = CreateTempDirectory();
    var missingPath = Path.Combine(testRoot, "Legacy", "Camera", "image.bmp");

    var resolver = new TemplateImagePathResolver(testRoot);
    var resolved = resolver.ResolvePath(missingPath);

    AssertEqual(missingPath, resolved, "unmatched missing path");
}

static void VerifyLocalSettingsKeepsCurrentProductName()
{
    var testRoot = CreateTempDirectory();
    var store = new LocalAppSettingsStore(testRoot, new JsonSerializerOptions { WriteIndented = true });
    store.Save(new LocalAppSettings(
        "192.168.10.11",
        "192.168.3.40",
        502,
        CameraAcquisitionSettings.Default,
        "MODEL-2026"));

    var loaded = store.Load() ?? throw new InvalidOperationException("settings should load");

    AssertEqual("MODEL-2026", loaded.CurrentProductName, "current product name");
}

static void VerifyLegacyLocalSettingsDefaultsCurrentProductName()
{
    var testRoot = CreateTempDirectory();
    var configDirectory = Path.Combine(testRoot, "Data", "Config");
    Directory.CreateDirectory(configDirectory);
    File.WriteAllText(
        Path.Combine(configDirectory, "appsettings.json"),
        """
        {
          "CameraIpAddress": "192.168.10.11",
          "PlcIpAddress": "192.168.3.40",
          "PlcPort": 502,
          "CameraSettings": {
            "ExposureTimeMicroseconds": 8000,
            "Gain": 0,
            "CaptureDelaySeconds": 0.3,
            "AutoExposureTarget": 255,
            "AutoExposureEnabled": false
          }
        }
        """);

    var store = new LocalAppSettingsStore(testRoot, new JsonSerializerOptions());
    var loaded = store.Load() ?? throw new InvalidOperationException("legacy settings should load");

    AssertEqual(null, loaded.CurrentProductName, "legacy current product name");
}

static void VerifyProductionOkDoesNotSaveImages()
{
    var decision = InspectionImageSavePolicy.Decide(writeToPlc: true, InspectionDecision.Ok);

    AssertBoolEqual(false, decision.KeepIncomingRawImagePath, "production OK raw image path");
    AssertBoolEqual(false, decision.SaveDiagnosticImage, "production OK diagnostic image");
    AssertEqual(
        "\u751f\u4ea7OK\u4e0d\u4fdd\u5b58\u56fe\u7247\uff0c\u53ea\u4fdd\u5b58\u68c0\u6d4b\u8bb0\u5f55\u3002",
        decision.ProductionLogMessage,
        "production OK log");
}

static void VerifyProductionNgSavesOnlyDiagnosticImage()
{
    var decision = InspectionImageSavePolicy.Decide(writeToPlc: true, InspectionDecision.Ng);

    AssertBoolEqual(false, decision.KeepIncomingRawImagePath, "production NG raw image path");
    AssertBoolEqual(true, decision.SaveDiagnosticImage, "production NG diagnostic image");
    AssertEqual(null, decision.ProductionLogMessage, "production NG log");
}

static void VerifyManualInspectionKeepsExistingImageBehavior()
{
    var decision = InspectionImageSavePolicy.Decide(writeToPlc: false, InspectionDecision.Ok);

    AssertBoolEqual(true, decision.KeepIncomingRawImagePath, "manual raw image path");
    AssertBoolEqual(true, decision.SaveDiagnosticImage, "manual diagnostic image");
    AssertEqual(null, decision.ProductionLogMessage, "manual log");
}

static string CreateTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "JuliMvs.App.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void AssertEqual(string? expected, string? actual, string name)
{
    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'");
    }
}

static void AssertBoolEqual(bool expected, bool actual, string name)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'");
    }
}
