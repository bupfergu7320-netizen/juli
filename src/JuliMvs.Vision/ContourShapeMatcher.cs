using JuliMvs.Core;
using OpenCvSharp;

namespace JuliMvs.Vision;

public sealed class ContourShapeMatcher
{
    public const double DefaultMaximumErrorPixels = 8.0;
    public const double DefaultMinimumSeparationPixels = 1.0;
    public const double DefaultMaximumAreaDifferenceRatio = 0.15;
    private const int DistanceMapPaddingPixels = 96;
    private const int CoarseSearchMaximumPointCount = 240;
    private const int MaximumTemplateDistanceCacheEntries = 4;

    private readonly Dictionary<TemplateDistanceCacheKey, TemplateDistanceCache> _templateDistanceCache = new();
    private readonly object _templateDistanceCacheLock = new();

    public ContourShapeMatch Match(
        ContourFeatureExtraction current,
        ContourFeatureExtraction template,
        ContourShapeMatcherOptions? options = null)
    {
        return Match(current, template, template.ContourPoints, "正面", options);
    }

    public ContourShapeMatch MatchMirroredTemplate(
        ContourFeatureExtraction current,
        ContourFeatureExtraction template,
        ContourShapeMatcherOptions? options = null)
    {
        var mirrored = ContourFeatureExtractor.MirrorContourPoints(
            template.ContourPoints,
            template.CenterXPixel);
        return Match(current, template, mirrored, "镜像", options);
    }

    public void WarmupTemplate(
        ContourFeatureExtraction template,
        bool includeMirroredTemplate)
    {
        ArgumentNullException.ThrowIfNull(template);

        _ = Match(template, template);
        if (!includeMirroredTemplate)
        {
            return;
        }

        _ = MatchMirroredTemplate(template, template);
    }

    public ContourShapeMatch RefineNearAngle(
        ContourFeatureExtraction current,
        ContourFeatureExtraction template,
        double angleOffsetSeedDegrees,
        ContourShapeMatcherOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(template);
        options ??= ContourShapeMatcherOptions.Default;

        if (current.ContourPoints.Count == 0 || template.ContourPoints.Count == 0)
        {
            return ContourShapeMatch.Unavailable("NG_MATCH_BAD: 主轴Shape小范围精修不可用，当前或模板缺少轮廓点。");
        }

        var areaDifferenceRatio = CalculateRatioDifference(current.AreaPixels, template.AreaPixels);
        using var currentDistance = BuildDistanceMap(
            current.ContourPoints,
            current.ImageWidthPixels,
            current.ImageHeightPixels);
        using var templateDistance = GetTemplateDistance(template, template.ContourPoints, "正面");
        var halfRange = Math.Max(options.NarrowAngleSearchRangeDegrees, options.NarrowAngleFineStepDegrees);
        var fine = SearchPoses(
            currentDistance,
            templateDistance,
            template.ContourPoints,
            current.ContourPoints,
            template.CenterXPixel,
            template.CenterYPixel,
            current.CenterXPixel,
            current.CenterYPixel,
            angleOffsetSeedDegrees - halfRange,
            angleOffsetSeedDegrees + halfRange,
            angleStepDegrees: options.NarrowAngleFineStepDegrees,
            translationRadiusPixels: options.FineTranslationRadiusPixels,
            translationStepPixels: options.FineTranslationStepPixels,
            options);
        var refined = RefineTranslationAtFixedAngle(
            currentDistance,
            templateDistance,
            template.ContourPoints,
            current.ContourPoints,
            template.CenterXPixel,
            template.CenterYPixel,
            fine.CenterXPixel,
            fine.CenterYPixel,
            fine.BestAngleDegrees,
            options.SubpixelTranslationRadiusPixels,
            options.SubpixelTranslationStepPixels,
            options.MaximumSampleDistancePixels);

        var bestError = refined.BestErrorPixels;
        var secondError = fine.SecondErrorPixels;
        var separation = secondError - bestError;
        var score = Math.Clamp(1.0 - bestError / Math.Max(options.MaximumErrorPixels, 0.0001), 0.0, 1.0);
        if (areaDifferenceRatio > options.MaximumAreaDifferenceRatio &&
            bestError > options.MaximumErrorPixels * 0.75)
        {
            return ContourShapeMatch.Fail(
                $"NG_MATCH_BAD: 主轴Shape小范围精修贴合差，面积差异={areaDifferenceRatio:P1}，最大允许={options.MaximumAreaDifferenceRatio:P1}，匹配误差={bestError:F2}px。",
                areaDifferenceRatio,
                refined.BestAngleDegrees,
                bestError,
                secondError,
                separation,
                score);
        }

        if (bestError > options.MaximumErrorPixels)
        {
            return ContourShapeMatch.Fail(
                $"NG_MATCH_BAD: 主轴Shape小范围精修贴合差，最佳误差={bestError:F2}px，最大允许={options.MaximumErrorPixels:F2}px。",
                areaDifferenceRatio,
                refined.BestAngleDegrees,
                bestError,
                secondError,
                separation,
                score);
        }

        var requiresSeparation = options.MinimumSeparationPixels > 0.0;
        if (requiresSeparation &&
            (double.IsNaN(separation) ||
            double.IsInfinity(separation) ||
            separation < options.MinimumSeparationPixels))
        {
            return ContourShapeMatch.Fail(
                $"NG_ANGLE_UNSTABLE: Chamfer小范围角度谷底不明显，最佳误差={bestError:F2}px，第二误差={secondError:F2}px，分离={separation:F2}px，最小要求={options.MinimumSeparationPixels:F2}px。",
                areaDifferenceRatio,
                refined.BestAngleDegrees,
                bestError,
                secondError,
                separation,
                score);
        }

        return ContourShapeMatch.Pass(
            "主轴精修",
            AngleMath.NormalizeDegrees360(refined.BestAngleDegrees),
            refined.BestAngleDegrees,
            refined.CenterXPixel,
            refined.CenterYPixel,
            bestError,
            secondError,
            separation,
            score,
            areaDifferenceRatio,
            $"主轴Shape小范围精修OK: 主轴种子={angleOffsetSeedDegrees:F2}deg，精修偏移={refined.BestAngleDegrees:F2}deg，误差={bestError:F2}px，第二误差={secondError:F2}px，分离={separation:F2}px，分数={score:F3}，XY精修=({refined.CenterXPixel - current.CenterXPixel:F2},{refined.CenterYPixel - current.CenterYPixel:F2})px。");
    }

