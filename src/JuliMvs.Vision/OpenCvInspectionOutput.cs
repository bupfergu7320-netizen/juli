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
        ContourMirrorFaceDebugResult? frontBackDecisionDiagnostic = null)
    {
        Result = result;
        DiagnosticImage = diagnosticImage;
        AlignmentSnapshot = alignmentSnapshot;
        CandidateDiagnostics = candidateDiagnostics ?? Array.Empty<ContourCandidateDiagnostic>();
        AngleDiagnostic = angleDiagnostic;
        TemplateSimilarity = templateSimilarity;
        FrontBackDecisionDiagnostic = frontBackDecisionDiagnostic;
    }

    public InspectionResult Result { get; init; }

    public Mat DiagnosticImage { get; init; }

    public XyrAlignmentSnapshot? AlignmentSnapshot { get; init; }

    public IReadOnlyList<ContourCandidateDiagnostic> CandidateDiagnostics { get; init; }

    public AngleResolutionDiagnostic? AngleDiagnostic { get; init; }

    public TemplateSimilarityResult? TemplateSimilarity { get; init; }

    public ContourMirrorFaceDebugResult? FrontBackDecisionDiagnostic { get; init; }
}
