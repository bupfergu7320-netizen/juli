using System.Globalization;
using System.IO;
using System.Text.Json;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;
using JuliMvs.Vision;

namespace JuliMvs.App.Services;

internal sealed class InspectionReportWriter
{
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public InspectionReportWriter(string baseDirectory, JsonSerializerOptions jsonOptions)
    {
        _baseDirectory = baseDirectory;
        _jsonOptions = jsonOptions;
    }

    public string SaveInspectionReport(InspectionReportContext context)
    {
        var now = DateTimeOffset.Now;
        var directory = Path.Combine(
            _baseDirectory,
            "Data",
            "InspectionReports",
            now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var fileName = $"{now:HHmmssfff}-{SanitizeFileName(context.Result.PartNo)}-{context.Result.Id:N}.json";
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(BuildInspectionReport(context, now), _jsonOptions));
        return path;
    }

    public string SavePassivePlcVerificationReport(PassivePlcVerificationReportContext context)
    {
        var now = DateTimeOffset.Now;
        var directory = Path.Combine(
            _baseDirectory,
            "Data",
            "PassivePlcVerificationReports",
            now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var fileName = $"{now:HHmmssfff}-{SanitizeFileName(context.Result.PartNo)}-{context.Result.Id:N}.json";
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(BuildPassivePlcVerificationReport(context, now), _jsonOptions));
        return path;
    }

    private static object BuildInspectionReport(InspectionReportContext context, DateTimeOffset savedAt)
    {
        var result = context.Result;
        var template = context.Template;
        var parameters = context.Parameters;
        var output = context.Output;
        var measurement = result.Measurement;
        var plcOutput = measurement is null
            ? null
            : PlcInspectionOutputCalculator.CalculateFinalCorrection(measurement, context.PlcOutputTransform);
        var visionDeviation = measurement is null
            ? null
            : new PlcOutputCommand(
                measurement.XOffsetMm,
                measurement.YOffsetMm,
                measurement.AngleOffsetDegrees);
        var correctionDeviation = measurement is null
            ? null
            : new PlcOutputCommand(
                -measurement.XOffsetMm,
                -measurement.YOffsetMm,
                -measurement.AngleOffsetDegrees);
        var finalCorrection = measurement is null
            ? null
            : new PlcOutputCommand(
                measurement.XCompensationMm,
                measurement.YCompensationMm,
                measurement.RotationCompensationDegrees);

