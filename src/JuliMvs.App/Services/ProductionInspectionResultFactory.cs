using JuliMvs.Core.Inspection;

namespace JuliMvs.App.Services;

internal static class ProductionInspectionResultFactory
{
    public const string OkMessage = "OK，正反面检测通过，输出XYR纠偏。";

    public static InspectionResult CreateOk(
        string? batchNo,
        InspectionMeasurement measurement,
        string? message = null,
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
            string.IsNullOrWhiteSpace(message) ? OkMessage : message.Trim(),
            measurement,
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

    public static InspectionResult CreateUnsafeXyrNg(
        string? batchNo,
        string message,
        string? rawImagePath = null,
        string? partNo = null,
        NgReason ngReason = NgReason.MatchFailed)
    {
        return InspectionResult.FromMeasurement(
            string.IsNullOrWhiteSpace(batchNo) ? "PLC-ONLY" : batchNo.Trim(),
            string.IsNullOrWhiteSpace(partNo)
                ? DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff")
                : partNo.Trim(),
            InspectionDecision.Ng,
            ngReason,
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
