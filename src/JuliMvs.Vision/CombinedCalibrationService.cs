using JuliMvs.Core.Vision;
using OpenCvSharp;

namespace JuliMvs.Vision;

public sealed class CombinedCalibrationService
{
    public const int RequiredNinePointCount = 9;

    private readonly LensDistortionCalibrationService _lensDistortionCalibrationService;

    public CombinedCalibrationService()
        : this(new LensDistortionCalibrationService())
    {
    }

    public CombinedCalibrationService(LensDistortionCalibrationService lensDistortionCalibrationService)
    {
        _lensDistortionCalibrationService = lensDistortionCalibrationService;
    }

    public CombinedCalibrationResult Calibrate(IReadOnlyList<CombinedCalibrationInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count != RequiredNinePointCount)
        {
            throw new InvalidOperationException($"联合标定必须采集{RequiredNinePointCount}个9点XY点位。");
        }

        var hasDuplicateMachinePoint = inputs
            .GroupBy(input => (Math.Round(input.MachineXMm, 4), Math.Round(input.MachineYMm, 4)))
            .Any(group => group.Count() > 1);
        if (hasDuplicateMachinePoint)
        {
            throw new InvalidOperationException("9个机械点不能重复。请检查机械X/Y坐标。");
        }

        var distortionInputs = inputs.Select(input => input.DistortionInput).ToList();
        var distortionCalibration = _lensDistortionCalibrationService.Calibrate(distortionInputs);
        var calibrationPoints = inputs
            .Select(input =>
            {
                var undistorted = UndistortPoint(input.CenterPixel, distortionCalibration);
                return new CalibrationPoint(
                    undistorted.X,
                    undistorted.Y,
                    input.MachineXMm,
                    input.MachineYMm);
            })
            .ToList();

        var hasDuplicatePixelPoint = calibrationPoints
            .GroupBy(point => (Math.Round(point.PixelX, 4), Math.Round(point.PixelY, 4)))
            .Any(group => group.Count() > 1);
        if (hasDuplicatePixelPoint)
        {
            throw new InvalidOperationException("9个去畸变后的像素点不能重复。请逐点拍照采集。");
        }

        var cameraCalibration = CameraCalibrationSolver.Solve(calibrationPoints) with
        {
            SourceDistortionCalibrationId = distortionCalibration.CalibrationId
        };

        return new CombinedCalibrationResult(distortionCalibration, cameraCalibration);
    }

    private static Point2d UndistortPoint(Point2d point, LensDistortionCalibration calibration)
    {
        using var cameraMatrix = Mat.FromArray(new[,]
        {
            { calibration.CameraMatrix[0], calibration.CameraMatrix[1], calibration.CameraMatrix[2] },
            { calibration.CameraMatrix[3], calibration.CameraMatrix[4], calibration.CameraMatrix[5] },
            { calibration.CameraMatrix[6], calibration.CameraMatrix[7], calibration.CameraMatrix[8] }
        });
        using var distortion = Mat.FromArray(calibration.DistortionCoefficients);
        using var source = Mat.FromArray(new[] { new Point2f((float)point.X, (float)point.Y) });
        using var destination = new Mat();

        Cv2.UndistortPoints(source, destination, cameraMatrix, distortion, null, cameraMatrix);
        var undistorted = destination.Get<Point2f>(0);
        return new Point2d(undistorted.X, undistorted.Y);
    }
}

public sealed record CombinedCalibrationInput(
    string PointName,
    double MachineXMm,
    double MachineYMm,
    LensDistortionCalibrationInput DistortionInput,
    Point2d CenterPixel);

public sealed record CombinedCalibrationResult(
    LensDistortionCalibration LensDistortionCalibration,
    CameraCalibration CameraCalibration);
