namespace JuliMvs.Core.Vision;

public sealed record CalibrationPoint(
    double PixelX,
    double PixelY,
    double MachineXMm,
    double MachineYMm);
