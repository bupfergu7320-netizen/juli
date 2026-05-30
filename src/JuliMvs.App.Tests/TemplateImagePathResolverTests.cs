using JuliMvs.App.Services;
using JuliMvs.Core;
using JuliMvs.Core.Camera;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Persistence;
using JuliMvs.Core.Vision;
using JuliMvs.Persistence;
using JuliMvs.Plc;
using JuliMvs.Vision;
using OpenCvSharp;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using JuliMvs.App.Tests;

if (args.Length > 0 &&
    string.Equals(args[0], "synthetic-field-images", StringComparison.OrdinalIgnoreCase))
{
    var firstImagePath = args.Length > 1
        ? args[1]
        : @"C:\Users\Administrator\xwechat_files\wxid_ubp8dkceud5322_b31c\temp\RWTemp\2026-05\9e20f478899dc29eb19741386f9343c8\f97084a0ed1528c921a9845e50453875.jpg";
    var secondImagePath = args.Length > 2
        ? args[2]
        : @"C:\Users\Administrator\xwechat_files\wxid_ubp8dkceud5322_b31c\temp\RWTemp\2026-05\9e20f478899dc29eb19741386f9343c8\47ebb7754d28474c13b081264d0b6652.jpg";
    var outputDirectory = args.Length > 3
        ? args[3]
        : Path.Combine(@"D:\JuliMvsCalibrationPlcChangeover", "DATA", "SyntheticFieldImageTests", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    SyntheticFieldImageTest.Run(firstImagePath, secondImagePath, outputDirectory);
    return;
}

if (args.Length > 1 &&
    string.Equals(args[0], "field-speed", StringComparison.OrdinalIgnoreCase))
{
    FieldSpeedBenchmark.Run(args[1], args.Skip(2).ToArray());
    return;
}

if (args.Length > 2 &&
    string.Equals(args[0], "field-fallback", StringComparison.OrdinalIgnoreCase))
{
    FieldFallbackExperiment.Run(args[1], args[2]);
    return;
}

if (args.Length > 2 &&
    string.Equals(args[0], "field-align", StringComparison.OrdinalIgnoreCase))
{
    var outputDirectory = args.Length > 3
        ? args[3]
        : Path.Combine(@"D:\JuliMvsCalibrationPlcChangeover", "DATA", "FieldAlignment", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    FieldAlignmentOverlay.Run(args[1], args[2], outputDirectory);
    return;
}

if (args.Length > 0 &&
    string.Equals(args[0], "synthetic-four-way-ellipse", StringComparison.OrdinalIgnoreCase))
{
    var outputDirectory = args.Length > 1
        ? args[1]
        : Path.Combine(@"D:\JuliMvsCalibrationPlcChangeover", "DATA", "SyntheticFourWayEllipseTests", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    SyntheticFourWayEllipseTest.Run(outputDirectory);
    return;
}

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
VerifyPlcValidationSkipsTemplateWhenVisionJudgmentDisabled();
VerifyProductionInspectionCreatesOkXyrCorrection();
VerifyProductionInspectionCreatesBackSideNgZeroCorrection();
VerifyProductionInspectionCreatesUnsafeXyrNgZeroCorrection();
VerifyClearCalibrationDisablesAllCalibrationButKeepsProductionSettings();
VerifyProductRecipeKeepsBackSideNgPerProduct();
VerifyProductRecipeSaveFromTemplateKeepsTemplateBackSideNg();
VerifyProductRecipeKeepsFourWaySymmetricPerProduct();
VerifyProductRecipeDefaultsAngleDetectionToAuto();
VerifyAutoAngleStrategyClassifiesMixedRoundParts();
VerifyProductRecipeClearsLegacyFrontBump();
VerifyBackSideNgDoesNotRequireSelectedFrontBump();
VerifyContourFeatureExtractorClassifiesStrongEllipse();
VerifyContourFeatureExtractorKeepsWeakRoundRDetection();
VerifyContourRadiusSignatureMatchesRotation();
VerifyProductionContourReliabilityRejectsFieldAngleJump();
VerifyProductionContourReliabilityAllowsLowScoreForXyrOutput();
VerifyProductionContourReliabilityAcceptsReliableContour();
VerifyProductionContourReliabilityAcceptsLargePositionOffset();
VerifyProductionContourReliabilityDoesNotLockWeakRoundR();
VerifyProductionShapeResolverUsesShapeMatch();
VerifyProductionShapeResolverUsesFourWaySymmetricR();
VerifyProductionShapeResolverAllowsFourWayAxisSpreadWithChamfer();
VerifyProductionShapeResolverKeepsFullFourWayDefectAlignmentAngle();
VerifyFourWayAxisDiffUses180Equivalent();
VerifyProductionShapeResolverAcceptsLowSeparationFourWayEllipse();
VerifyContourShapeMatcherRefinesSubpixelTranslation();
VerifyProductionAutoAngleResolverUsesRadiusAssist();
VerifyProductionAutoAngleResolverUsesConservativeEllipseRadiusAssist();
VerifyProductionAutoAngleResolverSkipsBadRadiusAssist();
VerifyProductionShapeResolverRejectsAmbiguousShape();
VerifyProductionShapeResolverDetectsBackSideWithShapeMatch();
VerifyProductionAutoAngleResolverRejectsWeakRoundWithoutDirection();
VerifyProductionMissingMaterialDetectorAcceptsMatchingContour();
VerifyProductionMissingMaterialDetectorUsesFullShapeAngleForFourWayAlignment();
VerifyProductionMissingMaterialDetectorRejectsMissingOrChippedEdge();
VerifyProductionMissingMaterialDetectorIgnoresShallowEdgeDifference();
VerifyProductionMissingMaterialDetectorIgnoresExtraBurr();
VerifyProductionMissingMaterialCoarseFallbackAcceptsVisibleOkAreaDrift();
VerifyProductionMissingMaterialCoarseFallbackRejectsVisibleEdgeLoss();
VerifyPlcOutputDirectionSettingsApplySimpleXySigns();
VerifyDirectionFormatterShowsXySigns();
VerifyProductTemplateNameIsUniqueAndRebuildOverwrites();
VerifyLegacyDuplicateProductTemplatesKeepNewest();
VerifyMostRecentTemplateLoadsNewestAcrossProducts();
VerifyTemplateSelectionTextShowsOnlyNameAndTime();
VerifyPlcTriggerGateTreatsD1000HighAsSingleCapture();
VerifyCalibrationBoardDetectionPrefersLowestRmsGrid();
VerifyRAxisCenterResidualsIdentifyWorstAngle();

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

static void VerifyPlcValidationSkipsTemplateWhenVisionJudgmentDisabled()
{
    var validator = new PlcCaptureRequestValidator();

    var decision = validator.Validate(new PlcCaptureRequestState(
        ProductionEnabled: true,
        ChangeoverTemplateRequested: false,
        CameraConnected: true,
        TemplateLoaded: false,
        BatchCanInspect: false,
        VisionJudgmentDisabled: true));

    AssertEqual(PlcCaptureRequestAction.Proceed.ToString(), decision.Action.ToString(), "PLC validation bypass action");
}

static void VerifyProductionInspectionCreatesOkXyrCorrection()
{
    var sourceMeasurement = new InspectionMeasurement(
        CenterXPixel: 120,
        CenterYPixel: 240,
        XOffsetMm: 1.25,
        YOffsetMm: -0.75,
        XCompensationMm: -1.40,
        YCompensationMm: 0.60,
        AngleDegrees: 15.0,
        AngleOffsetDegrees: 5.0,
        RotationCompensationDegrees: -5.0,
        WidthMm: 20.0,
        HeightMm: 18.0,
        AreaPixels: 30000,
        MatchScore: 0.8);

    var result = ProductionInspectionResultFactory.CreateOk(
        "BATCH-1",
        sourceMeasurement,
        rawImagePath: @"D:\image.bmp",
        partNo: "PART-1");
    var measurement = result.Measurement ?? throw new InvalidOperationException("production result should have measurement");

    AssertEqual(InspectionDecision.Ok.ToString(), result.Decision.ToString(), "production XYR decision");
    AssertEqual(NgReason.None.ToString(), result.NgReason.ToString(), "production XYR NG reason");
    AssertDoubleEqual(1.25, measurement.XOffsetMm, "production XYR X offset");
    AssertDoubleEqual(-0.75, measurement.YOffsetMm, "production XYR Y offset");
    AssertDoubleEqual(5.0, measurement.AngleOffsetDegrees, "production XYR R offset");
    AssertDoubleEqual(-1.40, measurement.XCompensationMm, "production XYR X compensation");
    AssertDoubleEqual(0.60, measurement.YCompensationMm, "production XYR Y compensation");
    AssertDoubleEqual(-5.0, measurement.RotationCompensationDegrees, "production XYR R compensation");
}

static void VerifyProductionInspectionCreatesBackSideNgZeroCorrection()
{
    var result = ProductionInspectionResultFactory.CreateBackSideNg(
        "BATCH-1",
        "反面NG: test",
        @"D:\image.bmp",
        "PART-1");
    var measurement = result.Measurement ?? throw new InvalidOperationException("bypass NG result should have measurement");

    AssertEqual(InspectionDecision.Ng.ToString(), result.Decision.ToString(), "bypass back NG decision");
    AssertEqual(NgReason.BackSideDetected.ToString(), result.NgReason.ToString(), "bypass back NG reason");
    AssertEqual("BATCH-1", result.BatchNo, "bypass back NG batch");
    AssertEqual("PART-1", result.PartNo, "bypass back NG part");
    AssertEqual(@"D:\image.bmp", result.RawImagePath, "bypass back NG raw image");
    AssertDoubleEqual(0, measurement.XOffsetMm, "bypass back NG X offset");
    AssertDoubleEqual(0, measurement.YOffsetMm, "bypass back NG Y offset");
    AssertDoubleEqual(0, measurement.AngleOffsetDegrees, "bypass back NG R offset");
    AssertDoubleEqual(0, measurement.XCompensationMm, "bypass back NG X compensation");
    AssertDoubleEqual(0, measurement.YCompensationMm, "bypass back NG Y compensation");
    AssertDoubleEqual(0, measurement.RotationCompensationDegrees, "bypass back NG R compensation");
}

static void VerifyProductionInspectionCreatesUnsafeXyrNgZeroCorrection()
{
    var result = ProductionInspectionResultFactory.CreateUnsafeXyrNg(
        "BATCH-1",
        "轮廓角度NG: test",
        @"D:\image.bmp",
        "PART-1");
    var measurement = result.Measurement ?? throw new InvalidOperationException("unsafe XYR NG result should have measurement");

    AssertEqual(InspectionDecision.Ng.ToString(), result.Decision.ToString(), "unsafe XYR NG decision");
    AssertEqual(NgReason.MatchFailed.ToString(), result.NgReason.ToString(), "unsafe XYR NG reason");
    AssertEqual("BATCH-1", result.BatchNo, "unsafe XYR NG batch");
    AssertEqual("PART-1", result.PartNo, "unsafe XYR NG part");
    AssertEqual(@"D:\image.bmp", result.RawImagePath, "unsafe XYR NG raw image");
    AssertDoubleEqual(0, measurement.XOffsetMm, "unsafe XYR NG X offset");
    AssertDoubleEqual(0, measurement.YOffsetMm, "unsafe XYR NG Y offset");
    AssertDoubleEqual(0, measurement.AngleOffsetDegrees, "unsafe XYR NG R offset");
    AssertDoubleEqual(0, measurement.XCompensationMm, "unsafe XYR NG X compensation");
    AssertDoubleEqual(0, measurement.YCompensationMm, "unsafe XYR NG Y compensation");
    AssertDoubleEqual(0, measurement.RotationCompensationDegrees, "unsafe XYR NG R compensation");
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
        FourWaySymmetricEnabled = true,
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
    AssertBoolEqual(true, cleared.FourWaySymmetricEnabled, "kept four-way symmetric");
    AssertDoubleEqual(0.7, cleared.BackSideNgMinimumBackScore, "kept backside min score");
    AssertDoubleEqual(-0.2, cleared.BackSideNgMaximumScoreDifference, "kept backside score diff");
    AssertDoubleEqual(-1.0, cleared.PlcOutputTransform?.Xx ?? 0, "kept PLC Xx");
    AssertDoubleEqual(1.2, cleared.PlcOutputTransform?.Yy ?? 0, "kept PLC Yy");
}

static void VerifyProductRecipeKeepsBackSideNgPerProduct()
{
    var currentRuntime = VisionParameters.Default with
    {
        CameraCalibration = new CameraCalibration
        {
            Enabled = true,
            CalibrationId = "runtime-camera",
            Points =
            [
                new CalibrationPoint(100, 200, 0, 0)
            ]
        },
        RAxisCenterCalibration = new RAxisCenterCalibration
        {
            Enabled = true,
            CalibrationId = "runtime-r",
            SourceCameraCalibrationId = "runtime-camera",
            CenterXMm = 1.2,
            CenterYMm = 3.4
        },
        InvertRotationCompensation = true,
        BackSideNgEnabled = false
    };
    var productRecipe = VisionParameters.Default with
    {
        BackSideNgEnabled = true,
        BackSideNgMinimumBackScore = 0.58,
        BackSideNgMaximumScoreDifference = -0.06,
        InvertRotationCompensation = false
    };

    var saved = JuliMvs.Core.Persistence.ProductRecipeVisionParameters.ForSave(productRecipe);
    var applied = JuliMvs.Core.Persistence.ProductRecipeVisionParameters.ApplyToRuntime(currentRuntime, saved);

    AssertBoolEqual(true, saved.BackSideNgEnabled, "saved product backside NG");
    AssertDoubleEqual(0.58, saved.BackSideNgMinimumBackScore, "saved backside min score");
    AssertDoubleEqual(-0.06, saved.BackSideNgMaximumScoreDifference, "saved backside score diff");
    AssertBoolEqual(true, applied.BackSideNgEnabled, "loaded product backside NG");
    AssertBoolEqual(true, applied.CameraCalibration.Enabled, "kept runtime camera calibration");
    AssertBoolEqual(true, applied.RAxisCenterCalibration.Enabled, "kept runtime R-axis calibration");
    AssertBoolEqual(true, applied.InvertRotationCompensation, "kept global R direction");
}

static void VerifyProductRecipeSaveFromTemplateKeepsTemplateBackSideNg()
{
    var runtimeParameters = VisionParameters.Default with
    {
        BackSideNgEnabled = true,
        InvertRotationCompensation = true,
        CameraCalibration = new CameraCalibration
        {
            Enabled = true,
            CalibrationId = "runtime-camera",
            SourceDistortionCalibrationId = string.Empty
        },
        RAxisCenterCalibration = new RAxisCenterCalibration
        {
            Enabled = true,
            CalibrationId = "runtime-r",
            SourceCameraCalibrationId = "runtime-camera"
        }
    };
    var templateParameters = VisionParameters.Default with
    {
        BackSideNgEnabled = false,
        AngleDetectionMode = AngleDetectionMode.TemplateRotation
    };
    var template = new PartTemplate(
        Guid.NewGuid(),
        "BATCH-2",
        "MODEL-B",
        "template.bmp",
        DateTimeOffset.Now,
        100,
        100,
        0,
        0,
        "runtime-camera",
        string.Empty,
        0,
        10,
        10,
        100,
        1,
        ImageRoi.Empty,
        templateParameters);

    var saved = JuliMvs.Core.Persistence.ProductRecipeVisionParameters.ForSave(template, runtimeParameters);

    AssertBoolEqual(false, saved.BackSideNgEnabled, "template save keeps product backside NG off");
    AssertBoolEqual(false, saved.InvertRotationCompensation, "template save does not store global R invert");
    AssertIntEqual(
        (int)AngleDetectionMode.AutoPcaOrPolarRing,
        (int)saved.AngleDetectionMode,
        "template save defaults angle detection to auto");
}

static void VerifyProductRecipeKeepsFourWaySymmetricPerProduct()
{
    var currentRuntime = VisionParameters.Default with
    {
        CameraCalibration = new CameraCalibration
        {
            Enabled = true,
            CalibrationId = "runtime-camera"
        },
        RAxisCenterCalibration = new RAxisCenterCalibration
        {
            Enabled = true,
            CalibrationId = "runtime-r",
            SourceCameraCalibrationId = "runtime-camera"
        },
        FourWaySymmetricEnabled = false,
        InvertRotationCompensation = true
    };
    var productRecipe = VisionParameters.Default with
    {
        FourWaySymmetricEnabled = true,
        InvertRotationCompensation = false
    };

    var saved = JuliMvs.Core.Persistence.ProductRecipeVisionParameters.ForSave(productRecipe);
    var applied = JuliMvs.Core.Persistence.ProductRecipeVisionParameters.ApplyToRuntime(currentRuntime, saved);

    AssertBoolEqual(true, saved.FourWaySymmetricEnabled, "saved product four-way symmetric");
    AssertBoolEqual(true, applied.FourWaySymmetricEnabled, "loaded product four-way symmetric");
    AssertBoolEqual(true, applied.InvertRotationCompensation, "kept global R direction with four-way symmetric");
}

static void VerifyProductRecipeDefaultsAngleDetectionToAuto()
{
    var legacyRecipe = VisionParameters.Default with
    {
        AngleDetectionMode = AngleDetectionMode.OuterContour
    };

    var saved = JuliMvs.Core.Persistence.ProductRecipeVisionParameters.ForSave(legacyRecipe);
    var applied = JuliMvs.Core.Persistence.ProductRecipeVisionParameters.ApplyToRuntime(
        VisionParameters.Default,
        legacyRecipe);

    AssertIntEqual(
        (int)AngleDetectionMode.AutoPcaOrPolarRing,
        (int)saved.AngleDetectionMode,
        "saved recipe angle detection defaults to auto");
    AssertIntEqual(
        (int)AngleDetectionMode.AutoPcaOrPolarRing,
        (int)applied.AngleDetectionMode,
        "runtime recipe angle detection defaults to auto");
}

static void VerifyAutoAngleStrategyClassifiesMixedRoundParts()
{
    var strongEllipse = AutoAngleStrategy.Select(
        widthPixels: 240,
        heightPixels: 180,
        pcaRatio: 1.45,
        circularity: 0.82,
        templateRadiusSignalPixels: 8.0);
    AssertIntEqual(
        (int)AutoPartShapeClass.StrongEllipse,
        (int)strongEllipse.ShapeClass,
        "strong ellipse class");
    AssertIntEqual(
        (int)AutoAngleMethod.PcaAxis,
        (int)strongEllipse.Method,
        "strong ellipse method");
    AssertBoolEqual(true, strongEllipse.AllowsRCorrection, "strong ellipse R enabled");

    var irregularRound = AutoAngleStrategy.Select(
        widthPixels: 200,
        heightPixels: 198,
        pcaRatio: 1.02,
        circularity: 0.88,
        templateRadiusSignalPixels: 5.0);
    AssertIntEqual(
        (int)AutoPartShapeClass.IrregularRound,
        (int)irregularRound.ShapeClass,
        "irregular round class");
    AssertIntEqual(
        (int)AutoAngleMethod.ContourPolar,
        (int)irregularRound.Method,
        "irregular round method");

    var weakEllipse = AutoAngleStrategy.Select(
        widthPixels: 215,
        heightPixels: 200,
        pcaRatio: 1.08,
        circularity: 0.92,
        templateRadiusSignalPixels: 3.0);
    AssertIntEqual(
        (int)AutoPartShapeClass.WeakEllipse,
        (int)weakEllipse.ShapeClass,
        "weak ellipse class");
    AssertIntEqual(
        (int)AutoAngleMethod.ContourPolar,
        (int)weakEllipse.Method,
        "weak ellipse method");

    var weakRound = AutoAngleStrategy.Select(
        widthPixels: 201,
        heightPixels: 200,
        pcaRatio: 1.01,
        circularity: 0.98,
        templateRadiusSignalPixels: 0.1);
    AssertIntEqual(
        (int)AutoPartShapeClass.IrregularRound,
        (int)weakRound.ShapeClass,
        "weak round class");
    AssertIntEqual(
        (int)AutoAngleMethod.ContourPolar,
        (int)weakRound.Method,
        "weak round method");
    AssertBoolEqual(true, weakRound.AllowsRCorrection, "weak round R enabled");
}

static void VerifyPlcOutputDirectionSettingsApplySimpleXySigns()
{
    var transform = new PlcOutputTransform(
        Xx: 0.5,
        Xy: 0.25,
        Yx: -0.25,
        Yy: 0.75,
        XBias: 3.0,
        YBias: -4.0,
        RScale: 1.5,
        RBias: 2.0);

    var applied = PlcOutputDirectionSettings.ApplySimpleXyDirection(
        transform,
        invertX: true,
        invertY: false);

    AssertDoubleEqual(-1.0, applied.Xx, "X invert Xx");
    AssertDoubleEqual(0.0, applied.Xy, "X invert Xy");
    AssertDoubleEqual(0.0, applied.XBias, "X invert XBias");
    AssertDoubleEqual(0.0, applied.Yx, "X invert Yx");
    AssertDoubleEqual(1.0, applied.Yy, "X invert Yy");
    AssertDoubleEqual(0.0, applied.YBias, "X invert YBias");
    AssertDoubleEqual(1.5, applied.RScale, "X invert keeps RScale");
    AssertDoubleEqual(2.0, applied.RBias, "X invert keeps RBias");
    AssertBoolEqual(true, PlcOutputDirectionSettings.IsSimpleXInverted(applied), "simple X inverted");
    AssertBoolEqual(false, PlcOutputDirectionSettings.IsSimpleYInverted(applied), "simple Y not inverted");

    var both = PlcOutputDirectionSettings.ApplySimpleXyDirection(
        transform,
        invertX: true,
        invertY: true);
    AssertDoubleEqual(-1.0, both.Xx, "both Xx");
    AssertDoubleEqual(-1.0, both.Yy, "both Yy");
    AssertBoolEqual(true, PlcOutputDirectionSettings.IsSimpleXInverted(both), "both X inverted");
    AssertBoolEqual(true, PlcOutputDirectionSettings.IsSimpleYInverted(both), "both Y inverted");

    var advanced = transform with { Xy = 0.25 };
    AssertBoolEqual(false, PlcOutputDirectionSettings.IsSimpleXyTransform(advanced), "advanced matrix is not simple XY");
}

static void VerifyDirectionFormatterShowsXySigns()
{
    var text = TurntableStatusMessageFormatter.FormatDirectionText(
        VisionParameters.Default with { InvertRotationCompensation = true },
        PlcOutputTransform.Identity with { Xx = -1.0, Yy = -1.0 });

    AssertBoolEqual(text.Contains("X方向=取反", StringComparison.Ordinal), true, "formatter X direction");
    AssertBoolEqual(text.Contains("Y方向=取反", StringComparison.Ordinal), true, "formatter Y direction");
    AssertBoolEqual(text.Contains("R取反", StringComparison.Ordinal), true, "formatter R direction");
    AssertBoolEqual(text.Contains("D1002", StringComparison.Ordinal), false, "formatter hides output matrix");

    var advancedText = TurntableStatusMessageFormatter.FormatDirectionText(
        VisionParameters.Default,
        PlcOutputTransform.Identity with { Xy = 0.25 });
    AssertBoolEqual(advancedText.Contains("高级矩阵", StringComparison.Ordinal), false, "formatter hides advanced XY text");
    AssertBoolEqual(advancedText.Contains("X方向=不取反", StringComparison.Ordinal), true, "formatter advanced X fallback");
    AssertBoolEqual(advancedText.Contains("Y方向=不取反", StringComparison.Ordinal), true, "formatter advanced Y fallback");
}

static void VerifyProductRecipeClearsLegacyFrontBump()
{
    var productRecipe = VisionParameters.Default with
    {
        BackSideNgEnabled = true,
        FrontBumpFeature = new FrontBumpFeature
        {
            Enabled = true,
            XPixel = 120.0,
            YPixel = 230.0,
            AngleDegrees = 15.0,
            RadiusPixels = 80.0
        }
    };

    var saved = JuliMvs.Core.Persistence.ProductRecipeVisionParameters.ForSave(productRecipe);

    AssertBoolEqual(true, saved.BackSideNgEnabled, "saved backside NG");
    AssertBoolEqual(false, saved.FrontBumpFeature.Enabled, "cleared legacy recipe front bump");
}

static void VerifyBackSideNgDoesNotRequireSelectedFrontBump()
{
    var templateImagePath = Path.Combine(CreateTempDirectory(), "template.bmp");
    Directory.CreateDirectory(Path.GetDirectoryName(templateImagePath)!);
    File.WriteAllText(templateImagePath, "template image placeholder");
    var parameters = VisionParameters.Default with
    {
        BackSideNgEnabled = true,
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
        templateImagePath,
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

    AssertBoolEqual(true, setup.IsReady, "backside NG setup without selected front bump");
    AssertEqual(ProductionSetupBlockReason.None.ToString(), setup.Reason.ToString(), "backside NG setup reason");
}

static void VerifyContourFeatureExtractorClassifiesStrongEllipse()
{
    using var image = CreateSyntheticPartImage((mat, center) =>
    {
        Cv2.Ellipse(
            mat,
            center,
            new Size(130, 70),
            angle: 25,
            startAngle: 0,
            endAngle: 360,
            Scalar.White,
            thickness: -1);
    });
    var extractor = new ContourFeatureExtractor();

    var feature = extractor.Extract(image);

    AssertEqual(AutoPartShapeClass.StrongEllipse.ToString(), feature.Strategy.ShapeClass.ToString(), "strong ellipse shape");
    AssertBoolEqual(true, feature.Strategy.AllowsRCorrection, "strong ellipse allows R");
    AssertBoolEqual(feature.AxisRatio > 1.2, true, "strong ellipse axis ratio");
    AssertIntEqual(ContourFeatureExtractor.DefaultRadiusSampleCount, feature.RadiusSignature.Count, "strong ellipse radius samples");
}

static void VerifyContourFeatureExtractorKeepsWeakRoundRDetection()
{
    using var image = CreateSyntheticPartImage((mat, center) =>
    {
        Cv2.Circle(mat, center, 95, Scalar.White, thickness: -1);
    });
    var extractor = new ContourFeatureExtractor();

    var feature = extractor.Extract(image);

    AssertEqual(AutoPartShapeClass.IrregularRound.ToString(), feature.Strategy.ShapeClass.ToString(), "weak round shape");
    AssertEqual(AutoAngleMethod.ContourPolar.ToString(), feature.Strategy.Method.ToString(), "weak round method");
    AssertBoolEqual(true, feature.Strategy.AllowsRCorrection, "weak round keeps R detection");
}

static void VerifyContourRadiusSignatureMatchesRotation()
{
    using var template = CreateSyntheticPartImage((mat, center) =>
    {
        Cv2.Circle(mat, center, 95, Scalar.White, thickness: -1);
        Cv2.Circle(mat, new Point(center.X + 38, center.Y - 72), 24, Scalar.White, thickness: -1);
    });
    using var current = CreateSyntheticPartImage((mat, center) =>
    {
        Cv2.Circle(mat, center, 95, Scalar.White, thickness: -1);
        Cv2.Circle(mat, new Point(center.X + 72, center.Y + 38), 24, Scalar.White, thickness: -1);
    });
    var extractor = new ContourFeatureExtractor();
    var templateFeature = extractor.Extract(template);
    var currentFeature = extractor.Extract(current);

    var match = ContourFeatureExtractor.MatchRadiusSignature(
        currentFeature.RadiusSignature,
        templateFeature.RadiusSignature);

    AssertBoolEqual(true, match.ErrorPixels < 8.0, "rotated contour match error");
    AssertBoolEqual(true, Math.Abs(match.AngleDegrees - 90.0) < 3.0, "rotated contour match angle");
}

static void VerifyProductionContourReliabilityRejectsFieldAngleJump()
{
    var current = CreateContourFeature(
        centerX: 2984.0,
        centerY: 1870.4,
        areaPixels: 4513684.5,
        allowsRCorrection: true);
    var template = CreateProductionReliabilityTemplate();
    var templateFeature = CreateContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        allowsRCorrection: true);
    var result = ProductionContourReliabilityGuard.Evaluate(
        current,
        templateFeature,
        template,
        matchScore: 0.0);

    AssertBoolEqual(false, result.IsReliable, "field angle jump rejected");
    AssertBoolEqual(
        result.Reason.Contains("面积", StringComparison.Ordinal) ||
        result.Reason.Contains("中心", StringComparison.Ordinal) ||
        result.Reason.Contains("分数", StringComparison.Ordinal),
        true,
        "field angle jump rejection reason");
}

static void VerifyProductionContourReliabilityAllowsLowScoreForXyrOutput()
{
    var template = CreateProductionReliabilityTemplate();
    var current = CreateContourFeature(
        centerX: template.ReferenceCenterXPixel + 8.0,
        centerY: template.ReferenceCenterYPixel - 6.0,
        areaPixels: template.AreaPixels * 1.01,
        allowsRCorrection: true);
    var templateFeature = CreateContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        allowsRCorrection: true);

    var result = ProductionContourReliabilityGuard.Evaluate(
        current,
        templateFeature,
        template,
        matchScore: 0.208);

    AssertBoolEqual(true, result.IsReliable, "field low score still reaches defect check");
    AssertBoolEqual(true, result.Warning?.Contains("继续输出XYR", StringComparison.Ordinal) == true, "low score warning");
}

static void VerifyProductionContourReliabilityAcceptsReliableContour()
{
    var template = CreateProductionReliabilityTemplate();
    var current = CreateContourFeature(
        centerX: 2737.4,
        centerY: 1838.9,
        areaPixels: 3342818.0,
        allowsRCorrection: true);
    var templateFeature = CreateContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        allowsRCorrection: true);
    var result = ProductionContourReliabilityGuard.Evaluate(
        current,
        templateFeature,
        template,
        matchScore: 0.75);

    AssertBoolEqual(true, result.IsReliable, "reliable contour accepted");
}

static void VerifyProductionContourReliabilityAcceptsLargePositionOffset()
{
    var template = CreateProductionReliabilityTemplate();
    var current = CreateContourFeature(
        centerX: template.ReferenceCenterXPixel + 320.0,
        centerY: template.ReferenceCenterYPixel - 40.0,
        areaPixels: template.AreaPixels * 1.01,
        allowsRCorrection: true);
    var templateFeature = CreateContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        allowsRCorrection: true);
    var result = ProductionContourReliabilityGuard.Evaluate(
        current,
        templateFeature,
        template,
        matchScore: 0.84);

    AssertBoolEqual(true, result.IsReliable, "large position offset accepted");
}

