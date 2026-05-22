namespace JuliMvs.App.Services;

internal sealed class PlcTriggerGate
{
    private int _inspectionInProgress;
    private int _triggerLatched;

    public void Reset()
    {
        Interlocked.Exchange(ref _inspectionInProgress, 0);
        Interlocked.Exchange(ref _triggerLatched, 0);
    }

    public bool IsBusy => Interlocked.CompareExchange(ref _inspectionInProgress, 0, 0) != 0;

    public bool TryBeginManualOperation()
    {
        return Interlocked.CompareExchange(ref _inspectionInProgress, 1, 0) == 0;
    }

    public void EndOperation()
    {
        Interlocked.Exchange(ref _inspectionInProgress, 0);
    }

    public PlcTriggerDecision Evaluate(bool captureRequested)
    {
        if (IsBusy)
        {
            return PlcTriggerDecision.Busy;
        }

        if (!captureRequested)
        {
            return Interlocked.Exchange(ref _triggerLatched, 0) == 1
                ? PlcTriggerDecision.Cleared
                : PlcTriggerDecision.Idle;
        }

        return Interlocked.CompareExchange(ref _triggerLatched, 1, 0) == 0 &&
            Interlocked.CompareExchange(ref _inspectionInProgress, 1, 0) == 0
                ? PlcTriggerDecision.StartInspection
                : PlcTriggerDecision.Busy;
    }

    public void MarkTriggerCleared()
    {
        Interlocked.Exchange(ref _triggerLatched, 0);
    }
}

internal enum PlcTriggerDecision
{
    Idle,
    Busy,
    Cleared,
    StartInspection
}
