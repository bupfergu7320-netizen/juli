using System.Globalization;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;

namespace JuliMvs.App.Services;

internal static class CalibrationResultMessageFormatter
{
    public static string FormatLensDistortionQuality(double rmsPixels)
    {
        return rmsPixels <= 0.30
            ? "\u4f18"
            : rmsPixels <= 0.60
                ? "\u53ef\u7528"
                : rmsPixels <= 1.00
                    ? "\u52c9\u5f3a\u53ef\u7528\uff0c\u5efa\u8bae\u68c0\u67e5\u56fe\u7247\u540e\u91cd\u62cd"
                    : "\u4e0d\u5efa\u8bae\u4fdd\u5b58";
    }

    public static string FormatLensDistortionInitialGuidance(string statusText, int minimumImageCount)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}{1}{1}\u8bf7\u91c7\u96c6\u81f3\u5c11{2}\u5f20\u6807\u5b9a\u677f\u56fe\u7247\uff1b\u73b0\u573a\u7cbe\u5bc6\u5c11\u5f20\u63a8\u8350\u62cd10\u5f20\u3002{1}\u8f6f\u4ef6\u53c2\u6570\u53ea\u586b: \u884c\u65707\u3001\u5217\u65707\u3001\u70b9\u8ddd10mm\u3002\u4e0b\u8868X/Y/R\u53ea\u662f\u73b0\u573a\u79fb\u52a8\u5e73\u53f0\u62cd\u56fe\u7528\uff0c\u4e0d\u586b\u8fdb\u7578\u53d8\u53c2\u6570\u3002{1}\u6807\u5b9a\u677f\u5fc5\u987b\u5728\u5de5\u4ef6\u68c0\u6d4b\u5e73\u9762\uff0c\u5706\u70b9\u5b8c\u6574\u6e05\u6670\u8fdb\u5165\u753b\u9762\u3002{1}{1}{3}",
            statusText,
            Environment.NewLine,
            minimumImageCount,
            FormatLensDistortionCapturePlan());
    }

    public static string FormatLensDistortionCapturePlan()
    {
        return string.Join(
            Environment.NewLine,
            "\u7cbe\u5bc6\u5c11\u5f20\u63a8\u8350\u91c7\u56fe\u8868:",
            "1.  X=-30, Y=0,   R=0    \u5de6",
            "2.  X=+30, Y=0,   R=0    \u53f3",
            "3.  X=0,   Y=+30, R=0    \u4e0a",
            "4.  X=0,   Y=-30, R=0    \u4e0b",
            "5.  X=-30, Y=+30, R=0    \u5de6\u4e0a",
            "6.  X=+30, Y=+30, R=0    \u53f3\u4e0a",
            "7.  X=-30, Y=-30, R=0    \u5de6\u4e0b",
            "8.  X=+30, Y=-30, R=0    \u53f3\u4e0b",
            "9.  X=0,   Y=0,   R=+60  \u4e2d\u5fc3\u5927\u89d2\u5ea6",
            "10. X=0,   Y=0,   R=-60  \u4e2d\u5fc3\u5927\u89d2\u5ea6",
            string.Empty,
            "\u5982\u679c\u89d2\u843d\u5706\u70b9\u51fa\u89c6\u91ce\uff0c\u628a\u89d2\u843d\u00b130\u6539\u6210\u00b120\uff1b\u5982\u679cR\u00b160\u51fa\u89c6\u91ce\uff0c\u5148\u6539\u6210\u00b145\uff1b\u5982\u679c\u73b0\u573a\u5e38\u752880\u5ea6\u4ee5\u4e0a\u4e14\u5706\u70b9\u5b8c\u6574\uff0c\u53ef\u628a\u00b160\u6539\u6210\u00b180\u3002");
    }

    public static string FormatCameraCalibrationResult(CameraCalibration calibration)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "\u6807\u5b9a\u7ed3\u679c: RMS={0:F4}mm{1}\u5efa\u8bae: RMS <= 0.1000mm \u4e3a\u5408\u683c\u53c2\u8003\uff0c\u73b0\u573a\u53ef\u6309\u673a\u68b0\u7cbe\u5ea6\u8c03\u6574\u3002",
            calibration.RmsErrorMm,
            Environment.NewLine);
    }

    public static string FormatCombinedCalibrationResult(CombinedCalibrationResult result)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "\u8054\u5408\u6807\u5b9a\u7ed3\u679c{0}\u7578\u53d8: RMS={1:F4}px, \u56fe\u7247={2}{0}9\u70b9XY: RMS={3:F4}mm, \u70b9\u6570={4}{0}\u5efa\u8bae: \u7578\u53d8RMS <= 0.6000px\uff0c9\u70b9XY RMS <= 0.1000mm\u3002",
            Environment.NewLine,
            result.LensDistortionCalibration.RmsReprojectionErrorPixels,
            result.LensDistortionCalibration.CapturedImageCount,
            result.CameraCalibration.RmsErrorMm,
            result.CameraCalibration.Points.Count);
    }

    public static string FormatRAxisCenterStatus(RAxisCenterCalibration calibration)
    {
        return calibration.Enabled
            ? string.Format(
                CultureInfo.InvariantCulture,
                "R\u8f74\u4e2d\u5fc3\u5df2\u542f\u7528: X={0:F4}mm Y={1:F4}mm RMS={2:F4}mm",
                calibration.CenterXMm,
                calibration.CenterYMm,
                calibration.RmsErrorMm)
            : "R\u8f74\u4e2d\u5fc3\u672a\u542f\u7528\u6216\u5df2\u5931\u6548\u3002";
    }

    public static string FormatRAxisCenterResult(RAxisCenterCalibration calibration)
    {
        var residuals = RAxisCenterCalibrationSolver.CalculateResiduals(calibration);
        var worst = residuals
            .OrderByDescending(residual => residual.DistanceMm)
            .FirstOrDefault();
        var worstText = worst is null
            ? "\u6700\u5927\u8bef\u5dee: \u6682\u65e0\u660e\u7ec6"
            : string.Format(
                CultureInfo.InvariantCulture,
                "\u6700\u5927\u8bef\u5dee: R{0:0.###}\u00b0 = {1:F4}mm",
                worst.AngleDegrees,
                worst.DistanceMm);
        var residualText = residuals.Count == 0
            ? "\u5404\u89d2\u5ea6\u8bef\u5dee: \u6682\u65e0\u660e\u7ec6"
            : "\u5404\u89d2\u5ea6\u8bef\u5dee: " + string.Join(
                "\uff1b",
                residuals.Select(residual => string.Format(
                    CultureInfo.InvariantCulture,
                    "R{0:0.###}\u00b0 {1:F4}mm",
                    residual.AngleDegrees,
                    residual.DistanceMm)));

        return string.Join(
            Environment.NewLine,
            string.Format(
                CultureInfo.InvariantCulture,
                "\u6807\u5b9a\u7ed3\u679c: \u4e2d\u5fc3X={0:F4}mm, \u4e2d\u5fc3Y={1:F4}mm",
                calibration.CenterXMm,
                calibration.CenterYMm),
            string.Format(
                CultureInfo.InvariantCulture,
                "\u534a\u5f84={0:F4}mm, RMS={1:F4}mm, Max={2:F4}mm, \u70b9\u6570={3}",
                calibration.RadiusMm,
                calibration.RmsErrorMm,
                calibration.MaxErrorMm,
                calibration.Points.Count),
            worstText,
            residualText,
            "\u5efa\u8bae: RMS <= 0.0500mm \u4e3a\u7cbe\u5bc6\u53c2\u8003\uff1b\u67d0\u4e2aR\u89d2\u5ea6\u8bef\u5dee\u660e\u663e\u504f\u5927\u65f6\uff0c\u5148\u91cd\u62cd\u8be5\u89d2\u5ea6\uff0c\u518d\u68c0\u67e5R\u8f74\u5230\u4f4d\u7b49\u5f85\u3001\u6807\u5b9a\u677f\u56fa\u5b9a\u548c\u4e2d\u5fc3\u8bc6\u522b\u3002");
    }

    public static string FormatCalibrationPointSuggestion(double suggestedMachineXMm, double suggestedMachineYMm)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "\u521d\u59cb\u53c2\u8003: X={0:0.###}mm, Y={1:0.###}mm\uff1b\u653e\u4e0d\u5230\u5c31\u6539\u5230\u53ef\u8fbe\u8303\u56f4\u5185\u5206\u6563\u7684\u4f4d\u7f6e\uff0c\u5b9e\u9645\u4ee5\u8bbe\u5907HMI\u5f53\u524d\u5750\u6807\u4e3a\u51c6\u3002",
            suggestedMachineXMm,
            suggestedMachineYMm);
    }

    public static string FormatRAxisCenterPointSuggestion(double angleDegrees)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "\u5c06\u5e73\u53f0R\u8f74\u8f6c\u5230 {0:0.###}deg\u9644\u8fd1\uff1bX/Y\u4fdd\u6301\u4e0d\u52a8\uff0c\u628aR\u89d2\u5ea6\u6539\u6210HMI/PLC\u5b9e\u9645\u5230\u4f4d\u503c\u540e\u518d\u62cd\u7167\u91c7\u96c6\u6807\u5b9a\u677f\u4e2d\u5fc3\u5706\u70b9\u3002",
            angleDegrees);
    }
}
