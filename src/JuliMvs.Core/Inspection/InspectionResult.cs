namespace JuliMvs.Core.Inspection;

public sealed record InspectionResult(
    Guid Id,
    string BatchNo,
    string PartNo,
    InspectionDecision Decision,
    NgReason NgReason,
    string Message,
    InspectionMeasurement? Measurement,
    string? RawImagePath,
    string? ResultImagePath,
    DateTimeOffset CreatedAt)
{
    public static InspectionResult FromMeasurement(
        string batchNo,
        string partNo,
        InspectionDecision decision,
        NgReason ngReason,
        string message,
        InspectionMeasurement measurement,
        string? rawImagePath = null,
        string? resultImagePath = null)
    {
        return new InspectionResult(
            Guid.NewGuid(),
            batchNo,
            partNo,
            decision,
            ngReason,
            message,
            measurement,
            rawImagePath,
            resultImagePath,
            DateTimeOffset.Now);
    }

    public static InspectionResult Error(string batchNo, string partNo, NgReason reason, string message)
    {
        return new InspectionResult(
            Guid.NewGuid(),
            batchNo,
            partNo,
            InspectionDecision.Error,
            reason,
            message,
            null,
            null,
            null,
            DateTimeOffset.Now);
    }
}
