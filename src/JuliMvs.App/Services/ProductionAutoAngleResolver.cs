using JuliMvs.Core.Vision;
using JuliMvs.Core;
using JuliMvs.Vision;

namespace JuliMvs.App.Services;

internal sealed class ProductionAutoAngleResolver
{
    private const double RadiusAngleAssistMaximumDeltaDegrees = 2.0;
    private const double WeakRadiusSignalPixels = 3.0;
    private const double UsefulRadiusSignalPixels = 8.0;
    private const double StrongRadiusSignalPixels = 20.0;
    private const double WeakRadiusSeparationPixels = 0.20;
    private const double UsefulRadiusSeparationPixels = 0.60;
    private const double ShapeFailureFallbackMinimumAreaDifferenceRatio = 0.02;
    private const double ShapeFailureFallbackMaximumAreaDifferenceRatio = 0.15;
    private const double FourWayMinimumAxisRatio = 1.03;
    private const double FourWayHighAxisSpreadDegrees = 5.0;
    private const double FourWayMediumAxisSpreadDegrees = 10.0;
    private const double FourWayMaximumShapeRefineDeltaDegrees = 5.0;
    private const double FourWayMinimumShapeSeparationPixels = 0.0;
    private const double FourWayAlternativeExclusionDegrees = 1.0;
    private const double FourWayHalfTurnAlignmentTieTolerancePixels = 0.5;

    private readonly ContourShapeMatcher _shapeMatcher = new();

    public void WarmupTemplate(
        ContourFeatureExtraction templateFeature,
        bool includeFrontBack)
    {
        ArgumentNullException.ThrowIfNull(templateFeature);

        _shapeMatcher.WarmupTemplate(templateFeature, includeFrontBack);
    }

    public ProductionAutoAngleResult Resolve(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        PartTemplate template,
        ContourShapeMatch? frontShapeMatch = null,
        bool fourWaySymmetric = false)
    {
        ArgumentNullException.ThrowIfNull(currentFeature);
        ArgumentNullException.ThrowIfNull(templateFeature);
        ArgumentNullException.ThrowIfNull(template);

        if (fourWaySymmetric)
        {
            return ResolveFourWaySymmetric(currentFeature, templateFeature, template, frontShapeMatch);
        }

        var front = frontShapeMatch ?? _shapeMatcher.Match(currentFeature, templateFeature);
        if (!front.IsReliable)
        {
            var fallback = TryResolveAfterShapeFailure(currentFeature, templateFeature, template, front);
            if (fallback is not null)
            {
                return fallback;
            }

            return ProductionAutoAngleResult.Unreliable($"{front.Message}，XYR已清零");
        }

        var angleFusion = ResolveAngleWithRadiusAssist(front, currentFeature, templateFeature);
        return ProductionAutoAngleResult.Reliable(
            template.ReferenceAngleDegrees + angleFusion.AngleOffsetDegrees,
            front.CenterXPixel,
            front.CenterYPixel,
            front.Score,
            AllowsFullRotation: true,
            $"Shape轮廓配准: {front.Message} {angleFusion.Message}") with
        {
            AlignmentAngleDegrees = template.ReferenceAngleDegrees + front.AlignmentAngleOffsetDegrees
        };
    }

