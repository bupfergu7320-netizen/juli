using JuliMvs.Core.Inspection;
using JuliMvs.Core.Plc;

namespace JuliMvs.Plc;

public sealed class SimulatedPlcClient : IPlcClient
{
    private InspectionResult? _lastResult;

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<PlcSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PlcSnapshot(
            IsRunning: IsConnected,
            WorkpieceInPosition: IsConnected,
            CaptureRequested: false,
            ProductModel: "PART-A",
            TargetProduction: 0,
            CurrentProduction: 0,
            AlarmCode: null));
    }

    public Task WriteInspectionResultAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _lastResult = result;
        return Task.CompletedTask;
    }

    public InspectionResult? GetLastResultForDiagnostics()
    {
        return _lastResult;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
