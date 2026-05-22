using System.Globalization;
using JuliMvs.Core.Vision;

namespace JuliMvs.App.Services;

internal sealed class CalibrationEditorSolver
{
    public CameraCalibration CalculateCamera(
        IReadOnlyList<CameraCalibrationEditorPoint> points,
        int requiredPointCount)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count != requiredPointCount)
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "9\u70b9XY\u6807\u5b9a\u5fc5\u987b\u8f93\u5165{0}\u4e2a\u70b9\u3002",
                    requiredPointCount));
        }

        var calibrationPoints = points
            .Select(point => new CalibrationPoint(point.PixelX, point.PixelY, point.MachineXMm, point.MachineYMm))
            .ToList();

        var missingCapture = points
            .Where(point => !point.IsCaptured)
            .Select(point => point.Name)
            .ToList();
        if (missingCapture.Count > 0)
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "\u4ee5\u4e0b\u70b9\u4f4d\u8fd8\u6ca1\u6709\u62cd\u7167\u91c7\u96c6: {0}\u3002",
                    string.Join("\u3001", missingCapture)));
        }

        var hasDuplicatePixelPoint = calibrationPoints
            .GroupBy(point => (Math.Round(point.PixelX, 4), Math.Round(point.PixelY, 4)))
            .Any(group => group.Count() > 1);
        if (hasDuplicatePixelPoint)
        {
            throw new InvalidOperationException("9\u4e2a\u50cf\u7d20\u70b9\u4e0d\u80fd\u91cd\u590d\u3002\u8bf7\u9010\u70b9\u62cd\u7167\u91c7\u96c6\u50cf\u7d20\u5750\u6807\u3002");
        }

        var hasDuplicateMachinePoint = calibrationPoints
            .GroupBy(point => (Math.Round(point.MachineXMm, 4), Math.Round(point.MachineYMm, 4)))
            .Any(group => group.Count() > 1);
        if (hasDuplicateMachinePoint)
        {
            throw new InvalidOperationException("9\u4e2a\u673a\u68b0\u70b9\u4e0d\u80fd\u91cd\u590d\u3002\u8bf7\u68c0\u67e5\u673a\u68b0X/Y\u5750\u6807\u3002");
        }

        return CameraCalibrationSolver.Solve(calibrationPoints);
    }

    public RAxisCenterCalibration CalculateRAxisCenter(
        IReadOnlyList<RAxisCenterCalibrationEditorPoint> points,
        CameraCalibration cameraCalibration,
        string captureTarget)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(cameraCalibration);
        if (!cameraCalibration.Enabled)
        {
            throw new InvalidOperationException("R\u8f74\u4e2d\u5fc3\u6807\u5b9a\u524d\u5fc5\u987b\u5148\u5b8c\u6210\u6709\u65489\u70b9XY\u6807\u5b9a\u3002");
        }

        var missingCapture = points
            .Where(point => !point.IsCaptured)
            .Select(point => point.Name)
            .ToList();
        if (missingCapture.Count > 0)
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "\u4ee5\u4e0bR\u89d2\u5ea6\u8fd8\u6ca1\u6709\u62cd\u7167\u91c7\u96c6: {0}\u3002",
                    string.Join("\u3001", missingCapture)));
        }

        var distinctAngles = points
            .Select(point => Math.Round(point.AngleDegrees, 4))
            .Distinct()
            .Count();
        if (distinctAngles < 3)
        {
            throw new InvalidOperationException("R\u8f74\u4e2d\u5fc3\u6807\u5b9a\u81f3\u5c11\u9700\u89813\u4e2a\u4e0d\u540cR\u89d2\u5ea6\u3002");
        }

        var hasDuplicateMachinePoint = points
            .GroupBy(point => (Math.Round(point.MachineXMm, 4), Math.Round(point.MachineYMm, 4)))
            .Any(group => group.Count() > 1);
        if (hasDuplicateMachinePoint)
        {
            throw new InvalidOperationException("R\u8f74\u4e2d\u5fc3\u6807\u5b9a\u70b9\u4e0d\u80fd\u91cd\u590d\u3002\u8bf7\u786e\u8ba4\u6bcf\u4e2a\u89d2\u5ea6\u5df2\u91cd\u65b0\u62cd\u7167\u91c7\u96c6\u3002");
        }

        var calibrationPoints = points
            .Select(point => new RAxisCenterCalibrationPoint(
                point.AngleDegrees,
                point.PixelX,
                point.PixelY,
                point.MachineXMm,
                point.MachineYMm))
            .ToList();

        return RAxisCenterCalibrationSolver.Solve(calibrationPoints) with
        {
            SourceCameraCalibrationId = cameraCalibration.CalibrationId,
            CaptureTarget = captureTarget
        };
    }
}

internal sealed record CameraCalibrationEditorPoint(
    string Name,
    double PixelX,
    double PixelY,
    double MachineXMm,
    double MachineYMm,
    bool IsCaptured);

internal sealed record RAxisCenterCalibrationEditorPoint(
    string Name,
    double AngleDegrees,
    double PixelX,
    double PixelY,
    double MachineXMm,
    double MachineYMm,
    bool IsCaptured);
