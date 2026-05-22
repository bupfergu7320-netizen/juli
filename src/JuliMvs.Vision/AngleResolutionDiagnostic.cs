using JuliMvs.Core.Vision;

namespace JuliMvs.Vision;

public sealed record AngleResolutionDiagnostic(
    AngleDetectionMode Mode,
    double ContourAngleDegrees,
    double ResolvedAngleDegrees,
    bool AllowsFullRotation,
    bool IsReliable,
    string Source,
    double Score,
    double AlternativeScore,
    double ScoreMargin,
    string Message,
    IReadOnlyList<AngleCandidateDiagnostic>? Candidates = null);