    private static ProductionAutoAngleResult? TryResolveAfterShapeFailure(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        PartTemplate template,
        ContourShapeMatch failedShapeMatch)
    {
        var areaDifferenceRatio = CalculateRatioDifference(currentFeature.AreaPixels, template.AreaPixels);
        if (areaDifferenceRatio < ShapeFailureFallbackMinimumAreaDifferenceRatio ||
            areaDifferenceRatio > ShapeFailureFallbackMaximumAreaDifferenceRatio)
        {
            return null;
        }

        var radiusMatch = ContourFeatureExtractor.MatchRadiusSignatureWithAlternatives(
            currentFeature.RadiusSignature,
            templateFeature.RadiusSignature,
            alternativeExclusionDegrees: 12.0);
        var radiusSeparation = radiusMatch.AlternativeErrorPixels - radiusMatch.ErrorPixels;
        var hasUsableRadiusAngle =
            currentFeature.RadiusSignalPixels >= WeakRadiusSignalPixels &&
            templateFeature.RadiusSignalPixels >= WeakRadiusSignalPixels &&
            double.IsFinite(radiusSeparation) &&
            radiusSeparation >= UsefulRadiusSeparationPixels;

        double angleOffset;
        string method;
        if (hasUsableRadiusAngle)
        {
            angleOffset = AngleMath.NormalizeDegrees360(radiusMatch.AngleDegrees);
            method = $"半径序列兜底R={angleOffset:F2}deg，半径分离={radiusSeparation:F2}px";
        }
        else if (currentFeature.AxisFeature.HasEllipse && templateFeature.AxisFeature.HasEllipse)
        {
            angleOffset = AngleMath.NormalizeDeltaDegrees(
                currentFeature.AxisFeature.EllipseAngleDegrees,
                templateFeature.AxisFeature.EllipseAngleDegrees);
            method =
                $"椭圆主轴兜底R={angleOffset:F2}deg，半径分离={radiusSeparation:F2}px，" +
                $"当前轴比={currentFeature.AxisFeature.MeanRatio:F3}";
        }
        else
        {
            angleOffset = AngleMath.NormalizeDeltaDegrees(
                currentFeature.PcaAngleDegrees,
                templateFeature.PcaAngleDegrees);
            method =
                $"PCA兜底R={angleOffset:F2}deg，半径分离={radiusSeparation:F2}px，" +
                $"当前PCA={currentFeature.PcaRatio:F3}";
        }

        var matchScore = Math.Clamp(1.0 - areaDifferenceRatio / ShapeFailureFallbackMaximumAreaDifferenceRatio, 0.20, 0.50);
        return ProductionAutoAngleResult.Reliable(
            template.ReferenceAngleDegrees + angleOffset,
            currentFeature.CenterXPixel,
            currentFeature.CenterYPixel,
            matchScore,
            AllowsFullRotation: false,
            $"Shape首次匹配失败但轮廓面积仍在同批允许范围内，启用宽容兜底: {method}；" +
            $"Shape失败原因: {failedShapeMatch.Message}") with
        {
            AlignmentAngleDegrees = template.ReferenceAngleDegrees + angleOffset,
            SkipMissingMaterialDetection = true
        };
    }

    private static double CalculateRatioDifference(double current, double reference)
    {
        var denominator = Math.Max(Math.Abs(reference), 0.0001);
        return Math.Abs(current - reference) / denominator;
    }

    private ProductionAutoAngleResult ResolveFourWaySymmetric(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        PartTemplate template,
        ContourShapeMatch? frontShapeMatch)
    {
        var currentAxis = currentFeature.AxisFeature;
        var templateAxis = templateFeature.AxisFeature;
        var currentAxisCheck = ClassifyFourWayAxis(currentAxis, "当前来料");
        if (!currentAxisCheck.IsUsable)
        {
            return ProductionAutoAngleResult.Unreliable($"{currentAxisCheck.Message}，XYR已清零");
        }

        var templateAxisCheck = ClassifyFourWayAxis(templateAxis, "模板");
        if (!templateAxisCheck.IsUsable)
        {
            return ProductionAutoAngleResult.Unreliable($"{templateAxisCheck.Message}，XYR已清零");
        }

        var axisOffset = ResolveFourWayAxisOffset(currentAxis, templateAxis);
        var axisSeedOffset = AngleMath.NormalizeDeltaDegrees(
            currentAxis.MeanAngleDegrees,
            templateAxis.MeanAngleDegrees);
        var refineOptions = ContourShapeMatcherOptions.Default with
        {
            MaximumErrorPixels = 10.0,
            MinimumSeparationPixels = FourWayMinimumShapeSeparationPixels,
            AlternativeExclusionDegrees = FourWayAlternativeExclusionDegrees,
            AllowHalfTurnEquivalent = true,
            NarrowAngleSearchRangeDegrees = FourWayMaximumShapeRefineDeltaDegrees
        };
        var refined = _shapeMatcher.RefineNearAngle(
            currentFeature,
            templateFeature,
            axisSeedOffset,
            refineOptions);
        if (!refined.IsReliable)
        {
            return ProductionAutoAngleResult.Unreliable($"{refined.Message}，XYR已清零");
        }

        var refinedOffset = AngleMath.NormalizeDegrees180(refined.AngleOffsetDegrees);
        var refineDelta = Math.Abs(AngleMath.NormalizeDeltaDegrees(refinedOffset, axisSeedOffset));
        if (refineDelta > FourWayMaximumShapeRefineDeltaDegrees)
        {
            return ProductionAutoAngleResult.Unreliable(
                $"四边对称主轴不稳定: 主轴R={axisOffset:F2}deg，Chamfer精修R={refinedOffset:F2}deg，差={refineDelta:F2}deg，最大允许={FourWayMaximumShapeRefineDeltaDegrees:F2}deg，XYR已清零");
        }

        var imageAlignmentOffset = ResolveFourWayImageAlignmentOffset(
            currentFeature,
            templateFeature,
            refined,
            refineOptions);
        var resolvedAngle = template.ReferenceAngleDegrees + axisOffset;
        var message =
            "四边对称主轴定位: " +
            "angle_mode=AXIS_0_180, r_has_180_ambiguity=true; " +
            $"角度置信度=模板{templateAxisCheck.Confidence}/来料{currentAxisCheck.Confidence}; " +
            $"模板PCA={templateAxis.RegionAngleDegrees:F2}deg, 模板椭圆={templateAxis.EllipseAngleDegrees:F2}deg, 模板轴比={templateAxis.MeanRatio:F3}; " +
            $"当前PCA={currentAxis.RegionAngleDegrees:F2}deg, 当前椭圆={currentAxis.EllipseAngleDegrees:F2}deg, 当前轴比={currentAxis.MeanRatio:F3}; " +
            $"主轴R={axisOffset:F2}deg，Chamfer小范围精修R={refinedOffset:F2}deg，缺料对齐R={imageAlignmentOffset:F2}deg，精修差={refineDelta:F2}deg。{refined.Message}";
        return ProductionAutoAngleResult.Reliable(
            resolvedAngle,
            refined.CenterXPixel,
            refined.CenterYPixel,
            refined.Score,
            AllowsFullRotation: false,
            message,
            angleMode: ProductionAngleMode.Axis0To180,
            rHas180Ambiguity: true) with
        {
            AlignmentAngleDegrees = template.ReferenceAngleDegrees + imageAlignmentOffset
        };
    }

