namespace JuliMvs.Core.Plc;

public sealed record PlcSnapshot(
    bool IsRunning,
    bool WorkpieceInPosition,
    bool CaptureRequested,
    string ProductModel,
    int TargetProduction,
    int CurrentProduction,
    string? AlarmCode)
;