    private ContourShapeMatch Match(
        ContourFeatureExtraction current,
        ContourFeatureExtraction template,
        IReadOnlyList<Point2d> templatePoints,
        string modelName,
        ContourShapeMatcherOptions? options)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(template);
        options ??= ContourShapeMatcherOptions.Default;

        if (current.ContourPoints.Count == 0 || templatePoints.Count == 0)
        {
            return ContourShapeMatch.Unavailable($"{modelName}Shape匹配不可用: 当前或模板缺少轮廓点。");
        }

        var areaDifferenceRatio = CalculateRatioDifference(current.AreaPixels, template.AreaPixels);
        using var currentDistance = BuildDistanceMap(
            current.ContourPoints,
            current.ImageWidthPixels,
            current.ImageHeightPixels);
        using var templateDistance = GetTemplateDistance(template, templatePoints, modelName);
        var coarseTemplatePoints = DownsamplePoints(templatePoints, CoarseSearchMaximumPointCount);
        var coarseCurrentPoints = DownsamplePoints(current.ContourPoints, CoarseSearchMaximumPointCount);
        var coarse = SearchPoses(
            currentDistance,
            templateDistance,
            coarseTemplatePoints,
            coarseCurrentPoints,
            template.CenterXPixel,
            template.CenterYPixel,
            current.CenterXPixel,
            current.CenterYPixel,
            -180.0,
            180.0,
            angleStepDegrees: 3.0,
            translationRadiusPixels: options.CoarseTranslationRadiusPixels,
            translationStepPixels: options.CoarseTranslationStepPixels,
            options);
        var fine = SearchPoses(
            currentDistance,
            templateDistance,
            templatePoints,
            current.ContourPoints,
            template.CenterXPixel,
            template.CenterYPixel,
            coarse.CenterXPixel,
            coarse.CenterYPixel,
            coarse.BestAngleDegrees - 3.0,
            coarse.BestAngleDegrees + 3.0,
            angleStepDegrees: 0.25,
            translationRadiusPixels: options.FineTranslationRadiusPixels,
            translationStepPixels: options.FineTranslationStepPixels,
            options);
        var refined = RefineTranslationAtFixedAngle(
            currentDistance,
            templateDistance,
            templatePoints,
            current.ContourPoints,
            template.CenterXPixel,
            template.CenterYPixel,
            fine.CenterXPixel,
            fine.CenterYPixel,
            fine.BestAngleDegrees,
            options.SubpixelTranslationRadiusPixels,
            options.SubpixelTranslationStepPixels,
            options.MaximumSampleDistancePixels);

