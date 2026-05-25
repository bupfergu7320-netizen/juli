namespace JuliMvs.Vision;

public enum FrontBackDebugDecision
{
    Unavailable,
    Front,
    Back,
    Uncertain
}

public sealed record FrontBackDebugResult(
    double FrontScore,
    double BackScore,
    double ScoreDifference,
    bool IsReliable,
    FrontBackDebugDecision SuggestedDecision,
    string FrontAlignment,
    string BackAlignment,
    string Message)
{
    public EdgeRingFaceDebugResult? EdgeRing { get; init; }

    public SameAngleOverlayDebugResult? SameAngleOverlay { get; init; }

    public ContourMirrorFaceDebugResult? ContourMirror { get; init; }

    public FixedAngleOverlayDebugResult? FixedAngleOverlay { get; init; }
}

public sealed record EdgeRingFaceDebugResult(
    double FrontScore,
    double BackScore,
    double ScoreDifference,
    bool IsReliable,
    FrontBackDebugDecision SuggestedDecision,
    int SampleCount,
    double StableSampleRatio,
    double GradientDirectionAgreement,
    double TemplateEdgeContrast,
    double CurrentEdgeContrast,
    string Message);

public sealed record SameAngleOverlayDebugResult(
    double Score,
    double SizeScore,
    double ShapeScore,
    double MaskIoU,
    double EdgeDistanceScore,
    bool IsReliable,
    FrontBackDebugDecision SuggestedDecision,
    string Alignment,
    string Message);

public sealed record ContourMirrorFaceDebugResult(
    double FrontScore,
    double BackScore,
    double ScoreDifference,
    bool IsReliable,
    FrontBackDebugDecision SuggestedDecision,
    double FrontAngleOffsetDegrees,
    double BackAngleOffsetDegrees,
    double FrontAlternativeScore,
    double BackAlternativeScore,
    double CurrentSignal,
    double TemplateSignal,
    double SearchRangeDegrees,
    string Message);

public sealed record ContourSampleMirrorFaceDebugResult(
    double FrontScore,
    double BackScore,
    double ScoreDifference,
    bool IsReliable,
    FrontBackDebugDecision SuggestedDecision,
    int SampleCount,
    double MinimumScoreDifference,
    double CurrentSignal,
    double TemplateSignal,
    double FrontAngleOffsetDegrees,
    double BackAngleOffsetDegrees,
    string Message);

public sealed record FixedAngleOverlayDebugResult(
    FixedAngleOverlayVariantDebugResult CenterOnly,
    FixedAngleOverlayVariantDebugResult ResolvedAngle,
    FixedAngleOverlayVariantDebugResult? MirrorAngle,
    string? DiagnosticImagePath,
    string Message);

public sealed record FixedAngleOverlayVariantDebugResult(
    string Name,
    double Score,
    double MaskIoU,
    double MismatchRatio,
    double TemplateOnlyRatio,
    double CurrentOnlyRatio,
    double CurrentAngleDegrees,
    double TemplateAngleDegrees,
    string Alignment);
