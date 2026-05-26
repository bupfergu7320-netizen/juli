using System.Globalization;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;

namespace JuliMvs.App.Services;

internal sealed class CalibrationQualityEvaluator
{
    private readonly CalibrationQualityThresholds _thresholds;

    public CalibrationQualityEvaluator(CalibrationQualityThresholds thresholds)
    {
        _thresholds = thresholds;
    }

    public CalibrationQualityResult EvaluateLensDistortion(LensDistortionCalibration calibration)
    {
        var issues = new List<string>();
        if (!calibration.Enabled)
        {
            issues.Add("\u7578\u53d8\u6807\u5b9a\u672a\u542f\u7528\u3002");
        }

        if (calibration.RmsReprojectionErrorPixels > _thresholds.MaximumLensDistortionRmsPixels)
        {
            issues.Add(Format(
                "\u7578\u53d8RMS={0:F4}px\uff0c\u8d85\u8fc7\u4e0a\u9650{1:F4}px\u3002",
                calibration.RmsReprojectionErrorPixels,
                _thresholds.MaximumLensDistortionRmsPixels));
        }

        return BuildResult(
            "\u7578\u53d8\u8d28\u91cf",
            issues,
            Format(
                "\u7578\u53d8RMS={0:F4}px\uff0c\u4e0a\u9650={1:F4}px\u3002",
                calibration.RmsReprojectionErrorPixels,
                _thresholds.MaximumLensDistortionRmsPixels));
    }

    public CalibrationQualityResult EnsureLensDistortion(LensDistortionCalibration calibration)
    {
        var quality = EvaluateLensDistortion(calibration);
        if (!quality.IsAccepted)
        {
            throw new InvalidOperationException(quality.Summary);
        }

        return quality;
    }

    public CalibrationQualityResult EvaluateCamera(CameraCalibration calibration)
    {
        var issues = new List<string>();
        if (!calibration.Enabled)
        {
            issues.Add("9\u70b9XY\u6807\u5b9a\u672a\u542f\u7528\u3002");
        }

        if (calibration.Points.Count != _thresholds.RequiredCameraCalibrationPointCount)
        {
            issues.Add(Format(
                "9\u70b9XY\u70b9\u6570={0}\uff0c\u8981\u6c42={1}\u3002",
                calibration.Points.Count,
                _thresholds.RequiredCameraCalibrationPointCount));
        }

        if (calibration.RmsErrorMm > _thresholds.MaximumCameraCalibrationRmsMm)
        {
            issues.Add(Format(
                "9\u70b9XY RMS={0:F4}mm\uff0c\u8d85\u8fc7\u4e0a\u9650{1:F4}mm\u3002",
                calibration.RmsErrorMm,
                _thresholds.MaximumCameraCalibrationRmsMm));
        }

        return BuildResult(
            "9\u70b9XY\u8d28\u91cf",
            issues,
            Format(
                "9\u70b9XY RMS={0:F4}mm\uff0c\u4e0a\u9650={1:F4}mm\u3002",
                calibration.RmsErrorMm,
                _thresholds.MaximumCameraCalibrationRmsMm));
    }

    public CalibrationQualityResult EnsureCamera(CameraCalibration calibration)
    {
        var quality = EvaluateCamera(calibration);
        if (!quality.IsAccepted)
        {
            throw new InvalidOperationException(quality.Summary);
        }

        return quality;
    }

    public CalibrationQualityResult EvaluateCombined(CombinedCalibrationResult calibration)
    {
        var distortionQuality = EvaluateLensDistortion(calibration.LensDistortionCalibration);
        var cameraQuality = EvaluateCamera(calibration.CameraCalibration);
        var issues = distortionQuality.Issues.Concat(cameraQuality.Issues).ToList();
        return BuildResult(
            "\u8054\u5408\u6807\u5b9a\u8d28\u91cf",
            issues,
            Format(
                "\u7578\u53d8RMS={0:F4}px\uff0c\u4e0a\u9650={1:F4}px\uff1b9\u70b9XY RMS={2:F4}mm\uff0c\u4e0a\u9650={3:F4}mm\u3002",
                calibration.LensDistortionCalibration.RmsReprojectionErrorPixels,
                _thresholds.MaximumLensDistortionRmsPixels,
                calibration.CameraCalibration.RmsErrorMm,
                _thresholds.MaximumCameraCalibrationRmsMm));
    }

    public CalibrationQualityResult EnsureCombined(CombinedCalibrationResult calibration)
    {
        var quality = EvaluateCombined(calibration);
        if (!quality.IsAccepted)
        {
            throw new InvalidOperationException(quality.Summary);
        }

        return quality;
    }