        var bestError = refined.BestErrorPixels;
        var second = SelectSecondCandidate(fine, coarse);
        var secondError = second.ErrorPixels;
        var secondAngle = second.AngleDegrees;
        var separation = secondError - bestError;
        var score = Math.Clamp(1.0 - bestError / Math.Max(options.MaximumErrorPixels, 0.0001), 0.0, 1.0);
        if (areaDifferenceRatio > options.MaximumAreaDifferenceRatio &&
            bestError > options.MaximumErrorPixels * 0.75)
        {
            return ContourShapeMatch.Fail(
                $"{modelName}Shape匹配失败: 面积差异={areaDifferenceRatio:P1}，最大允许={options.MaximumAreaDifferenceRatio:P1}，匹配误差={bestError:F2}px。",
                areaDifferenceRatio,
                refined.BestAngleDegrees,
                bestError,
                secondError,
                separation,
                score);
        }

        if (bestError > options.MaximumErrorPixels)
        {
            return ContourShapeMatch.Fail(
                $"{modelName}Shape匹配失败: 最佳误差={bestError:F2}px，最大允许={options.MaximumErrorPixels:F2}px。",
                areaDifferenceRatio,
                refined.BestAngleDegrees,
                bestError,
                secondError,
                separation,
                score);
        }

        var requiresSeparation = options.MinimumSeparationPixels > 0.0;
        if (requiresSeparation &&
            (double.IsNaN(separation) ||
            double.IsInfinity(separation) ||
            separation < options.MinimumSeparationPixels))
        {
            if (options.AllowHalfTurnEquivalent &&
                IsHalfTurnEquivalent(refined.BestAngleDegrees, secondAngle))
            {
                var halfTurnAngle = AngleMath.NormalizeDegrees180(refined.BestAngleDegrees);
                return ContourShapeMatch.Pass(
                    modelName,
                    AngleMath.NormalizeDegrees360(halfTurnAngle),
                    halfTurnAngle,
                    refined.CenterXPixel,
                    refined.CenterYPixel,
                    bestError,
                    secondError,
                    separation,
                    score,
                    areaDifferenceRatio,
                    $"{modelName}Shape match OK: 180deg equivalent symmetry, offset={halfTurnAngle:F2}deg, error={bestError:F2}px, second={secondError:F2}px, separation={separation:F2}px, score={score:F3}.",
                    alignmentAngleOffsetDegrees: refined.BestAngleDegrees);
            }

            return ContourShapeMatch.Fail(
                $"{modelName}Shape匹配失败: 候选分离不足，最佳误差={bestError:F2}px，第二误差={secondError:F2}px，分离={separation:F2}px，最小要求={options.MinimumSeparationPixels:F2}px。",
                areaDifferenceRatio,
                refined.BestAngleDegrees,
                bestError,
                secondError,
                separation,
                score);
        }

