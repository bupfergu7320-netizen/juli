using System.Globalization;
using JuliMvs.Core;
using JuliMvs.Core.Vision;
using OpenCvSharp;

namespace JuliMvs.Vision;

public sealed class PoseInvariantTemplateMatcher
{
    private const int MinimumPatchPixels = 96;
    private const double PatchPaddingRatio = 1.55;
    private const double EdgeDistanceScalePixels = 5.0;
    private const int EdgeRingSampleCount = 360;
    private const double EdgeRingInnerOffsetPixels = 4.0;
    private const double EdgeRingOuterOffsetPixels = 4.0;
    private const double EdgeRingContrastScale = 22.0;

    private readonly object _templateSimilarityModelSync = new();
    private TemplateSimilarityModel? _cachedTemplateSimilarityModel;

    private sealed class TemplateSimilarityModel : IDisposable
    {
        public TemplateSimilarityModel(
            Guid templateId,
            string imagePath,
            long imageLength,
            long imageLastWriteTimeUtcTicks,
            string lensDistortionCacheKey,
            Size templateSize,
            Mat templateMask,
            int templateMaskPixels,
            Point2d templateAnchor,
            Rect patchRect,
            Mat templatePatch,
            Point[]? templatePatchContour,
            int templateEdgePixels,
            Mat templateEdgeDistance)
        {
            TemplateId = templateId;
            ImagePath = imagePath;
            ImageLength = imageLength;
            ImageLastWriteTimeUtcTicks = imageLastWriteTimeUtcTicks;
            LensDistortionCacheKey = lensDistortionCacheKey;
            TemplateSize = templateSize;
            TemplateMask = templateMask;
            TemplateMaskPixels = templateMaskPixels;
            TemplateAnchor = templateAnchor;
            PatchRect = patchRect;
            TemplatePatch = templatePatch;
            TemplatePatchContour = templatePatchContour;
            TemplateEdgePixels = templateEdgePixels;
            TemplateEdgeDistance = templateEdgeDistance;
        }

        public Guid TemplateId { get; }

        public string ImagePath { get; }

        public long ImageLength { get; }

        public long ImageLastWriteTimeUtcTicks { get; }

        public string LensDistortionCacheKey { get; }

        public Size TemplateSize { get; }

        public Mat TemplateMask { get; }

        public int TemplateMaskPixels { get; }

        public Point2d TemplateAnchor { get; }

        public Rect PatchRect { get; }

        public Mat TemplatePatch { get; }

        public Point[]? TemplatePatchContour { get; }

        public int TemplateEdgePixels { get; }

        public Mat TemplateEdgeDistance { get; }

        public bool Matches(PartTemplate template, FileInfo imageFile, string lensDistortionCacheKey)
        {
            return TemplateId == template.Id &&
                string.Equals(ImagePath, imageFile.FullName, StringComparison.OrdinalIgnoreCase) &&
                ImageLength == imageFile.Length &&
                ImageLastWriteTimeUtcTicks == imageFile.LastWriteTimeUtc.Ticks &&
                string.Equals(LensDistortionCacheKey, lensDistortionCacheKey, StringComparison.Ordinal);
        }

        public void Dispose()
        {
            TemplateMask.Dispose();
            TemplatePatch.Dispose();
            TemplateEdgeDistance.Dispose();
        }
    }

    private sealed record SimilarityScores(
        double FinalScore,
        double SizeScore,
        double ShapeScore,
        double MaskIoU,
        double EdgeDistanceScore,
        string AlignmentSource);

    private sealed record FixedOverlayMetrics(
        double Score,
        double MaskIoU,
        double MismatchRatio,
        double TemplateOnlyRatio,
        double CurrentOnlyRatio);

    private sealed record EdgeRingSample(double Gradient, double Contrast);

    public TemplateSimilarityResult? TryCompare(
        Mat preparedCurrentImage,
        PartDetection currentDetection,
        PartTemplate template,
        VisionParameters parameters,
        double currentAngleDegrees)
    {
        ArgumentNullException.ThrowIfNull(preparedCurrentImage);
        ArgumentNullException.ThrowIfNull(currentDetection);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(parameters);

        if (string.IsNullOrWhiteSpace(template.ImagePath))
        {
            return null;
        }

        var templateImageFile = new FileInfo(template.ImagePath);
        if (!templateImageFile.Exists)
        {
            return null;
        }

        using var currentMask = BuildCurrentMask(preparedCurrentImage.Size(), currentDetection);
        if (Cv2.CountNonZero(currentMask) == 0)
        {
            return null;
        }

        lock (_templateSimilarityModelSync)
        {
            var model = GetOrBuildTemplateSimilarityModel(template, parameters, templateImageFile);
            if (model is null || model.TemplateMaskPixels == 0)
            {
                return null;
            }

            var currentAnchor = CalculateMaskCentroid(
                currentMask,
                new Point2d(currentDetection.CenterXPixel, currentDetection.CenterYPixel));
            var sizeScore = CalculateSizeScore(currentDetection, template);
            var angleCandidates = BuildAlignmentAngleCandidates(currentAngleDegrees, template.ReferenceAngleDegrees);
            var scores = angleCandidates
                .Select(candidate => CalculateSimilarityScores(
                    currentMask,
                    model.TemplatePatch,
                    model.PatchRect,
                    model.TemplatePatchContour,
                    model.TemplateEdgePixels,
                    model.TemplateEdgeDistance,
                    currentAnchor,
                    model.TemplateAnchor,
                    candidate.CurrentAngleDegrees,
                    candidate.TemplateAngleDegrees,
                    sizeScore,
                    candidate.Source))
                .OrderByDescending(candidate => candidate.FinalScore)
                .First();
            var finalScore = scores.FinalScore;
            var isReliable = scores.MaskIoU > 0.0001 || scores.ShapeScore > 0.0001 || scores.EdgeDistanceScore > 0.0001;
            var isSamePart = isReliable && finalScore >= parameters.ShapeScoreThreshold;
            var message =
                $"位姿无关匹配分数={finalScore:F3}, 尺寸={scores.SizeScore:F3}, 轮廓={scores.ShapeScore:F3}, " +
                $"掩膜重合={scores.MaskIoU:F3}, 边缘={scores.EdgeDistanceScore:F3}, 阈值={parameters.ShapeScoreThreshold:F3}, " +
                $"对齐方式={scores.AlignmentSource}";

            return new TemplateSimilarityResult(
                finalScore,
                scores.SizeScore,
                scores.ShapeScore,
                scores.MaskIoU,
                scores.EdgeDistanceScore,
                isSamePart,
                isReliable,
                message);
        }
    }

