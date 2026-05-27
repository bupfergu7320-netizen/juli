namespace JuliMvs.Vision;

public sealed class ContourFrontBackMatcher
{
    public const double DefaultMaximumAllowedErrorPixels = 15.0;
    public const double DefaultMinimumSeparationPixels = 2.5;
    public const double DefaultMinimumRadiusSignalPixels = 0.5;
    public const double DefaultMinimumTemplateMirrorSeparationPixels = 2.5;

    public ContourFrontBackMatch Match(
        ContourFeatureExtraction current,
        ContourFeatureExtraction template,
        ContourFrontBackMatcherOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(template);

        options ??= ContourFrontBackMatcherOptions.Default;
        if (current.RadiusSignature.Count == 0 || template.RadiusSignature.Count == 0)
        {
            return ContourFrontBackMatch.Unavailable("正反面判断不可用: 当前图或模板缺少外轮廓半径序列。");
        }

        if (current.RadiusSignalPixels < options.MinimumRadiusSignalPixels ||
            template.RadiusSignalPixels < options.MinimumRadiusSignalPixels)
        {
            return ContourFrontBackMatch.Unavailable(
                $"正反面判断不可用: 外轮廓半径变化过弱，当前={current.RadiusSignalPixels:F3}px，模板={template.RadiusSignalPixels:F3}px，最小要求={options.MinimumRadiusSignalPixels:F3}px。");
        }

        var mirroredTemplateSignature = ContourFeatureExtractor.MirrorRadiusSignature(template.RadiusSignature);
        var templateMirror = ContourFeatureExtractor.MatchRadiusSignatureWithAlternatives(
            template.RadiusSignature,
            mirroredTemplateSignature);
        if (templateMirror.ErrorPixels < options.MinimumTemplateMirrorSeparationPixels)
        {
            return ContourFrontBackMatch.Unavailable(
                $"正反面判断不可用: 正面模板和镜像模板过于接近，模板镜像误差={templateMirror.ErrorPixels:F2}px，最小要求={options.MinimumTemplateMirrorSeparationPixels:F2}px。");
        }

        var front = ContourFeatureExtractor.MatchRadiusSignatureWithAlternatives(
            current.RadiusSignature,
            template.RadiusSignature);
        var back = ContourFeatureExtractor.MatchRadiusSignatureWithAlternatives(
            current.RadiusSignature,
            mirroredTemplateSignature);
        var bestError = Math.Min(front.ErrorPixels, back.ErrorPixels);
        var difference = back.ErrorPixels - front.ErrorPixels;
        var separation = Math.Abs(difference);
        var isReliable = bestError <= options.MaximumAllowedErrorPixels &&
            separation >= options.MinimumSeparationPixels;
        var decision = isReliable
            ? difference > 0
                ? ContourFrontBackDecision.Front
                : ContourFrontBackDecision.Back
            : ContourFrontBackDecision.Uncertain;
        var message = decision switch
        {
            ContourFrontBackDecision.Front =>
                $"正反面判断: 正面。正面误差={front.ErrorPixels:F2}px，镜像误差={back.ErrorPixels:F2}px，分离={separation:F2}px。",
            ContourFrontBackDecision.Back =>
                $"正反面判断: 反面。正面误差={front.ErrorPixels:F2}px，镜像误差={back.ErrorPixels:F2}px，分离={separation:F2}px。",
            _ when bestError > options.MaximumAllowedErrorPixels =>
                $"正反面判断不确定: 轮廓匹配误差过大，正面误差={front.ErrorPixels:F2}px，镜像误差={back.ErrorPixels:F2}px，最大允许={options.MaximumAllowedErrorPixels:F2}px。",
            _ =>
                $"正反面判断不确定: 正面/镜像分离不足，正面误差={front.ErrorPixels:F2}px，镜像误差={back.ErrorPixels:F2}px，分离={separation:F2}px，分离阈值={options.MinimumSeparationPixels:F2}px。"
        };

        return new ContourFrontBackMatch(
            decision,
            isReliable,
            front.ErrorPixels,
            back.ErrorPixels,
            difference,
            separation,
            bestError,
            front.AngleDegrees,
            back.AngleDegrees,
            front.AlternativeErrorPixels,
            back.AlternativeErrorPixels,
            current.RadiusSignalPixels,
            template.RadiusSignalPixels,
            templateMirror.ErrorPixels,
            message);
    }
}

public sealed record ContourFrontBackMatcherOptions(
    double MaximumAllowedErrorPixels,
    double MinimumSeparationPixels,
    double MinimumRadiusSignalPixels,
    double MinimumTemplateMirrorSeparationPixels)
{
    public static ContourFrontBackMatcherOptions Default { get; } = new(
        ContourFrontBackMatcher.DefaultMaximumAllowedErrorPixels,
        ContourFrontBackMatcher.DefaultMinimumSeparationPixels,
        ContourFrontBackMatcher.DefaultMinimumRadiusSignalPixels,
        ContourFrontBackMatcher.DefaultMinimumTemplateMirrorSeparationPixels);
}

public enum ContourFrontBackDecision
{
    Unavailable,
    Front,
    Back,
    Uncertain
}

public sealed record ContourFrontBackMatch(
    ContourFrontBackDecision Decision,
    bool IsReliable,
    double FrontErrorPixels,
    double BackErrorPixels,
    double ScoreDifferencePixels,
    double SeparationPixels,
    double BestErrorPixels,
    double FrontAngleDegrees,
    double BackAngleDegrees,
    double FrontAlternativeErrorPixels,
    double BackAlternativeErrorPixels,
    double CurrentRadiusSignalPixels,
    double TemplateRadiusSignalPixels,
    double TemplateMirrorErrorPixels,
    string Message)
{
    public static ContourFrontBackMatch Unavailable(string message)
    {
        return new ContourFrontBackMatch(
            ContourFrontBackDecision.Unavailable,
            IsReliable: false,
            FrontErrorPixels: double.PositiveInfinity,
            BackErrorPixels: double.PositiveInfinity,
            ScoreDifferencePixels: 0,
            SeparationPixels: 0,
            BestErrorPixels: double.PositiveInfinity,
            FrontAngleDegrees: 0,
            BackAngleDegrees: 0,
            FrontAlternativeErrorPixels: double.PositiveInfinity,
            BackAlternativeErrorPixels: double.PositiveInfinity,
            CurrentRadiusSignalPixels: 0,
            TemplateRadiusSignalPixels: 0,
            TemplateMirrorErrorPixels: double.PositiveInfinity,
            message);
    }
}