static void VerifyProductionContourReliabilityDoesNotLockWeakRoundR()
{
    var template = CreateProductionReliabilityTemplate();
    var currentWeakRound = CreateContourFeature(
        centerX: template.ReferenceCenterXPixel + 20.0,
        centerY: template.ReferenceCenterYPixel - 15.0,
        areaPixels: template.AreaPixels * 1.01,
        allowsRCorrection: false);
    var templateWeakRound = CreateContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        allowsRCorrection: false);
    var templateWeakEllipse = CreateContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        allowsRCorrection: true);

    AssertBoolEqual(
        false,
        ProductionContourReliabilityGuard.ShouldLockR(currentWeakRound, templateWeakRound),
        "weak round does not lock R");
    AssertBoolEqual(
        false,
        ProductionContourReliabilityGuard.ShouldLockR(currentWeakRound, templateWeakEllipse),
        "directional template does not lock R just because current contour is weak");

    var result = ProductionContourReliabilityGuard.Evaluate(
        currentWeakRound,
        templateWeakEllipse,
        template,
        matchScore: 1.0);

    AssertBoolEqual(true, result.IsReliable, "weak round accepted after R match");
}

static void VerifyProductionShapeResolverUsesShapeMatch()
{
    var resolver = new ProductionAutoAngleResolver();
    var template = CreateProductionReliabilityTemplate();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        pcaRatio: 1.09,
        pcaAngleDegrees: -30.0,
        radiusSignature: CreateDistinctiveRadiusSignature(shiftBins: 0),
        axisFeature: CreateAxisFeature(angleDegrees: -30.0, ratio: 1.09));
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel + 6.0,
        centerY: template.ReferenceCenterYPixel - 4.0,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        pcaRatio: 1.09,
        pcaAngleDegrees: 70.0,
        radiusSignature: CreateDistinctiveRadiusSignature(shiftBins: 60, perturb: true),
        axisFeature: CreateAxisFeature(angleDegrees: 70.0, ratio: 1.09));

    var result = resolver.Resolve(currentFeature, templateFeature, template);

    AssertBoolEqual(true, result.IsReliable, $"shape match reliable: {result.Message}");
    AssertBoolEqual(true, result.AllowsFullRotation, "shape full rotation");
    AssertBoolEqual(true, result.Message.Contains("Shape", StringComparison.Ordinal), "shape message");
    AssertBoolEqual(Math.Abs(result.CenterXPixel - currentFeature.CenterXPixel) < 2.0, true, "shape center x");
    AssertBoolEqual(Math.Abs(result.CenterYPixel - currentFeature.CenterYPixel) < 2.0, true, "shape center y");
}