        return ContourShapeMatch.Pass(
            modelName,
            AngleMath.NormalizeDegrees360(refined.BestAngleDegrees),
            refined.BestAngleDegrees,
            refined.CenterXPixel,
            refined.CenterYPixel,
            bestError,
            secondError,
            separation,
            score,
            areaDifferenceRatio,
            $"{modelName}Shape匹配OK: 偏移={refined.BestAngleDegrees:F2}deg，误差={bestError:F2}px，第二={secondError:F2}px，分离={separation:F2}px，分数={score:F3}，XY精修=({refined.CenterXPixel - fine.CenterXPixel:F2},{refined.CenterYPixel - fine.CenterYPixel:F2})px。");
    }

    private static (double ErrorPixels, double AngleDegrees) SelectSecondCandidate(
        ContourShapeSearchResult fine,
        ContourShapeSearchResult coarse)
    {
        if (fine.SecondErrorPixels <= coarse.SecondErrorPixels)
        {
            return (fine.SecondErrorPixels, fine.SecondAngleDegrees);
        }

        return (coarse.SecondErrorPixels, coarse.SecondAngleDegrees);
    }

    private static bool IsHalfTurnEquivalent(double bestAngleDegrees, double secondAngleDegrees)
    {
        if (double.IsNaN(secondAngleDegrees) || double.IsInfinity(secondAngleDegrees))
        {
            return false;
        }

        var delta = Math.Abs(AngleMath.NormalizeDeltaDegrees360(secondAngleDegrees, bestAngleDegrees));
        return Math.Abs(delta - 180.0) <= 15.0;
    }

    private ContourDistanceMap GetTemplateDistance(
        ContourFeatureExtraction template,
        IReadOnlyList<Point2d> templatePoints,
        string modelName)
    {
        var key = TemplateDistanceCacheKey.Create(template, templatePoints, modelName);
        lock (_templateDistanceCacheLock)
        {
            if (_templateDistanceCache.TryGetValue(key, out var cached))
            {
                return cached.Distance.Clone();
            }

            var templateDistance = BuildDistanceMap(
                templatePoints,
                template.ImageWidthPixels,
                template.ImageHeightPixels);
            TrimTemplateDistanceCacheIfNeeded();
            _templateDistanceCache[key] = new TemplateDistanceCache(key, templateDistance.Clone());
            return templateDistance;
        }
    }

    private void TrimTemplateDistanceCacheIfNeeded()
    {
        while (_templateDistanceCache.Count >= MaximumTemplateDistanceCacheEntries)
        {
            var firstKey = _templateDistanceCache.Keys.First();
            _templateDistanceCache[firstKey].Dispose();
            _templateDistanceCache.Remove(firstKey);
        }
    }

    private static ContourDistanceMap BuildDistanceMap(
        IReadOnlyList<Point2d> contourPoints,
        int imageWidthPixels,
        int imageHeightPixels)
    {
        var roi = BuildDistanceRoi(contourPoints, imageWidthPixels, imageHeightPixels);
        using var edgeSource = new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.All(0));
        var points = contourPoints
            .Select(point => new Point(
                Math.Clamp((int)Math.Round(point.X) - roi.X, 0, roi.Width - 1),
                Math.Clamp((int)Math.Round(point.Y) - roi.Y, 0, roi.Height - 1)))
            .ToArray();
        if (points.Length >= 2)
        {
            Cv2.Polylines(edgeSource, new[] { points }, isClosed: true, Scalar.White, thickness: 1);
        }

        using var inverted = new Mat();
        Cv2.Threshold(edgeSource, inverted, 1.0, 255.0, ThresholdTypes.BinaryInv);
        var distance = new Mat();
        Cv2.DistanceTransform(inverted, distance, DistanceTypes.L2, DistanceTransformMasks.Mask3);
        return new ContourDistanceMap(distance, roi.X, roi.Y);
    }

    private static Rect BuildDistanceRoi(
        IReadOnlyList<Point2d> contourPoints,
        int imageWidthPixels,
        int imageHeightPixels)
    {
        var minPointX = contourPoints.Min(point => point.X);
        var minPointY = contourPoints.Min(point => point.Y);
        var maxPointX = contourPoints.Max(point => point.X);
        var maxPointY = contourPoints.Max(point => point.Y);
        var imageRightExclusive = Math.Max(imageWidthPixels, (int)Math.Ceiling(maxPointX) + 1);
        var imageBottomExclusive = Math.Max(imageHeightPixels, (int)Math.Ceiling(maxPointY) + 1);
        var x = Math.Max(0, (int)Math.Floor(minPointX) - DistanceMapPaddingPixels);
        var y = Math.Max(0, (int)Math.Floor(minPointY) - DistanceMapPaddingPixels);
        var right = Math.Min(
            imageRightExclusive,
            Math.Max(x + 1, (int)Math.Ceiling(maxPointX) + DistanceMapPaddingPixels + 1));
        var bottom = Math.Min(
            imageBottomExclusive,
            Math.Max(y + 1, (int)Math.Ceiling(maxPointY) + DistanceMapPaddingPixels + 1));
        return new Rect(
            x,
            y,
            Math.Max(1, right - x),
            Math.Max(1, bottom - y));
    }

    private static IReadOnlyList<Point2d> DownsamplePoints(
        IReadOnlyList<Point2d> points,
        int maximumPointCount)
    {
        if (points.Count <= maximumPointCount)
        {
            return points;
        }

        var sampled = new Point2d[maximumPointCount];
        var stride = (double)points.Count / maximumPointCount;
        for (var index = 0; index < sampled.Length; index++)
        {
            sampled[index] = points[(int)Math.Floor(index * stride)];
        }

        return sampled;
    }

    private static ContourShapeSearchResult SearchPoses(
        ContourDistanceMap currentDistance,
        ContourDistanceMap templateDistance,
        IReadOnlyList<Point2d> templatePoints,
        IReadOnlyList<Point2d> currentPoints,
        double templateCenterX,
        double templateCenterY,
        double centerSeedX,
        double centerSeedY,
        double startDegrees,
        double endDegrees,
        double angleStepDegrees,
        double translationRadiusPixels,
        double translationStepPixels,
        ContourShapeMatcherOptions options)
    {
        var bestAngle = 0.0;
        var bestCenterX = centerSeedX;
        var bestCenterY = centerSeedY;
        var bestError = double.PositiveInfinity;
        var secondError = double.PositiveInfinity;
        var secondAngle = double.NaN;
        for (var angle = startDegrees; angle <= endDegrees + 0.0001; angle += angleStepDegrees)
        {
            var normalizedAngle = AngleMath.NormalizeDegrees360(angle);
            for (var yOffset = -translationRadiusPixels; yOffset <= translationRadiusPixels + 0.0001; yOffset += translationStepPixels)
            {
                for (var xOffset = -translationRadiusPixels; xOffset <= translationRadiusPixels + 0.0001; xOffset += translationStepPixels)
                {
                    var centerX = centerSeedX + xOffset;
                    var centerY = centerSeedY + yOffset;
                    var error = ScorePose(
                        currentDistance,
                        templateDistance,
                        templatePoints,
                        currentPoints,
                        templateCenterX,
                        templateCenterY,
                        centerX,
                        centerY,
                        normalizedAngle,
                        options.MaximumSampleDistancePixels);
                    if (error < bestError)
                    {
                        if (Math.Abs(AngleMath.NormalizeDeltaDegrees360(normalizedAngle, bestAngle)) >= options.AlternativeExclusionDegrees)
                        {
                            secondError = bestError;
                            secondAngle = bestAngle;
                        }

                        bestError = error;
                        bestAngle = normalizedAngle;
                        bestCenterX = centerX;
                        bestCenterY = centerY;
                    }
                    else if (Math.Abs(AngleMath.NormalizeDeltaDegrees360(normalizedAngle, bestAngle)) >= options.AlternativeExclusionDegrees &&
                        error < secondError)
                    {
                        secondError = error;
                        secondAngle = normalizedAngle;
                    }
                }
            }
        }

        return new ContourShapeSearchResult(bestAngle, bestCenterX, bestCenterY, bestError, secondError, secondAngle);
    }

    private static ContourShapeSearchResult RefineTranslationAtFixedAngle(
        ContourDistanceMap currentDistance,
        ContourDistanceMap templateDistance,
        IReadOnlyList<Point2d> templatePoints,
        IReadOnlyList<Point2d> currentPoints,
        double templateCenterX,
        double templateCenterY,
        double centerSeedX,
        double centerSeedY,
        double angleDegrees,
        double translationRadiusPixels,
        double translationStepPixels,
        double maximumSampleDistance)
    {
        if (translationRadiusPixels <= 0.0 || translationStepPixels <= 0.0)
        {
            var error = ScorePoseSubpixel(
                currentDistance,
                templateDistance,
                templatePoints,
                currentPoints,
                templateCenterX,
                templateCenterY,
                centerSeedX,
                centerSeedY,
                angleDegrees,
                maximumSampleDistance);
            return new ContourShapeSearchResult(angleDegrees, centerSeedX, centerSeedY, error, double.PositiveInfinity, double.NaN);
        }

        var bestCenterX = centerSeedX;
        var bestCenterY = centerSeedY;
        var bestError = double.PositiveInfinity;
        for (var yOffset = -translationRadiusPixels; yOffset <= translationRadiusPixels + 0.0001; yOffset += translationStepPixels)
        {
            for (var xOffset = -translationRadiusPixels; xOffset <= translationRadiusPixels + 0.0001; xOffset += translationStepPixels)
            {
                var centerX = centerSeedX + xOffset;
                var centerY = centerSeedY + yOffset;
                var error = ScorePoseSubpixel(
                    currentDistance,
                    templateDistance,
                    templatePoints,
                    currentPoints,
                    templateCenterX,
                    templateCenterY,
                    centerX,
                    centerY,
                    angleDegrees,
                    maximumSampleDistance);
                if (error < bestError)
                {
                    bestError = error;
                    bestCenterX = centerX;
                    bestCenterY = centerY;
                }
            }
        }

        return new ContourShapeSearchResult(angleDegrees, bestCenterX, bestCenterY, bestError, double.PositiveInfinity, double.NaN);
    }

    private static double ScorePose(
        ContourDistanceMap currentDistance,
        ContourDistanceMap templateDistance,
        IReadOnlyList<Point2d> templatePoints,
        IReadOnlyList<Point2d> currentPoints,
        double templateCenterX,
        double templateCenterY,
        double currentCenterX,
        double currentCenterY,
        double angleDegrees,
        double maximumSampleDistance)
    {
        var templateToCurrent = ScoreTemplateToCurrent(
            currentDistance,
            templatePoints,
            templateCenterX,
            templateCenterY,
            currentCenterX,
            currentCenterY,
            angleDegrees,
            maximumSampleDistance);
        var currentToTemplate = ScoreCurrentToTemplate(
            templateDistance,
            currentPoints,
            currentCenterX,
            currentCenterY,
            templateCenterX,
            templateCenterY,
            angleDegrees,
            maximumSampleDistance);

        return 0.5 * (templateToCurrent + currentToTemplate);
    }

    private static double ScorePoseSubpixel(
        ContourDistanceMap currentDistance,
        ContourDistanceMap templateDistance,
        IReadOnlyList<Point2d> templatePoints,
        IReadOnlyList<Point2d> currentPoints,
        double templateCenterX,
        double templateCenterY,
        double currentCenterX,
        double currentCenterY,
        double angleDegrees,
        double maximumSampleDistance)
    {
        var templateToCurrent = ScoreTemplateToCurrentSubpixel(
            currentDistance,
            templatePoints,
            templateCenterX,
            templateCenterY,
            currentCenterX,
            currentCenterY,
            angleDegrees,
            maximumSampleDistance);
        var currentToTemplate = ScoreCurrentToTemplateSubpixel(
            templateDistance,
            currentPoints,
            currentCenterX,
            currentCenterY,
            templateCenterX,
            templateCenterY,
            angleDegrees,
            maximumSampleDistance);

        return 0.5 * (templateToCurrent + currentToTemplate);
    }

    private static double ScoreTemplateToCurrent(
        ContourDistanceMap currentDistance,
        IReadOnlyList<Point2d> templatePoints,
        double templateCenterX,
        double templateCenterY,
        double currentCenterX,
        double currentCenterY,
        double angleDegrees,
        double maximumSampleDistance)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var sum = 0.0;
        var count = 0;
        for (var index = 0; index < templatePoints.Count; index++)
        {
            var point = templatePoints[index];
            var dx = point.X - templateCenterX;
            var dy = point.Y - templateCenterY;
            var x = currentCenterX + dx * cos - dy * sin;
            var y = currentCenterY + dx * sin + dy * cos;
            var ix = (int)Math.Round(x);
            var iy = (int)Math.Round(y);
            sum += currentDistance.SampleRounded(ix, iy, maximumSampleDistance);

            count++;
        }

        return count == 0 ? double.PositiveInfinity : sum / count;
    }

    private static double ScoreTemplateToCurrentSubpixel(
        ContourDistanceMap currentDistance,
        IReadOnlyList<Point2d> templatePoints,
        double templateCenterX,
        double templateCenterY,
        double currentCenterX,
        double currentCenterY,
        double angleDegrees,
        double maximumSampleDistance)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var sum = 0.0;
        var count = 0;
        foreach (var point in templatePoints)
        {
            var dx = point.X - templateCenterX;
            var dy = point.Y - templateCenterY;
            var x = currentCenterX + dx * cos - dy * sin;
            var y = currentCenterY + dx * sin + dy * cos;
            sum += currentDistance.SampleSubpixel(x, y, maximumSampleDistance);

            count++;
        }

        return count == 0 ? double.PositiveInfinity : sum / count;
    }

    private static double ScoreCurrentToTemplate(
        ContourDistanceMap templateDistance,
        IReadOnlyList<Point2d> currentPoints,
        double currentCenterX,
        double currentCenterY,
        double templateCenterX,
        double templateCenterY,
        double angleDegrees,
        double maximumSampleDistance)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var sum = 0.0;
        var count = 0;
        for (var index = 0; index < currentPoints.Count; index++)
        {
            var point = currentPoints[index];
            var dx = point.X - currentCenterX;
            var dy = point.Y - currentCenterY;
            var x = templateCenterX + dx * cos + dy * sin;
            var y = templateCenterY - dx * sin + dy * cos;
            var ix = (int)Math.Round(x);
            var iy = (int)Math.Round(y);
            sum += templateDistance.SampleRounded(ix, iy, maximumSampleDistance);

            count++;
        }

        return count == 0 ? double.PositiveInfinity : sum / count;
    }

    private static double ScoreCurrentToTemplateSubpixel(
        ContourDistanceMap templateDistance,
        IReadOnlyList<Point2d> currentPoints,
        double currentCenterX,
        double currentCenterY,
        double templateCenterX,
        double templateCenterY,
        double angleDegrees,
        double maximumSampleDistance)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var sum = 0.0;
        var count = 0;
        foreach (var point in currentPoints)
        {
            var dx = point.X - currentCenterX;
            var dy = point.Y - currentCenterY;
            var x = templateCenterX + dx * cos + dy * sin;
            var y = templateCenterY - dx * sin + dy * cos;
            sum += templateDistance.SampleSubpixel(x, y, maximumSampleDistance);

            count++;
        }

        return count == 0 ? double.PositiveInfinity : sum / count;
    }

    private static double CalculateRatioDifference(double current, double reference)
    {
        var denominator = Math.Max(Math.Abs(reference), 0.0001);
        return Math.Abs(current - reference) / denominator;
    }
}