    private double ResolveFourWayImageAlignmentOffset(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        ContourShapeMatch foldedRefined,
        ContourShapeMatcherOptions refineOptions)
    {
        var halfTurnSeed = AngleMath.NormalizeDegrees360(foldedRefined.AngleOffsetDegrees + 180.0);
        var halfTurnRefined = _shapeMatcher.RefineNearAngle(
            currentFeature,
            templateFeature,
            halfTurnSeed,
            refineOptions);
        if (!halfTurnRefined.IsReliable)
        {
            return foldedRefined.AlignmentAngleOffsetDegrees;
        }

        return halfTurnRefined.ErrorPixels <= foldedRefined.ErrorPixels + FourWayHalfTurnAlignmentTieTolerancePixels
            ? halfTurnRefined.AlignmentAngleOffsetDegrees
            : foldedRefined.AlignmentAngleOffsetDegrees;
    }

    private static double ResolveFourWayAxisOffset(
        ContourAxisFeature currentAxis,
        ContourAxisFeature templateAxis)
    {
        if (currentAxis.HasEllipse && templateAxis.HasEllipse)
        {
            return AngleMath.NormalizeDeltaDegrees(
                currentAxis.EllipseAngleDegrees,
                templateAxis.EllipseAngleDegrees);
        }

        return AngleMath.NormalizeDeltaDegrees(
            currentAxis.MeanAngleDegrees,
            templateAxis.MeanAngleDegrees);
    }

    private static FourWayAxisCheck ClassifyFourWayAxis(ContourAxisFeature axis, string label)
    {
        if (axis.MeanRatio < FourWayMinimumAxisRatio)
        {
            return FourWayAxisCheck.Bad(
                $"四边对称主轴不可用: {label}轴比={axis.MeanRatio:F3}，最小要求={FourWayMinimumAxisRatio:F3}");
        }

        if (!axis.HasEllipse)
        {
            return FourWayAxisCheck.Bad($"四边对称主轴不可用: {label}fitEllipse失败");
        }

        if (axis.MaximumAngleSpreadDegrees <= FourWayHighAxisSpreadDegrees)
        {
            return FourWayAxisCheck.Usable("HIGH", $"{label}PCA/fitEllipse差={axis.MaximumAngleSpreadDegrees:F2}deg");
        }

        if (axis.MaximumAngleSpreadDegrees <= FourWayMediumAxisSpreadDegrees)
        {
            return FourWayAxisCheck.Usable("MEDIUM", $"{label}PCA/fitEllipse差={axis.MaximumAngleSpreadDegrees:F2}deg，进入Chamfer验证");
        }

        return FourWayAxisCheck.Usable("LOW", $"{label}PCA/fitEllipse差={axis.MaximumAngleSpreadDegrees:F2}deg，必须通过Chamfer验证");
    }

    public ContourShapeFrontBackMatch MatchFrontBack(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature)
    {
        ArgumentNullException.ThrowIfNull(currentFeature);
        ArgumentNullException.ThrowIfNull(templateFeature);

        var front = _shapeMatcher.Match(currentFeature, templateFeature);
        var back = _shapeMatcher.MatchMirroredTemplate(currentFeature, templateFeature);
        return ContourShapeFrontBackMatch.From(front, back);
    }

