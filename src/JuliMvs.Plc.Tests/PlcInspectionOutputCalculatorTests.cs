using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;

VerifyPlcFinalCorrectionUsesRDirectionFromCalibration();
VerifyRCommandDirectionIsCoupledIntoXyCompensation();
VerifyLegacyRAxisCalibrationDirectionIsInferred();
VerifyPlcOutputUsesFinalCorrectionMeasurement();
VerifyXyOutputSignsDoNotChangeR();

Console.WriteLine("PLC final correction output uses R-axis center, machine R direction, and XCompensation/YCompensation/RotationCompensation.");

static void VerifyPlcFinalCorrectionUsesRDirectionFromCalibration()
{
    var currentPose = new PartPose2D(8.717255660872011, 12.64648054091137, 84.33216094970703);
    var templatePose = new PartPose2D(-2.9405798910181833, -2.8623001798228245, 50.33216094970703);
    var rAxisCenter = new RAxisCenterCalibration
    {
        Enabled = true,
        CenterXMm = -6.708302254717929,
        CenterYMm = 20.677342510033046,
        MachineAngleDirection = -1
    };

    var snapshot = XyrAlignmentSolver.Solve(currentPose, templatePose, rAxisCenter, allowFullRotation: true);

    AssertEqual(-34.0, snapshot.HomeRActionDegrees, nameof(snapshot.HomeRActionDegrees));
    AssertEqual(-13.51, Math.Round(snapshot.HomeXActionMm, 2, MidpointRounding.AwayFromZero), nameof(snapshot.HomeXActionMm));
    AssertEqual(-25.51, Math.Round(snapshot.HomeYActionMm, 2, MidpointRounding.AwayFromZero), nameof(snapshot.HomeYActionMm));

    var wrongDirection = XyrAlignmentSolver.Solve(
        currentPose,
        templatePose,
        rAxisCenter with { MachineAngleDirection = 1 },
        allowFullRotation: true);
    AssertEqual(-4.53, Math.Round(wrongDirection.HomeXActionMm, 2, MidpointRounding.AwayFromZero), "wrongDirection.HomeXActionMm");
    AssertEqual(-8.26, Math.Round(wrongDirection.HomeYActionMm, 2, MidpointRounding.AwayFromZero), "wrongDirection.HomeYActionMm");
}

static void VerifyRCommandDirectionIsCoupledIntoXyCompensation()
{
    var currentPose = new PartPose2D(8.41, 5.95, -1.04);
    var templatePose = new PartPose2D(-3.44, -1.72, 54.46);
    var rAxisCenter = new RAxisCenterCalibration
    {
        Enabled = true,
        CenterXMm = -6.708302254717929,
        CenterYMm = 20.677342510033046,
        MachineAngleDirection = -1
    };

    var normal = XyrAlignmentSolver.Solve(
        currentPose,
        templatePose,
        rAxisCenter,
        rCommandDirection: 1,
        allowFullRotation: true);
    var inverted = XyrAlignmentSolver.Solve(
        currentPose,
        templatePose,
        rAxisCenter,
        rCommandDirection: -1,
        allowFullRotation: true);

    AssertEqual(55.50, Math.Round(normal.VisionHomeRActionDegrees, 2, MidpointRounding.AwayFromZero), nameof(normal.VisionHomeRActionDegrees));
    AssertEqual(55.50, Math.Round(normal.HomeRActionDegrees, 2, MidpointRounding.AwayFromZero), nameof(normal.HomeRActionDegrees));
    AssertEqual(-55.50, Math.Round(inverted.HomeRActionDegrees, 2, MidpointRounding.AwayFromZero), nameof(inverted.HomeRActionDegrees));
    AssertEqual(1, normal.RCommandDirection, nameof(normal.RCommandDirection));
    AssertEqual(-1, inverted.RCommandDirection, nameof(inverted.RCommandDirection));

    if (Math.Abs(normal.HomeXActionMm - inverted.HomeXActionMm) < 1.0 ||
        Math.Abs(normal.HomeYActionMm - inverted.HomeYActionMm) < 1.0)
    {
        throw new InvalidOperationException("Inverting PLC R command must recompute XY around the R-axis center.");
    }
}