public sealed record ContourShapeMatcherOptions(
    double MaximumErrorPixels,
    double MinimumSeparationPixels,
    double MaximumAreaDifferenceRatio,
    double AlternativeExclusionDegrees,
    double MaximumSampleDistancePixels,
    double CoarseTranslationRadiusPixels,
    double CoarseTranslationStepPixels,
    double FineTranslationRadiusPixels,
    double FineTranslationStepPixels,
    double SubpixelTranslationRadiusPixels,
    double SubpixelTranslationStepPixels,
    double NarrowAngleSearchRangeDegrees,
    double NarrowAngleFineStepDegrees,
    bool AllowHalfTurnEquivalent)
{
    public static ContourShapeMatcherOptions Default { get; } = new(
        ContourShapeMatcher.DefaultMaximumErrorPixels,
        ContourShapeMatcher.DefaultMinimumSeparationPixels,
        ContourShapeMatcher.DefaultMaximumAreaDifferenceRatio,
        AlternativeExclusionDegrees: 12.0,
        MaximumSampleDistancePixels: 30.0,
        CoarseTranslationRadiusPixels: 6.0,
        CoarseTranslationStepPixels: 3.0,
        FineTranslationRadiusPixels: 2.0,
        FineTranslationStepPixels: 1.0,
        SubpixelTranslationRadiusPixels: 0.75,
        SubpixelTranslationStepPixels: 0.25,
        NarrowAngleSearchRangeDegrees: 5.0,
        NarrowAngleFineStepDegrees: 0.25,
        AllowHalfTurnEquivalent: false);
}

