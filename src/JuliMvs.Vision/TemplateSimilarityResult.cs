namespace JuliMvs.Vision;

public sealed record TemplateSimilarityResult(
    double FinalScore,
    double SizeScore,
    double ShapeScore,
    double MaskIoU,
    double EdgeDistanceScore,
    bool IsSamePart,
    bool IsReliable,
    string Message);
