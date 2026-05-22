namespace JuliMvs.Core.Vision;

public sealed record PartPose2D(
    double XMm,
    double YMm,
    double AngleDegrees,
    double Score = 1.0)
{
    public MachinePoint Center => new(XMm, YMm);
}
