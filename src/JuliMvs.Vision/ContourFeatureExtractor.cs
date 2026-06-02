using JuliMvs.Core.Vision;
using OpenCvSharp;

namespace JuliMvs.Vision;

public sealed class ContourFeatureExtractor
{
    public const int DefaultRadiusSampleCount = 720;
    private const int ArtifactRemovalRadiusSampleCount = 720;

    public ContourFeatureExtraction Extract(
        Mat image,
        VisionParameters? parameters = null,
        int radiusSampleCount = DefaultRadiusSampleCount,
        ContourFeatureExtraction? referenceFeature = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Empty())
        {
            throw new InvalidOperationException("图片为空，无法提取工件外轮廓。");
        }

        parameters ??= VisionParameters.Default;
        radiusSampleCount = Math.Max(90, radiusSampleCount);

        using var gray = ToGray(image);
        using var roiImage = ApplyRoi(gray, parameters.Roi, out var offset);
        using var blurred = Blur(roiImage, parameters.BlurKernelSize);
        using var binary = Threshold(blurred, parameters.BinaryThreshold);
        using var inverted = new Mat();
        Cv2.BitwiseNot(binary, inverted);
        var contour = FindBestContour(binary, inverted, offset, parameters, referenceFeature);
        var moments = Cv2.Moments(contour);
        if (Math.Abs(moments.M00) < 0.0001)
        {
            throw new InvalidOperationException("工件外轮廓面积过小，无法计算中心。");
        }

        var centerXInRoi = moments.M10 / moments.M00;
        var centerYInRoi = moments.M01 / moments.M00;
        var centerX = centerXInRoi + offset.X;
        var centerY = centerYInRoi + offset.Y;
        var area = Cv2.ContourArea(contour);
        var perimeter = Cv2.ArcLength(contour, closed: true);
        var circularity = perimeter <= 0
            ? 0
            : 4.0 * Math.PI * area / (perimeter * perimeter);
        var shape = Cv2.MinAreaRect(contour);
        var width = Math.Max(shape.Size.Width, shape.Size.Height);
        var height = Math.Max(Math.Min(shape.Size.Width, shape.Size.Height), 0.0001);
        var axisRatio = width / height;
        var radiusSignature = BuildRadiusSignature(
            contour,
            centerXInRoi,
            centerYInRoi,
            radiusSampleCount,
            smooth: true);
        var radiusSignal = CalculateRadiusSignalPixels(radiusSignature);
        var normalizedRadiusSignature = NormalizeRadiusSignature(radiusSignature);
        var normalizedRadiusSignal = CalculateRadiusSignalPixels(normalizedRadiusSignature);
        var sampledContourPoints = BuildSampledContourPoints(contour, offset, maximumPointCount: 1440);
        var pca = CalculatePca(contour);
        var ellipse = CalculateEllipseAxis(contour);
        var axisFeature = BuildAxisFeature(pca, ellipse);
        var strategy = AutoAngleStrategy.Select(
            width,
            height,
            pca.Ratio,
            circularity,
            radiusSignal);

