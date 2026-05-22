using System.Globalization;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;

namespace JuliMvs.App.Services;

internal static class TurntableStatusMessageFormatter
{
    public static string FormatDirectionText(
        VisionParameters visionParameters,
        PlcOutputTransform outputTransform)
    {
        var rCommandDirectionText = visionParameters.InvertRotationCompensation
            ? "R\u65b9\u5411=\u53d6\u53cd\u5e76\u540c\u6b65\u91cd\u7b97XY"
            : "R\u65b9\u5411=\u4e0d\u53d6\u53cd";
        var backSideNgText = visionParameters.BackSideNgEnabled
            ? "\u53cd\u9762NG=\u5df2\u542f\u7528"
            : "\u53cd\u9762NG=\u672a\u542f\u7528";
        return
            "PLC\u7edd\u5bf9\u5b9a\u4f4d\u5408\u540c: TargetX=BaseX+D1002, TargetY=BaseY+D1004, TargetR=BaseR+D1006" +
            Environment.NewLine +
            "PLC\u8f93\u51faR\u8f74\u4e2d\u5fc3\u540e\u6700\u7ec8\u7ea0\u504f\u91cf: D1002=HomeXAction, D1004=HomeYAction, D1006=HomeRAction; D1002/D1004\u6309PLC\u6700\u7ec8R\u547d\u4ee4\u540c\u6b65\u91cd\u7b97XY\u8865\u507f" +
            Environment.NewLine +
            rCommandDirectionText +
            Environment.NewLine +
            backSideNgText +
            Environment.NewLine +
            $"PLC\u6700\u7ec8\u7ea0\u504f\u8f93\u51fa: {InspectionDiagnosticMessageFormatter.FormatPlcOutputTransform(outputTransform)}";
    }

    public static string FormatCalibrationText(
        CameraCalibration cameraCalibration,
        RAxisCenterCalibration rAxisCenterCalibration)
    {
        var xyText = cameraCalibration.Enabled
            ? string.Format(
                CultureInfo.InvariantCulture,
                "\u5df2\u542f\u7528\uff0c9\u70b9XY\u8bef\u5dee {0:F4} mm",
                cameraCalibration.RmsErrorMm)
            : "\u672a\u542f\u7528\u6216\u5df2\u5931\u6548\u3002\u8bf7\u5148\u5b8c\u62109\u70b9XY\u6807\u5b9a\u3002";
        var rAxisText = rAxisCenterCalibration.Enabled
            ? string.Format(
                CultureInfo.InvariantCulture,
                "R\u8f74\u4e2d\u5fc3 X={0:F3} Y={1:F3} RMS={2:F4} mm",
                rAxisCenterCalibration.CenterXMm,
                rAxisCenterCalibration.CenterYMm,
                rAxisCenterCalibration.RmsErrorMm) +
                (rAxisCenterCalibration.GetMachineAngleDirection() < 0
                    ? "\uff0cR+\u65b9\u5411=\u987a\u65f6\u9488"
                    : "\uff0cR+\u65b9\u5411=\u9006\u65f6\u9488")
            : "R\u8f74\u4e2d\u5fc3\u672a\u542f\u7528\u6216\u5df2\u5931\u6548\u3002";
        return $"{xyText}{Environment.NewLine}{rAxisText}";
    }

    public static string FormatFlowHint(bool isMachineCalibrationReady)
    {
        return isMachineCalibrationReady
            ? "\u63a8\u8350\u6d41\u7a0b: \u6807\u5b9a\u7ba1\u7406\u5b8c\u62109\u70b9XY\u548cR\u8f74\u4e2d\u5fc3 -> \u6362\u578b\u5efa\u7acb\u6807\u51c6\u4f4d/\u6a21\u677f -> \u8f6c\u76d8\u5b9a\u4f4d\u9a8c\u8bc1PLC\u65cb\u8f6c\u524d\u7ea0\u504f\u8f93\u51fa\u3002"
            : "\u5f53\u524d\u672a\u5b8c\u6210\u6709\u6548\u673a\u5668\u6807\u5b9a\u3002\u8bf7\u5148\u5230\u76f8\u673a\u8bbe\u7f6e -> \u6807\u5b9a\u7ba1\u7406\u5b8c\u62109\u70b9XY\u548cR\u8f74\u4e2d\u5fc3\u6807\u5b9a\u3002";
    }

    public static string GetCurrentProductName(string batchProductName, string currentProductName)
    {
        var productName = !string.IsNullOrWhiteSpace(batchProductName)
            ? batchProductName
            : currentProductName;
        return string.IsNullOrWhiteSpace(productName) ? "-" : productName;
    }

    public static string FormatInspectionDecision(InspectionDecision decision)
    {
        return decision switch
        {
            InspectionDecision.Ok => "OK",
            InspectionDecision.Ng => "NG",
            InspectionDecision.Error => "\u5f02\u5e38",
            _ => "\u672a\u77e5"
        };
    }
}