public sealed record ContourShapeMatch(
    bool IsReliable,
    string ModelName,
    double ResolvedAngleDegrees,
    double AngleOffsetDegrees,
    double CenterXPixel,
    double CenterYPixel,
    double ErrorPixels,
    double AlternativeErrorPixels,
    double SeparationPixels,
    double Score,
    double AreaDifferenceRatio,
    string Message)
{
    public static ContourShapeMatch Pass(
        string modelName,
        double resolvedAngleDegrees,
        double angleOffsetDegrees,
        double centerXPixel,
        double centerYPixel,
        double errorPixels,
        double alternativeErrorPixels,
        double separationPixels,
        double score,
        double areaDifferenceRatio,
        string message)
    {
        return Pass(
            modelName,
            resolvedAngleDegrees,
            angleOffsetDegrees,
            centerXPixel,
            centerYPixel,
            errorPixels,
            alternativeErrorPixels,
            separationPixels,
            score,
            areaDifferenceRatio,
            message,
            alignmentAngleOffsetDegrees: angleOffsetDegrees);
    }

    public static ContourShapeMatch Pass(
        string modelName,
        double resolvedAngleDegrees,
        double angleOffsetDegrees,
        double centerXPixel,
        double centerYPixel,
        double errorPixels,
        double alternativeErrorPixels,
        double separationPixels,
        double score,
        double areaDifferenceRatio,
        string message,
        double alignmentAngleOffsetDegrees)
    {
        return new ContourShapeMatch(
            true,
            modelName,
            resolvedAngleDegrees,
            angleOffsetDegrees,
            centerXPixel,
            centerYPixel,
            errorPixels,
            alternativeErrorPixels,
            separationPixels,
            score,
            areaDifferenceRatio,
            message)
        {
            AlignmentAngleOffsetDegrees = alignmentAngleOffsetDegrees
        };
    }

    public static ContourShapeMatch Fail(
        string message,
        double areaDifferenceRatio = 0.0,
        double angleOffsetDegrees = 0.0,
        double errorPixels = double.PositiveInfinity,
        double alternativeErrorPixels = double.PositiveInfinity,
        double separationPixels = 0.0,
        double score = 0.0)
    {
        return new ContourShapeMatch(
            false,
            string.Empty,
            0.0,
            angleOffsetDegrees,
            0.0,
            0.0,
            errorPixels,
            alternativeErrorPixels,
            separationPixels,
            score,
            areaDifferenceRatio,
            message);
    }

    public static ContourShapeMatch Unavailable(string message)
    {
        return Fail(message);
    }

    public double AlignmentAngleOffsetDegrees { get; init; } = AngleOffsetDegrees;
}

