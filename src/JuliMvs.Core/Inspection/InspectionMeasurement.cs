namespace JuliMvs.Core.Inspection;

public sealed record InspectionMeasurement(
    double CenterXPixel,
    double CenterYPixel,
    double XOffsetMm,
    double YOffsetMm,
    double XCompensationMm,
    double YCompensationMm,
    double AngleDegrees,
    double AngleOffsetDegrees,
    double RotationCompensationDegrees,
    double WidthMm,
    double HeightMm,
    double AreaPixels,
    double MatchScore);
