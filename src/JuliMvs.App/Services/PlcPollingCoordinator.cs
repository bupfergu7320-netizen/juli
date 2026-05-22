using JuliMvs.Core.Plc;

namespace JuliMvs.App.Services;

internal sealed class PlcPollingCoordinator
{
    private readonly PlcTriggerGate _triggerGate;

    public PlcPollingCoordinator(PlcTriggerGate triggerGate)
    {
        _triggerGate = triggerGate;
    }

    public PlcPollingDecision Evaluate(PlcSnapshot snapshot)
    {
        var triggerDecision = _triggerGate.Evaluate(snapshot.CaptureRequested);
        return new PlcPollingDecision(
            Status: snapshot.CaptureRequested ? PlcPollingStatus.CaptureRequested : PlcPollingStatus.Normal,
            IsStatusNormal: true,
            ShouldDelayAndContinue: triggerDecision == PlcTriggerDecision.Busy,
            LogTriggerCleared: triggerDecision == PlcTriggerDecision.Cleared,
            StartInspection: triggerDecision == PlcTriggerDecision.StartInspection);
    }
}

internal sealed record PlcPollingDecision(
    PlcPollingStatus Status,
    bool IsStatusNormal,
    bool ShouldDelayAndContinue,
    bool LogTriggerCleared,
    bool StartInspection);

internal enum PlcPollingStatus
{
    Normal,
    CaptureRequested
}
