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
                BackSideNgRule = "外轮廓半径序列镜像匹配：比较当前工件与正面模板、镜像模板的整圈半径误差；旧的人工凸起特征不参与判断。",
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
                LegacyFrontBack = context.FrontBackDebug,
                ContourSampleMirrorDecision = output.ContourSampleMirrorDecisionDiagnostic
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
            Purpose = "正常自动生产过程中的被动PLC验证；不会额外发出测试动作命令。",
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
                D1000ClearTiming = "拍照完成后由上位机清零",
                Handshake = "D1000触发拍照；上位机拍照完成后清D1000；检测完成后写D1010=1/2。",
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
                Compare = "OK结果请比较ExpectedPlcCorrectionCommand与ReadbackAfterWrite里的D1002/D1004/D1006；PLC四舍五入后应一致。D1002/D1004/D1006是R轴中心后的最终纠偏量。",
                RAxisDirection = "RAxisCenter.MachineAngleDirection表示PLC R正方向在机器XY平面里的旋转方向；旧标定文件为0时，会从保存的R轴中心点自动推断。",
                IfPartDoesNotFit = "如果工件放不进模具，请结合RawImagePath/ResultImagePath复盘同一张图片，并对比机器实际动作。",
                Limitation = "本报告只能证明上位机写入和读回的PLC数值；不能单独证明机器实际物理运动方向。"
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