static void VerifyProductionShapeResolverUsesFourWaySymmetricR()
{
    var resolver = new ProductionAutoAngleResolver();
    var template = CreateProductionReliabilityTemplate();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.StrongEllipse,
        method: AutoAngleMethod.PcaAxis,
        pcaRatio: 1.25,
        pcaAngleDegrees: 0.0,
        radiusSignature: CreateFourWaySymmetricRadiusSignature(shiftBins: 0),
        axisFeature: CreateAxisFeature(angleDegrees: 0.0, ratio: 1.25));
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel + 4.0,
        centerY: template.ReferenceCenterYPixel - 3.0,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.StrongEllipse,
        method: AutoAngleMethod.PcaAxis,
        pcaRatio: 1.25,
        pcaAngleDegrees: 25.0,
        radiusSignature: CreateFourWaySymmetricRadiusSignature(shiftBins: 50),
        axisFeature: CreateAxisFeature(angleDegrees: 25.0, ratio: 1.25));

    var result = resolver.Resolve(
        currentFeature,
        templateFeature,
        template,
        fourWaySymmetric: true);

    AssertBoolEqual(true, result.IsReliable, $"four-way shape reliable: {result.Message}");
    AssertBoolEqual(false, result.AllowsFullRotation, "four-way R uses 180 equivalent");
    AssertBoolEqual(true, Math.Abs(AngleMath.NormalizeDeltaDegrees(result.ResolvedAngleDegrees, template.ReferenceAngleDegrees + 25.0)) < 1.0, "four-way R follows shape match");
    AssertBoolEqual(true, result.Message.Contains("四边对称", StringComparison.Ordinal), "four-way message");
}

