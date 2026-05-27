using JuliMvs.App.Services;
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
VerifyProductionInspectionCreatesOkZeroCorrection();
VerifyProductionInspectionCreatesOkXyrCorrection();
VerifyProductionInspectionCreatesBackSideNgZeroCorrection();
VerifyClearCalibrationDisablesAllCalibrationButKeepsProductionSettings();
VerifyProductRecipeKeepsBackSideNgPerProduct();
VerifyProductRecipeSaveFromTemplateKeepsTemplateBackSideNg();
VerifyProductRecipeDefaultsAngleDetectionToAuto();
VerifyAutoAngleStrategyClassifiesMixedRoundParts();
VerifyProductRecipeClearsLegacyFrontBump();
VerifyBackSideNgDoesNotRequireSelectedFrontBump();
VerifyContourFeatureExtractorClassifiesStrongEllipse();
VerifyContourFeatureExtractorLocksNearCircleR();
VerifyContourRadiusSignatureMatchesRotation();
VerifyContourFrontBackMatcherDetectsFront();
VerifyContourFrontBackMatcherDetectsBack();
VerifyContourFrontBackMatcherRejectsNearCircle();
VerifyPlcOutputDirectionSettingsApplySimpleXySigns();
VerifyDirectionFormatterShowsXySigns();
VerifyProductTemplateNameIsUniqueAndRebuildOverwrites();
VerifyLegacyDuplicateProductTemplatesKeepNewest();
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

static void VerifyProductionInspectionCreatesOkZeroCorrection()
{
    var result = ProductionInspectionResultFactory.CreateOk("BATCH-1", @"D:\image.bmp", "PART-1");
    var measurement = result.Measurement ?? throw new InvalidOperationException("bypass result should have measurement");

    AssertEqual(InspectionDecision.Ok.ToString(), result.Decision.ToString(), "bypass decision");
    AssertEqual(NgReason.None.ToString(), result.NgReason.ToString(), "bypass NG reason");
    AssertEqual("BATCH-1", result.BatchNo, "bypass batch");
    AssertEqual("PART-1", result.PartNo, "bypass part");
    AssertEqual(@"D:\image.bmp", result.RawImagePath, "bypass raw image");
    AssertDoubleEqual(0, measurement.XOffsetMm, "bypass X offset");
    AssertDoubleEqual(0, measurement.YOffsetMm, "bypass Y offset");
    AssertDoubleEqual(0, measurement.AngleOffsetDegrees, "bypass R offset");
    AssertDoubleEqual(0, measurement.XCompensationMm, "bypass X compensation");
    AssertDoubleEqual(0, measurement.YCompensationMm, "bypass Y compensation");
    AssertDoubleEqual(0, measurement.RotationCompensationDegrees, "bypass R compensation");
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

    var nearCircle = AutoAngleStrategy.Select(
        widthPixels: 201,
        heightPixels: 200,
        pcaRatio: 1.01,
        circularity: 0.98,
        templateRadiusSignalPixels: 0.1);
    AssertIntEqual(
        (int)AutoPartShapeClass.NearCircle,
        (int)nearCircle.ShapeClass,
        "near circle class");
    AssertIntEqual(
        (int)AutoAngleMethod.Disabled,
        (int)nearCircle.Method,
        "near circle method");
    AssertBoolEqual(false, nearCircle.AllowsRCorrection, "near circle R disabled");
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

static void VerifyContourFeatureExtractorLocksNearCircleR()
{
    using var image = CreateSyntheticPartImage((mat, center) =>
    {
        Cv2.Circle(mat, center, 95, Scalar.White, thickness: -1);
    });
    var extractor = new ContourFeatureExtractor();

    var feature = extractor.Extract(image);

    AssertEqual(AutoPartShapeClass.NearCircle.ToString(), feature.Strategy.ShapeClass.ToString(), "near circle shape");
    AssertEqual(AutoAngleMethod.Disabled.ToString(), feature.Strategy.Method.ToString(), "near circle method");
    AssertBoolEqual(false, feature.Strategy.AllowsRCorrection, "near circle locks R");
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

static void VerifyContourFrontBackMatcherDetectsFront()
{
    var extractor = new ContourFeatureExtractor();
    var matcher = new ContourFrontBackMatcher();
    using var templateImage = CreateAsymmetricRoundPartImage(mirrored: false);
    using var currentImage = CreateAsymmetricRoundPartImage(mirrored: false);
    var template = extractor.Extract(templateImage);
    var current = extractor.Extract(currentImage);

    var match = matcher.Match(current, template);

    AssertEqual(ContourFrontBackDecision.Front.ToString(), match.Decision.ToString(), "front/back front decision");
    AssertBoolEqual(true, match.IsReliable, "front/back front reliable");
    AssertBoolEqual(true, match.FrontErrorPixels < match.BackErrorPixels, "front/back front score order");
}

static void VerifyContourFrontBackMatcherDetectsBack()
{
    var extractor = new ContourFeatureExtractor();
    var matcher = new ContourFrontBackMatcher();
    using var templateImage = CreateAsymmetricRoundPartImage(mirrored: false);
    using var currentImage = CreateAsymmetricRoundPartImage(mirrored: true);
    var template = extractor.Extract(templateImage);
    var current = extractor.Extract(currentImage);

    var match = matcher.Match(current, template);

    AssertEqual(ContourFrontBackDecision.Back.ToString(), match.Decision.ToString(), "front/back back decision");
    AssertBoolEqual(true, match.IsReliable, "front/back back reliable");
    AssertBoolEqual(true, match.BackErrorPixels < match.FrontErrorPixels, "front/back back score order");
}

static void VerifyContourFrontBackMatcherRejectsNearCircle()
{
    var extractor = new ContourFeatureExtractor();
    var matcher = new ContourFrontBackMatcher();
    using var image = CreateSyntheticPartImage((mat, center) =>
    {
        Cv2.Circle(mat, center, 95, Scalar.White, thickness: -1);
    });
    var feature = extractor.Extract(image);

    var match = matcher.Match(feature, feature);

    AssertEqual(ContourFrontBackDecision.Unavailable.ToString(), match.Decision.ToString(), "front/back near circle unavailable");
    AssertBoolEqual(false, match.IsReliable, "front/back near circle reliable");
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

static Mat CreateAsymmetricRoundPartImage(bool mirrored)
{
    return CreateSyntheticPartImage((mat, center) =>
    {
        Cv2.Circle(mat, center, 95, Scalar.White, thickness: -1);
        var direction = mirrored ? -1 : 1;
        Cv2.Circle(mat, new Point(center.X + direction * 46, center.Y - 72), 28, Scalar.White, thickness: -1);
        Cv2.Circle(mat, new Point(center.X - direction * 72, center.Y + 34), 16, Scalar.Black, thickness: -1);
    });
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