    public bool Warmup(PartTemplate template, VisionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(parameters);

        if (string.IsNullOrWhiteSpace(template.ImagePath))
        {
            return false;
        }

        var templateImageFile = new FileInfo(template.ImagePath);
        if (!templateImageFile.Exists)
        {
            return false;
        }

        lock (_templateSimilarityModelSync)
        {
            var model = GetOrBuildTemplateSimilarityModel(template, parameters, templateImageFile);
            return model is not null && model.TemplateMaskPixels > 0;
        }
    }

    public FrontBackDebugResult? CheckFrontBackDebug(
        Mat preparedCurrentImage,
        PartDetection currentDetection,
        PartTemplate template,
        VisionParameters parameters,
        double currentAngleDegrees,
        double? mirroredAngleOffsetDegrees = null,
        string? fixedOverlayDiagnosticPath = null)
    {
        ArgumentNullException.ThrowIfNull(preparedCurrentImage);
        ArgumentNullException.ThrowIfNull(currentDetection);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(parameters);

        if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
        {
            return null;
        }

        using var templateImage = Cv2.ImRead(template.ImagePath, ImreadModes.Color);
        if (templateImage.Empty())
        {
            return null;
        }

        using var preparedTemplate = PrepareImage(templateImage, parameters);
        using var templateMask = BuildTemplateMask(preparedTemplate, template);
        using var currentMask = BuildCurrentMask(preparedCurrentImage.Size(), currentDetection);
        if (Cv2.CountNonZero(templateMask) == 0 || Cv2.CountNonZero(currentMask) == 0)
        {
            return null;
        }

        var templateAnchor = CalculateMaskCentroid(
            templateMask,
            new Point2d(template.ReferenceCenterXPixel, template.ReferenceCenterYPixel));
        var currentAnchor = CalculateMaskCentroid(
            currentMask,
            new Point2d(currentDetection.CenterXPixel, currentDetection.CenterYPixel));
        var patchRect = BuildTemplatePatchRect(template, templateAnchor, preparedTemplate.Width, preparedTemplate.Height);
        using var templatePatch = new Mat(templateMask, patchRect).Clone();
        var templatePatchContour = FindLargestContour(templatePatch);
        using var templateEdgeDistance = BuildTemplateEdgeDistance(templatePatch, out var templateEdgePixels);
        var sizeScore = CalculateSizeScore(currentDetection, template);
        var sameAngleOverlay = CalculateSameAngleOverlayDebug(
            currentMask,
            templatePatch,
            patchRect,
            templatePatchContour,
            templateEdgePixels,
            templateEdgeDistance,
            currentDetection,
            template,
            currentAnchor,
            templateAnchor,
            currentAngleDegrees,
            template.ReferenceAngleDegrees,
            sizeScore);
        var fixedAngleOverlay = CalculateFixedAngleOverlayDebug(
            currentMask,
            preparedTemplate.Size(),
            templatePatch,
            patchRect,
            currentDetection,
            template,
            currentAnchor,
            templateAnchor,
            currentAngleDegrees,
            mirroredAngleOffsetDegrees,
            fixedOverlayDiagnosticPath);
        var angleCandidates = BuildAlignmentAngleCandidates(currentAngleDegrees, template.ReferenceAngleDegrees);
        var frontScores = angleCandidates
            .Select(candidate => CalculateSimilarityScores(
                currentMask,
                templatePatch,
                patchRect,
                templatePatchContour,
                templateEdgePixels,
                templateEdgeDistance,
                currentAnchor,
                templateAnchor,
                candidate.CurrentAngleDegrees,
                candidate.TemplateAngleDegrees,
                sizeScore,
                candidate.Source))
            .OrderByDescending(candidate => candidate.FinalScore)
            .First();

        using var backTemplateMask = BuildOppositeFaceHypothesisMask(templateMask, templateAnchor);
        using var backTemplatePatch = new Mat(backTemplateMask, patchRect).Clone();
        var backTemplatePatchContour = FindLargestContour(backTemplatePatch);
        using var backTemplateEdgeDistance = BuildTemplateEdgeDistance(backTemplatePatch, out var backTemplateEdgePixels);
        var backScores = angleCandidates
            .Select(candidate => CalculateSimilarityScores(
                currentMask,
                backTemplatePatch,
                patchRect,
                backTemplatePatchContour,
                backTemplateEdgePixels,
                backTemplateEdgeDistance,
                currentAnchor,
                templateAnchor,
                currentAngleDegrees: -candidate.CurrentAngleDegrees,
                candidate.TemplateAngleDegrees,
                sizeScore,
                $"{candidate.Source}+front-back-hypothesis"))
            .OrderByDescending(candidate => candidate.FinalScore)
            .First();

        var difference = frontScores.FinalScore - backScores.FinalScore;
        const double minimumScore = 0.55;
        const double reliableMargin = 0.08;
        var isReliable = Math.Max(frontScores.FinalScore, backScores.FinalScore) >= minimumScore &&
            Math.Abs(difference) >= reliableMargin;
        var suggestedDecision = isReliable
            ? difference > 0.0 ? FrontBackDebugDecision.Front : FrontBackDebugDecision.Back
            : FrontBackDebugDecision.Uncertain;
        var message =
            $"正反面调试: 正面={frontScores.FinalScore:F3}, 反面={backScores.FinalScore:F3}, " +
            $"分差(正面-反面)={difference:F3}, 可靠={isReliable}, 最低分={minimumScore:F3}, 最小分差={reliableMargin:F3}。 " +
            "此结果只显示，不参与自动NG或PLC输出。";

        var edgeRing = CalculateEdgeRingFaceDebug(
            preparedTemplate,
            templateMask,
            preparedCurrentImage,
            currentMask,
            templateAnchor,
            currentAnchor);

        return new FrontBackDebugResult(
            frontScores.FinalScore,
            backScores.FinalScore,
            difference,
            isReliable,
            suggestedDecision,
            frontScores.AlignmentSource,
            backScores.AlignmentSource,
            message)
        {
            EdgeRing = edgeRing,
            SameAngleOverlay = sameAngleOverlay,
            FixedAngleOverlay = fixedAngleOverlay
        };
    }