static void VerifyProductionShapeResolverAllowsFourWayAxisSpreadWithChamfer()
{
    var resolver = new ProductionAutoAngleResolver();
    var template = CreateProductionReliabilityTemplate();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.StrongEllipse,
        method: AutoAngleMethod.PcaAxis,
        pcaRatio: 1.12,
        pcaAngleDegrees: -17.0,
        radiusSignature: CreateFourWaySymmetricRadiusSignature(shiftBins: 0),
        axisFeature: CreateAxisFeatureFromAngles(-17.0, -17.0, 28.0, 1.12));
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel - 5.0,
        centerY: template.ReferenceCenterYPixel + 4.0,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.StrongEllipse,
        method: AutoAngleMethod.PcaAxis,
        pcaRatio: 1.12,
        pcaAngleDegrees: 18.0,
        radiusSignature: CreateFourWaySymmetricRadiusSignature(shiftBins: 70),
        axisFeature: CreateAxisFeatureFromAngles(18.0, 18.0, 63.0, 1.12));

    var result = resolver.Resolve(
        currentFeature,
        templateFeature,
        template,
        fourWaySymmetric: true);

    AssertBoolEqual(true, result.IsReliable, $"four-way allows axis spread after Chamfer: {result.Message}");
    AssertBoolEqual(false, result.AllowsFullRotation, "four-way R still uses 180 equivalent");
    AssertBoolEqual(true, result.Message.Contains("角度置信度", StringComparison.Ordinal), "axis confidence logged");
}

static void VerifyProductionShapeResolverKeepsFullFourWayDefectAlignmentAngle()
{
    var resolver = new ProductionAutoAngleResolver();
    var template = CreateProductionReliabilityTemplate();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.StrongEllipse,
        method: AutoAngleMethod.PcaAxis,
        pcaRatio: 1.25,
        pcaAngleDegrees: 0.0,
        radiusSignature: CreateFourWaySymmetricRadiusSignature(shiftBins: 0),
        axisFeature: CreateAxisFeature(angleDegrees: 0.0, ratio: 1.25));
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel + 4.0,
        centerY: template.ReferenceCenterYPixel - 3.0,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.StrongEllipse,
        method: AutoAngleMethod.PcaAxis,
        pcaRatio: 1.25,
        pcaAngleDegrees: -68.0,
        radiusSignature: CreateFourWaySymmetricRadiusSignature(shiftBins: 224),
        axisFeature: CreateAxisFeature(angleDegrees: -68.0, ratio: 1.25));

    var result = resolver.Resolve(
        currentFeature,
        templateFeature,
        template,
        fourWaySymmetric: true);

    AssertBoolEqual(true, result.IsReliable, $"four-way full defect alignment reliable: {result.Message}");
    AssertBoolEqual(false, result.AllowsFullRotation, "four-way R uses axis mode");
    AssertBoolEqual(true, result.RHas180Ambiguity, "four-way R keeps 180 ambiguity flag");
    AssertBoolEqual(
        true,
        Math.Abs(AngleMath.NormalizeDeltaDegrees(result.ResolvedAngleDegrees, template.ReferenceAngleDegrees - 68.0)) < 1.0,
        "four-way PLC/UI R stays folded axis angle");
    AssertBoolEqual(
        true,
        Math.Abs(AngleMath.NormalizeDeltaDegrees360(result.AlignmentAngleDegrees, template.ReferenceAngleDegrees + 112.0)) < 1.0,
        "four-way defect alignment keeps full image angle");
}

static void VerifyFourWayAxisDiffUses180Equivalent()
{
    var axis = CreateAxisFeatureFromAngles(2.0, 2.0, 178.0, 1.12);

    AssertBoolEqual(axis.MaximumAngleSpreadDegrees <= 5.0, true, "178 and 2 degrees are close for 180-axis");
    AssertBoolEqual(Math.Abs(axis.MeanAngleDegrees) < 5.0, true, "axis mean crosses 180 boundary");
}

