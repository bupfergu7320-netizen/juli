using JuliMvs.Core.Inspection;

namespace JuliMvs.App.Services;

internal static class InspectionImageSavePolicy
{
    public static InspectionImageSaveDecision Decide(bool writeToPlc, InspectionDecision decision)
    {
        if (!writeToPlc)
        {
            return new InspectionImageSaveDecision(
                KeepIncomingRawImagePath: true,
                SaveDiagnosticImage: true,
                ProductionLogMessage: null);
        }

        if (decision == InspectionDecision.Ok)
        {
            return new InspectionImageSaveDecision(
                KeepIncomingRawImagePath: false,
                SaveDiagnosticImage: false,
                ProductionLogMessage: "\u751f\u4ea7OK\u4e0d\u4fdd\u5b58\u56fe\u7247\uff0c\u53ea\u4fdd\u5b58\u68c0\u6d4b\u8bb0\u5f55\u3002");
        }

        return new InspectionImageSaveDecision(
            KeepIncomingRawImagePath: false,
            SaveDiagnosticImage: true,
            ProductionLogMessage: null);
    }
}

internal sealed record InspectionImageSaveDecision(
    bool KeepIncomingRawImagePath,
    bool SaveDiagnosticImage,
    string? ProductionLogMessage);
