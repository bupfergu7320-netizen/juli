using JuliMvs.Core.Inspection;

namespace JuliMvs.App.Services;

internal static class VisionJudgmentBypassResultFactory
{
    public const string Message = "OK，视觉判断已禁用，仅验证PLC通信和拍照流程。";

    public static InspectionResult CreateOk(
        string? batchNo,
        string? rawImagePath = null,
        string? partNo = null)
    {
        return InspectionResult.FromMeasurement(
            string.IsNullOrWhiteSpace(batchNo) ? "PLC-ONLY" : batchNo.Trim(),
            string.IsNullOrWhiteSpace(partNo)
                ? DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff")
                : partNo.Trim(),
            InspectionDecision.Ok,
            NgReason.None,
            Message,
            CreateZeroMeasurement(),
            rawImagePath);
    }

    public static InspectionResult CreateBackSideNg(
        string? batchNo,
        string message,
        string? rawImagePath = null,
        string? partNo = null)
    {
        return InspectionResult.FromMeasurement(
            string.IsNullOrWhiteSpace(batchNo) ? "PLC-ONLY" : batchNo.Trim(),
            string.IsNullOrWhiteSpace(partNo)
                ? DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff")
                : partNo.Trim(),
            InspectionDecision.Ng,
            NgReason.BackSideDetected,
            message,
            CreateZeroMeasurement(),
            rawImagePath);
    }

    private static InspectionMeasurement CreateZeroMeasurement()
    {
        return new InspectionMeasurement(
            CenterXPixel: 0,
            CenterYPixel: 0,
            XOffsetMm: 0,
            YOffsetMm: 0,
            XCompensationMm: 0,
            YCompensationMm: 0,
            AngleDegrees: 0,
            AngleOffsetDegrees: 0,
            RotationCompensationDegrees: 0,
            WidthMm: 0,
            HeightMm: 0,
            AreaPixels: 0,
            MatchScore: 0);
    }
}
