using JuliMvs.App.Services;
using JuliMvs.Core.Camera;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;
using JuliMvs.Vision;
using OpenCvSharp;
using System.Text.Json;

VerifyOldPublishedDataPathResolvesUnderCurrentBaseDirectory();
VerifyMigratedTemplatePathResolvesFromTemplatesDirectory();
VerifyExistingPathIsPreserved();
VerifyUnmatchedMissingPathIsPreserved();
VerifyMissingTemplateImageBlocksProductionSetup();
VerifyLocalSettingsKeepsCurrentProductName();
VerifyLegacyLocalSettingsDefaultsCurrentProductName();
VerifyProductionOkDoesNotSaveImages();
VerifyProductionNgSavesOnlyDiagnosticImage();
VerifyManualInspectionKeepsExistingImageBehavior();
VerifyClearCalibrationDisablesAllCalibrationButKeepsProductionSettings();
VerifyCalibrationBoardDetectionPrefersLowestRmsGrid();

Console.WriteLine("App services keep template images portable, local settings backward-compatible, production image saving limited, and calibration clearing safe.");

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

static void VerifyMigratedTemplatePathResolvesFromTemplatesDirectory()
{
    var testRoot = CreateTempDirectory();
    var oldTemplatePath = Path.Combine(
        testRoot,
        "JuliMvs_App_20260520_155148",
        "Data",
        "Camera",
        "20260520",
        "camera-160941629.bmp");
    var stableTemplatePath = Path.Combine(
        testRoot,
        "JuliMvs_App_20260520_165347",
        "Data",
        "Templates",
        "PART-A",
        "BATCH-1",
        "camera-160941629.bmp");
    Directory.CreateDirectory(Path.GetDirectoryName(stableTemplatePath)!);
    File.WriteAllText(stableTemplatePath, "template image placeholder");

    var resolver = new TemplateImagePathResolver(Path.Combine(testRoot, "JuliMvs_App_20260520_165347"));
    var resolved = resolver.ResolvePath(oldTemplatePath);

    AssertEqual(stableTemplatePath, resolved, "migrated template path");
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

static void VerifyMissingTemplateImageBlocksProductionSetup()
{
    var parameters = VisionParameters.Default with
    {
        CameraCalibration = new CameraCalibration
        {
            Enabled = true,
            CalibrationId = "camera-1",
            SourceDistortionCalibrationId = string.Empty
        },
        RAxisCenterCalibration = new RAxisCenterCalibration
        {
            Enabled = true,
            SourceCameraCalibrationId = "camera-1"
        }
    };
    var template = new PartTemplate(
        Guid.NewGuid(),
        "BATCH-1",
        "PART-A",
        Path.Combine(CreateTempDirectory(), "missing-template.bmp"),
        DateTimeOffset.Now,
        100,
        100,
        0,
        0,
        "camera-1",
        string.Empty,
        0,
        10,
        10,
        100,
        1,
        ImageRoi.Empty,
        parameters);

    var setup = OpenCvVisionService.ValidateProductionSetup(template, parameters);

    AssertBoolEqual(false, setup.IsReady, "missing template image setup ready");
    AssertEqual(ProductionSetupBlockReason.TemplateImageMissing.ToString(), setup.Reason.ToString(), "missing template image reason");
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

static void VerifyClearCalibrationDisablesAllCalibrationButKeepsProductionSettings()
{
    var settings = new MachineSettings
    {
        LensDistortionCalibration = new LensDistortionCalibration
        {
            Enabled = true,
            CalibrationId = "distortion-1",
            ImageWidth = 2448,
            ImageHeight = 2048,
            CameraMatrix = [1, 0, 0, 0, 1, 0, 0, 0, 1],
            DistortionCoefficients = [0.1, 0.2],
            RmsReprojectionErrorPixels = 0.2,
            CapturedImageCount = 12,
            CreatedAt = DateTimeOffset.Now
        },
        CameraCalibration = new CameraCalibration
        {
            Enabled = true,
            CalibrationId = "camera-1",
            SourceDistortionCalibrationId = "distortion-1",
            RmsErrorMm = 0.03,
            Points =
            [
                new CalibrationPoint(100, 200, 0, 0),
                new CalibrationPoint(200, 200, 30, 0)
            ]
        },
        RAxisCenterCalibration = new RAxisCenterCalibration
        {
            Enabled = true,
            CalibrationId = "r-axis-1",
            SourceCameraCalibrationId = "camera-1",
            CenterXMm = 12.3,
            CenterYMm = 45.6,
            RmsErrorMm = 0.02,
            CaptureTarget = "target"
        },
        InvertXCompensation = true,
        InvertYCompensation = true,
        InvertRotationCompensation = true,
        BackSideNgEnabled = true,
        BackSideNgMinimumBackScore = 0.7,
        BackSideNgMaximumScoreDifference = -0.2,
        PlcOutputTransform = new PlcOutputTransform(Xx: -1.0, Yy: 1.2, RScale: 1.0)
    };

    var cleared = settings.ClearCalibration();

    AssertBoolEqual(false, cleared.LensDistortionCalibration?.Enabled == true, "cleared lens distortion");
    AssertBoolEqual(false, cleared.CameraCalibration.Enabled, "cleared camera calibration");
    AssertBoolEqual(false, cleared.RAxisCenterCalibration?.Enabled == true, "cleared R-axis center calibration");
    AssertBoolEqual(true, cleared.InvertXCompensation, "kept invert X");
    AssertBoolEqual(true, cleared.InvertYCompensation, "kept invert Y");
    AssertBoolEqual(true, cleared.InvertRotationCompensation, "kept invert R");
    AssertBoolEqual(true, cleared.BackSideNgEnabled, "kept backside NG");
    AssertDoubleEqual(0.7, cleared.BackSideNgMinimumBackScore, "kept backside min score");
    AssertDoubleEqual(-0.2, cleared.BackSideNgMaximumScoreDifference, "kept backside score diff");
    AssertDoubleEqual(-1.0, cleared.PlcOutputTransform?.Xx ?? 0, "kept PLC Xx");
    AssertDoubleEqual(1.2, cleared.PlcOutputTransform?.Yy ?? 0, "kept PLC Yy");
}

static void VerifyCalibrationBoardDetectionPrefersLowestRmsGrid()
{
    using var image = new Mat(new Size(640, 480), MatType.CV_8UC1, Scalar.Black);
    Cv2.Rectangle(image, new Rect(120, 80, 380, 320), Scalar.White, -1);
    Cv2.Rectangle(image, new Rect(170, 115, 280, 280), new Scalar(40), 8);

    for (var row = 0; row < 7; row++)
    {
        for (var column = 0; column < 7; column++)
        {
            Cv2.Circle(
                image,
                new Point(205 + column * 35, 150 + row * 35),
                11,
                Scalar.Black,
                -1);
        }
    }

    var interferencePoints = new[]
    {
        new Point(48, 42),
        new Point(585, 52),
        new Point(52, 428),
        new Point(582, 420),
        new Point(80, 118),
        new Point(548, 138),
        new Point(92, 310),
        new Point(530, 302)
    };
    foreach (var point in interferencePoints)
    {
        Cv2.Circle(image, point, 17, Scalar.White, -1);
        Cv2.Circle(image, point, 10, Scalar.Black, -1);
    }

    var service = new CalibrationBoardVisionService();
    using var result = service.DetectCircleGrid(image, rows: 7, columns: 7, spacingMm: 10.0);

    AssertIntEqual(49, result.DetectedPointCount, "detected calibration board points");
    AssertBoolEqual(result.RmsErrorPixels < 0.001, true, "calibration board RMS");
    AssertBoolEqual(result.XyDifferencePercent < 0.001, true, "calibration board XY difference");
    AssertDoubleEqual(205, result.Points[0].X, "top-left calibration X");
    AssertDoubleEqual(150, result.Points[0].Y, "top-left calibration Y");
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

static void AssertIntEqual(int expected, int actual, string name)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'");
    }
}

static void AssertDoubleEqual(double expected, double actual, string name)
{
    if (Math.Abs(expected - actual) > 0.000001)
    {
        throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'");
    }
}
