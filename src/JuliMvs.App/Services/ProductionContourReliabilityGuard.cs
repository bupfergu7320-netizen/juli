using JuliMvs.Core.Vision;
using JuliMvs.Vision;

namespace JuliMvs.App.Services;

internal static class ProductionContourReliabilityGuard
{
    public const double MinimumContourMatchScoreForR = 0.2;
    public const double PreferredContourMatchScoreForR = 0.5;
    public const double MaximumAreaDifferenceRatio = 0.15;

    public static bool ShouldLockR(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature)
    {
        ArgumentNullException.ThrowIfNull(currentFeature);
        ArgumentNullException.ThrowIfNull(templateFeature);

        return false;
    }

    public static ProductionContourReliabilityResult Evaluate(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        PartTemplate template,
        double matchScore)
    {
        ArgumentNullException.ThrowIfNull(currentFeature);
        ArgumentNullException.ThrowIfNull(templateFeature);
        ArgumentNullException.ThrowIfNull(template);

        var areaDifferenceRatio = CalculateRatioDifference(currentFeature.AreaPixels, template.AreaPixels);
        if (areaDifferenceRatio > MaximumAreaDifferenceRatio)
        {
            return ProductionContourReliabilityResult.Fail(
                $"当前轮廓面积与模板差异过大，差异={areaDifferenceRatio:P1}，最大允许={MaximumAreaDifferenceRatio:P1}");
        }

        if (matchScore < MinimumContourMatchScoreForR)
        {
            return ProductionContourReliabilityResult.Fail(
                $"轮廓角度匹配分数过低，分数={matchScore:F3}，最小要求={MinimumContourMatchScoreForR:F3}");
        }

        if (matchScore < PreferredContourMatchScoreForR)
        {
            return ProductionContourReliabilityResult.PassWithWarning(
                $"轮廓匹配分数偏低，分数={matchScore:F3}，建议达到={PreferredContourMatchScoreForR:F3}；已继续做缺料检测。");
        }

        return ProductionContourReliabilityResult.Pass;
    }

    private static double CalculateRatioDifference(double current, double reference)
    {
        var denominator = Math.Max(Math.Abs(reference), 0.0001);
        return Math.Abs(current - reference) / denominator;
    }

}

internal sealed record ProductionContourReliabilityResult(bool IsReliable, string Reason, string? Warning = null)
{
    public static ProductionContourReliabilityResult Pass { get; } = new(true, string.Empty);

    public static ProductionContourReliabilityResult PassWithWarning(string warning)
    {
        return new ProductionContourReliabilityResult(true, string.Empty, warning);
    }

    public static ProductionContourReliabilityResult Fail(string reason)
    {
        return new ProductionContourReliabilityResult(false, reason);
    }
}