static void VerifyLegacyRAxisCalibrationDirectionIsInferred()
{
    var calibration = new RAxisCenterCalibration
    {
        Enabled = true,
        Points =
        [
            new RAxisCenterCalibrationPoint(0, 0, 0, 0.011916828422779524, -0.0003857968073361917),
            new RAxisCenterCalibrationPoint(45, 0, 0, -16.599228042277446, 1.314396215272808),
            new RAxisCenterCalibrationPoint(90, 0, 0, -27.426388945099216, 13.98988853961717),
            new RAxisCenterCalibrationPoint(135, 0, 0, -26.111751143132274, 30.573299782676145),
            new RAxisCenterCalibrationPoint(180, 0, 0, -13.429280563640596, 41.37401709010146),
            new RAxisCenterCalibrationPoint(225, 0, 0, 3.1903702172673145, 40.031564413014536),
            new RAxisCenterCalibrationPoint(270, 0, 0, 14.017379977523754, 27.35626490112253),
            new RAxisCenterCalibrationPoint(315, 0, 0, 12.680563633192243, 10.77969493526706)
        ]
    };

    AssertEqual(-1, calibration.GetMachineAngleDirection(), nameof(calibration.GetMachineAngleDirection));
}

static void VerifyPlcOutputUsesFinalCorrectionMeasurement()
{
var measurement = new InspectionMeasurement(
    CenterXPixel: 0,
    CenterYPixel: 0,
    XOffsetMm: 28.30,
    YOffsetMm: 19.56,
    XCompensationMm: -5.29,
    YCompensationMm: -54.59,
    AngleDegrees: -70.96,
    AngleOffsetDegrees: -82.00,
    RotationCompensationDegrees: 82.00,
    WidthMm: 254.0,
    HeightMm: 253.0,
    AreaPixels: 4510000.0,
    MatchScore: 0.99);

var transform = PlcOutputTransform.Identity;
var finalOutput = PlcInspectionOutputCalculator.CalculateFinalCorrection(measurement, transform);
var preRotationOutput = PlcInspectionOutputCalculator.CalculatePreRotationCorrection(measurement, transform);

AssertEqual(-5.29, finalOutput.XDeviation, nameof(finalOutput.XDeviation));
AssertEqual(-54.59, finalOutput.YDeviation, nameof(finalOutput.YDeviation));
AssertEqual(82.00, finalOutput.RDeviation, nameof(finalOutput.RDeviation));

AssertEqual(-28.30, preRotationOutput.XDeviation, nameof(preRotationOutput.XDeviation));
AssertEqual(-19.56, preRotationOutput.YDeviation, nameof(preRotationOutput.YDeviation));
AssertEqual(82.00, preRotationOutput.RDeviation, nameof(preRotationOutput.RDeviation));

if (Math.Abs(finalOutput.XDeviation - preRotationOutput.XDeviation) < 0.001 ||
    Math.Abs(finalOutput.YDeviation - preRotationOutput.YDeviation) < 0.001)
{
    throw new InvalidOperationException("Final PLC XY output must use R-axis-center compensation, not pre-rotation correction.");
}
}

static void VerifyXyOutputSignsDoNotChangeR()
{
    var measurement = new InspectionMeasurement(
        CenterXPixel: 0,
        CenterYPixel: 0,
        XOffsetMm: 0,
        YOffsetMm: 0,
        XCompensationMm: 12.34,
        YCompensationMm: -56.78,
        AngleDegrees: 0,
        AngleOffsetDegrees: 0,
        RotationCompensationDegrees: 9.87,
        WidthMm: 254.0,
        HeightMm: 253.0,
        AreaPixels: 4510000.0,
        MatchScore: 0.99);

    var invertedX = PlcInspectionOutputCalculator.CalculateFinalCorrection(
        measurement,
        PlcOutputTransform.Identity with { Xx = -1.0 });
    var invertedY = PlcInspectionOutputCalculator.CalculateFinalCorrection(
        measurement,
        PlcOutputTransform.Identity with { Yy = -1.0 });
    var invertedBoth = PlcInspectionOutputCalculator.CalculateFinalCorrection(
        measurement,
        PlcOutputTransform.Identity with { Xx = -1.0, Yy = -1.0 });

    AssertEqual(-12.34, invertedX.XDeviation, "invertedX.XDeviation");
    AssertEqual(-56.78, invertedX.YDeviation, "invertedX.YDeviation");
    AssertEqual(9.87, invertedX.RDeviation, "invertedX.RDeviation");

    AssertEqual(12.34, invertedY.XDeviation, "invertedY.XDeviation");
    AssertEqual(56.78, invertedY.YDeviation, "invertedY.YDeviation");
    AssertEqual(9.87, invertedY.RDeviation, "invertedY.RDeviation");

    AssertEqual(-12.34, invertedBoth.XDeviation, "invertedBoth.XDeviation");
    AssertEqual(56.78, invertedBoth.YDeviation, "invertedBoth.YDeviation");
    AssertEqual(9.87, invertedBoth.RDeviation, "invertedBoth.RDeviation");
}

static void AssertEqual(double expected, double actual, string name)
{
    if (Math.Abs(expected - actual) > 0.0001)
    {
        throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }
}
