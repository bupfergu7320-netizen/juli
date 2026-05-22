namespace JuliMvs.Vision;

public sealed record AngleCandidateDiagnostic(
    int Rank,
    double AngleOffsetDegrees,
    double ResolvedAngleDegrees,
    double Score,
    string Stage);