static void VerifyProductionShapeResolverAcceptsLowSeparationFourWayEllipse()
{
    var resolver = new ProductionAutoAngleResolver();
    var template = CreateProductionReliabilityTemplate();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.StrongEllipse,
        method: AutoAngleMethod.PcaAxis,
        pcaRatio: 1.25,
        pcaAngleDegrees: 0.0,
        radiusSignature: CreateCircleRadiusSignature(),
        axisFeature: CreateAxisFeature(angleDegrees: 0.0, ratio: 1.25));
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.StrongEllipse,
        method: AutoAngleMethod.PcaAxis,
        pcaRatio: 1.25,
        pcaAngleDegrees: 0.0,
        radiusSignature: CreateCircleRadiusSignature(),
        axisFeature: CreateAxisFeature(angleDegrees: 0.0, ratio: 1.25));

    var result = resolver.Resolve(
        currentFeature,
        templateFeature,
        template,
        fourWaySymmetric: true);

    AssertBoolEqual(true, result.IsReliable, $"low-separation four-way ellipse accepted: {result.Message}");
    AssertBoolEqual(false, result.AllowsFullRotation, "low-separation four-way still uses axis mode");
}

static void VerifyContourShapeMatcherRefinesSubpixelTranslation()
{
    var matcher = new ContourShapeMatcher();
    var template = CreateTypedContourFeature(
        centerX: 500.0,
        centerY: 400.0,
        areaPixels: 32000.0,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        pcaRatio: 1.09,
        pcaAngleDegrees: 0.0,
        radiusSignature: CreateDistinctiveRadiusSignature(shiftBins: 0));
    const double expectedCenterX = 512.35;
    const double expectedCenterY = 390.65;
    var currentPoints = template.ContourPoints
        .Select(point => new Point2d(
            point.X + expectedCenterX - template.CenterXPixel,
            point.Y + expectedCenterY - template.CenterYPixel))
        .ToArray();
    var current = template with
    {
        CenterXPixel = Math.Round(expectedCenterX),
        CenterYPixel = Math.Round(expectedCenterY),
        ContourPoints = currentPoints,
    };
    var noRefine = matcher.Match(
        current,
        template,
        ContourShapeMatcherOptions.Default with
        {
            SubpixelTranslationRadiusPixels = 0.0,
            SubpixelTranslationStepPixels = 0.25,
        });
    var refined = matcher.Match(current, template);

    AssertBoolEqual(true, noRefine.IsReliable, $"no-refine shape reliable: {noRefine.Message}");
    AssertBoolEqual(true, refined.IsReliable, $"refined shape reliable: {refined.Message}");
    var noRefineDistance = Distance(noRefine.CenterXPixel, noRefine.CenterYPixel, expectedCenterX, expectedCenterY);
    var refinedDistance = Distance(refined.CenterXPixel, refined.CenterYPixel, expectedCenterX, expectedCenterY);
    AssertBoolEqual(true, refinedDistance < noRefineDistance, "subpixel XY refinement improves center");
    AssertBoolEqual(true, refinedDistance <= 0.30, "subpixel XY refinement final error");
    AssertBoolEqual(true, refined.ErrorPixels <= noRefine.ErrorPixels, "subpixel XY refinement does not increase shape error");
}

static void VerifyProductionAutoAngleResolverUsesRadiusAssist()
{
    var template = CreateProductionReliabilityTemplate();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: CreateDistinctiveRadiusSignature(shiftBins: 0));
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: CreateDistinctiveRadiusSignature(shiftBins: 12));
    var shapeMatch = ContourShapeMatch.Pass(
        "正面",
        resolvedAngleDegrees: 6.9,
        angleOffsetDegrees: 6.9,
        centerXPixel: template.ReferenceCenterXPixel,
        centerYPixel: template.ReferenceCenterYPixel,
        errorPixels: 5.0,
        alternativeErrorPixels: 7.0,
        separationPixels: 2.0,
        score: 0.4,
        areaDifferenceRatio: 0.0,
        "test shape");

    var result = ProductionAutoAngleResolver.ResolveAngleWithRadiusAssist(
        shapeMatch,
        currentFeature,
        templateFeature);

    AssertDoubleEqual(6.315, Math.Round(result.AngleOffsetDegrees, 3), "radius assist fused R");
    AssertDoubleEqual(0.35, result.ShapeWeight, "irregular shape weight");
    AssertDoubleEqual(0.65, result.RadiusWeight, "irregular radius weight");
    AssertBoolEqual(true, result.Message.Contains("半径序列修正R", StringComparison.Ordinal), "radius assist message");
}

static void VerifyProductionAutoAngleResolverUsesConservativeEllipseRadiusAssist()
{
    var template = CreateProductionReliabilityTemplate();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.StrongEllipse,
        method: AutoAngleMethod.PcaAxis,
        pcaRatio: 1.3,
        radiusSignature: CreateDistinctiveRadiusSignature(shiftBins: 0));
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.StrongEllipse,
        method: AutoAngleMethod.PcaAxis,
        pcaRatio: 1.3,
        radiusSignature: CreateDistinctiveRadiusSignature(shiftBins: 12));
    var shapeMatch = ContourShapeMatch.Pass(
        "正面",
        resolvedAngleDegrees: 6.9,
        angleOffsetDegrees: 6.9,
        centerXPixel: template.ReferenceCenterXPixel,
        centerYPixel: template.ReferenceCenterYPixel,
        errorPixels: 5.0,
        alternativeErrorPixels: 7.0,
        separationPixels: 2.0,
        score: 0.4,
        areaDifferenceRatio: 0.0,
        "test shape");

    var result = ProductionAutoAngleResolver.ResolveAngleWithRadiusAssist(
        shapeMatch,
        currentFeature,
        templateFeature);

    AssertDoubleEqual(6.585, Math.Round(result.AngleOffsetDegrees, 3), "strong ellipse fused R");
    AssertDoubleEqual(0.65, result.ShapeWeight, "strong ellipse shape weight");
    AssertDoubleEqual(0.35, result.RadiusWeight, "strong ellipse radius weight");
}

static void VerifyProductionAutoAngleResolverSkipsBadRadiusAssist()
{
    var template = CreateProductionReliabilityTemplate();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: CreateDistinctiveRadiusSignature(shiftBins: 0));
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: CreateDistinctiveRadiusSignature(shiftBins: 90));
    var shapeMatch = ContourShapeMatch.Pass(
        "正面",
        resolvedAngleDegrees: 6.9,
        angleOffsetDegrees: 6.9,
        centerXPixel: template.ReferenceCenterXPixel,
        centerYPixel: template.ReferenceCenterYPixel,
        errorPixels: 5.0,
        alternativeErrorPixels: 7.0,
        separationPixels: 2.0,
        score: 0.4,
        areaDifferenceRatio: 0.0,
        "test shape");

    var result = ProductionAutoAngleResolver.ResolveAngleWithRadiusAssist(
        shapeMatch,
        currentFeature,
        templateFeature);

    AssertDoubleEqual(6.9, result.AngleOffsetDegrees, "bad radius assist skipped R");
    AssertDoubleEqual(1.0, result.ShapeWeight, "bad radius assist shape weight");
    AssertDoubleEqual(0.0, result.RadiusWeight, "bad radius assist radius weight");
    AssertBoolEqual(true, result.Message.Contains("跳过", StringComparison.Ordinal), "bad radius assist message");
}

static void VerifyProductionShapeResolverRejectsAmbiguousShape()
{
    var resolver = new ProductionAutoAngleResolver();
    var template = CreateProductionReliabilityTemplate();
    var templateSignature = CreateAmbiguousEllipseRadiusSignature(shiftBins: 0);
    var currentSignature = CreateAmbiguousEllipseRadiusSignature(shiftBins: 180);
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.WeakEllipse,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: templateSignature,
        axisFeature: CreateAxisFeature(angleDegrees: 0.0, ratio: 1.01));
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.WeakEllipse,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: currentSignature,
        axisFeature: CreateAxisFeature(angleDegrees: 0.0, ratio: 1.01));

    var result = resolver.Resolve(currentFeature, templateFeature, template);

    AssertBoolEqual(false, result.IsReliable, $"ambiguous shape rejected: {result.Message}");
    AssertBoolEqual(true, result.Message.Contains("分离不足", StringComparison.Ordinal), "ambiguous shape reason");
}

static void VerifyProductionShapeResolverDetectsBackSideWithShapeMatch()
{
    var resolver = new ProductionAutoAngleResolver();
    var template = CreateProductionReliabilityTemplate();
    var templateSignature = CreateStrongAsymmetricRadiusSignature(shiftBins: 0);
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: templateSignature,
        axisFeature: CreateAxisFeature(angleDegrees: 0.0, ratio: 1.01));
    var mirroredSignature = ContourFeatureExtractor.MirrorRadiusSignature(templateSignature);
    var backFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: mirroredSignature,
        axisFeature: CreateAxisFeature(angleDegrees: 0.0, ratio: 1.01));

    var result = resolver.MatchFrontBack(backFeature, templateFeature);

    AssertBoolEqual(true, result.IsReliable, $"shape front/back reliable: {result.Message}");
    AssertEqual(ContourFrontBackDecision.Back.ToString(), result.Decision.ToString(), "shape front/back detects back");
}

static void VerifyProductionAutoAngleResolverRejectsWeakRoundWithoutDirection()
{
    var resolver = new ProductionAutoAngleResolver();
    var template = CreateProductionReliabilityTemplate();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: Array.Empty<float>());
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: Array.Empty<float>());

    var result = resolver.Resolve(currentFeature, templateFeature, template);

    AssertBoolEqual(false, result.IsReliable, "weak round without direction is NG instead of locked R");
    AssertBoolEqual(false, result.AllowsFullRotation, "weak round no unsafe R output");
}

static void VerifyProductionMissingMaterialDetectorAcceptsMatchingContour()
{
    using var detector = new ProductionMissingMaterialDetector();
    var template = CreateProductionReliabilityTemplate();
    var signature = CreateDefectBaseRadiusSignature();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: signature);
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: signature);
    var angle = ProductionAutoAngleResult.Reliable(
        template.ReferenceAngleDegrees,
        template.ReferenceCenterXPixel,
        template.ReferenceCenterYPixel,
        1.0,
        AllowsFullRotation: true,
        "test");

    var result = detector.Evaluate(currentFeature, templateFeature, template, angle);

    AssertBoolEqual(true, result.IsPass, result.Message);
}