    private static FixedAngleOverlayDebugResult CalculateFixedAngleOverlayDebug(
        Mat currentMask,
        Size templateSize,
        Mat templatePatch,
        Rect patchRect,
        PartDetection currentDetection,
        PartTemplate template,
        Point2d currentAnchor,
        Point2d templateAnchor,
        double currentAngleDegrees,
        double? mirroredAngleOffsetDegrees,
        string? diagnosticImagePath)
    {
        using var centerOnlyMask = AlignCurrentMaskCenterOnly(
            currentMask,
            templateSize,
            currentAnchor,
            templateAnchor);
        var centerOnly = BuildFixedOverlayVariant(
            "CenterOnly",
            centerOnlyMask,
            templatePatch,
            patchRect,
            currentAngleDegrees,
            template.ReferenceAngleDegrees,
            "translate-center-only");

        using var resolvedMask = AlignCurrentMaskToTemplate(
            currentMask,
            templateSize,
            currentDetection,
            template,
            currentAnchor,
            templateAnchor,
            currentAngleDegrees,
            template.ReferenceAngleDegrees);
        var resolved = BuildFixedOverlayVariant(
            "ResolvedAngle",
            resolvedMask,
            templatePatch,
            patchRect,
            currentAngleDegrees,
            template.ReferenceAngleDegrees,
            "resolved-angle");

        FixedAngleOverlayVariantDebugResult? mirror = null;
        Mat? mirrorMask = null;
        if (mirroredAngleOffsetDegrees is { } mirrorOffset)
        {
            var mirrorAngle = AngleMath.NormalizeDegrees360(template.ReferenceAngleDegrees + mirrorOffset);
            mirrorMask = AlignCurrentMaskToTemplate(
                currentMask,
                templateSize,
                currentDetection,
                template,
                currentAnchor,
                templateAnchor,
                mirrorAngle,
                template.ReferenceAngleDegrees);
            mirror = BuildFixedOverlayVariant(
                "MirrorAngle",
                mirrorMask,
                templatePatch,
                patchRect,
                mirrorAngle,
                template.ReferenceAngleDegrees,
                "mirror-contour-angle");
        }

        string? savedPath = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(diagnosticImagePath))
            {
                SaveFixedOverlayDiagnosticImage(
                    templatePatch,
                    patchRect,
                    [
                        ("CenterOnly", centerOnlyMask),
                        ("ResolvedAngle", resolvedMask),
                        ("MirrorAngle", mirrorMask)
                    ],
                    diagnosticImagePath);
                savedPath = diagnosticImagePath;
            }
        }
        finally
        {
            mirrorMask?.Dispose();
        }

        var message =
            $"固定角度叠放调试: 仅中心对齐差异={centerOnly.MismatchRatio:F3}, " +
            $"识别角度对齐差异={resolved.MismatchRatio:F3}" +
            (mirror is null ? string.Empty : $", 镜像角度对齐差异={mirror.MismatchRatio:F3}") +
            "。此结果只显示，不参与自动NG、PLC输出或XYR计算。";

        return new FixedAngleOverlayDebugResult(
            centerOnly,
            resolved,
            mirror,
            savedPath,
            message);
    }

    private static FixedAngleOverlayVariantDebugResult BuildFixedOverlayVariant(
        string name,
        Mat alignedCurrentMask,
        Mat templatePatch,
        Rect patchRect,
        double currentAngleDegrees,
        double templateAngleDegrees,
        string alignment)
    {
        using var currentPatch = new Mat(alignedCurrentMask, patchRect).Clone();
        var metrics = CalculateFixedOverlayMetrics(templatePatch, currentPatch);
        return new FixedAngleOverlayVariantDebugResult(
            name,
            metrics.Score,
            metrics.MaskIoU,
            metrics.MismatchRatio,
            metrics.TemplateOnlyRatio,
            metrics.CurrentOnlyRatio,
            currentAngleDegrees,
            templateAngleDegrees,
            alignment);
    }

    private static SameAngleOverlayDebugResult CalculateSameAngleOverlayDebug(
        Mat currentMask,
        Mat templatePatch,
        Rect patchRect,
        Point[]? templatePatchContour,
        int templateEdgePixels,
        Mat templateEdgeDistance,
        PartDetection currentDetection,
        PartTemplate template,
        Point2d currentAnchor,
        Point2d templateAnchor,
        double currentAngleDegrees,
        double templateAngleDegrees,
        double sizeScore)
    {
        var scores = CalculateSimilarityScores(
            currentMask,
            templatePatch,
            patchRect,
            templatePatchContour,
            templateEdgePixels,
            templateEdgeDistance,
            currentAnchor,
            templateAnchor,
            currentAngleDegrees,
            templateAngleDegrees,
            sizeScore,
            "same-angle-overlay");
        const double minimumScore = 0.72;
        const double minimumMaskIoU = 0.45;
        var isFrontLike = scores.FinalScore >= minimumScore && scores.MaskIoU >= minimumMaskIoU;
        var suggestedDecision = isFrontLike ? FrontBackDebugDecision.Front : FrontBackDebugDecision.Back;
        var message =
            $"同角度叠放调试: 分数={scores.FinalScore:F3}, 掩膜重合={scores.MaskIoU:F3}, " +
            $"轮廓={scores.ShapeScore:F3}, 边缘={scores.EdgeDistanceScore:F3}, 尺寸={scores.SizeScore:F3}, " +
            $"判定参考: 分数>={minimumScore:F3} 且 掩膜重合>={minimumMaskIoU:F3} 视为同角度可对齐。此结果只显示，不参与自动NG或PLC输出。";

        return new SameAngleOverlayDebugResult(
            scores.FinalScore,
            scores.SizeScore,
            scores.ShapeScore,
            scores.MaskIoU,
            scores.EdgeDistanceScore,
            true,
            suggestedDecision,
            scores.AlignmentSource,
            message);
    }

    private static EdgeRingFaceDebugResult? CalculateEdgeRingFaceDebug(
        Mat preparedTemplateImage,
        Mat templateMask,
        Mat preparedCurrentImage,
        Mat currentMask,
        Point2d templateAnchor,
        Point2d currentAnchor)
    {
        var templateContour = FindLargestContour(templateMask);
        var currentContour = FindLargestContour(currentMask);
        if (templateContour is null || currentContour is null)
        {
            return null;
        }

        using var templateGray = ToGray(preparedTemplateImage);
        using var currentGray = ToGray(preparedCurrentImage);
        var templateSamples = SampleEdgeRing(templateGray, templateContour, templateAnchor);
        var currentSamples = SampleEdgeRing(currentGray, currentContour, currentAnchor);
        var count = Math.Min(templateSamples.Count, currentSamples.Count);
        if (count < 24)
        {
            return null;
        }

        var frontAgreementSum = 0.0;
        var backAgreementSum = 0.0;
        var stableCount = 0;
        var templateContrastSum = 0.0;
        var currentContrastSum = 0.0;
        for (var index = 0; index < count; index++)
        {
            var templateSample = templateSamples[index];
            var currentSample = currentSamples[index];
            var contrast = Math.Min(templateSample.Contrast, currentSample.Contrast);
            var weight = Math.Clamp(contrast / EdgeRingContrastScale, 0.0, 1.0);
            if (weight >= 0.35)
            {
                stableCount++;
            }

            frontAgreementSum += weight * DirectionAgreement(templateSample.Gradient, currentSample.Gradient);
            backAgreementSum += weight * DirectionAgreement(templateSample.Gradient, -currentSample.Gradient);
            templateContrastSum += templateSample.Contrast;
            currentContrastSum += currentSample.Contrast;
        }

        var frontScore = Math.Clamp(frontAgreementSum / count, 0.0, 1.0);
        var backScore = Math.Clamp(backAgreementSum / count, 0.0, 1.0);
        var difference = frontScore - backScore;
        var stableRatio = stableCount / (double)count;
        const double reliableMargin = 0.15;
        const double minimumStableRatio = 0.35;
        var isReliable = stableRatio >= minimumStableRatio && Math.Abs(difference) >= reliableMargin;
        var suggestedDecision = isReliable
            ? difference > 0.0 ? FrontBackDebugDecision.Front : FrontBackDebugDecision.Back
            : FrontBackDebugDecision.Uncertain;
        var message =
            $"边缘环带调试: 正面={frontScore:F3}, 反面={backScore:F3}, 分差={difference:F3}, " +
            $"稳定边缘比例={stableRatio:F3}, 样本={count}, 可靠={isReliable}。此结果只显示，不参与自动NG或PLC输出。";

        return new EdgeRingFaceDebugResult(
            frontScore,
            backScore,
            difference,
            isReliable,
            suggestedDecision,
            count,
            stableRatio,
            frontScore,
            templateContrastSum / count,
            currentContrastSum / count,
            message);
    }

    private static List<EdgeRingSample> SampleEdgeRing(Mat gray, Point[] contour, Point2d anchor)
    {
        var ordered = contour
            .Select(point => new Point2d(point.X, point.Y))
            .OrderBy(point => Math.Atan2(point.Y - anchor.Y, point.X - anchor.X))
            .ToArray();
        var samples = new List<EdgeRingSample>(EdgeRingSampleCount);
        if (ordered.Length < 3)
        {
            return samples;
        }

        for (var index = 0; index < EdgeRingSampleCount; index++)
        {
            var angle = -Math.PI + (2.0 * Math.PI * index / EdgeRingSampleCount);
            var point = ordered
                .OrderBy(candidate =>
                {
                    var candidateAngle = Math.Atan2(candidate.Y - anchor.Y, candidate.X - anchor.X);
                    return Math.Abs(NormalizeRadians(candidateAngle - angle));
                })
                .First();
            var normalX = point.X - anchor.X;
            var normalY = point.Y - anchor.Y;
            var length = Math.Sqrt((normalX * normalX) + (normalY * normalY));
            if (length < 0.0001)
            {
                continue;
            }

            normalX /= length;
            normalY /= length;
            var inner = SampleGray(gray, point.X - normalX * EdgeRingInnerOffsetPixels, point.Y - normalY * EdgeRingInnerOffsetPixels);
            var outer = SampleGray(gray, point.X + normalX * EdgeRingOuterOffsetPixels, point.Y + normalY * EdgeRingOuterOffsetPixels);
            if (inner is null || outer is null)
            {
                continue;
            }

            var gradient = inner.Value - outer.Value;
            samples.Add(new EdgeRingSample(gradient, Math.Abs(gradient)));
        }

        return samples;
    }

    private static double? SampleGray(Mat gray, double x, double y)
    {
        var ix = (int)Math.Round(x);
        var iy = (int)Math.Round(y);
        if (ix < 0 || iy < 0 || ix >= gray.Width || iy >= gray.Height)
        {
            return null;
        }

        return gray.At<byte>(iy, ix);
    }

    private static double DirectionAgreement(double templateGradient, double currentGradient)
    {
        var product = templateGradient * currentGradient;
        if (Math.Abs(product) < 0.0001)
        {
            return 0.5;
        }

        return product > 0.0 ? 1.0 : 0.0;
    }

    private static double NormalizeRadians(double radians)
    {
        while (radians <= -Math.PI)
        {
            radians += Math.PI * 2.0;
        }

        while (radians > Math.PI)
        {
            radians -= Math.PI * 2.0;
        }

        return radians;
    }

    private static SimilarityScores CalculateSimilarityScores(
        Mat currentMask,
        Mat templatePatch,
        Rect patchRect,
        Point[]? templatePatchContour,
        int templateEdgePixels,
        Mat templateEdgeDistance,
        Point2d currentAnchor,
        Point2d templateAnchor,
        double currentAngleDegrees,
        double templateAngleDegrees,
        double sizeScore,
        string alignmentSource)
    {
        using var currentPatch = AlignCurrentMaskPatchToTemplate(
            currentMask,
            patchRect,
            currentAnchor,
            templateAnchor,
            currentAngleDegrees,
            templateAngleDegrees);

        var maskIoU = CalculateMaskIoU(templatePatch, currentPatch);
        var shapeScore = CalculateShapeScore(templatePatchContour, currentPatch);
        var edgeDistanceScore = CalculateEdgeDistanceScore(templateEdgePixels, templateEdgeDistance, currentPatch);
        var poseScore = Math.Clamp(
            (sizeScore * 0.25) +
            (shapeScore * 0.25) +
            (maskIoU * 0.30) +
            (edgeDistanceScore * 0.20),
            0.0,
            1.0);
        var shapeDominantScore = Math.Clamp(
            (sizeScore * 0.35) +
            (shapeScore * 0.45) +
            (maskIoU * 0.15) +
            (edgeDistanceScore * 0.05),
            0.0,
            1.0);

        return new SimilarityScores(
            Math.Max(poseScore, shapeDominantScore),
            sizeScore,
            shapeScore,
            maskIoU,
            edgeDistanceScore,
            alignmentSource);
    }

    private TemplateSimilarityModel? GetOrBuildTemplateSimilarityModel(
        PartTemplate template,
        VisionParameters parameters,
        FileInfo templateImageFile)
    {
        var lensDistortionCacheKey = BuildLensDistortionCacheKey(parameters.LensDistortionCalibration);
        if (_cachedTemplateSimilarityModel is { } cached &&
            cached.Matches(template, templateImageFile, lensDistortionCacheKey))
        {
            return cached;
        }

        _cachedTemplateSimilarityModel?.Dispose();
        _cachedTemplateSimilarityModel = null;

        using var templateImage = Cv2.ImRead(templateImageFile.FullName, ImreadModes.Color);
        if (templateImage.Empty())
        {
            return null;
        }

        using var preparedTemplate = PrepareImage(templateImage, parameters);
        var templateMask = BuildTemplateMask(preparedTemplate, template);
        var templateMaskPixels = Cv2.CountNonZero(templateMask);
        var templateAnchor = CalculateMaskCentroid(
            templateMask,
            new Point2d(template.ReferenceCenterXPixel, template.ReferenceCenterYPixel));
        var patchRect = BuildTemplatePatchRect(template, templateAnchor, preparedTemplate.Width, preparedTemplate.Height);
        var templatePatch = new Mat(templateMask, patchRect).Clone();
        var templatePatchContour = FindLargestContour(templatePatch);
        var templateEdgeDistance = BuildTemplateEdgeDistance(templatePatch, out var templateEdgePixels);

        var model = new TemplateSimilarityModel(
            template.Id,
            templateImageFile.FullName,
            templateImageFile.Length,
            templateImageFile.LastWriteTimeUtc.Ticks,
            lensDistortionCacheKey,
            preparedTemplate.Size(),
            templateMask,
            templateMaskPixels,
            templateAnchor,
            patchRect,
            templatePatch,
            templatePatchContour,
            templateEdgePixels,
            templateEdgeDistance);
        _cachedTemplateSimilarityModel = model;
        return model;
    }

    private static string BuildLensDistortionCacheKey(LensDistortionCalibration calibration)
    {
        if (!calibration.Enabled)
        {
            return "disabled";
        }

        var matrix = string.Join(
            ",",
            calibration.CameraMatrix.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
        var distortion = string.Join(
            ",",
            calibration.DistortionCoefficients.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
        return string.Join(
            "|",
            calibration.CalibrationId,
            calibration.ImageWidth.ToString(CultureInfo.InvariantCulture),
            calibration.ImageHeight.ToString(CultureInfo.InvariantCulture),
            matrix,
            distortion);
    }

    private static Mat PrepareImage(Mat image, VisionParameters parameters)
    {
        if (!parameters.LensDistortionCalibration.CanApplyTo(image.Width, image.Height))
        {
            return image.Clone();
        }

        using var cameraMatrix = Mat.FromArray(new[,]
        {
            { parameters.LensDistortionCalibration.CameraMatrix[0], parameters.LensDistortionCalibration.CameraMatrix[1], parameters.LensDistortionCalibration.CameraMatrix[2] },
            { parameters.LensDistortionCalibration.CameraMatrix[3], parameters.LensDistortionCalibration.CameraMatrix[4], parameters.LensDistortionCalibration.CameraMatrix[5] },
            { parameters.LensDistortionCalibration.CameraMatrix[6], parameters.LensDistortionCalibration.CameraMatrix[7], parameters.LensDistortionCalibration.CameraMatrix[8] }
        });
        using var distortion = Mat.FromArray(parameters.LensDistortionCalibration.DistortionCoefficients);
        var corrected = new Mat();
        Cv2.Undistort(image, corrected, cameraMatrix, distortion);
        return corrected;
    }

    private static Mat BuildTemplateMask(Mat preparedTemplate, PartTemplate template)
    {
        using var gray = ToGray(preparedTemplate);
        using var blurred = Blur(gray);
        using var binary = Threshold(blurred);
        using var inverted = new Mat();
        Cv2.BitwiseNot(binary, inverted);

        var binaryMask = SelectTemplateMask(binary, template);
        var invertedMask = SelectTemplateMask(inverted, template);
        var binaryScore = CalculateMaskAreaScore(binaryMask, template.AreaPixels);
        var invertedScore = CalculateMaskAreaScore(invertedMask, template.AreaPixels);
        if (invertedScore > binaryScore)
        {
            binaryMask.Dispose();
            return invertedMask;
        }

        invertedMask.Dispose();
        return binaryMask;
    }

    private static Mat BuildCurrentMask(Size imageSize, PartDetection detection)
    {
        var mask = new Mat(imageSize, MatType.CV_8UC1, Scalar.Black);
        var contour = detection.Contour
            .Select(point => new Point(point.X + detection.Offset.X, point.Y + detection.Offset.Y))
            .ToArray();
        Cv2.DrawContours(mask, [contour], -1, Scalar.White, -1);
        return mask;
    }

    private static Mat BuildOppositeFaceHypothesisMask(Mat frontTemplateMask, Point2d templateAnchor)
    {
        using var transform = Mat.FromArray(new[,]
        {
            { -1.0, 0.0, templateAnchor.X * 2.0 },
            { 0.0, 1.0, 0.0 }
        });
        var backMask = new Mat(frontTemplateMask.Size(), MatType.CV_8UC1, Scalar.Black);
        Cv2.WarpAffine(
            frontTemplateMask,
            backMask,
            transform,
            frontTemplateMask.Size(),
            InterpolationFlags.Nearest,
            BorderTypes.Constant,
            Scalar.Black);
        return backMask;
    }

    private static Mat AlignCurrentMaskPatchToTemplate(
        Mat currentMask,
        Rect patchRect,
        Point2d currentAnchor,
        Point2d templateAnchor,
        double currentAngleDegrees,
        double templateAngleDegrees)
    {
        var transformValues = BuildCurrentToTemplateAffineValues(
            currentAnchor,
            templateAnchor,
            currentAngleDegrees,
            templateAngleDegrees);
        transformValues[2] -= patchRect.X;
        transformValues[5] -= patchRect.Y;

        using var transform = Mat.FromArray(new[,]
        {
            { transformValues[0], transformValues[1], transformValues[2] },
            { transformValues[3], transformValues[4], transformValues[5] }
        });
        var patch = new Mat(patchRect.Size, MatType.CV_8UC1, Scalar.Black);
        Cv2.WarpAffine(
            currentMask,
            patch,
            transform,
            patchRect.Size,
            InterpolationFlags.Nearest,
            BorderTypes.Constant,
            Scalar.Black);
        return patch;
    }

    private static Mat AlignCurrentMaskToTemplate(
        Mat currentMask,
        Size templateSize,
        PartDetection currentDetection,
        PartTemplate template,
        Point2d currentAnchor,
        Point2d templateAnchor,
        double currentAngleDegrees,
        double templateAngleDegrees)
    {
        _ = currentDetection;
        _ = template;
        var transformValues = BuildCurrentToTemplateAffineValues(
            currentAnchor,
            templateAnchor,
            currentAngleDegrees,
            templateAngleDegrees);
        using var transform = Mat.FromArray(new[,]
        {
            { transformValues[0], transformValues[1], transformValues[2] },
            { transformValues[3], transformValues[4], transformValues[5] }
        });
        var aligned = new Mat(templateSize, MatType.CV_8UC1, Scalar.Black);
        Cv2.WarpAffine(
            currentMask,
            aligned,
            transform,
            templateSize,
            InterpolationFlags.Nearest,
            BorderTypes.Constant,
            Scalar.Black);
        return aligned;
    }

    private static double[] BuildCurrentToTemplateAffineValues(
        Point2d currentAnchor,
        Point2d templateAnchor,
        double currentAngleDegrees,
        double templateAngleDegrees)
    {
        var deltaRadians = (templateAngleDegrees - currentAngleDegrees) * Math.PI / 180.0;
        var cos = Math.Cos(deltaRadians);
        var sin = Math.Sin(deltaRadians);
        var tx = templateAnchor.X - (cos * currentAnchor.X) + (sin * currentAnchor.Y);
        var ty = templateAnchor.Y - (sin * currentAnchor.X) - (cos * currentAnchor.Y);
        return [cos, -sin, tx, sin, cos, ty];
    }

    private static Mat AlignCurrentMaskCenterOnly(
        Mat currentMask,
        Size templateSize,
        Point2d currentAnchor,
        Point2d templateAnchor)
    {
        using var transform = Mat.FromArray(new[,]
        {
            { 1.0, 0.0, templateAnchor.X - currentAnchor.X },
            { 0.0, 1.0, templateAnchor.Y - currentAnchor.Y }
        });
        var aligned = new Mat(templateSize, MatType.CV_8UC1, Scalar.Black);
        Cv2.WarpAffine(
            currentMask,
            aligned,
            transform,
            templateSize,
            InterpolationFlags.Nearest,
            BorderTypes.Constant,
            Scalar.Black);
        return aligned;
    }

    private static FixedOverlayMetrics CalculateFixedOverlayMetrics(Mat templatePatch, Mat currentPatch)
    {
        using var intersection = new Mat();
        using var union = new Mat();
        using var templateOnly = new Mat();
        using var currentOnly = new Mat();
        using var currentNot = InvertMask(currentPatch);
        using var templateNot = InvertMask(templatePatch);
        Cv2.BitwiseAnd(templatePatch, currentPatch, intersection);
        Cv2.BitwiseOr(templatePatch, currentPatch, union);
        Cv2.BitwiseAnd(templatePatch, currentNot, templateOnly);
        Cv2.BitwiseAnd(currentPatch, templateNot, currentOnly);

        var templatePixels = Cv2.CountNonZero(templatePatch);
        var currentPixels = Cv2.CountNonZero(currentPatch);
        var unionPixels = Cv2.CountNonZero(union);
        if (unionPixels == 0 || templatePixels == 0 || currentPixels == 0)
        {
            return new FixedOverlayMetrics(0.0, 0.0, 1.0, 1.0, 1.0);
        }

        var mismatchPixels = Cv2.CountNonZero(templateOnly) + Cv2.CountNonZero(currentOnly);
        var mismatchRatio = Math.Clamp(mismatchPixels / (double)unionPixels, 0.0, 1.0);
        var templateOnlyRatio = Math.Clamp(Cv2.CountNonZero(templateOnly) / (double)templatePixels, 0.0, 1.0);
        var currentOnlyRatio = Math.Clamp(Cv2.CountNonZero(currentOnly) / (double)currentPixels, 0.0, 1.0);
        var maskIoU = Math.Clamp(Cv2.CountNonZero(intersection) / (double)unionPixels, 0.0, 1.0);
        return new FixedOverlayMetrics(
            Math.Clamp(1.0 - mismatchRatio, 0.0, 1.0),
            maskIoU,
            mismatchRatio,
            templateOnlyRatio,
            currentOnlyRatio);
    }

    private static Mat InvertMask(Mat mask)
    {
        var inverted = new Mat();
        Cv2.BitwiseNot(mask, inverted);
        return inverted;
    }

    private static void SaveFixedOverlayDiagnosticImage(
        Mat templatePatch,
        Rect patchRect,
        IReadOnlyList<(string Name, Mat? AlignedMask)> variants,
        string diagnosticImagePath)
    {
        var validVariants = variants
            .Where(variant => variant.AlignedMask is not null)
            .ToArray();
        if (validVariants.Length == 0)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticImagePath) ?? ".");
        var tileWidth = patchRect.Width;
        var tileHeight = patchRect.Height + 34;
        using var canvas = new Mat(tileHeight, tileWidth * validVariants.Length, MatType.CV_8UC3, Scalar.Black);
        for (var index = 0; index < validVariants.Length; index++)
        {
            var variant = validVariants[index];
            using var currentPatch = new Mat(variant.AlignedMask!, patchRect).Clone();
            using var tile = BuildOverlayTile(templatePatch, currentPatch, variant.Name);
            var target = new Rect(index * tileWidth, 0, tileWidth, tileHeight);
            using var roi = new Mat(canvas, target);
            tile.CopyTo(roi);
        }

        Cv2.ImWrite(diagnosticImagePath, canvas);
    }

    private static Mat BuildOverlayTile(Mat templatePatch, Mat currentPatch, string label)
    {
        using var intersection = new Mat();
        using var templateOnly = new Mat();
        using var currentOnly = new Mat();
        using var templateNot = InvertMask(templatePatch);
        using var currentNot = InvertMask(currentPatch);
        Cv2.BitwiseAnd(templatePatch, currentPatch, intersection);
        Cv2.BitwiseAnd(templatePatch, currentNot, templateOnly);
        Cv2.BitwiseAnd(currentPatch, templateNot, currentOnly);

        var tile = new Mat(templatePatch.Height + 34, templatePatch.Width, MatType.CV_8UC3, Scalar.Black);
        using var overlay = new Mat(templatePatch.Size(), MatType.CV_8UC3, Scalar.Black);
        overlay.SetTo(new Scalar(0, 180, 0), intersection);
        overlay.SetTo(new Scalar(0, 0, 255), templateOnly);
        overlay.SetTo(new Scalar(255, 0, 0), currentOnly);
        using (var overlayTarget = new Mat(tile, new Rect(0, 34, templatePatch.Width, templatePatch.Height)))
        {
            overlay.CopyTo(overlayTarget);
        }

        Cv2.PutText(
            tile,
            label,
            new Point(8, 23),
            HersheyFonts.HersheySimplex,
            0.65,
            Scalar.White,
            1,
            LineTypes.AntiAlias);
        return tile;
    }

    private static IReadOnlyList<(double CurrentAngleDegrees, double TemplateAngleDegrees, string Source)> BuildAlignmentAngleCandidates(
        double currentAngleDegrees,
        double templateAngleDegrees)
    {
        return
        [
            (currentAngleDegrees, templateAngleDegrees, "resolved-angle"),
            (-currentAngleDegrees, templateAngleDegrees, "inverted-current-angle"),
            (currentAngleDegrees, -templateAngleDegrees, "inverted-template-angle"),
            (-currentAngleDegrees, -templateAngleDegrees, "inverted-both-angles"),
            (currentAngleDegrees + 180.0, templateAngleDegrees, "resolved-angle-plus-180"),
            (currentAngleDegrees - 180.0, templateAngleDegrees, "resolved-angle-minus-180")
        ];
    }

    private static Point2f[] BuildPosePoints(double centerX, double centerY, double angleDegrees, double vectorLength)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return
        [
            new Point2f((float)centerX, (float)centerY),
            new Point2f((float)(centerX + cos * vectorLength), (float)(centerY + sin * vectorLength)),
            new Point2f((float)(centerX - sin * vectorLength), (float)(centerY + cos * vectorLength))
        ];
    }

    private static Rect BuildTemplatePatchRect(PartTemplate template, Point2d templateAnchor, int imageWidth, int imageHeight)
    {
        var referenceSize = Math.Max(template.ReferenceWidthPixels, template.ReferenceHeightPixels);
        var patchSize = Math.Max(MinimumPatchPixels, (int)Math.Ceiling(referenceSize * PatchPaddingRatio));
        return BuildCenteredRect(
            templateAnchor.X,
            templateAnchor.Y,
            patchSize,
            imageWidth,
            imageHeight);
    }

    private static Point2d CalculateMaskCentroid(Mat mask, Point2d fallback)
    {
        var moments = Cv2.Moments(mask, binaryImage: true);
        if (Math.Abs(moments.M00) < 0.0001)
        {
            return fallback;
        }

        return new Point2d(moments.M10 / moments.M00, moments.M01 / moments.M00);
    }

    private static double CalculateSizeScore(PartDetection detection, PartTemplate template)
    {
        var widthRatio = Math.Abs(detection.WidthMm - template.WidthMm) / Math.Max(template.WidthMm, 0.0001);
        var heightRatio = Math.Abs(detection.HeightMm - template.HeightMm) / Math.Max(template.HeightMm, 0.0001);
        var areaRatio = Math.Abs(detection.AreaPixels - template.AreaPixels) / Math.Max(template.AreaPixels, 0.0001);
        var detectionFillRatio = CalculateFillRatio(detection.AreaPixels, detection.WidthPixels, detection.HeightPixels);
        var templateFillRatio = CalculateFillRatio(template.AreaPixels, template.ReferenceWidthPixels, template.ReferenceHeightPixels);
        var fillRatioDiff = Math.Abs(detectionFillRatio - templateFillRatio) / Math.Max(templateFillRatio, 0.0001);
        var penalty =
            (widthRatio * 0.30) +
            (heightRatio * 0.30) +
            (areaRatio * 0.20) +
            (fillRatioDiff * 0.20);
        return Math.Clamp(1.0 - penalty, 0.0, 1.0);
    }

    private static double CalculateMaskIoU(Mat templatePatch, Mat currentPatch)
    {
        using var intersection = new Mat();
        using var union = new Mat();
        Cv2.BitwiseAnd(templatePatch, currentPatch, intersection);
        Cv2.BitwiseOr(templatePatch, currentPatch, union);
        var unionPixels = Cv2.CountNonZero(union);
        if (unionPixels == 0)
        {
            return 0.0;
        }

        return Math.Clamp(Cv2.CountNonZero(intersection) / (double)unionPixels, 0.0, 1.0);
    }

    private static double CalculateShapeScore(Point[]? templateContour, Mat currentPatch)
    {
        var currentContour = FindLargestContour(currentPatch);
        if (templateContour is null || currentContour is null)
        {
            return 0.0;
        }

        var distance = Cv2.MatchShapes(templateContour, currentContour, ShapeMatchModes.I1);
        if (double.IsNaN(distance) || double.IsInfinity(distance))
        {
            return 0.0;
        }

        return Math.Clamp(Math.Exp(-distance * 5.0), 0.0, 1.0);
    }

    private static double CalculateEdgeDistanceScore(
        int templateEdgePixels,
        Mat templateEdgeDistance,
        Mat currentPatch)
    {
        using var currentEdges = BuildMaskEdges(currentPatch);
        var currentEdgePixels = Cv2.CountNonZero(currentEdges);
        if (currentEdgePixels == 0 || templateEdgePixels == 0)
        {
            return 0.0;
        }

        var averageDistance = Cv2.Mean(templateEdgeDistance, currentEdges).Val0;
        return Math.Clamp(Math.Exp(-averageDistance / EdgeDistanceScalePixels), 0.0, 1.0);
    }

    private static Mat BuildTemplateEdgeDistance(Mat templatePatch, out int templateEdgePixels)
    {
        using var templateEdges = BuildMaskEdges(templatePatch);
        templateEdgePixels = Cv2.CountNonZero(templateEdges);
        using var distanceInput = new Mat();
        Cv2.BitwiseNot(templateEdges, distanceInput);
        var distance = new Mat();
        Cv2.DistanceTransform(distanceInput, distance, DistanceTypes.L2, DistanceTransformMasks.Mask3);
        return distance;
    }

    private static Point[]? FindLargestContour(Mat mask)
    {
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        return contours
            .Where(contour => contour.Length >= 3)
            .OrderByDescending(contour => Cv2.ContourArea(contour))
            .FirstOrDefault();
    }

    private static Mat BuildMaskEdges(Mat mask)
    {
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        var edges = new Mat();
        Cv2.MorphologyEx(mask, edges, MorphTypes.Gradient, kernel);
        return edges;
    }

    private static Mat SelectTemplateMask(Mat binary, PartTemplate template)
    {
        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var mask = new Mat(binary.Size(), MatType.CV_8UC1, Scalar.Black);
        if (contours.Length == 0)
        {
            return mask;
        }

        var selected = contours
            .Select(contour => new
            {
                Contour = contour,
                Area = Cv2.ContourArea(contour),
                AreaScore = CalculateAreaScore(Cv2.ContourArea(contour), template.AreaPixels)
            })
            .Where(candidate => candidate.Area > 0)
            .OrderByDescending(candidate => candidate.AreaScore)
            .ThenByDescending(candidate => candidate.Area)
            .FirstOrDefault();
        if (selected is null)
        {
            return mask;
        }

        Cv2.DrawContours(mask, [selected.Contour], -1, Scalar.White, -1);
        return mask;
    }

    private static double CalculateMaskAreaScore(Mat mask, double templateAreaPixels)
    {
        return CalculateAreaScore(Cv2.CountNonZero(mask), templateAreaPixels);
    }

    private static double CalculateAreaScore(double areaPixels, double templateAreaPixels)
    {
        var ratio = Math.Abs(areaPixels - templateAreaPixels) / Math.Max(templateAreaPixels, 0.0001);
        return Math.Clamp(1.0 - ratio, 0.0, 1.0);
    }

    private static Mat ToGray(Mat image)
    {
        if (image.Channels() == 1)
        {
            return image.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private static Mat Blur(Mat gray)
    {
        var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
        return blurred;
    }

    private static Mat Threshold(Mat gray)
    {
        var binary = new Mat();
        Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        Cv2.MorphologyEx(
            binary,
            binary,
            MorphTypes.Close,
            Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));
        return binary;
    }

    private static Rect BuildCenteredRect(double centerX, double centerY, int requestedSize, int imageWidth, int imageHeight)
    {
        var size = Math.Clamp(requestedSize, 1, Math.Min(imageWidth, imageHeight));
        var x = (int)Math.Round(centerX - size / 2.0);
        var y = (int)Math.Round(centerY - size / 2.0);
        x = Math.Clamp(x, 0, Math.Max(0, imageWidth - size));
        y = Math.Clamp(y, 0, Math.Max(0, imageHeight - size));
        return new Rect(x, y, size, size);
    }

    private static double CalculateFillRatio(double areaPixels, double widthPixels, double heightPixels)
    {
        var boxArea = Math.Max(widthPixels * heightPixels, 0.0001);
        return Math.Clamp(areaPixels / boxArea, 0.0, 1.0);
    }

    private static bool HasTemplatePixelShape(PartTemplate template)
    {
        return template.ReferenceWidthPixels > 0.0001 && template.ReferenceHeightPixels > 0.0001;
    }
}
