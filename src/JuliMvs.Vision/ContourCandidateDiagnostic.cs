namespace JuliMvs.Vision;

public sealed record ContourCandidateDiagnostic(
    int Rank,
    int CandidateIndex,
    string Source,
    bool IsSelected,
    double Score,
    double CenterXPixel,
    double CenterYPixel,
    double WidthPixels,
    double HeightPixels,
    double WidthMm,
    double HeightMm,
    double AreaPixels,
    double FillRatio,
    double CenterDistancePixels);
