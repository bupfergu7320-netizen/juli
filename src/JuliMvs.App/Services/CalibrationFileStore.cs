using System.Globalization;
using System.IO;
using System.Text.Json;
using JuliMvs.Core.Batch;
using JuliMvs.Core.Camera;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;
using OpenCvSharp;

namespace JuliMvs.App.Services;

internal sealed class CalibrationFileStore
{
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public CalibrationFileStore(string baseDirectory, JsonSerializerOptions jsonOptions)
    {
        _baseDirectory = baseDirectory;
        _jsonOptions = jsonOptions;
    }

    public string SaveCalibrationBoardDiagnosticImage(
        Mat diagnostic,
        bool success,
        CalibrationImageSaveTarget? saveTarget = null)
    {
        var now = DateTime.Now;
        var directory = saveTarget is null
            ? Path.Combine(_baseDirectory, "Data", "Diagnostics", now.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
            : GetCalibrationImageDirectory(saveTarget.Kind, "Diagnostic", now);
        Directory.CreateDirectory(directory);

        var prefix = saveTarget is null
            ? success ? "calibration-board-success" : "calibration-board-fail"
            : $"{saveTarget.FilePrefix}-{(success ? "success" : "fail")}";
        var path = Path.Combine(directory, $"{prefix}-{now:HHmmssfff}.bmp");
        Cv2.ImWrite(path, diagnostic);
        return path;
    }

    public string SaveCalibrationReport(CalibrationReportContext context)
    {
        var now = DateTimeOffset.Now;
        var directory = Path.Combine(
            _baseDirectory,
            "Data",
            "CalibrationReports",
            now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);

        var fileName = $"{now:HHmmssfff}-{SanitizeFileName(context.CalibrationType)}.json";
        var path = Path.Combine(directory, fileName);
        var report = new
        {
            SchemaVersion = 1,
            SavedAt = now,
            context.CalibrationType,
            context.Quality,
            context.Calibration,
            context.Thresholds,
            context.CurrentMachine
        };

        File.WriteAllText(path, JsonSerializer.Serialize(report, _jsonOptions));
        return path;
    }

    public CameraCaptureContext CreateCameraCaptureContext(
        CalibrationImageSaveTarget? saveTarget,
        CameraDeviceInfo? camera,
        BatchSession batchSession,
        CameraAcquisitionSettings cameraSettings,
        VisionParameters visionParameters)
    {
        var now = DateTime.Now;
        var directory = saveTarget is null
            ? Path.Combine(_baseDirectory, "Data", "Camera", now.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
            : GetCalibrationImageDirectory(saveTarget.Kind, "Raw", now);
        var prefix = saveTarget?.FilePrefix ?? "camera";
        var imagePath = Path.Combine(directory, $"{prefix}-{now:HHmmssfff}.bmp");
        return new CameraCaptureContext(
            directory,
            imagePath,
            Path.ChangeExtension(imagePath, ".json"),
            camera,
            batchSession.BatchNo,
            batchSession.ProductName,
            batchSession.Status.ToString(),
            cameraSettings,
            visionParameters);
    }

    public void SaveCameraMetadata(CameraCaptureContext context, CameraFrame frame)
    {
        var metadata = new
        {
            context.ImagePath,
            CapturedAt = frame.CapturedAt,
            SavedAt = DateTimeOffset.Now,
            frame.FrameNumber,
            frame.Width,
            frame.Height,
            frame.PixelFormat,
            frame.ActualExposureTimeMicroseconds,
            context.Camera,
            context.BatchNo,
            context.ProductName,
            context.BatchStatus,
            context.CameraSettings,
            context.VisionParameters
        };

        File.WriteAllText(
            context.MetadataPath,
            JsonSerializer.Serialize(metadata, _jsonOptions));
    }

    public static CalibrationImageSaveTarget CreateLensDistortionImageSaveTarget(int imageNumber)
    {
        return new CalibrationImageSaveTarget(
            CalibrationImageKind.LensDistortion,
            $"lens-distortion-{Math.Max(1, imageNumber):00}");
    }

    public static CalibrationImageSaveTarget CreateNinePointImageSaveTarget(int pointNumber)
    {
        return new CalibrationImageSaveTarget(
            CalibrationImageKind.NinePointXY,
            $"nine-point-pt{Math.Max(1, pointNumber):00}");
    }

    public static CalibrationImageSaveTarget CreateCombinedCalibrationImageSaveTarget(int pointNumber)
    {
        return new CalibrationImageSaveTarget(
            CalibrationImageKind.CombinedCalibration,
            $"combined-pt{Math.Max(1, pointNumber):00}");
    }

    public static CalibrationImageSaveTarget CreateRAxisCenterImageSaveTarget(double angleDegrees)
    {
        return new CalibrationImageSaveTarget(
            CalibrationImageKind.RAxisCenter,
            $"r-axis-{FormatAngleFileToken(angleDegrees)}");
    }

    private string GetCalibrationImageDirectory(
        CalibrationImageKind kind,
        string imageType,
        DateTime timestamp)
    {
        return Path.Combine(
            _baseDirectory,
            "Data",
            "Calibration",
            GetCalibrationImageKindDirectoryName(kind),
            timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            imageType);
    }

    private static string GetCalibrationImageKindDirectoryName(CalibrationImageKind kind)
    {
        return kind switch
        {
            CalibrationImageKind.CombinedCalibration => "CombinedCalibration",
            CalibrationImageKind.LensDistortion => "LensDistortion",
            CalibrationImageKind.NinePointXY => "NinePointXY",
            CalibrationImageKind.RAxisCenter => "RAxisCenter",
            _ => kind.ToString()
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "calibration" : sanitized;
    }

    private static string FormatAngleFileToken(double angleDegrees)
    {
        var absoluteAngle = Math.Abs(angleDegrees);
        var angleText = absoluteAngle.ToString("000.###", CultureInfo.InvariantCulture).Replace('.', 'p');
        return angleDegrees < -0.0005 ? $"rm{angleText}" : $"r{angleText}";
    }
}

internal enum CalibrationImageKind
{
    CombinedCalibration,
    LensDistortion,
    NinePointXY,
    RAxisCenter
}

internal sealed record CalibrationImageSaveTarget(
    CalibrationImageKind Kind,
    string FilePrefix);

internal sealed record CameraCaptureContext(
    string Directory,
    string ImagePath,
    string MetadataPath,
    CameraDeviceInfo? Camera,
    string BatchNo,
    string ProductName,
    string BatchStatus,
    CameraAcquisitionSettings CameraSettings,
    VisionParameters VisionParameters);

internal sealed record CalibrationReportContext(
    string CalibrationType,
    object Calibration,
    object Quality,
    CalibrationReportThresholds Thresholds,
    CurrentMachineCalibrationSnapshot CurrentMachine);

internal sealed record CalibrationReportThresholds(
    double MaximumAcceptedLensDistortionRmsPixels,
    double MaximumAcceptedCameraCalibrationRmsMm,
    int MinimumAcceptedRAxisCenterPointCount,
    double MinimumAcceptedRAxisCenterAngleCoverageDegrees,
    double MaximumAcceptedRAxisCenterRmsMm,
    double MaximumAcceptedRAxisCenterMaxMm);

internal sealed record CurrentMachineCalibrationSnapshot(
    string LensDistortionCalibrationId,
    string CameraCalibrationId,
    string RAxisCenterCalibrationId,
    PlcOutputTransform PlcOutputTransform);