        return new ContourFeatureExtraction(
            CenterXPixel: centerX,
            CenterYPixel: centerY,
            AreaPixels: area,
            PerimeterPixels: perimeter,
            WidthPixels: width,
            HeightPixels: height,
            AxisRatio: axisRatio,
            PcaRatio: pca.Ratio,
            PcaAngleDegrees: pca.AngleDegrees,
            Circularity: circularity,
            RadiusSignalPixels: radiusSignal,
            RadiusSignature: radiusSignature,
            NormalizedRadiusSignalPixels: normalizedRadiusSignal,
            NormalizedRadiusSignature: normalizedRadiusSignature,
            ContourPoints: sampledContourPoints,
            ImageWidthPixels: image.Width,
            ImageHeightPixels: image.Height,
            AxisFeature: axisFeature,
            Strategy: strategy);
    }

    public static ContourRadiusMatch MatchRadiusSignature(
        IReadOnlyList<float> currentSignature,
        IReadOnlyList<float> templateSignature)
    {
        var best = MatchRadiusSignatureWithAlternatives(currentSignature, templateSignature);
        return new ContourRadiusMatch(
            best.Shift,
            best.AngleDegrees,
            best.ErrorPixels,
            best.ErrorNormalized);
    }

    public static ContourRadiusMatchWithAlternative MatchRadiusSignatureWithAlternatives(
        IReadOnlyList<float> currentSignature,
        IReadOnlyList<float> templateSignature,
        double alternativeExclusionDegrees = 8.0)
    {
        var sampleCount = Math.Min(currentSignature.Count, templateSignature.Count);
        if (sampleCount == 0)
        {
            return new ContourRadiusMatchWithAlternative(
                Shift: 0,
                AngleDegrees: 0,
                ErrorPixels: double.PositiveInfinity,
                ErrorNormalized: double.PositiveInfinity,
                AlternativeShift: 0,
                AlternativeAngleDegrees: 0,
                AlternativeErrorPixels: double.PositiveInfinity,
                AlternativeErrorNormalized: double.PositiveInfinity);
        }

        var bestShift = 0;
        var bestErrorPixels = double.PositiveInfinity;
        var bestErrorNormalized = double.PositiveInfinity;
        var alternativeShift = 0;
        var alternativeErrorPixels = double.PositiveInfinity;
        var alternativeErrorNormalized = double.PositiveInfinity;
        var exclusionBins = Math.Max(1, (int)Math.Round(Math.Abs(alternativeExclusionDegrees) / 360.0 * sampleCount));
        for (var shift = 0; shift < sampleCount; shift++)
        {
            var sumPixels = 0.0;
            var sumNormalized = 0.0;
            var abandoned = false;
            for (var index = 0; index < sampleCount; index++)
            {
                var current = currentSignature[index];
                var template = templateSignature[PositiveModulo(index - shift, sampleCount)];
                var diff = Math.Abs(current - template);
                sumPixels += diff;
                var denominator = Math.Max((Math.Abs(current) + Math.Abs(template)) / 2.0, 1.0);
                sumNormalized += diff / denominator;
                if (!double.IsInfinity(alternativeErrorPixels) &&
                    sumPixels >= alternativeErrorPixels * sampleCount)
                {
                    abandoned = true;
                    break;
                }
            }

            if (abandoned)
            {
                continue;
            }

            var errorPixels = sumPixels / sampleCount;
            if (errorPixels < bestErrorPixels)
            {
                if (Math.Abs(CircularShiftDistance(shift, bestShift, sampleCount)) > exclusionBins)
                {
                    alternativeShift = bestShift;
                    alternativeErrorPixels = bestErrorPixels;
                    alternativeErrorNormalized = bestErrorNormalized;
                }

                bestErrorPixels = errorPixels;
                bestErrorNormalized = sumNormalized / sampleCount;
                bestShift = shift;
            }
            else if (Math.Abs(CircularShiftDistance(shift, bestShift, sampleCount)) > exclusionBins &&
                errorPixels < alternativeErrorPixels)
            {
                alternativeShift = shift;
                alternativeErrorPixels = errorPixels;
                alternativeErrorNormalized = sumNormalized / sampleCount;
            }
        }

        return new ContourRadiusMatchWithAlternative(
            bestShift,
            bestShift * 360.0 / sampleCount,
            bestErrorPixels,
            bestErrorNormalized,
            alternativeShift,
            alternativeShift * 360.0 / sampleCount,
            alternativeErrorPixels,
            alternativeErrorNormalized);
    }

    public static float[] MirrorRadiusSignature(IReadOnlyList<float> signature)
    {
        var mirrored = new float[signature.Count];
        if (signature.Count == 0)
        {
            return mirrored;
        }

        mirrored[0] = signature[0];
        for (var index = 1; index < signature.Count; index++)
        {
            mirrored[index] = signature[signature.Count - index];
        }

        return mirrored;
    }

    public static ContourCorrelationMatch MatchNormalizedRadiusSignatureWithAlternatives(
        IReadOnlyList<float> currentSignature,
        IReadOnlyList<float> templateSignature,
        double alternativeExclusionDegrees = 18.0)
    {
        var sampleCount = Math.Min(currentSignature.Count, templateSignature.Count);
        if (sampleCount == 0)
        {
            return new ContourCorrelationMatch(
                Shift: 0,
                AngleDegrees: 0,
                Score: 0,
                AlternativeShift: 0,
                AlternativeAngleDegrees: 0,
                AlternativeScore: 0);
        }

        var bestShift = 0;
        var bestScore = double.NegativeInfinity;
        var alternativeShift = 0;
        var alternativeScore = double.NegativeInfinity;
        var exclusionBins = Math.Max(1, (int)Math.Round(Math.Abs(alternativeExclusionDegrees) / 360.0 * sampleCount));
        for (var shift = 0; shift < sampleCount; shift++)
        {
            var score = CalculateCircularCorrelation(currentSignature, templateSignature, shift, sampleCount);
            if (score > bestScore)
            {
                if (Math.Abs(CircularShiftDistance(shift, bestShift, sampleCount)) > exclusionBins)
                {
                    alternativeShift = bestShift;
                    alternativeScore = bestScore;
                }

                bestScore = score;
                bestShift = shift;
            }
            else if (Math.Abs(CircularShiftDistance(shift, bestShift, sampleCount)) > exclusionBins &&
                score > alternativeScore)
            {
                alternativeShift = shift;
                alternativeScore = score;
            }
        }

        if (double.IsNegativeInfinity(alternativeScore))
        {
            alternativeScore = 0.0;
        }

        return new ContourCorrelationMatch(
            bestShift,
            bestShift * 360.0 / sampleCount,
            Math.Clamp(bestScore, 0.0, 1.0),
            alternativeShift,
            alternativeShift * 360.0 / sampleCount,
            Math.Clamp(alternativeScore, 0.0, 1.0));
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

    private static Mat ApplyRoi(Mat image, ImageRoi roi, out Point offset)
    {
        if (roi.IsEmpty)
        {
            offset = new Point(0, 0);
            return image.Clone();
        }

        var x = Math.Clamp(roi.X, 0, image.Width - 1);
        var y = Math.Clamp(roi.Y, 0, image.Height - 1);
        var width = Math.Clamp(roi.Width, 1, image.Width - x);
        var height = Math.Clamp(roi.Height, 1, image.Height - y);
        offset = new Point(x, y);
        return new Mat(image, new Rect(x, y, width, height)).Clone();
    }

    private static Mat Blur(Mat gray, int kernelSize)
    {
        var normalizedKernel = Math.Max(kernelSize, 3);
        if (normalizedKernel % 2 == 0)
        {
            normalizedKernel++;
        }

        var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(normalizedKernel, normalizedKernel), 0.0);
        return blurred;
    }

    private static Mat Threshold(Mat gray, int binaryThreshold)
    {
        var binary = new Mat();
        if (binaryThreshold <= 0)
        {
            Cv2.Threshold(gray, binary, 0.0, 255.0, ThresholdTypes.Otsu);
        }
        else
        {
            Cv2.Threshold(gray, binary, binaryThreshold, 255.0, ThresholdTypes.Binary);
        }

        using var smallKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(binary, binary, MorphTypes.Open, smallKernel);
        Cv2.MorphologyEx(binary, binary, MorphTypes.Close, smallKernel);
        return binary;
    }

    private static Point[] FindBestContour(
        Mat binary,
        Mat inverted,
        Point offset,
        VisionParameters parameters,
        ContourFeatureExtraction? referenceFeature)
    {
        var candidates = FindContourCandidates(binary, offset, parameters)
            .Concat(FindContourCandidates(inverted, offset, parameters))
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("No valid part contour candidate was found.");
        }

        var selected = referenceFeature is null
            ? SelectUnreferencedContourCandidate(candidates, binary.Size(), offset)
            : SelectReferencedContourCandidate(candidates, referenceFeature);
        return RemoveConnectedContourArtifacts(selected.Contour, binary.Size());
    }

    private static Point[] RemoveConnectedContourArtifacts(Point[] contour, Size imageSize)
    {
        if (contour.Length < 3)
        {
            return contour;
        }

        var original = CalculateContourMetrics(contour);
        var minimumDimension = Math.Min(original.Width, original.Height);
        if (minimumDimension < 80.0)
        {
            return contour;
        }

        var kernelSizes = BuildArtifactRemovalKernelSizes(minimumDimension).ToArray();
        if (kernelSizes.Length == 0)
        {
            return contour;
        }

        var maximumKernelSize = kernelSizes.Max();
        var padding = Math.Max(maximumKernelSize * 2, 12);
        var bounds = Cv2.BoundingRect(contour);
        var roi = ExpandRect(bounds, padding, imageSize);
        if (roi.Width <= maximumKernelSize * 2 || roi.Height <= maximumKernelSize * 2)
        {
            return contour;
        }

        var localContour = contour
            .Select(point => new Point(point.X - roi.X, point.Y - roi.Y))
            .ToArray();
        using var sourceMask = new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.Black);
        Cv2.FillPoly(sourceMask, new[] { localContour }, Scalar.White);

        Point[]? bestOpenedContour = null;
        var bestOpenedSelectionScore = double.PositiveInfinity;
        Point[]? bestRecoveredContour = null;
        var bestRecoveredSelectionScore = double.PositiveInfinity;
        foreach (var kernelSize in kernelSizes)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(kernelSize, kernelSize));
            using var opened = new Mat();
            Cv2.MorphologyEx(sourceMask, opened, MorphTypes.Open, kernel);
            var openedBody = TryFindLargestContour(opened);
            if (openedBody is null || openedBody.Length < 3)
            {
                continue;
            }

            using var openedBodyMask = new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.Black);
            Cv2.FillPoly(openedBodyMask, new[] { openedBody }, Scalar.White);
            var recoveryKernelSize = NormalizeOddKernelSize(Math.Max(3, kernelSize / 5));
            using var recoveryKernel = Cv2.GetStructuringElement(
                MorphShapes.Ellipse,
                new Size(recoveryKernelSize, recoveryKernelSize));
            using var recoveryMask = new Mat();
            using var recovered = new Mat();
            Cv2.Dilate(openedBodyMask, recoveryMask, recoveryKernel);
            Cv2.BitwiseAnd(sourceMask, recoveryMask, recovered);

            TryUseArtifactRemovalCandidate(
                openedBody,
                openedBodyMask,
                sourceMask,
                roi,
                original,
                minimumDimension,
                ref bestOpenedContour,
                ref bestOpenedSelectionScore);
            TryUseArtifactRemovalCandidate(
                TryFindLargestContour(recovered),
                recovered,
                sourceMask,
                roi,
                original,
                minimumDimension,
                ref bestRecoveredContour,
                ref bestRecoveredSelectionScore);
        }

        return bestOpenedContour is not null
            ? bestOpenedContour
            : bestRecoveredContour is not null
                ? bestRecoveredContour
                : RemoveLocalizedRadialArtifacts(contour, imageSize);
    }

    private static void TryUseArtifactRemovalCandidate(
        Point[]? localContour,
        Mat candidateMask,
        Mat sourceMask,
        Rect roi,
        ContourMetrics original,
        double minimumDimension,
        ref Point[]? bestContour,
        ref double bestSelectionScore)
    {
        if (!TryBuildSafeConnectedArtifactCandidate(localContour, roi, original, minimumDimension, out var cleaned, out _))
        {
            return;
        }

        var profile = AnalyzeRemovedContourArtifacts(sourceMask, candidateMask, minimumDimension);
        if (!TryCalculateMainContourSelectionScore(original, cleaned, profile, out var selectionScore) ||
            selectionScore >= bestSelectionScore)
        {
            return;
        }

        bestSelectionScore = selectionScore;
        bestContour = cleaned;
    }

    private static bool TryCalculateMainContourSelectionScore(
        ContourMetrics original,
        Point[] candidate,
        ContourArtifactRemovalProfile removalProfile,
        out double score)
    {
        var metrics = CalculateContourMetrics(candidate);
        var areaLoss = (original.Area - metrics.Area) / Math.Max(original.Area, 0.0001);
        var widthDiff = CalculateRatioDifference(metrics.Width, original.Width);
        var heightDiff = CalculateRatioDifference(metrics.Height, original.Height);
        var perimeterReduction = (original.Perimeter - metrics.Perimeter) / Math.Max(original.Perimeter, 0.0001);
        score = double.PositiveInfinity;
        var removesVisibleAttachment = areaLoss >= 0.0007 && perimeterReduction >= 0.032;
        var localizedAttachment =
            removalProfile.SignificantComponentCount > 0 &&
            removalProfile.LargestAreaShare >= 0.30 &&
            removalProfile.LargestExtentRatio <= 0.85 &&
            (removalProfile.SignificantComponentCount <= 12 || removalProfile.LargestAreaShare >= 0.50);
        if (!removesVisibleAttachment || !localizedAttachment)
        {
            return false;
        }

        score = areaLoss * 12.0 +
            widthDiff * 3.0 +
            heightDiff * 3.0 +
            removalProfile.LargestExtentRatio * 0.10 -
            Math.Min(perimeterReduction, 0.08);
        return true;
    }

    private static ContourArtifactRemovalProfile AnalyzeRemovedContourArtifacts(
        Mat sourceMask,
        Mat candidateMask,
        double minimumDimension)
    {
        using var candidateNot = new Mat();
        using var removed = new Mat();
        Cv2.BitwiseNot(candidateMask, candidateNot);
        Cv2.BitwiseAnd(sourceMask, candidateNot, removed);
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var count = Cv2.ConnectedComponentsWithStats(
            removed,
            labels,
            stats,
            centroids,
            PixelConnectivity.Connectivity8,
            MatType.CV_32S);
        var minimumComponentArea = Math.Max(16, minimumDimension * minimumDimension * 0.00002);
        var significantArea = 0;
        var significantCount = 0;
        var largestArea = 0;
        var largestExtent = 0.0;
        for (var label = 1; label < count; label++)
        {
            var area = stats.At<int>(label, (int)ConnectedComponentsTypes.Area);
            if (area < minimumComponentArea)
            {
                continue;
            }

            significantArea += area;
            significantCount++;
            if (area <= largestArea)
            {
                continue;
            }

            var width = stats.At<int>(label, (int)ConnectedComponentsTypes.Width);
            var height = stats.At<int>(label, (int)ConnectedComponentsTypes.Height);
            largestArea = area;
            largestExtent = Math.Max(width, height) / Math.Max(minimumDimension, 0.0001);
        }

        var largestShare = significantArea <= 0
            ? 0.0
            : largestArea / (double)significantArea;
        return new ContourArtifactRemovalProfile(
            significantCount,
            largestShare,
            largestExtent);
    }

    private static bool TryBuildSafeConnectedArtifactCandidate(
        Point[]? localContour,
        Rect roi,
        ContourMetrics original,
        double minimumDimension,
        out Point[] cleaned,
        out double score)
    {
        cleaned = Array.Empty<Point>();
        score = 0.0;
        if (localContour is null || localContour.Length < 3)
        {
            return false;
        }

        var candidate = localContour
            .Select(point => new Point(point.X + roi.X, point.Y + roi.Y))
            .ToArray();
        var metrics = CalculateContourMetrics(candidate);
        if (!IsSafeConnectedArtifactRemoval(original, metrics, minimumDimension))
        {
            return false;
        }

        var candidateScore = ScoreConnectedArtifactRemoval(original, metrics);
        if (candidateScore <= 0.0)
        {
            return false;
        }

        cleaned = candidate;
        score = candidateScore;
        return true;
    }

    private static Point[] RemoveLocalizedRadialArtifacts(
        Point[] contour,
        Size imageSize)
    {
        if (contour.Length < 3)
        {
            return contour;
        }

        var original = CalculateContourMetrics(contour);
        var minimumDimension = Math.Min(original.Width, original.Height);
        var axisRatio = original.Width / Math.Max(original.Height, 0.0001);
        if (minimumDimension < 80.0 || axisRatio > 1.04 || original.Circularity < 0.84)
        {
            return contour;
        }

        var bounds = Cv2.BoundingRect(contour);
        var roi = ExpandRect(bounds, padding: 16, imageSize);
        var localContour = contour
            .Select(point => new Point(point.X - roi.X, point.Y - roi.Y))
            .ToArray();
        var localCenterX = original.CenterX - roi.X;
        var localCenterY = original.CenterY - roi.Y;

        using var sourceMask = new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.Black);
        Cv2.FillPoly(sourceMask, new[] { localContour }, Scalar.White);

        var envelope = BuildRadialEnvelopeMask(
            roi.Size,
            localCenterX,
            localCenterY,
            BuildLocalizedRadialArtifactCaps(localContour, localCenterX, localCenterY, minimumDimension));
        using (envelope)
        using (var cleanedMask = new Mat())
        {
            Cv2.BitwiseAnd(sourceMask, envelope, cleanedMask);
            var localCleaned = TryFindLargestContour(cleanedMask);
            if (localCleaned is null || localCleaned.Length < 3)
            {
                return contour;
            }

            var cleaned = localCleaned
                .Select(point => new Point(point.X + roi.X, point.Y + roi.Y))
                .ToArray();
            var cleanedMetrics = CalculateContourMetrics(cleaned);
            if (!IsSafeLocalizedRadialArtifactRemoval(original, cleanedMetrics, minimumDimension))
            {
                return contour;
            }

            return cleaned;
        }
    }

    private static float[] BuildLocalizedRadialArtifactCaps(
        IReadOnlyList<Point> contour,
        double centerX,
        double centerY,
        double minimumDimension)
    {
        var signature = BuildRadiusSignature(
            contour,
            centerX,
            centerY,
            ArtifactRemovalRadiusSampleCount,
            smooth: false);
        var caps = signature.ToArray();
        MedianFilterCircularSignatureInPlace(caps, radius: 90);
        SmoothCircularAverageSignatureInPlace(caps, radius: 12);
        var padding = Math.Max(8.0, minimumDimension * 0.008);
        for (var index = 0; index < caps.Length; index++)
        {
            caps[index] += (float)padding;
        }

        return caps;
    }

    private static Mat BuildRadialEnvelopeMask(
        Size size,
        double centerX,
        double centerY,
        IReadOnlyList<float> radiusCaps)
    {
        var envelope = new Mat(size.Height, size.Width, MatType.CV_8UC1, Scalar.Black);
        if (radiusCaps.Count < 3)
        {
            return envelope;
        }

        var points = new Point[radiusCaps.Count];
        for (var index = 0; index < radiusCaps.Count; index++)
        {
            var angle = index * Math.PI * 2.0 / radiusCaps.Count;
            var x = centerX + Math.Cos(angle) * radiusCaps[index];
            var y = centerY + Math.Sin(angle) * radiusCaps[index];
            points[index] = new Point(
                Math.Clamp((int)Math.Round(x), 0, size.Width - 1),
                Math.Clamp((int)Math.Round(y), 0, size.Height - 1));
        }

        Cv2.FillPoly(envelope, new[] { points }, Scalar.White);
        return envelope;
    }

    private static IEnumerable<int> BuildArtifactRemovalKernelSizes(double minimumDimension)
    {
        var sizes = new[]
            {
                minimumDimension * 0.006,
                minimumDimension * 0.010,
                minimumDimension * 0.014,
                minimumDimension * 0.018,
                minimumDimension * 0.024,
                minimumDimension * 0.032,
                minimumDimension * 0.042,
                minimumDimension * 0.054,
                minimumDimension * 0.068,
                minimumDimension * 0.086,
                minimumDimension * 0.110
            }
            .Select(size => NormalizeOddKernelSize((int)Math.Round(size)))
            .Where(size => size >= 5)
            .Distinct()
            .OrderBy(size => size);

        foreach (var size in sizes)
        {
            yield return size;
        }
    }

    private static bool IsSafeConnectedArtifactRemoval(
        ContourMetrics original,
        ContourMetrics cleaned,
        double minimumDimension)
    {
        var areaRatio = cleaned.Area / Math.Max(original.Area, 0.0001);
        if (areaRatio is < 0.900 or > 1.002)
        {
            return false;
        }

        var centerShift = Distance(original.CenterX, original.CenterY, cleaned.CenterX, cleaned.CenterY);
        if (centerShift > Math.Max(6.0, minimumDimension * 0.080))
        {
            return false;
        }

        if (CalculateRatioDifference(cleaned.Width, original.Width) > 0.140 ||
            CalculateRatioDifference(cleaned.Height, original.Height) > 0.080)
        {
            return false;
        }

        return cleaned.Perimeter < original.Perimeter &&
            cleaned.Circularity >= original.Circularity * 0.98;
    }

    private static bool IsSafeLocalizedRadialArtifactRemoval(
        ContourMetrics original,
        ContourMetrics cleaned,
        double minimumDimension)
    {
        var areaRatio = cleaned.Area / Math.Max(original.Area, 0.0001);
        if (areaRatio is < 0.975 or > 1.002)
        {
            return false;
        }

        var centerShift = Distance(original.CenterX, original.CenterY, cleaned.CenterX, cleaned.CenterY);
        if (centerShift > Math.Max(5.0, minimumDimension * 0.012))
        {
            return false;
        }

        if (CalculateRatioDifference(cleaned.Width, original.Width) > 0.030 ||
            CalculateRatioDifference(cleaned.Height, original.Height) > 0.030)
        {
            return false;
        }

        return cleaned.Perimeter < original.Perimeter &&
            cleaned.Circularity >= original.Circularity * 0.985;
    }

    private static double ScoreConnectedArtifactRemoval(ContourMetrics original, ContourMetrics cleaned)
    {
        var perimeterReduction = (original.Perimeter - cleaned.Perimeter) / Math.Max(original.Perimeter, 0.0001);
        var circularityGain = (cleaned.Circularity - original.Circularity) / Math.Max(original.Circularity, 0.0001);
        var areaLoss = (original.Area - cleaned.Area) / Math.Max(original.Area, 0.0001);
        if (perimeterReduction <= 0.0 && circularityGain <= 0.0)
        {
            return 0.0;
        }

        return perimeterReduction * 4.0 + Math.Max(circularityGain, 0.0) - areaLoss * 2.0;
    }

    private static Point[]? TryFindLargestContour(Mat mask)
    {
        Cv2.FindContours(
            mask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);
        return contours
            .Select(contour => new { Contour = contour, Area = Math.Abs(Cv2.ContourArea(contour)) })
            .OrderByDescending(candidate => candidate.Area)
            .FirstOrDefault()
            ?.Contour;
    }

    private static ContourMetrics CalculateContourMetrics(Point[] contour)
    {
        var area = Math.Abs(Cv2.ContourArea(contour));
        var perimeter = Cv2.ArcLength(contour, closed: true);
        var moments = Cv2.Moments(contour);
        var centerX = Math.Abs(moments.M00) < 0.0001 ? 0.0 : moments.M10 / moments.M00;
        var centerY = Math.Abs(moments.M00) < 0.0001 ? 0.0 : moments.M01 / moments.M00;
        var shape = Cv2.MinAreaRect(contour);
        var width = Math.Max(shape.Size.Width, shape.Size.Height);
        var height = Math.Max(Math.Min(shape.Size.Width, shape.Size.Height), 0.0001);
        var circularity = perimeter <= 0.0
            ? 0.0
            : 4.0 * Math.PI * area / (perimeter * perimeter);
        return new ContourMetrics(area, perimeter, centerX, centerY, width, height, circularity);
    }

    private static Rect ExpandRect(Rect rect, int padding, Size imageSize)
    {
        var x = Math.Max(rect.X - padding, 0);
        var y = Math.Max(rect.Y - padding, 0);
        var right = Math.Min(rect.Right + padding, imageSize.Width);
        var bottom = Math.Min(rect.Bottom + padding, imageSize.Height);
        return new Rect(x, y, Math.Max(right - x, 1), Math.Max(bottom - y, 1));
    }

    private static int NormalizeOddKernelSize(int size)
    {
        var normalized = Math.Max(size, 3);
        return normalized % 2 == 0 ? normalized + 1 : normalized;
    }

    private static IEnumerable<ContourCandidate> FindContourCandidates(
        Mat binary,
        Point offset,
        VisionParameters parameters)
    {
        Cv2.FindContours(
            binary,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);
        var minArea = Math.Max(parameters.MinPartAreaPixels, 1.0);
        var maxArea = parameters.MaxPartAreaPixels;
        var imageArea = Math.Max(binary.Width * binary.Height, 1);
        foreach (var contour in contours)
        {
            var area = Math.Abs(Cv2.ContourArea(contour));
            if (area < minArea || area > maxArea || area > imageArea * 0.95)
            {
                continue;
            }

            var shape = Cv2.MinAreaRect(contour);
            var width = Math.Max(shape.Size.Width, shape.Size.Height);
            var height = Math.Max(Math.Min(shape.Size.Width, shape.Size.Height), 0.0001);
            if (width <= 2.0 || height <= 2.0)
            {
                continue;
            }

            var bounds = Cv2.BoundingRect(contour);
            var fillRatio = Math.Clamp(area / Math.Max(width * height, 0.0001), 0.0, 1.5);
            var touchesBorder =
                bounds.X <= 1 ||
                bounds.Y <= 1 ||
                bounds.Right >= binary.Width - 1 ||
                bounds.Bottom >= binary.Height - 1;
            yield return new ContourCandidate(
                contour,
                area,
                shape.Center.X + offset.X,
                shape.Center.Y + offset.Y,
                width,
                height,
                fillRatio,
                touchesBorder);
        }
    }

    private static ContourCandidate SelectUnreferencedContourCandidate(
        IReadOnlyList<ContourCandidate> candidates,
        Size imageSize,
        Point offset)
    {
        var imageArea = Math.Max(imageSize.Width * imageSize.Height, 1.0);
        var halfWidth = imageSize.Width / 2.0;
        var halfHeight = imageSize.Height / 2.0;
        var imageCenterX = offset.X + halfWidth;
        var imageCenterY = offset.Y + halfHeight;
        var maxCenterDistance = Math.Max(
            Math.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight),
            1.0);
        return candidates
            .OrderByDescending(candidate =>
                ScoreUnreferencedContourCandidate(
                    candidate,
                    imageArea,
                    imageCenterX,
                    imageCenterY,
                    maxCenterDistance))
            .ThenByDescending(candidate => candidate.Area)
            .First();
    }

    private static double ScoreUnreferencedContourCandidate(
        ContourCandidate candidate,
        double imageArea,
        double imageCenterX,
        double imageCenterY,
        double maxCenterDistance)
    {
        var areaFraction = candidate.Area / imageArea;
        var distanceToCenter = Distance(candidate.CenterX, candidate.CenterY, imageCenterX, imageCenterY);
        var centerScore = 1.0 - Math.Clamp(distanceToCenter / maxCenterDistance, 0.0, 1.0);
        var areaScore = Math.Log10(Math.Max(candidate.Area, 1.0));
        var borderPenalty = candidate.TouchesBorder ? 4.0 : 0.0;
        var oversizedPenalty = areaFraction > 0.60 ? (areaFraction - 0.60) * 6.0 : 0.0;
        var tinyPenalty = areaFraction < 0.003 ? (0.003 - areaFraction) * 200.0 : 0.0;
        var fillPenalty = candidate.FillRatio is < 0.20 or > 0.95 ? 0.75 : 0.0;
        return areaScore + centerScore * 1.25 - borderPenalty - oversizedPenalty - tinyPenalty - fillPenalty;
    }

    private static ContourCandidate SelectReferencedContourCandidate(
        IReadOnlyList<ContourCandidate> candidates,
        ContourFeatureExtraction referenceFeature)
    {
        var referenceFillRatio = referenceFeature.AreaPixels /
            Math.Max(referenceFeature.WidthPixels * referenceFeature.HeightPixels, 0.0001);
        var referenceSize = Math.Max(
            Math.Max(referenceFeature.WidthPixels, referenceFeature.HeightPixels),
            1.0);
        return candidates
            .OrderBy(candidate => ScoreReferencedContourCandidate(candidate, referenceFeature, referenceFillRatio, referenceSize))
            .ThenByDescending(candidate => candidate.Area)
            .First();
    }

    private static double ScoreReferencedContourCandidate(
        ContourCandidate candidate,
        ContourFeatureExtraction referenceFeature,
        double referenceFillRatio,
        double referenceSize)
    {
        var areaDifference = CalculateRatioDifference(candidate.Area, referenceFeature.AreaPixels);
        var widthDifference = CalculateRatioDifference(candidate.Width, referenceFeature.WidthPixels);
        var heightDifference = CalculateRatioDifference(candidate.Height, referenceFeature.HeightPixels);
        var fillDifference = CalculateRatioDifference(candidate.FillRatio, referenceFillRatio);
        var centerDistance = Distance(
            candidate.CenterX,
            candidate.CenterY,
            referenceFeature.CenterXPixel,
            referenceFeature.CenterYPixel);
        var centerDifference = Math.Min(centerDistance / referenceSize, 2.0);
        var borderPenalty = candidate.TouchesBorder ? 2.0 : 0.0;
        return
            areaDifference * 0.36 +
            widthDifference * 0.24 +
            heightDifference * 0.24 +
            fillDifference * 0.10 +
            centerDifference * 0.06 +
            borderPenalty;
    }

    private static double CalculateRatioDifference(double current, double reference)
    {
        var denominator = Math.Max(Math.Abs(reference), 0.0001);
        return Math.Abs(current - reference) / denominator;
    }

    private static double Distance(double leftX, double leftY, double rightX, double rightY)
    {
        var dx = leftX - rightX;
        var dy = leftY - rightY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Point[] FindLargestContour(Mat binary, VisionParameters parameters)
    {
        Cv2.FindContours(
            binary,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);
        var minArea = Math.Max(parameters.MinPartAreaPixels, 1.0);
        var maxArea = parameters.MaxPartAreaPixels;
        var bestContour = contours
            .Select(contour => new { Contour = contour, Area = Cv2.ContourArea(contour) })
            .Where(candidate => candidate.Area >= minArea && candidate.Area <= maxArea)
            .OrderByDescending(candidate => candidate.Area)
            .FirstOrDefault();
        if (bestContour is null)
        {
            throw new InvalidOperationException("未找到满足面积范围的工件外轮廓。");
        }

        return bestContour.Contour;
    }

    private static float[] BuildRadiusSignature(
        IReadOnlyList<Point> contour,
        double centerX,
        double centerY,
        int sampleCount,
        bool smooth)
    {
        var signature = new float[sampleCount];
        if (contour.Count < 3)
        {
            return signature;
        }

        for (var index = 0; index < contour.Count; index++)
        {
            var start = contour[index];
            var end = contour[(index + 1) % contour.Count];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy)));
            for (var step = 0; step <= steps; step++)
            {
                var t = (double)step / steps;
                var x = start.X + dx * t;
                var y = start.Y + dy * t;
                var radiusX = x - centerX;
                var radiusY = y - centerY;
                var radius = Math.Sqrt(radiusX * radiusX + radiusY * radiusY);
                if (radius <= 0.0001)
                {
                    continue;
                }

                var angle = Math.Atan2(radiusY, radiusX);
                if (angle < 0)
                {
                    angle += Math.PI * 2.0;
                }

                var signatureIndex = Math.Clamp(
                    (int)Math.Round(angle * sampleCount / (Math.PI * 2.0)) % sampleCount,
                    0,
                    sampleCount - 1);
                signature[signatureIndex] = Math.Max(signature[signatureIndex], (float)radius);
            }
        }

        FillMissingCircularSignatureBins(signature);
        if (smooth)
        {
            MedianFilterCircularSignatureInPlace(signature, radius: 1);
            SmoothCircularAverageSignatureInPlace(signature, radius: 2);
        }

        return signature;
    }

    private static Point2d[] BuildSampledContourPoints(
        IReadOnlyList<Point> contour,
        Point offset,
        int maximumPointCount)
    {
        if (contour.Count == 0)
        {
            return Array.Empty<Point2d>();
        }

        var densePoints = new List<Point2d>();
        for (var index = 0; index < contour.Count; index++)
        {
            var start = contour[index];
            var end = contour[(index + 1) % contour.Count];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy)));
            for (var step = 0; step < steps; step++)
            {
                var t = (double)step / steps;
                densePoints.Add(new Point2d(
                    start.X + dx * t + offset.X,
                    start.Y + dy * t + offset.Y));
            }
        }

        if (densePoints.Count <= maximumPointCount)
        {
            return densePoints.ToArray();
        }

        var sampled = new Point2d[maximumPointCount];
        var stride = (double)densePoints.Count / maximumPointCount;
        for (var index = 0; index < sampled.Length; index++)
        {
            sampled[index] = densePoints[(int)Math.Floor(index * stride)];
        }

        return sampled;
    }

    public static Point2d[] MirrorContourPoints(
        IReadOnlyList<Point2d> points,
        double centerX)
    {
        var mirrored = new Point2d[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            mirrored[index] = new Point2d(
                centerX - (point.X - centerX),
                point.Y);
        }

        return mirrored;
    }

    private static (double Ratio, double AngleDegrees) CalculatePca(IReadOnlyList<Point> contour)
    {
        if (contour.Count < 3)
        {
            return (1.0, 0.0);
        }

        var meanX = contour.Average(point => (double)point.X);
        var meanY = contour.Average(point => (double)point.Y);
        var xx = 0.0;
        var xy = 0.0;
        var yy = 0.0;
        foreach (var point in contour)
        {
            var dx = point.X - meanX;
            var dy = point.Y - meanY;
            xx += dx * dx;
            xy += dx * dy;
            yy += dy * dy;
        }

        xx /= contour.Count;
        xy /= contour.Count;
        yy /= contour.Count;
        var trace = xx + yy;
        var determinant = xx * yy - xy * xy;
        var root = Math.Sqrt(Math.Max(trace * trace / 4.0 - determinant, 0.0));
        var major = trace / 2.0 + root;
        var minor = Math.Max(trace / 2.0 - root, 0.0001);
        var angleDegrees = Math.Atan2(2.0 * xy, xx - yy) * 90.0 / Math.PI;
        return (
            Math.Sqrt(Math.Max(major, 0.0001) / minor),
            JuliMvs.Core.AngleMath.NormalizeDegrees180(angleDegrees));
    }

    private static (bool HasEllipse, double Ratio, double AngleDegrees) CalculateEllipseAxis(IReadOnlyList<Point> contour)
    {
        if (contour.Count < 5)
        {
            return (false, 1.0, 0.0);
        }

        try
        {
            var ellipse = Cv2.FitEllipse(contour);
            var major = Math.Max(ellipse.Size.Width, ellipse.Size.Height);
            var minor = Math.Max(Math.Min(ellipse.Size.Width, ellipse.Size.Height), 0.0001);
            var angle = (double)ellipse.Angle;
            if (ellipse.Size.Height > ellipse.Size.Width)
            {
                angle += 90.0;
            }

            return (
                true,
                major / minor,
                JuliMvs.Core.AngleMath.NormalizeDegrees180(angle));
        }
        catch (OpenCVException)
        {
            return (false, 1.0, 0.0);
        }
    }

    private static ContourAxisFeature BuildAxisFeature(
        (double Ratio, double AngleDegrees) edgePca,
        (bool HasEllipse, double Ratio, double AngleDegrees) ellipse)
    {
        var meanAngle = ellipse.HasEllipse
            ? AverageAxisAngleDegrees(edgePca.AngleDegrees, ellipse.AngleDegrees)
            : edgePca.AngleDegrees;
        var meanRatio = ellipse.HasEllipse
            ? (edgePca.Ratio + ellipse.Ratio) / 2.0
            : edgePca.Ratio;
        var spread = ellipse.HasEllipse
            ? Math.Abs(JuliMvs.Core.AngleMath.NormalizeDeltaDegrees(edgePca.AngleDegrees, ellipse.AngleDegrees))
            : 0.0;
        return new ContourAxisFeature(
            RegionRatio: edgePca.Ratio,
            RegionAngleDegrees: edgePca.AngleDegrees,
            EdgeRatio: edgePca.Ratio,
            EdgeAngleDegrees: edgePca.AngleDegrees,
            EllipseRatio: ellipse.HasEllipse ? ellipse.Ratio : 1.0,
            EllipseAngleDegrees: ellipse.HasEllipse ? ellipse.AngleDegrees : 0.0,
            HasEllipse: ellipse.HasEllipse,
            MeanRatio: meanRatio,
            MaximumAngleSpreadDegrees: spread);
    }

    private static double AverageAxisAngleDegrees(double leftDegrees, double rightDegrees)
    {
        var leftRadians = leftDegrees * 2.0 * Math.PI / 180.0;
        var rightRadians = rightDegrees * 2.0 * Math.PI / 180.0;
        var x = Math.Cos(leftRadians) + Math.Cos(rightRadians);
        var y = Math.Sin(leftRadians) + Math.Sin(rightRadians);
        if (Math.Abs(x) < 0.000001 && Math.Abs(y) < 0.000001)
        {
            return JuliMvs.Core.AngleMath.NormalizeDegrees180(leftDegrees);
        }

        return JuliMvs.Core.AngleMath.NormalizeDegrees180(Math.Atan2(y, x) * 90.0 / Math.PI);
    }

    private static double CalculateRadiusSignalPixels(IReadOnlyList<float> signature)
    {
        if (signature.Count == 0)
        {
            return 0.0;
        }

        var mean = signature.Average(value => (double)value);
        var variance = signature
            .Select(value => ((double)value - mean) * ((double)value - mean))
            .DefaultIfEmpty(0.0)
            .Average();
        return Math.Sqrt(Math.Max(variance, 0.0));
    }

    private static float[] NormalizeRadiusSignature(IReadOnlyList<float> signature)
    {
        var normalized = signature.ToArray();
        if (normalized.Length == 0)
        {
            return normalized;
        }

        var mean = normalized.Average(value => (double)value);
        var stdDev = Math.Sqrt(Math.Max(
            normalized
                .Select(value => ((double)value - mean) * ((double)value - mean))
                .DefaultIfEmpty(0.0)
                .Average(),
            0.0));
        if (stdDev < 0.000001)
        {
            Array.Fill(normalized, 0f);
            return normalized;
        }

        for (var index = 0; index < normalized.Length; index++)
        {
            normalized[index] = (float)((normalized[index] - mean) / stdDev);
        }

        return normalized;
    }

    private static double CalculateCircularCorrelation(
        IReadOnlyList<float> currentSignature,
        IReadOnlyList<float> templateSignature,
        int shift,
        int sampleCount)
    {
        var dot = 0.0;
        var currentEnergy = 0.0;
        var templateEnergy = 0.0;
        for (var index = 0; index < sampleCount; index++)
        {
            var current = currentSignature[index];
            var template = templateSignature[PositiveModulo(index - shift, sampleCount)];
            dot += current * template;
            currentEnergy += current * current;
            templateEnergy += template * template;
        }

        var denominator = Math.Sqrt(currentEnergy * templateEnergy);
        if (denominator < 0.000001)
        {
            return 0.0;
        }

        return (dot / denominator + 1.0) / 2.0;
    }

    private static void FillMissingCircularSignatureBins(float[] signature)
    {
        if (signature.Length == 0 || signature.All(value => value <= 0f))
        {
            return;
        }

        for (var index = 0; index < signature.Length; index++)
        {
            if (signature[index] > 0f)
            {
                continue;
            }

            var previousIndex = FindNearestCircularSignatureValue(signature, index, -1);
            var nextIndex = FindNearestCircularSignatureValue(signature, index, 1);
            signature[index] = previousIndex >= 0 && nextIndex >= 0
                ? (signature[previousIndex] + signature[nextIndex]) / 2f
                : previousIndex >= 0
                    ? signature[previousIndex]
                    : signature[nextIndex];
        }
    }

    private static int FindNearestCircularSignatureValue(float[] signature, int startIndex, int direction)
    {
        for (var offset = 1; offset < signature.Length; offset++)
        {
            var index = PositiveModulo(startIndex + offset * direction, signature.Length);
            if (signature[index] > 0f)
            {
                return index;
            }
        }

        return -1;
    }

    private static void SmoothCircularAverageSignatureInPlace(float[] signature, int radius)
    {
        if (signature.Length < 3 || radius <= 0)
        {
            return;
        }

        var copy = signature.ToArray();
        var windowSize = radius * 2 + 1;
        for (var index = 0; index < signature.Length; index++)
        {
            var sum = 0.0;
            for (var offset = -radius; offset <= radius; offset++)
            {
                sum += copy[PositiveModulo(index + offset, copy.Length)];
            }

            signature[index] = (float)(sum / windowSize);
        }
    }

    private static void MedianFilterCircularSignatureInPlace(float[] signature, int radius)
    {
        if (signature.Length < 3 || radius <= 0)
        {
            return;
        }

        var copy = signature.ToArray();
        var window = new float[radius * 2 + 1];
        for (var index = 0; index < signature.Length; index++)
        {
            for (var offset = -radius; offset <= radius; offset++)
            {
                window[offset + radius] = copy[PositiveModulo(index + offset, copy.Length)];
            }

            Array.Sort(window);
            signature[index] = window[window.Length / 2];
        }
    }

    private static int PositiveModulo(int value, int modulo)
    {
        var result = value % modulo;
        return result < 0 ? result + modulo : result;
    }

    private static int CircularShiftDistance(int left, int right, int modulo)
    {
        var raw = PositiveModulo(left - right, modulo);
        return raw > modulo / 2 ? raw - modulo : raw;
    }

    private sealed record ContourCandidate(
        Point[] Contour,
        double Area,
        double CenterX,
        double CenterY,
        double Width,
        double Height,
        double FillRatio,
        bool TouchesBorder);

    private sealed record ContourMetrics(
        double Area,
        double Perimeter,
        double CenterX,
        double CenterY,
        double Width,
        double Height,
        double Circularity);

    private sealed record ContourArtifactRemovalProfile(
        int SignificantComponentCount,
        double LargestAreaShare,
        double LargestExtentRatio);

}