    internal static ProductionAngleFusion ResolveAngleWithRadiusAssist(
        ContourShapeMatch shapeMatch,
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature)
    {
        if (currentFeature.RadiusSignature.Count == 0 ||
            templateFeature.RadiusSignature.Count == 0)
        {
            return new ProductionAngleFusion(
                shapeMatch.AngleOffsetDegrees,
                ShapeWeight: 1.0,
                RadiusWeight: 0.0,
                "半径序列修正跳过: 缺少半径序列。");
        }

        var radiusMatch = ContourFeatureExtractor.MatchRadiusSignatureWithAlternatives(
            currentFeature.RadiusSignature,
            templateFeature.RadiusSignature,
            alternativeExclusionDegrees: 12.0);
        var radiusAngle = AngleMath.NormalizeDegrees360(radiusMatch.AngleDegrees);
        var delta = Math.Abs(AngleMath.NormalizeDeltaDegrees360(radiusAngle, shapeMatch.AngleOffsetDegrees));
        var radiusSeparation = radiusMatch.AlternativeErrorPixels - radiusMatch.ErrorPixels;
        if (delta > RadiusAngleAssistMaximumDeltaDegrees)
        {
            return new ProductionAngleFusion(
                shapeMatch.AngleOffsetDegrees,
                ShapeWeight: 1.0,
                RadiusWeight: 0.0,
                $"半径序列修正跳过: 半径R={radiusAngle:F2}deg，与Shape差={delta:F2}deg。");
        }

        var weights = SelectAngleFusionWeights(templateFeature, radiusSeparation);
        if (weights.RadiusWeight <= 0.0)
        {
            return new ProductionAngleFusion(
                shapeMatch.AngleOffsetDegrees,
                ShapeWeight: 1.0,
                RadiusWeight: 0.0,
                "半径序列修正跳过: 当前模板半径方向特征不足。");
        }

        var fused = WeightedCircularMean360(
            shapeMatch.AngleOffsetDegrees,
            radiusAngle,
            weights.ShapeWeight,
            weights.RadiusWeight);
        return new ProductionAngleFusion(
            fused,
            weights.ShapeWeight,
            weights.RadiusWeight,
            $"半径序列修正R: Shape={shapeMatch.AngleOffsetDegrees:F2}deg，半径={radiusAngle:F2}deg，融合={fused:F2}deg，自动权重=Shape {weights.ShapeWeight:P0}/半径 {weights.RadiusWeight:P0}，半径分离={radiusSeparation:F2}px。");
    }

    private static ProductionAngleFusionWeights SelectAngleFusionWeights(
        ContourFeatureExtraction templateFeature,
        double radiusSeparationPixels)
    {
        if (templateFeature.RadiusSignalPixels < WeakRadiusSignalPixels)
        {
            return new ProductionAngleFusionWeights(1.0, 0.0);
        }

        var radiusWeight = templateFeature.Strategy.ShapeClass switch
        {
            AutoPartShapeClass.IrregularRound => 0.55,
            AutoPartShapeClass.WeakEllipse => 0.45,
            AutoPartShapeClass.StrongEllipse => 0.25,
            _ => 0.0
        };

        if (templateFeature.RadiusSignalPixels >= StrongRadiusSignalPixels)
        {
            radiusWeight += 0.10;
        }
        else if (templateFeature.RadiusSignalPixels >= UsefulRadiusSignalPixels)
        {
            radiusWeight += 0.05;
        }

        if (double.IsFinite(radiusSeparationPixels))
        {
            if (radiusSeparationPixels < WeakRadiusSeparationPixels)
            {
                radiusWeight = Math.Min(radiusWeight, 0.20);
            }
            else if (radiusSeparationPixels < UsefulRadiusSeparationPixels)
            {
                radiusWeight = Math.Min(radiusWeight, 0.35);
            }
        }

        radiusWeight = Math.Clamp(radiusWeight, 0.0, 0.75);
        return new ProductionAngleFusionWeights(1.0 - radiusWeight, radiusWeight);
    }

    private static double WeightedCircularMean360(
        double leftDegrees,
        double rightDegrees,
        double leftWeight,
        double rightWeight)
    {
        var leftRadians = leftDegrees * Math.PI / 180.0;
        var rightRadians = rightDegrees * Math.PI / 180.0;
        var x = leftWeight * Math.Cos(leftRadians) + rightWeight * Math.Cos(rightRadians);
        var y = leftWeight * Math.Sin(leftRadians) + rightWeight * Math.Sin(rightRadians);
        return AngleMath.NormalizeDegrees360(Math.Atan2(y, x) * 180.0 / Math.PI);
    }
}