        return new
        {
            SchemaVersion = 1,
            SavedAt = savedAt,
            TriggerSource = context.TriggerSource,
            WriteToPlc = context.WriteToPlc,
            Result = result,
            Files = new
            {
                RawImagePath = result.RawImagePath,
                ResultImagePath = result.ResultImagePath
            },
            Template = new
            {
                template.Id,
                template.ProductName,
                template.BatchNo,
                template.ImagePath,
                template.CreatedAt,
                template.ReferenceCenterXPixel,
                template.ReferenceCenterYPixel,
                template.ReferenceCenterXMm,
                template.ReferenceCenterYMm,
                template.ReferenceAngleDegrees,
                template.WidthMm,
                template.HeightMm,
                template.AreaPixels,
                template.ReferenceWidthPixels,
                template.ReferenceHeightPixels,
                template.MatchScoreBaseline,
                template.SourceCameraCalibrationId,
                template.SourceDistortionCalibrationId
            },
            VisionParameters = new
            {
                parameters.Roi,
                parameters.BinaryThreshold,
                parameters.BlurKernelSize,
                parameters.MinPartAreaPixels,
                parameters.MaxPartAreaPixels,
                parameters.AngleToleranceDegrees,
                parameters.XPositionToleranceMm,
                parameters.YPositionToleranceMm,
                parameters.WidthToleranceMm,
                parameters.HeightToleranceMm,
                parameters.AreaTolerancePercent,
                parameters.ShapeScoreThreshold,
                parameters.AngleDetectionMode,
                parameters.TemplateAngleSearchRangeDegrees,
                parameters.TemplateAngleCoarseStepDegrees,
                parameters.TemplateAngleFineStepDegrees,
                parameters.TemplateAngleMinimumScore,
                parameters.TemplateAngleMinimumScoreMargin,
                parameters.InvertXCompensation,
                parameters.InvertYCompensation,
                parameters.InvertRotationCompensation,
                parameters.BackSideNgEnabled,
                BackSideNgRule = "ContourMirror.ScoreDifference < 0",
                parameters.BackSideNgMinimumBackScore,
                parameters.BackSideNgMaximumScoreDifference
            },
            Calibration = new
            {
                LensDistortion = new
                {
                    parameters.LensDistortionCalibration.Enabled,
                    parameters.LensDistortionCalibration.CalibrationId,
                    parameters.LensDistortionCalibration.ImageWidth,
                    parameters.LensDistortionCalibration.ImageHeight,
                    parameters.LensDistortionCalibration.RmsReprojectionErrorPixels,
                    parameters.LensDistortionCalibration.CapturedImageCount,
                    parameters.LensDistortionCalibration.CreatedAt
                },
                Camera = new
                {
                    parameters.CameraCalibration.Enabled,
                    parameters.CameraCalibration.CalibrationId,
                    parameters.CameraCalibration.SourceDistortionCalibrationId,
                    parameters.CameraCalibration.RmsErrorMm,
                    PointCount = parameters.CameraCalibration.Points.Count,
                    parameters.CameraCalibration.CreatedAt
                },
                RAxisCenter = new
                {
                    parameters.RAxisCenterCalibration.Enabled,
                    parameters.RAxisCenterCalibration.CalibrationId,
                    parameters.RAxisCenterCalibration.CenterXMm,
                    parameters.RAxisCenterCalibration.CenterYMm,
                    parameters.RAxisCenterCalibration.RadiusMm,
                    parameters.RAxisCenterCalibration.RmsErrorMm,
                    parameters.RAxisCenterCalibration.MaxErrorMm,
                    parameters.RAxisCenterCalibration.MachineAngleDirection,
                    parameters.RAxisCenterCalibration.SourceCameraCalibrationId,
                    parameters.RAxisCenterCalibration.CaptureTarget,
                    PointCount = parameters.RAxisCenterCalibration.Points.Count,
                    parameters.RAxisCenterCalibration.CreatedAt
                }
            },
            Alignment = output.AlignmentSnapshot,
            CandidateDiagnostics = output.CandidateDiagnostics,
            Angle = output.AngleDiagnostic,
            DebugOnly = new
            {
                FrontBack = context.FrontBackDebug,
                ProductionFrontBackDecision = output.FrontBackDecisionDiagnostic
            },
            Plc = new
            {
                Connected = context.PlcConnected,
                Host = context.PlcHost,
                Port = context.PlcPort,
                OutputTransform = context.PlcOutputTransform,
                VisionDeviation = visionDeviation,
                PreRotationCorrection = correctionDeviation,
                RAxisCenterReferenceCorrection = finalCorrection,
                PlcFinalCorrectionCommand = plcOutput,
                PlcPreRotationCorrectionCommand = measurement is null
                    ? null
                    : PlcInspectionOutputCalculator.CalculatePreRotationCorrection(measurement, context.PlcOutputTransform)
            }
        };
    }