public sealed record ContourFeatureExtraction(
    double CenterXPixel,
    double CenterYPixel,
    double AreaPixels,
    double PerimeterPixels,
    double WidthPixels,
    double HeightPixels,
    double AxisRatio,
    double PcaRatio,
    double PcaAngleDegrees,
    double Circularity,
    double RadiusSignalPixels,
    IReadOnlyList<float> RadiusSignature,
    double NormalizedRadiusSignalPixels,
    IReadOnlyList<float> NormalizedRadiusSignature,
    IReadOnlyList<Point2d> ContourPoints,
    int ImageWidthPixels,
    int ImageHeightPixels,
    ContourAxisFeature AxisFeature,
    AutoAngleStrategyDecision Strategy);

public sealed record ContourAxisFeature(
    double RegionRatio,
    double RegionAngleDegrees,
    double EdgeRatio,
    double EdgeAngleDegrees,
    double EllipseRatio,
    double EllipseAngleDegrees,
    bool HasEllipse,
    double MeanRatio,
    double MaximumAngleSpreadDegrees)
{
    public double MeanAngleDegrees
    {
        get
        {
            if (!HasEllipse)
            {
                return JuliMvs.Core.AngleMath.NormalizeDegrees180(RegionAngleDegrees);
            }

            var regionRadians = RegionAngleDegrees * 2.0 * Math.PI / 180.0;
            var ellipseRadians = EllipseAngleDegrees * 2.0 * Math.PI / 180.0;
            var x = Math.Cos(regionRadians) + Math.Cos(ellipseRadians);
            var y = Math.Sin(regionRadians) + Math.Sin(ellipseRadians);
            if (Math.Abs(x) < 0.000001 && Math.Abs(y) < 0.000001)
            {
                return JuliMvs.Core.AngleMath.NormalizeDegrees180(RegionAngleDegrees);
            }

            return JuliMvs.Core.AngleMath.NormalizeDegrees180(Math.Atan2(y, x) * 90.0 / Math.PI);
        }
    }
}

public sealed record ContourRadiusMatch(
    int Shift,
    double AngleDegrees,
    double ErrorPixels,
    double ErrorNormalized);

public sealed record ContourRadiusMatchWithAlternative(
    int Shift,
    double AngleDegrees,
    double ErrorPixels,
    double ErrorNormalized,
    int AlternativeShift,
    double AlternativeAngleDegrees,
    double AlternativeErrorPixels,
    double AlternativeErrorNormalized);

public sealed record ContourCorrelationMatch(
    int Shift,
    double AngleDegrees,
    double Score,
    int AlternativeShift,
    double AlternativeAngleDegrees,
    double AlternativeScore);
