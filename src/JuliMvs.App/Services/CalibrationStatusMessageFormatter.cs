using System.Globalization;
using JuliMvs.Core.Vision;

namespace JuliMvs.App.Services;

internal static class CalibrationStatusMessageFormatter
{
    public static string FormatMachineStatus(
        LensDistortionCalibration distortionCalibration,
        CameraCalibration cameraCalibration,
        RAxisCenterCalibration rAxisCenterCalibration)
    {
        return string.Join(
            Environment.NewLine,
            FormatLensDistortionStatus(distortionCalibration),
            FormatCameraCalibrationStatus(cameraCalibration),
            FormatRAxisCenterStatus(rAxisCenterCalibration));
    }

    public static string FormatLensDistortionStatus(LensDistortionCalibration calibration)
    {
        return calibration.Enabled
            ? string.Format(
                CultureInfo.InvariantCulture,
                "\u7578\u53d8: \u5df2\u542f\u7528 {0}x{1}, RMS={2:F3}px",
                calibration.ImageWidth,
                calibration.ImageHeight,
                calibration.RmsReprojectionErrorPixels)
            : "\u7578\u53d8: \u672a\u542f\u7528";
    }

    private static string FormatCameraCalibrationStatus(CameraCalibration calibration)
    {
        return calibration.Enabled
            ? string.Format(
                CultureInfo.InvariantCulture,
                "9\u70b9XY: \u5df2\u542f\u7528 RMS={0:F4}mm",
                calibration.RmsErrorMm)
            : "9\u70b9XY: \u672a\u542f\u7528\u6216\u5df2\u5931\u6548";
    }

    private static string FormatRAxisCenterStatus(RAxisCenterCalibration calibration)
    {
        return calibration.Enabled
            ? string.Format(
                CultureInfo.InvariantCulture,
                "R\u8f74\u4e2d\u5fc3: \u5df2\u542f\u7528 X={0:F3}mm Y={1:F3}mm RMS={2:F4}mm",
                calibration.CenterXMm,
                calibration.CenterYMm,
                calibration.RmsErrorMm)
            : "R\u8f74\u4e2d\u5fc3: \u672a\u542f\u7528\u6216\u5df2\u5931\u6548";
    }
}