internal sealed record ProductionAngleFusion(
    double AngleOffsetDegrees,
    double ShapeWeight,
    double RadiusWeight,
    string Message);

internal sealed record ProductionAngleFusionWeights(
    double ShapeWeight,
    double RadiusWeight);

internal sealed record FourWayAxisCheck(
    bool IsUsable,
    string Confidence,
    string Message)
{
    public static FourWayAxisCheck Usable(string confidence, string message)
    {
        return new FourWayAxisCheck(true, confidence, message);
    }

    public static FourWayAxisCheck Bad(string message)
    {
        return new FourWayAxisCheck(false, "BAD", message);
    }
}

internal sealed record ContourShapeFrontBackMatch(
    ContourFrontBackDecision Decision,
    bool IsReliable,
    ContourShapeMatch Front,
    ContourShapeMatch Back,
    double SeparationPixels,
    string Message)
{
    public static ContourShapeFrontBackMatch From(
        ContourShapeMatch front,
        ContourShapeMatch back)
    {
        if (!front.IsReliable && !back.IsReliable)
        {
            return new ContourShapeFrontBackMatch(
                ContourFrontBackDecision.Unavailable,
                false,
                front,
                back,
                0.0,
                $"Shape正反不可用: 正面={front.Message}；镜像={back.Message}");
        }

        var separation = Math.Abs(back.ErrorPixels - front.ErrorPixels);
        var isReliable = separation >= 2.5;
        var decision = isReliable
            ? front.ErrorPixels <= back.ErrorPixels
                ? ContourFrontBackDecision.Front
                : ContourFrontBackDecision.Back
            : ContourFrontBackDecision.Uncertain;
        var message = decision switch
        {
            ContourFrontBackDecision.Front =>
                $"Shape正反: 正面。正面误差={front.ErrorPixels:F2}px，镜像误差={back.ErrorPixels:F2}px，分离={separation:F2}px。",
            ContourFrontBackDecision.Back =>
                $"Shape正反: 反面。正面误差={front.ErrorPixels:F2}px，镜像误差={back.ErrorPixels:F2}px，分离={separation:F2}px。",
            _ =>
                $"Shape正反不确定: 正面误差={front.ErrorPixels:F2}px，镜像误差={back.ErrorPixels:F2}px，分离={separation:F2}px。"
        };

        return new ContourShapeFrontBackMatch(
            decision,
            isReliable,
            front,
            back,
            separation,
            message);
    }
}

internal sealed record ProductionAutoAngleResult(
    bool IsReliable,
    double ResolvedAngleDegrees,
    double CenterXPixel,
    double CenterYPixel,
    double MatchScore,
    bool AllowsFullRotation,
    ProductionAngleMode AngleMode,
    bool RHas180Ambiguity,
    string Message)
{
    public static ProductionAutoAngleResult Reliable(
        double resolvedAngleDegrees,
        double centerXPixel,
        double centerYPixel,
        double matchScore,
        bool AllowsFullRotation,
        string message,
        ProductionAngleMode? angleMode = null,
        bool? rHas180Ambiguity = null)
    {
        var resolvedAngleMode = angleMode ?? (AllowsFullRotation
            ? ProductionAngleMode.Rotation0To360
            : ProductionAngleMode.Axis0To180);
        var resolvedAmbiguity = rHas180Ambiguity ?? resolvedAngleMode == ProductionAngleMode.Axis0To180;
        return new ProductionAutoAngleResult(
            true,
            resolvedAngleDegrees,
            centerXPixel,
            centerYPixel,
            matchScore,
            AllowsFullRotation,
            resolvedAngleMode,
            resolvedAmbiguity,
            message);
    }

    public static ProductionAutoAngleResult Unreliable(string message)
    {
        return new ProductionAutoAngleResult(
            false,
            0,
            0,
            0,
            0,
            false,
            ProductionAngleMode.None,
            false,
            message);
    }

    public double AlignmentAngleDegrees { get; init; } = ResolvedAngleDegrees;
    public bool SkipMissingMaterialDetection { get; init; }
    public string AngleModeText => AngleMode switch
    {
        ProductionAngleMode.Axis0To180 => "AXIS_0_180",
        ProductionAngleMode.Rotation0To360 => "ROTATION_0_360",
        _ => "NONE"
    };
}

internal enum ProductionAngleMode
{
    None,
    Rotation0To360,
    Axis0To180
}