    private static object BuildPassivePlcVerificationReport(
        PassivePlcVerificationReportContext context,
        DateTimeOffset savedAt)
    {
        var measurement = context.Result.Measurement;
        var visionDeviation = measurement is null
            ? null
            : new PlcOutputCommand(
                measurement.XOffsetMm,
                measurement.YOffsetMm,
                measurement.AngleOffsetDegrees);
        var correctionDeviation = measurement is null
            ? null
            : new PlcOutputCommand(
                -measurement.XOffsetMm,
                -measurement.YOffsetMm,
                -measurement.AngleOffsetDegrees);
        var finalCorrection = measurement is null
            ? null
            : new PlcOutputCommand(
                measurement.XCompensationMm,
                measurement.YCompensationMm,
                measurement.RotationCompensationDegrees);
        var expectedPlcOutput = measurement is null
            ? null
            : PlcInspectionOutputCalculator.CalculateFinalCorrection(measurement, context.PlcOutputTransform);
        var expectedRoundedPlcOutput = expectedPlcOutput is null
            ? null
            : new PlcOutputCommand(
                MitsubishiModbusTcpPlcClient.RoundDeviationForPlc(expectedPlcOutput.XDeviation),
                MitsubishiModbusTcpPlcClient.RoundDeviationForPlc(expectedPlcOutput.YDeviation),
                MitsubishiModbusTcpPlcClient.RoundDeviationForPlc(expectedPlcOutput.RDeviation));

        return new
        {
            SchemaVersion = 1,
            SavedAt = savedAt,
            Purpose = "Passive PLC verification during normal automatic production. No extra unit-test motion was commanded.",
            Safety = new
            {
                PassiveOnly = true,
                DoesNotWriteAdditionalPlcCommands = true,
                UsesNormalInspectionResultWrite = true,
                OperatorDoesNotNeedManualUnitVerification = true
            },
            Inspection = new
            {
                context.Result.Id,
                context.Result.BatchNo,
                context.Result.PartNo,
                context.Result.Decision,
                context.Result.NgReason,
                context.Result.Message,
                context.Result.CreatedAt,
                Measurement = measurement,
                Files = new
                {
                    context.Result.RawImagePath,
                    context.Result.ResultImagePath
                },
                context.TriggerSource
            },
            Plc = new
            {
                Host = context.PlcHost,
                Port = context.PlcPort,
                Connected = context.PlcWriteOutcome.IsConnected,
                TriggerCleared = context.PlcWriteOutcome.TriggerCleared,
                WaitingForReset = context.PlcWriteOutcome.ShouldSetPlcStatusWaitingReset,
                ResultCodeExpected = context.Result.Decision == InspectionDecision.Ok && measurement is not null ? 1 : 2,
                OutputTransform = context.PlcOutputTransform,
                VisionDeviation = visionDeviation,
                PreRotationCorrection = correctionDeviation,
                RAxisCenterReferenceCorrection = finalCorrection,
                ExpectedPlcCorrectionCommand = expectedPlcOutput,
                ExpectedPreRotationCorrectionCommand = measurement is null
                    ? null
                    : PlcInspectionOutputCalculator.CalculatePreRotationCorrection(measurement, context.PlcOutputTransform),
                ExpectedRoundedPlcCorrectionCommand = expectedRoundedPlcOutput,
                ReadbackAfterWrite = context.PlcWriteOutcome.ReadbackAfterWrite,
                LastReadbackBeforeReturn = context.PlcWriteOutcome.LastReadbackBeforeReturn
            },
            Interpretation = new
            {
                Compare = "For OK results, compare ExpectedPlcCorrectionCommand with ReadbackAfterWrite D1002/D1004/D1006. They should match after PLC rounding. D1002/D1004/D1006 are final R-axis-center correction values.",
                RAxisDirection = "RAxisCenter.MachineAngleDirection is the machine XY rotation direction for positive PLC R. Old calibration files with 0 are inferred from saved R-axis center points.",
                IfPartDoesNotFit = "Use RawImagePath/ResultImagePath with this report to replay the same image and compare actual machine behavior.",
                Limitation = "This report proves what the PC wrote and read back. It does not measure the physical machine movement by itself."
            }
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }
}

internal sealed record InspectionReportContext(
    InspectionResult Result,
    PartTemplate Template,
    VisionParameters Parameters,
    OpenCvInspectionOutput Output,
    string TriggerSource,
    bool WriteToPlc,
    bool PlcConnected,
    string PlcHost,
    int PlcPort,
    PlcOutputTransform PlcOutputTransform,
    FrontBackDebugResult? FrontBackDebug = null);

internal sealed record PassivePlcVerificationReportContext(
    InspectionResult Result,
    string TriggerSource,
    PlcInspectionResultWriteOutcome PlcWriteOutcome,
    string PlcHost,
    int PlcPort,
    PlcOutputTransform PlcOutputTransform);