static void VerifyProductionMissingMaterialDetectorRejectsMissingOrChippedEdge()
{
    using var detector = new ProductionMissingMaterialDetector();
    var template = CreateProductionReliabilityTemplate();
    var templateSignature = CreateDefectBaseRadiusSignature();
    var missingSignature = templateSignature.ToArray();
    for (var index = 70; index < 165; index++)
    {
        missingSignature[index] -= 46f;
    }

    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: templateSignature);
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: missingSignature);
    var angle = ProductionAutoAngleResult.Reliable(
        template.ReferenceAngleDegrees,
        template.ReferenceCenterXPixel,
        template.ReferenceCenterYPixel,
        1.0,
        AllowsFullRotation: true,
        "test");

    var result = detector.Evaluate(currentFeature, templateFeature, template, angle);

    AssertBoolEqual(false, result.IsPass, "missing/chipped edge should be NG");
    AssertBoolEqual(true, result.Message.StartsWith("NG ", StringComparison.Ordinal), result.Message);
    AssertBoolEqual(true, result.Message.Contains("面积=", StringComparison.Ordinal), "missing area logged");
    AssertBoolEqual(true, result.Message.Contains("深度=", StringComparison.Ordinal), "missing depth logged");
    AssertBoolEqual(true, result.Message.Contains("宽度=", StringComparison.Ordinal), "missing width logged");
}

static void VerifyProductionMissingMaterialDetectorUsesFullShapeAngleForFourWayAlignment()
{
    using var detector = new ProductionMissingMaterialDetector();
    var template = CreateProductionReliabilityTemplate();
    var signature = CreateDefectBaseRadiusSignature();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: signature);
    var currentFeature = templateFeature with
    {
        ContourPoints = RotateContourPoints(
            templateFeature.ContourPoints,
            templateFeature.CenterXPixel,
            templateFeature.CenterYPixel,
            112.0)
    };
    var foldedR = template.ReferenceAngleDegrees + AngleMath.NormalizeDegrees180(112.0);
    var angle = ProductionAutoAngleResult.Reliable(
        foldedR,
        template.ReferenceCenterXPixel,
        template.ReferenceCenterYPixel,
        1.0,
        AllowsFullRotation: false,
        "test") with
    {
        AlignmentAngleDegrees = template.ReferenceAngleDegrees + 112.0
    };

    var result = detector.Evaluate(currentFeature, templateFeature, template, angle);

    AssertBoolEqual(true, result.IsPass, $"four-way defect alignment should use full shape angle: {result.Message}");
}

static void VerifyProductionMissingMaterialDetectorIgnoresShallowEdgeDifference()
{
    using var detector = new ProductionMissingMaterialDetector();
    var template = CreateProductionReliabilityTemplate();
    var templateSignature = CreateDefectBaseRadiusSignature();
    var shallowDifferenceSignature = templateSignature.ToArray();
    for (var index = 40; index < 360; index++)
    {
        shallowDifferenceSignature[index] -= 2f;
    }

    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: templateSignature);
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: shallowDifferenceSignature);
    var angle = ProductionAutoAngleResult.Reliable(
        template.ReferenceAngleDegrees,
        template.ReferenceCenterXPixel,
        template.ReferenceCenterYPixel,
        1.0,
        AllowsFullRotation: true,
        "test");

    var result = detector.Evaluate(currentFeature, templateFeature, template, angle);

    AssertBoolEqual(true, result.IsPass, $"shallow edge difference should be OK: {result.Message}");
    AssertBoolEqual(true, result.Message.StartsWith("OK ", StringComparison.Ordinal), result.Message);
}

static void VerifyProductionMissingMaterialDetectorIgnoresExtraBurr()
{
    using var detector = new ProductionMissingMaterialDetector();
    var template = CreateProductionReliabilityTemplate();
    var templateSignature = CreateDefectBaseRadiusSignature();
    var burrSignature = templateSignature.ToArray();
    for (var index = 220; index < 260; index++)
    {
        burrSignature[index] += 32f;
    }

    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: templateSignature);
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: template.AreaPixels,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: burrSignature);
    var angle = ProductionAutoAngleResult.Reliable(
        template.ReferenceAngleDegrees,
        template.ReferenceCenterXPixel,
        template.ReferenceCenterYPixel,
        1.0,
        AllowsFullRotation: true,
        "test");

    var result = detector.Evaluate(currentFeature, templateFeature, template, angle);

    AssertBoolEqual(true, result.IsPass, $"extra burr should not be NG: {result.Message}");
    AssertBoolEqual(false, result.Message.Contains("毛刺", StringComparison.Ordinal), result.Message);
    AssertBoolEqual(false, result.Message.Contains("外扩", StringComparison.Ordinal), result.Message);
}

static void VerifyProductionMissingMaterialCoarseFallbackAcceptsVisibleOkAreaDrift()
{
    using var detector = new ProductionMissingMaterialDetector();
    var template = CreateProductionReliabilityTemplate();
    var templateSignature = CreateCircleRadiusSignature();
    var currentSignature = CreateCircleRadiusSignature();
    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: 5_005_730,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: templateSignature) with
    {
        WidthPixels = 2500,
        HeightPixels = 2500,
        Circularity = 0.829
    };
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: 4_499_886,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: currentSignature) with
    {
        WidthPixels = 2408,
        HeightPixels = 2333,
        Circularity = 0.764
    };

    var result = detector.EvaluateCoarseVisibleEdgeMissing(currentFeature, templateFeature, template);

    AssertBoolEqual(true, result.IsPass, $"field good part should pass coarse visible defect check: {result.Message}");
    AssertBoolEqual(true, result.Message.StartsWith("OK ", StringComparison.Ordinal), result.Message);
    AssertBoolEqual(false, result.Message.Contains("面积", StringComparison.Ordinal), result.Message);
    AssertBoolEqual(false, result.Message.Contains("尺寸", StringComparison.Ordinal), result.Message);
    AssertBoolEqual(false, result.Message.Contains("圆度", StringComparison.Ordinal), result.Message);
}

static void VerifyProductionMissingMaterialCoarseFallbackRejectsVisibleEdgeLoss()
{
    using var detector = new ProductionMissingMaterialDetector();
    var template = CreateProductionReliabilityTemplate();
    var templateSignature = CreateCircleRadiusSignature();
    var missingSignature = templateSignature.ToArray();
    for (var index = 95; index < 180; index++)
    {
        missingSignature[index] -= 36f;
    }

    var templateFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: 5_000_000,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: templateSignature) with
    {
        WidthPixels = 2500,
        HeightPixels = 2500,
        Circularity = 0.829
    };
    var currentFeature = CreateTypedContourFeature(
        centerX: template.ReferenceCenterXPixel,
        centerY: template.ReferenceCenterYPixel,
        areaPixels: 4_720_000,
        shapeClass: AutoPartShapeClass.IrregularRound,
        method: AutoAngleMethod.ContourPolar,
        radiusSignature: missingSignature) with
    {
        WidthPixels = 2470,
        HeightPixels = 2470,
        Circularity = 0.795
    };

    var result = detector.EvaluateCoarseVisibleEdgeMissing(currentFeature, templateFeature, template);

    AssertBoolEqual(false, result.IsPass, $"visible edge loss should be NG: {result.Message}");
    AssertBoolEqual(true, result.Message.StartsWith("NG ", StringComparison.Ordinal), result.Message);
}

static IReadOnlyList<Point2d> RotateContourPoints(
    IReadOnlyList<Point2d> points,
    double centerX,
    double centerY,
    double angleDegrees)
{
    var radians = angleDegrees * Math.PI / 180.0;
    var cos = Math.Cos(radians);
    var sin = Math.Sin(radians);
    return points
        .Select(point =>
        {
            var dx = point.X - centerX;
            var dy = point.Y - centerY;
            return new Point2d(
                centerX + dx * cos - dy * sin,
                centerY + dx * sin + dy * cos);
        })
        .ToArray();
}

static void VerifyProductTemplateNameIsUniqueAndRebuildOverwrites()
{
    var testRoot = CreateTempDirectory();
    var repository = new SqliteInspectionRepository(Path.Combine(testRoot, "juli-mvs.db"));
    repository.InitializeAsync().GetAwaiter().GetResult();

    var first = CreateTemplate("PART-A", "BATCH-1", DateTimeOffset.Parse("2026-05-22T10:00:00+08:00"), 10.0);
    var rebuilt = CreateTemplate("PART-A", "BATCH-2", DateTimeOffset.Parse("2026-05-23T10:00:00+08:00"), 20.0);
    var other = CreateTemplate("PART-B", "BATCH-3", DateTimeOffset.Parse("2026-05-23T11:00:00+08:00"), 30.0);

    repository.SaveTemplateAsync(first).GetAwaiter().GetResult();
    repository.SaveTemplateAsync(rebuilt).GetAwaiter().GetResult();
    repository.SaveTemplateAsync(other).GetAwaiter().GetResult();

    var loaded = repository.LoadLatestTemplateAsync("PART-A").GetAwaiter().GetResult();
    var all = repository.LoadTemplatesAsync().GetAwaiter().GetResult();

    AssertEqual("BATCH-2", loaded?.BatchNo ?? string.Empty, "rebuilt template batch");
    AssertDoubleEqual(20.0, loaded?.ReferenceCenterXMm ?? 0.0, "rebuilt template value");
    AssertEqual(2.ToString(), all.Count.ToString(), "unique product template count");
}

