using JuliMvs.Core;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Services;

internal sealed class ProductionMissingMaterialDetector : IDisposable
{
    private const int DefaultEdgeTolerancePixels = 12;
    private const int MinimumMissingAreaPixels = 2500;
    private const int MinimumMissingDepthPixels = 16;
    private const int MinimumMissingWidthPixels = 20;
    private const int SevereMissingAreaPixels = 9000;
    private const int SevereMissingDepthPixels = 36;
    private const double MinimumMissingAreaRatio = 0.00075;
    private const int RoiPaddingPixels = 14;
    private const double CoarseVisibleRadiusIndentRatio = 0.08;

    private readonly object _cacheLock = new();
    private TemplateMaskCache? _templateCache;

    public void WarmupTemplate(
        PartTemplate template,
        ContourFeatureExtraction templateFeature)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(templateFeature);

        _ = GetTemplateCache(template, templateFeature);
    }

    public ProductionMissingMaterialResult Evaluate(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        PartTemplate template,
        ProductionAutoAngleResult angleResult,
        bool buildDiagnosticOverlay = false)
    {
        ArgumentNullException.ThrowIfNull(currentFeature);
        ArgumentNullException.ThrowIfNull(templateFeature);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(angleResult);

        if (currentFeature.ContourPoints.Count < 3 || templateFeature.ContourPoints.Count < 3)
        {
            return ProductionMissingMaterialResult.Pass("边缘缺损/缺料检测跳过: 当前或模板轮廓点不足。");
        }

        var templateCache = GetTemplateCache(template, templateFeature);
        using var alignedCurrent = BuildAlignedCurrentMask(
            currentFeature,
            templateFeature,
            template,
            angleResult,
            templateCache.Roi);
        using var cleanCurrent = CleanIncomingMask(alignedCurrent);
        using var invertedCurrent = new Mat();
        Cv2.BitwiseNot(cleanCurrent, invertedCurrent);
        using var missing = new Mat();
        Cv2.BitwiseAnd(templateCache.CoreMask, invertedCurrent, missing);
        using var missingClean = RemoveSmallNoise(missing);

        var missingAreaPixels = Cv2.CountNonZero(missingClean);
        var analysis = missingAreaPixels <= 0
            ? MissingMaterialAnalysis.Empty
            : AnalyzeMissingComponents(missingClean, templateCache.TemplateDistance);
        var missingAreaRatio = missingAreaPixels / Math.Max(template.AreaPixels, 1.0);
        var isObviousMissing = analysis.HasSevereComponent ||
            (analysis.HasNormalComponent && missingAreaRatio >= MinimumMissingAreaRatio);
        Mat? diagnosticOverlay = null;
        if (isObviousMissing && buildDiagnosticOverlay)
        {
            diagnosticOverlay = BuildDiagnosticOverlay(
                currentFeature,
                templateFeature,
                template,
                angleResult,
                templateCache.Roi,
                cleanCurrent,
                missingClean);
        }

        var message =
            $"{(isObviousMissing ? "NG" : "OK")} " +
            $"面积={missingAreaPixels}px " +
            $"深度={analysis.MaximumDepthPixels:F1}px " +
            $"宽度={analysis.MaximumWidthPixels}px";

        return isObviousMissing
            ? ProductionMissingMaterialResult.Fail(message, diagnosticOverlay)
            : ProductionMissingMaterialResult.Pass(message);
    }

    public ProductionMissingMaterialResult EvaluateCoarseVisibleEdgeMissing(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        PartTemplate template)
    {
        ArgumentNullException.ThrowIfNull(currentFeature);
        ArgumentNullException.ThrowIfNull(templateFeature);
        ArgumentNullException.ThrowIfNull(template);

        var radiusIndent = CalculateRadiusIndentIncrease(
            currentFeature.RadiusSignature,
            templateFeature.RadiusSignature);

        var isVisibleMissing = radiusIndent.Ratio >= CoarseVisibleRadiusIndentRatio;

        var message =
            $"{(isVisibleMissing ? "NG" : "OK")} " +
            $"边缘缺损粗判 缺口={radiusIndent.Pixels:F1}px";

        return isVisibleMissing
            ? ProductionMissingMaterialResult.Fail(message)
            : ProductionMissingMaterialResult.Pass(message);
    }

    public void Clear()
    {
        lock (_cacheLock)
        {
            _templateCache?.Dispose();
            _templateCache = null;
        }
    }

    public void Dispose()
    {
        Clear();
    }

    private TemplateMaskCache GetTemplateCache(
        PartTemplate template,
        ContourFeatureExtraction templateFeature)
    {
        var key = TemplateMaskCacheKey.Create(template, templateFeature);
        lock (_cacheLock)
        {
            if (_templateCache is not null && _templateCache.Key.Equals(key))
            {
                return _templateCache;
            }

            var cache = BuildTemplateCache(key, templateFeature);
            _templateCache?.Dispose();
            _templateCache = cache;
            return _templateCache;
        }
    }

    private static TemplateMaskCache BuildTemplateCache(
        TemplateMaskCacheKey key,
        ContourFeatureExtraction templateFeature)
    {
        var roi = BuildRoi(templateFeature.ContourPoints);
        using var templateMask = BuildMaskInRoi(templateFeature.ContourPoints, roi);
        using var templateDistance = BuildTemplateInteriorDistance(templateMask);
        using var coreMask = new Mat();
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Ellipse,
            new Size(DefaultEdgeTolerancePixels * 2 + 1, DefaultEdgeTolerancePixels * 2 + 1));
        Cv2.Erode(templateMask, coreMask, kernel);
        return new TemplateMaskCache(key, roi, coreMask.Clone(), templateDistance.Clone());
    }

    private static Mat BuildAlignedCurrentMask(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        PartTemplate template,
        ProductionAutoAngleResult angleResult,
        Rect roi)
    {
        var angleOffset = AngleMath.NormalizeDeltaDegrees360(
            angleResult.AlignmentAngleDegrees,
            template.ReferenceAngleDegrees);
        var radians = -angleOffset * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var points = currentFeature.ContourPoints
            .Select(point =>
            {
                var dx = point.X - angleResult.CenterXPixel;
                var dy = point.Y - angleResult.CenterYPixel;
                var x = templateFeature.CenterXPixel + dx * cos - dy * sin;
                var y = templateFeature.CenterYPixel + dx * sin + dy * cos;
                return new Point(
                    Math.Clamp((int)Math.Round(x - roi.X), 0, roi.Width - 1),
                    Math.Clamp((int)Math.Round(y - roi.Y), 0, roi.Height - 1));
            })
            .ToArray();

        var mask = new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.All(0));
        if (points.Length >= 3)
        {
            Cv2.FillPoly(mask, new[] { points }, Scalar.White);
        }

        return mask;
    }

    private static Mat BuildDiagnosticOverlay(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        PartTemplate template,
        ProductionAutoAngleResult angleResult,
        Rect roi,
        Mat alignedCurrentMask,
        Mat missingMask)
    {
        var overlay = new Mat(roi.Height, roi.Width, MatType.CV_8UC3, new Scalar(18, 18, 18));
        using var templateMask = BuildMaskInRoi(templateFeature.ContourPoints, roi);
        using var templateFill = new Mat(roi.Height, roi.Width, MatType.CV_8UC3, new Scalar(28, 48, 28));
        templateFill.CopyTo(overlay, templateMask);

        var templatePoints = ToRoiPoints(templateFeature.ContourPoints, roi);
        if (templatePoints.Length >= 2)
        {
            Cv2.Polylines(
                overlay,
                new[] { templatePoints },
                isClosed: true,
                new Scalar(0, 220, 0),
                2,
                LineTypes.AntiAlias);
        }

        var alignedCurrentPoints = AlignCurrentPointsToTemplate(
            currentFeature,
            templateFeature,
            template,
            angleResult,
            roi);
        if (alignedCurrentPoints.Length >= 2)
        {
            Cv2.Polylines(
                overlay,
                new[] { alignedCurrentPoints },
                isClosed: true,
                new Scalar(255, 160, 0),
                2,
                LineTypes.AntiAlias);
        }

        using var red = new Mat(roi.Height, roi.Width, MatType.CV_8UC3, new Scalar(0, 0, 255));
        red.CopyTo(overlay, missingMask);

        Cv2.PutText(
            overlay,
            "NG missing edge: green=template blue=aligned red=missing",
            new Point(16, 32),
            HersheyFonts.HersheySimplex,
            0.72,
            Scalar.White,
            2,
            LineTypes.AntiAlias);
        Cv2.PutText(
            overlay,
            $"missing={Cv2.CountNonZero(missingMask)}px score={angleResult.MatchScore:F3}",
            new Point(16, 62),
            HersheyFonts.HersheySimplex,
            0.62,
            Scalar.White,
            1,
            LineTypes.AntiAlias);

        return overlay;
    }

    private static Point[] AlignCurrentPointsToTemplate(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        PartTemplate template,
        ProductionAutoAngleResult angleResult,
        Rect roi)
    {
        var angleOffset = AngleMath.NormalizeDeltaDegrees360(
            angleResult.AlignmentAngleDegrees,
            template.ReferenceAngleDegrees);
        var radians = -angleOffset * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return currentFeature.ContourPoints
            .Select(point =>
            {
                var dx = point.X - angleResult.CenterXPixel;
                var dy = point.Y - angleResult.CenterYPixel;
                var x = templateFeature.CenterXPixel + dx * cos - dy * sin;
                var y = templateFeature.CenterYPixel + dx * sin + dy * cos;
                return new Point(
                    Math.Clamp((int)Math.Round(x - roi.X), 0, roi.Width - 1),
                    Math.Clamp((int)Math.Round(y - roi.Y), 0, roi.Height - 1));
            })
            .ToArray();
    }

    private static Point[] ToRoiPoints(
        IReadOnlyList<Point2d> contourPoints,
        Rect roi)
    {
        return contourPoints
            .Select(point => new Point(
                Math.Clamp((int)Math.Round(point.X - roi.X), 0, roi.Width - 1),
                Math.Clamp((int)Math.Round(point.Y - roi.Y), 0, roi.Height - 1)))
            .ToArray();
    }

    private static Mat BuildMaskInRoi(
        IReadOnlyList<Point2d> contourPoints,
        Rect roi)
    {
        var mask = new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.All(0));
        var points = contourPoints
            .Select(point => new Point(
                Math.Clamp((int)Math.Round(point.X - roi.X), 0, roi.Width - 1),
                Math.Clamp((int)Math.Round(point.Y - roi.Y), 0, roi.Height - 1)))
            .ToArray();
        if (points.Length >= 3)
        {
            Cv2.FillPoly(mask, new[] { points }, Scalar.White);
        }

        return mask;
    }

    private static Mat CleanIncomingMask(Mat source)
    {
        var clean = new Mat();
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        using var openKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(source, clean, MorphTypes.Close, closeKernel);
        Cv2.MorphologyEx(clean, clean, MorphTypes.Open, openKernel);
        return clean;
    }

    private static Mat RemoveSmallNoise(Mat source)
    {
        var clean = new Mat();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(source, clean, MorphTypes.Open, kernel);
        return clean;
    }

    private static Mat BuildTemplateInteriorDistance(Mat templateMask)
    {
        var distance = new Mat();
        Cv2.DistanceTransform(templateMask, distance, DistanceTypes.L2, DistanceTransformMasks.Mask3);
        return distance;
    }

    private static MissingMaterialAnalysis AnalyzeMissingComponents(Mat binary, Mat templateDistance)
    {
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var count = Cv2.ConnectedComponentsWithStats(
            binary,
            labels,
            stats,
            centroids,
            PixelConnectivity.Connectivity8,
            MatType.CV_32S);
        var maximumDepthByLabel = new double[count];
        using var missingPixels = new Mat();
        Cv2.FindNonZero(binary, missingPixels);
        for (var index = 0; index < missingPixels.Rows; index++)
        {
            var point = missingPixels.At<Point>(index);
            var label = labels.At<int>(point.Y, point.X);
            if (label > 0)
            {
                maximumDepthByLabel[label] = Math.Max(
                    maximumDepthByLabel[label],
                    templateDistance.At<float>(point.Y, point.X));
            }
        }

        var largest = 0;
        var maximumDepth = 0.0;
        var maximumWidth = 0;
        var hasNormal = false;
        var hasSevere = false;
        for (var label = 1; label < count; label++)
        {
            var area = stats.At<int>(label, (int)ConnectedComponentsTypes.Area);
            var width = stats.At<int>(label, (int)ConnectedComponentsTypes.Width);
            var height = stats.At<int>(label, (int)ConnectedComponentsTypes.Height);
            var componentWidth = Math.Max(width, height);
            var depth = maximumDepthByLabel[label];
            largest = Math.Max(largest, area);
            maximumDepth = Math.Max(maximumDepth, depth);
            maximumWidth = Math.Max(maximumWidth, componentWidth);

            var normalMissing =
                area >= MinimumMissingAreaPixels &&
                depth >= MinimumMissingDepthPixels &&
                componentWidth >= MinimumMissingWidthPixels;
            var severeMissing =
                area >= SevereMissingAreaPixels ||
                depth >= SevereMissingDepthPixels;
            hasNormal |= normalMissing;
            hasSevere |= severeMissing;
        }

        return new MissingMaterialAnalysis(
            largest,
            maximumDepth,
            maximumWidth,
            hasNormal,
            hasSevere);
    }

    private static RadiusIndentIncrease CalculateRadiusIndentIncrease(
        IReadOnlyList<float> currentSignature,
        IReadOnlyList<float> templateSignature)
    {
        if (currentSignature.Count < 16 || templateSignature.Count < 16)
        {
            return RadiusIndentIncrease.Empty;
        }

        var currentStats = CalculateRadiusIndentStats(currentSignature);
        var templateStats = CalculateRadiusIndentStats(templateSignature);
        var extraIndentPixels = Math.Max(0.0, currentStats.IndentPixels - templateStats.IndentPixels);
        return new RadiusIndentIncrease(
            extraIndentPixels,
            extraIndentPixels / Math.Max(templateStats.MedianPixels, 1.0));
    }

    private static RadiusIndentStats CalculateRadiusIndentStats(IReadOnlyList<float> signature)
    {
        var values = signature
            .Where(static value => float.IsFinite(value) && value > 0)
            .Select(static value => (double)value)
            .OrderBy(static value => value)
            .ToArray();
        if (values.Length == 0)
        {
            return new RadiusIndentStats(0.0, 0.0);
        }

        var median = Quantile(values, 0.50);
        var low = Quantile(values, 0.05);
        return new RadiusIndentStats(median, Math.Max(0.0, median - low));
    }

    private static double Quantile(double[] sortedValues, double quantile)
    {
        if (sortedValues.Length == 1)
        {
            return sortedValues[0];
        }

        var position = Math.Clamp(quantile, 0.0, 1.0) * (sortedValues.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var fraction = position - lower;
        return sortedValues[lower] * (1.0 - fraction) + sortedValues[upper] * fraction;
    }

    private static string FormatRatioPercent(double ratio)
    {
        return $"{ratio * 100.0:F1}%";
    }

    private static Rect BuildRoi(IReadOnlyList<Point2d> points)
    {
        var minX = Math.Floor(points.Min(point => point.X)) - RoiPaddingPixels;
        var minY = Math.Floor(points.Min(point => point.Y)) - RoiPaddingPixels;
        var maxX = Math.Ceiling(points.Max(point => point.X)) + RoiPaddingPixels;
        var maxY = Math.Ceiling(points.Max(point => point.Y)) + RoiPaddingPixels;
        var x = Math.Max(0, (int)minX);
        var y = Math.Max(0, (int)minY);
        var width = Math.Max(1, (int)Math.Ceiling(maxX) - x + 1);
        var height = Math.Max(1, (int)Math.Ceiling(maxY) - y + 1);
        return new Rect(x, y, width, height);
    }

    private sealed record TemplateMaskCacheKey(
        Guid TemplateId,
        int ImageWidthPixels,
        int ImageHeightPixels,
        double CenterXPixel,
        double CenterYPixel,
        double AreaPixels,
        int PointCount)
    {
        public static TemplateMaskCacheKey Create(
            PartTemplate template,
            ContourFeatureExtraction templateFeature)
        {
            return new TemplateMaskCacheKey(
                template.Id,
                templateFeature.ImageWidthPixels,
                templateFeature.ImageHeightPixels,
                Math.Round(templateFeature.CenterXPixel, 3),
                Math.Round(templateFeature.CenterYPixel, 3),
                Math.Round(templateFeature.AreaPixels, 1),
                templateFeature.ContourPoints.Count);
        }
    }

    private sealed class TemplateMaskCache : IDisposable
    {
        public TemplateMaskCache(
            TemplateMaskCacheKey key,
            Rect roi,
            Mat coreMask,
            Mat templateDistance)
        {
            Key = key;
            Roi = roi;
            CoreMask = coreMask;
            TemplateDistance = templateDistance;
        }

        public TemplateMaskCacheKey Key { get; }

        public Rect Roi { get; }

        public Mat CoreMask { get; }

        public Mat TemplateDistance { get; }

        public void Dispose()
        {
            CoreMask.Dispose();
            TemplateDistance.Dispose();
        }
    }
}

internal sealed record MissingMaterialAnalysis(
    int LargestAreaPixels,
    double MaximumDepthPixels,
    int MaximumWidthPixels,
    bool HasNormalComponent,
    bool HasSevereComponent)
{
    public static MissingMaterialAnalysis Empty { get; } = new(0, 0, 0, false, false);

    public bool HasNgComponent => HasNormalComponent || HasSevereComponent;
}

internal sealed record RadiusIndentStats(
    double MedianPixels,
    double IndentPixels);

internal sealed record RadiusIndentIncrease(
    double Pixels,
    double Ratio)
{
    public static RadiusIndentIncrease Empty { get; } = new(0.0, 0.0);
}

internal sealed record ProductionMissingMaterialResult(
    bool IsPass,
    string Message,
    Mat? DiagnosticOverlay = null)
{
    public static ProductionMissingMaterialResult Pass(string message)
    {
        return new ProductionMissingMaterialResult(true, message);
    }

    public static ProductionMissingMaterialResult Fail(string message, Mat? diagnosticOverlay = null)
    {
        return new ProductionMissingMaterialResult(false, message, diagnosticOverlay);
    }
}