internal sealed record ContourShapeSearchResult(
    double BestAngleDegrees,
    double CenterXPixel,
    double CenterYPixel,
    double BestErrorPixels,
    double SecondErrorPixels,
    double SecondAngleDegrees);

internal sealed class ContourDistanceMap : IDisposable
{
    public ContourDistanceMap(Mat distance, int offsetX, int offsetY)
    {
        Distance = distance;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public Mat Distance { get; }

    public int OffsetX { get; }

    public int OffsetY { get; }

    public ContourDistanceMap Clone()
    {
        return new ContourDistanceMap(Distance.Clone(), OffsetX, OffsetY);
    }

    public double SampleRounded(int imageX, int imageY, double maximumSampleDistance)
    {
        var x = imageX - OffsetX;
        var y = imageY - OffsetY;
        if ((uint)x >= (uint)Distance.Width || (uint)y >= (uint)Distance.Height)
        {
            return maximumSampleDistance;
        }

        return Math.Min(Distance.At<float>(y, x), maximumSampleDistance);
    }

    public double SampleSubpixel(double imageX, double imageY, double maximumSampleDistance)
    {
        var x = imageX - OffsetX;
        var y = imageY - OffsetY;
        if (x < 0.0 || y < 0.0 || x > Distance.Width - 1 || y > Distance.Height - 1)
        {
            return maximumSampleDistance;
        }

        var x0 = Math.Clamp((int)Math.Floor(x), 0, Distance.Width - 1);
        var y0 = Math.Clamp((int)Math.Floor(y), 0, Distance.Height - 1);
        var x1 = Math.Min(x0 + 1, Distance.Width - 1);
        var y1 = Math.Min(y0 + 1, Distance.Height - 1);
        var wx = x - x0;
        var wy = y - y0;
        var top = Distance.At<float>(y0, x0) * (1.0 - wx) + Distance.At<float>(y0, x1) * wx;
        var bottom = Distance.At<float>(y1, x0) * (1.0 - wx) + Distance.At<float>(y1, x1) * wx;
        return Math.Min(top * (1.0 - wy) + bottom * wy, maximumSampleDistance);
    }

    public void Dispose()
    {
        Distance.Dispose();
    }
}

internal sealed record TemplateDistanceCacheKey(
    string ModelName,
    int ImageWidthPixels,
    int ImageHeightPixels,
    int PointCount,
    double CenterXPixel,
    double CenterYPixel,
    double AreaPixels,
    double FirstPointX,
    double FirstPointY,
    double MiddlePointX,
    double MiddlePointY,
    double LastPointX,
    double LastPointY)
{
    public static TemplateDistanceCacheKey Create(
        ContourFeatureExtraction template,
        IReadOnlyList<Point2d> points,
        string modelName)
    {
        var first = points.Count > 0 ? points[0] : default;
        var middle = points.Count > 0 ? points[points.Count / 2] : default;
        var last = points.Count > 0 ? points[^1] : default;
        return new TemplateDistanceCacheKey(
            modelName,
            template.ImageWidthPixels,
            template.ImageHeightPixels,
            points.Count,
            Math.Round(template.CenterXPixel, 3),
            Math.Round(template.CenterYPixel, 3),
            Math.Round(template.AreaPixels, 3),
            Math.Round(first.X, 3),
            Math.Round(first.Y, 3),
            Math.Round(middle.X, 3),
            Math.Round(middle.Y, 3),
            Math.Round(last.X, 3),
            Math.Round(last.Y, 3));
    }
}

internal sealed class TemplateDistanceCache : IDisposable
{
    public TemplateDistanceCache(TemplateDistanceCacheKey key, ContourDistanceMap distance)
    {
        Key = key;
        Distance = distance;
    }

    public TemplateDistanceCacheKey Key { get; }

    public ContourDistanceMap Distance { get; }

    public void Dispose()
    {
        Distance.Dispose();
    }
}