static void VerifyLegacyDuplicateProductTemplatesKeepNewest()
{
    var testRoot = CreateTempDirectory();
    var databasePath = Path.Combine(testRoot, "juli-mvs.db");
    SeedLegacyDuplicateTemplates(databasePath);

    var repository = new SqliteInspectionRepository(databasePath);
    repository.InitializeAsync().GetAwaiter().GetResult();
    var loaded = repository.LoadLatestTemplateAsync("PART-A").GetAwaiter().GetResult();
    var all = repository.LoadTemplatesAsync().GetAwaiter().GetResult();

    AssertEqual("BATCH-NEW", loaded?.BatchNo ?? string.Empty, "legacy duplicate newest batch");
    AssertDoubleEqual(22.0, loaded?.ReferenceCenterXMm ?? 0.0, "legacy duplicate newest value");
    AssertEqual(1.ToString(), all.Count.ToString(), "legacy duplicate unique count");
}

static void VerifyMostRecentTemplateLoadsNewestAcrossProducts()
{
    var testRoot = CreateTempDirectory();
    var repository = new SqliteInspectionRepository(Path.Combine(testRoot, "juli-mvs.db"));
    repository.InitializeAsync().GetAwaiter().GetResult();

    var first = CreateTemplate("PART-A", "BATCH-1", DateTimeOffset.Parse("2026-05-22T10:00:00+08:00"), 10.0);
    var newest = CreateTemplate("PART-B", "BATCH-2", DateTimeOffset.Parse("2026-05-23T10:00:00+08:00"), 20.0);
    var older = CreateTemplate("PART-C", "BATCH-3", DateTimeOffset.Parse("2026-05-21T10:00:00+08:00"), 30.0);

    repository.SaveTemplateAsync(first).GetAwaiter().GetResult();
    repository.SaveTemplateAsync(newest).GetAwaiter().GetResult();
    repository.SaveTemplateAsync(older).GetAwaiter().GetResult();

    var loaded = repository.LoadMostRecentTemplateAsync().GetAwaiter().GetResult();

    AssertEqual("PART-B", loaded?.ProductName ?? string.Empty, "most recent template product");
    AssertEqual("BATCH-2", loaded?.BatchNo ?? string.Empty, "most recent template batch");
}

static void VerifyTemplateSelectionTextShowsOnlyNameAndTime()
{
    var template = CreateTemplate(
        "PART-A",
        "BATCH-1",
        DateTimeOffset.Parse("2026-05-25T13:44:00+08:00"),
        49.76);

    var text = JuliMvs.App.MainWindow.FormatTemplateSelectionText(template);

    AssertEqual("PART-A  2026-05-25 13:44", text, "template selection text");
    AssertBoolEqual(text.Contains("R=", StringComparison.Ordinal), false, "template selection hides R");
}

static void VerifyPlcTriggerGateTreatsD1000HighAsSingleCapture()
{
    var gate = new PlcTriggerGate();

    AssertEqual(
        PlcTriggerDecision.StartInspection.ToString(),
        gate.Evaluate(captureRequested: true).ToString(),
        "first D1000 high starts inspection");
    gate.EndOperation();

    AssertEqual(
        PlcTriggerDecision.Busy.ToString(),
        gate.Evaluate(captureRequested: true).ToString(),
        "held D1000 high is not a second inspection");
    AssertEqual(
        PlcTriggerDecision.Cleared.ToString(),
        gate.Evaluate(captureRequested: false).ToString(),
        "D1000 low releases latch");
    AssertEqual(
        PlcTriggerDecision.StartInspection.ToString(),
        gate.Evaluate(captureRequested: true).ToString(),
        "next D1000 high starts next inspection");
}

static PartTemplate CreateTemplate(
    string productName,
    string batchNo,
    DateTimeOffset createdAt,
    double referenceCenterXMm)
{
    return new PartTemplate(
        Guid.NewGuid(),
        batchNo,
        productName,
        Path.Combine(Path.GetTempPath(), $"{productName}-{batchNo}.bmp"),
        createdAt,
        100.0,
        200.0,
        referenceCenterXMm,
        40.0,
        "camera-1",
        "distortion-1",
        1.5,
        10.0,
        20.0,
        300.0,
        0.95,
        ImageRoi.Empty,
        VisionParameters.Default,
        1000.0,
        2000.0);
}

static void SeedLegacyDuplicateTemplates(string databasePath)
{
    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    using var connection = new SqliteConnection($"Data Source={databasePath}");
    connection.Open();
    using (var create = connection.CreateCommand())
    {
        create.CommandText = """
            CREATE TABLE Templates (
                Id TEXT PRIMARY KEY,
                BatchNo TEXT NOT NULL,
                ProductName TEXT NOT NULL,
                ImagePath TEXT NULL,
                CreatedAt TEXT NOT NULL,
                ReferenceCenterXPixel REAL NOT NULL,
                ReferenceCenterYPixel REAL NOT NULL,
                ReferenceCenterXMm REAL NOT NULL,
                ReferenceCenterYMm REAL NOT NULL,
                SourceCameraCalibrationId TEXT NOT NULL,
                SourceDistortionCalibrationId TEXT NOT NULL,
                ReferenceAngleDegrees REAL NOT NULL,
                ReferenceWidthPixels REAL NOT NULL DEFAULT 0.0,
                ReferenceHeightPixels REAL NOT NULL DEFAULT 0.0,
                WidthMm REAL NOT NULL,
                HeightMm REAL NOT NULL,
                AreaPixels REAL NOT NULL,
                MatchScoreBaseline REAL NOT NULL,
                ParametersJson TEXT NULL
            );
            """;
        create.ExecuteNonQuery();
    }

    InsertLegacyTemplate(connection, "BATCH-OLD", "2026-05-22T10:00:00+08:00", 11.0);
    InsertLegacyTemplate(connection, "BATCH-NEW", "2026-05-23T10:00:00+08:00", 22.0);
}