    public CalibrationQualityResult EvaluateRAxisCenter(RAxisCenterCalibration calibration)
    {
        var issues = new List<string>();
        var angleCoverage = CalculateRAxisAngleCoverageDegrees(calibration.Points);
        if (!calibration.Enabled)
        {
            issues.Add("R\u8f74\u4e2d\u5fc3\u6807\u5b9a\u672a\u542f\u7528\u3002");
        }

        if (calibration.Points.Count < _thresholds.MinimumRAxisCenterPointCount)
        {
            issues.Add(Format(
                "R\u8f74\u4e2d\u5fc3\u70b9\u6570={0}\uff0c\u8981\u6c42>={1}\u3002",
                calibration.Points.Count,
                _thresholds.MinimumRAxisCenterPointCount));
        }

        if (angleCoverage < _thresholds.MinimumRAxisCenterAngleCoverageDegrees)
        {
            issues.Add(Format(
                "R\u8f74\u89d2\u5ea6\u8986\u76d6={0:F3}deg\uff0c\u8981\u6c42>={1:F3}deg\u3002",
                angleCoverage,
                _thresholds.MinimumRAxisCenterAngleCoverageDegrees));
        }

        if (calibration.RmsErrorMm > _thresholds.MaximumRAxisCenterRmsMm)
        {
            issues.Add(Format(
                "R\u8f74\u4e2d\u5fc3RMS={0:F4}mm\uff0c\u8d85\u8fc7\u4e0a\u9650{1:F4}mm\u3002",
                calibration.RmsErrorMm,
                _thresholds.MaximumRAxisCenterRmsMm));
        }

        if (calibration.MaxErrorMm > _thresholds.MaximumRAxisCenterMaxMm)
        {
            issues.Add(Format(
                "R\u8f74\u4e2d\u5fc3Max={0:F4}mm\uff0c\u8d85\u8fc7\u4e0a\u9650{1:F4}mm\u3002",
                calibration.MaxErrorMm,
                _thresholds.MaximumRAxisCenterMaxMm));
        }

        var residuals = RAxisCenterCalibrationSolver.CalculateResiduals(calibration);
        if (residuals.Count > 0)
        {
            var worst = residuals.OrderByDescending(residual => residual.DistanceMm).First();
            if (worst.DistanceMm > _thresholds.MaximumRAxisCenterMaxMm)
            {
                issues.Add(Format(
                    "\u6700\u5927\u8bef\u5dee\u5728R{0:0.###}\u00b0\uff0c\u8bef\u5dee={1:F4}mm\uff0c\u5efa\u8bae\u4f18\u5148\u91cd\u62cd\u8be5\u89d2\u5ea6\u3002",
                    worst.AngleDegrees,
                    worst.DistanceMm));
            }
        }

        return BuildResult(
            "R\u8f74\u4e2d\u5fc3\u8d28\u91cf",
            issues,
            Format(
                "\u70b9\u6570={0}\uff0c\u89d2\u5ea6\u8986\u76d6={1:F3}deg\uff0c\u4e0a\u9650/\u8981\u6c42: RMS<={2:F4}mm, Max<={3:F4}mm, \u8986\u76d6>={4:F3}deg\u3002",
                calibration.Points.Count,
                angleCoverage,
                _thresholds.MaximumRAxisCenterRmsMm,
                _thresholds.MaximumRAxisCenterMaxMm,
                _thresholds.MinimumRAxisCenterAngleCoverageDegrees));
    }

    public CalibrationQualityResult EnsureRAxisCenter(RAxisCenterCalibration calibration)
    {
        var quality = EvaluateRAxisCenter(calibration);
        if (!quality.IsAccepted)
        {
            throw new InvalidOperationException(quality.Summary);
        }

        return quality;
    }

    private static CalibrationQualityResult BuildResult(
        string name,
        IReadOnlyList<string> issues,
        string details)
    {
        var accepted = issues.Count == 0;
        var summary = accepted
            ? Format("{0}: \u5408\u683c\u3002{1}", name, details)
            : Format("{0}: \u4e0d\u5408\u683c\u3002{1} \u95ee\u9898: {2}", name, details, string.Join(" ", issues));
        return new CalibrationQualityResult(accepted, summary, issues.ToArray());
    }

    private static double CalculateRAxisAngleCoverageDegrees(
        IReadOnlyList<RAxisCenterCalibrationPoint> points)
    {
        if (points.Count < 2)
        {
            return 0.0;
        }

        var normalized = points
            .Select(point => NormalizeAngle360(point.AngleDegrees))
            .Distinct()
            .OrderBy(angle => angle)
            .ToArray();
        if (normalized.Length < 2)
        {
            return 0.0;
        }

        var maxGap = 0.0;
        for (var index = 0; index < normalized.Length; index++)
        {
            var current = normalized[index];
            var next = normalized[(index + 1) % normalized.Length];
            var gap = index == normalized.Length - 1
                ? next + 360.0 - current
                : next - current;
            maxGap = Math.Max(maxGap, gap);
        }

        return 360.0 - maxGap;
    }

    private static double NormalizeAngle360(double angleDegrees)
    {
        var normalized = angleDegrees % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    private static string Format(string format, params object[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, format, args);
    }
}

internal sealed record CalibrationQualityThresholds(
    double MaximumLensDistortionRmsPixels,
    double MaximumCameraCalibrationRmsMm,
    int RequiredCameraCalibrationPointCount,
    int MinimumRAxisCenterPointCount,
    double MinimumRAxisCenterAngleCoverageDegrees,
    double MaximumRAxisCenterRmsMm,
    double MaximumRAxisCenterMaxMm);

internal sealed record CalibrationQualityResult(
    bool IsAccepted,
    string Summary,
    IReadOnlyList<string> Issues);
