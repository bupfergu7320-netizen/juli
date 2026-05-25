using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using OpenCvSharp;

namespace JuliMvs.Vision;

public sealed record OpenCvInspectionOutput
{
    public OpenCvInspectionOutput(
        InspectionResult result,
        Mat diagnosticImage,
        XyrAlignmentSnapshot? alignmentSnapshot = null,
        IReadOnlyList<ContourCandidateDiagnostic>? candidateDiagnostics = null,
        AngleResolutionDiagnostic? angleDiagnostic = null,
        TemplateSimilarityResult? templateSimilarity = null,
        ContourSampleMirrorFaceDebugResult? contourSampleMirrorDecisionDiagnostic = null,
        VisionStageTimings? stageTimings = null)
    {
        Result = result;
        DiagnosticImage = diagnosticImage;
        AlignmentSnapshot = alignmentSnapshot;
        CandidateDiagnostics = candidateDiagnostics ?? Array.Empty<ContourCandidateDiagnostic>();
        AngleDiagnostic = angleDiagnostic;
        TemplateSimilarity = templateSimilarity;
        ContourSampleMirrorDecisionDiagnostic = contourSampleMirrorDecisionDiagnostic;
        StageTimings = stageTimings ?? VisionStageTimings.Empty;
    }

    public InspectionResult Result { get; init; }

    public Mat DiagnosticImage { get; init; }

    public XyrAlignmentSnapshot? AlignmentSnapshot { get; init; }

    public IReadOnlyList<ContourCandidateDiagnostic> CandidateDiagnostics { get; init; }

    public AngleResolutionDiagnostic? AngleDiagnostic { get; init; }

    public TemplateSimilarityResult? TemplateSimilarity { get; init; }

    public ContourSampleMirrorFaceDebugResult? ContourSampleMirrorDecisionDiagnostic { get; init; }

    public VisionStageTimings StageTimings { get; init; }
}

public sealed record VisionStageTimings(
    long PrepareImageMs,
    long DetectPartMs,
    long ResolveAngleMs,
    long TemplateSimilarityMs,
    long AlignmentMs,
    long DecisionMs,
    long FrontBackMs,
    long OverlayMs)
{
    public static VisionStageTimings Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}