static void InsertLegacyTemplate(
    SqliteConnection connection,
    string batchNo,
    string createdAt,
    double referenceCenterXMm)
{
    using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO Templates (
            Id, BatchNo, ProductName, ImagePath, CreatedAt,
            ReferenceCenterXPixel, ReferenceCenterYPixel, ReferenceCenterXMm, ReferenceCenterYMm,
            SourceCameraCalibrationId, SourceDistortionCalibrationId, ReferenceAngleDegrees,
            ReferenceWidthPixels, ReferenceHeightPixels, WidthMm, HeightMm, AreaPixels, MatchScoreBaseline
        ) VALUES (
            $Id, $BatchNo, 'PART-A', NULL, $CreatedAt,
            100.0, 200.0, $ReferenceCenterXMm, 40.0,
            'camera-1', 'distortion-1', 1.5,
            1000.0, 2000.0, 10.0, 20.0, 300.0, 0.95
        );
        """;
    command.Parameters.AddWithValue("$Id", Guid.NewGuid().ToString());
    command.Parameters.AddWithValue("$BatchNo", batchNo);
    command.Parameters.AddWithValue("$CreatedAt", createdAt);
    command.Parameters.AddWithValue("$ReferenceCenterXMm", referenceCenterXMm);
    command.ExecuteNonQuery();
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

static void VerifyRAxisCenterResidualsIdentifyWorstAngle()
{
    var points = new[]
    {
        new RAxisCenterCalibrationPoint(0, 0, 0, 10.0, 0.0),
        new RAxisCenterCalibrationPoint(90, 0, 0, 0.0, 10.0),
        new RAxisCenterCalibrationPoint(180, 0, 0, -10.0, 0.0),
        new RAxisCenterCalibrationPoint(270, 0, 0, 0.0, -11.0)
    };
    var calibration = RAxisCenterCalibrationSolver.Solve(points);
    var residuals = RAxisCenterCalibrationSolver.CalculateResiduals(calibration);
    var worst = residuals.OrderByDescending(residual => residual.DistanceMm).First();
    var resultText = CalibrationResultMessageFormatter.FormatRAxisCenterResult(calibration);

    AssertIntEqual(4, residuals.Count, "R-axis residual count");
    AssertDoubleEqual(270.0, worst.AngleDegrees, "worst R-axis angle");
    AssertBoolEqual(true, resultText.Contains("R270", StringComparison.Ordinal), "R-axis result includes worst angle");
    AssertBoolEqual(true, resultText.Contains("各角度误差", StringComparison.Ordinal), "R-axis result includes per-angle residuals");
}

static string CreateTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "JuliMvs.App.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static Mat CreateSyntheticPartImage(Action<Mat, Point> draw)
{
    var image = new Mat(new Size(420, 420), MatType.CV_8UC1, Scalar.Black);
    draw(image, new Point(210, 210));
    return image;
}

static PartTemplate CreateProductionReliabilityTemplate()
{
    return new PartTemplate(
        Guid.NewGuid(),
        "BATCH-1",
        "MODEL-333",
        null,
        DateTimeOffset.Now,
        2728.0,
        1902.0,
        -6.556,
        14.328,
        "camera-1",
        string.Empty,
        45.0,
        225.65977658163743,
        218.52479313035752,
        3340000.0,
        1.0,
        ImageRoi.Empty,
        VisionParameters.Default,
        1000.0,
        1000.0);
}

static ContourFeatureExtraction CreateContourFeature(
    double centerX,
    double centerY,
    double areaPixels,
    bool allowsRCorrection)
{
    return CreateTypedContourFeature(
        centerX,
        centerY,
        areaPixels,
        allowsRCorrection ? AutoPartShapeClass.WeakEllipse : AutoPartShapeClass.IrregularRound,
        AutoAngleMethod.ContourPolar,
        radiusSignature: allowsRCorrection ? null : Array.Empty<float>());
}

static ContourFeatureExtraction CreateTypedContourFeature(
    double centerX,
    double centerY,
    double areaPixels,
    AutoPartShapeClass shapeClass,
    AutoAngleMethod method,
    double pcaRatio = 1.1,
    double pcaAngleDegrees = 12.0,
    IReadOnlyList<float>? radiusSignature = null,
    ContourAxisFeature? axisFeature = null)
{
    var strategy = new AutoAngleStrategyDecision(
        shapeClass,
        method,
        method != AutoAngleMethod.Disabled,
        AxisRatio: shapeClass == AutoPartShapeClass.StrongEllipse ? 1.3 : shapeClass == AutoPartShapeClass.NearCircle ? 1.0 : 1.1,
        PcaRatio: pcaRatio,
        Circularity: shapeClass == AutoPartShapeClass.NearCircle ? 0.98 : 0.82,
        TemplateRadiusSignalPixels: method == AutoAngleMethod.Disabled ? 0.0 : 42.0,
        "test strategy");
    radiusSignature ??= method == AutoAngleMethod.Disabled
        ? Array.Empty<float>()
        : CreateSyntheticRadiusSignature(shiftBins: 0);
    var normalizedRadiusSignature = NormalizeTestRadiusSignature(radiusSignature);
    axisFeature ??= method == AutoAngleMethod.Disabled
        ? CreateAxisFeature(angleDegrees: 0.0, ratio: 1.0)
        : CreateAxisFeature(angleDegrees: pcaAngleDegrees, ratio: Math.Max(pcaRatio, 1.08));
    var contourPoints = CreateTestContourPoints(centerX, centerY, radiusSignature);
    return new ContourFeatureExtraction(
        CenterXPixel: centerX,
        CenterYPixel: centerY,
        AreaPixels: areaPixels,
        PerimeterPixels: 0,
        WidthPixels: 1000,
        HeightPixels: 950,
        AxisRatio: strategy.AxisRatio,
        PcaRatio: pcaRatio,
        PcaAngleDegrees: pcaAngleDegrees,
        Circularity: strategy.Circularity,
        RadiusSignalPixels: method == AutoAngleMethod.Disabled ? 0.0 : 42.0,
        RadiusSignature: radiusSignature,
        NormalizedRadiusSignalPixels: CalculateTestRadiusSignal(normalizedRadiusSignature),
        NormalizedRadiusSignature: normalizedRadiusSignature,
        ContourPoints: contourPoints,
        ImageWidthPixels: 4096,
        ImageHeightPixels: 3000,
        AxisFeature: axisFeature,
        Strategy: strategy);
}

static IReadOnlyList<Point2d> CreateTestContourPoints(
    double centerX,
    double centerY,
    IReadOnlyList<float> radiusSignature)
{
    if (radiusSignature.Count == 0)
    {
        return Array.Empty<Point2d>();
    }

    var points = new Point2d[Math.Min(radiusSignature.Count, 720)];
    var stride = (double)radiusSignature.Count / points.Length;
    for (var index = 0; index < points.Length; index++)
    {
        var sourceIndex = (int)Math.Floor(index * stride);
        var angle = 2.0 * Math.PI * sourceIndex / radiusSignature.Count;
        var radius = radiusSignature[sourceIndex];
        points[index] = new Point2d(
            centerX + Math.Cos(angle) * radius,
            centerY + Math.Sin(angle) * radius);
    }

    return points;
}

static ContourAxisFeature CreateAxisFeature(double angleDegrees, double ratio)
{
    return CreateAxisFeatureFromAngles(angleDegrees, angleDegrees, angleDegrees, ratio);
}

static ContourAxisFeature CreateAxisFeatureFromAngles(
    double regionAngleDegrees,
    double edgeAngleDegrees,
    double ellipseAngleDegrees,
    double ratio)
{
    return new ContourAxisFeature(
        RegionRatio: ratio,
        RegionAngleDegrees: regionAngleDegrees,
        EdgeRatio: ratio,
        EdgeAngleDegrees: edgeAngleDegrees,
        EllipseRatio: ratio,
        EllipseAngleDegrees: ellipseAngleDegrees,
        HasEllipse: true,
        MeanRatio: ratio,
        MaximumAngleSpreadDegrees: Math.Max(
            Math.Abs(AngleMath.NormalizeDeltaDegrees(regionAngleDegrees, edgeAngleDegrees)),
            Math.Max(
                Math.Abs(AngleMath.NormalizeDeltaDegrees(regionAngleDegrees, ellipseAngleDegrees)),
                Math.Abs(AngleMath.NormalizeDeltaDegrees(edgeAngleDegrees, ellipseAngleDegrees)))));
}

static IReadOnlyList<float> CreateSyntheticRadiusSignature(int shiftBins)
{
    const int sampleCount = 720;
    var signature = new float[sampleCount];
    for (var index = 0; index < sampleCount; index++)
    {
        var angle = 2.0 * Math.PI * (index - shiftBins) / sampleCount;
        signature[index] = (float)(100.0 +
            14.0 * Math.Cos(angle) +
            6.0 * Math.Cos(3.0 * angle + 0.4) +
            4.0 * Math.Sin(5.0 * angle));
    }

    return signature;
}

static IReadOnlyList<float> CreateDistinctiveRadiusSignature(int shiftBins, bool perturb = false)
{
    const int sampleCount = 720;
    var signature = new float[sampleCount];
    for (var index = 0; index < sampleCount; index++)
    {
        var angle = 2.0 * Math.PI * (index - shiftBins) / sampleCount;
        var notch = Math.Exp(-Math.Pow(AngleDeltaRadians(angle, 0.75), 2.0) / 0.012);
        var bump = Math.Exp(-Math.Pow(AngleDeltaRadians(angle, 3.8), 2.0) / 0.02);
        var perturbation = perturb ? 0.35 * Math.Sin(11.0 * angle + 0.2) : 0.0;
        signature[index] = (float)(100.0 +
            12.0 * Math.Cos(angle + 0.25) +
            4.0 * Math.Sin(3.0 * angle) -
            28.0 * notch +
            18.0 * bump +
            perturbation);
    }

    return signature;
}

static IReadOnlyList<float> CreateFourWaySymmetricRadiusSignature(int shiftBins)
{
    const int sampleCount = 720;
    var signature = new float[sampleCount];
    for (var index = 0; index < sampleCount; index++)
    {
        var angle = 2.0 * Math.PI * (index - shiftBins) / sampleCount;
        signature[index] = (float)(105.0 +
            22.0 * Math.Cos(2.0 * angle) +
            4.0 * Math.Cos(4.0 * angle + 0.15));
    }

    return signature;
}

static IReadOnlyList<float> CreateCircleRadiusSignature()
{
    const int sampleCount = 720;
    var signature = new float[sampleCount];
    Array.Fill(signature, 105f);
    return signature;
}

static IReadOnlyList<float> CreateStrongAsymmetricRadiusSignature(int shiftBins)
{
    const int sampleCount = 720;
    var signature = new float[sampleCount];
    for (var index = 0; index < sampleCount; index++)
    {
        var angle = 2.0 * Math.PI * (index - shiftBins) / sampleCount;
        var sharpNotch = Math.Exp(-Math.Pow(AngleDeltaRadians(angle, 0.55), 2.0) / 0.006);
        var wideBump = Math.Exp(-Math.Pow(AngleDeltaRadians(angle, 2.15), 2.0) / 0.018);
        var rearNotch = Math.Exp(-Math.Pow(AngleDeltaRadians(angle, 4.95), 2.0) / 0.012);
        var smallBump = Math.Exp(-Math.Pow(AngleDeltaRadians(angle, 5.72), 2.0) / 0.004);
        signature[index] = (float)(112.0 +
            9.0 * Math.Cos(angle + 0.18) +
            5.0 * Math.Sin(3.0 * angle - 0.45) -
            46.0 * sharpNotch +
            30.0 * wideBump -
            18.0 * rearNotch +
            16.0 * smallBump);
    }

    return signature;
}

static IReadOnlyList<float> CreateAmbiguousEllipseRadiusSignature(int shiftBins)
{
    const int sampleCount = 720;
    var signature = new float[sampleCount];
    for (var index = 0; index < sampleCount; index++)
    {
        var angle = 2.0 * Math.PI * (index - shiftBins) / sampleCount;
        signature[index] = (float)(100.0 + 7.0 * Math.Cos(2.0 * angle));
    }

    return signature;
}

static IReadOnlyList<float> CreateDefectBaseRadiusSignature()
{
    const int sampleCount = 720;
    var signature = new float[sampleCount];
    for (var index = 0; index < sampleCount; index++)
    {
        var angle = 2.0 * Math.PI * index / sampleCount;
        signature[index] = (float)(140.0 +
            10.0 * Math.Cos(angle + 0.2) +
            5.0 * Math.Sin(3.0 * angle - 0.35));
    }

    return signature;
}

static IReadOnlyList<float> NormalizeTestRadiusSignature(IReadOnlyList<float> signature)
{
    var normalized = signature.ToArray();
    if (normalized.Length == 0)
    {
        return normalized;
    }

    var mean = normalized.Average(value => (double)value);
    var stdDev = Math.Sqrt(Math.Max(
        normalized
            .Select(value => ((double)value - mean) * ((double)value - mean))
            .DefaultIfEmpty(0.0)
            .Average(),
        0.0));
    if (stdDev < 0.000001)
    {
        Array.Fill(normalized, 0f);
        return normalized;
    }

    for (var index = 0; index < normalized.Length; index++)
    {
        normalized[index] = (float)((normalized[index] - mean) / stdDev);
    }

    return normalized;
}

static double CalculateTestRadiusSignal(IReadOnlyList<float> signature)
{
    if (signature.Count == 0)
    {
        return 0.0;
    }

    var mean = signature.Average(value => (double)value);
    return Math.Sqrt(Math.Max(
        signature
            .Select(value => ((double)value - mean) * ((double)value - mean))
            .DefaultIfEmpty(0.0)
            .Average(),
        0.0));
}

static double AngleDeltaRadians(double left, double right)
{
    var delta = left - right;
    while (delta <= -Math.PI)
    {
        delta += 2.0 * Math.PI;
    }

    while (delta > Math.PI)
    {
        delta -= 2.0 * Math.PI;
    }

    return delta;
}

static double Distance(double leftX, double leftY, double rightX, double rightY)
{
    var dx = leftX - rightX;
    var dy = leftY - rightY;
    return Math.Sqrt(dx * dx + dy * dy);
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
