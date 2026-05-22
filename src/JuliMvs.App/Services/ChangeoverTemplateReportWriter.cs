using System.Globalization;
using System.IO;
using System.Text.Json;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Services;

internal sealed class ChangeoverTemplateReportWriter
{
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public ChangeoverTemplateReportWriter(string baseDirectory, JsonSerializerOptions jsonOptions)
    {
        _baseDirectory = baseDirectory;
        _jsonOptions = jsonOptions;
    }

    public ChangeoverTemplateSelfCheckEvidence SaveSelfCheckEvidence(
        ChangeoverTemplateSelfCheckReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var savedAt = DateTimeOffset.Now;
        var directory = Path.Combine(
            _baseDirectory,
            "Data",
            "ChangeoverTemplateSelfChecks",
            savedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            SanitizeFileName(context.Template.ProductName),
            SanitizeFileName(context.Template.BatchNo));
        Directory.CreateDirectory(directory);

        var fileToken = string.Create(
            CultureInfo.InvariantCulture,
            $"{savedAt:HHmmssfff}-{SanitizeFileName(context.Template.ProductName)}-{context.Template.Id:N}");
        var diagnosticImagePath = Path.Combine(directory, $"{fileToken}-diagnostic.bmp");
        if (!Cv2.ImWrite(diagnosticImagePath, context.SelfCheck.Output.DiagnosticImage))
        {
            throw new IOException($"Failed to save changeover template self-check diagnostic image: {diagnosticImagePath}");
        }

        var reportPath = Path.Combine(directory, $"{fileToken}.json");
        var report = BuildSelfCheckReport(context, savedAt, diagnosticImagePath);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, _jsonOptions));

        return new ChangeoverTemplateSelfCheckEvidence(diagnosticImagePath, reportPath);
    }

    private static object BuildSelfCheckReport(
        ChangeoverTemplateSelfCheckReportContext context,
        DateTimeOffset savedAt,
        string diagnosticImagePath)
    {
        var template = context.Template;
        var parameters = context.Parameters;
        var selfCheck = context.SelfCheck;
        var output = selfCheck.Output;

        return new
        {
            SchemaVersion = 1,
            SavedAt = savedAt,
            Purpose = "Changeover template self-check before saving template and recipe.",
            Passed = selfCheck.Passed,
            selfCheck.Message,
            Files = new
            {
                RawTemplateImagePath = context.RawTemplateImagePath,
                DiagnosticImagePath = diagnosticImagePath
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
                AngleDetectionMode = parameters.AngleDetectionMode.ToString(),
                AngleDetectionModeValue = (int)parameters.AngleDetectionMode,
                parameters.TemplateAngleSearchRangeDegrees,
                parameters.TemplateAngleCoarseStepDegrees,
                parameters.TemplateAngleFineStepDegrees,
                parameters.TemplateAngleMinimumScore,
                parameters.TemplateAngleMinimumScoreMargin,
                parameters.InvertXCompensation,
                parameters.InvertYCompensation,
                parameters.InvertRotationCompensation
            },
            ReplayInputs = new
            {
                Template = template,
                VisionParameters = parameters
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
            SelfCheck = new
            {
                selfCheck.Passed,
                selfCheck.Message,
                output.Result,
                output.Result.Measurement,
                output.AlignmentSnapshot,
                output.TemplateSimilarity,
                output.AngleDiagnostic,
                output.CandidateDiagnostics
            }
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }
}

internal sealed record ChangeoverTemplateSelfCheckReportContext(
    PartTemplate Template,
    VisionParameters Parameters,
    ChangeoverTemplateSelfCheckResult SelfCheck,
    string RawTemplateImagePath);

internal sealed record ChangeoverTemplateSelfCheckEvidence(
    string DiagnosticImagePath,
    string ReportPath);
