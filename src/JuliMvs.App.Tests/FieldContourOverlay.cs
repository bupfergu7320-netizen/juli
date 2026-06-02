using System.Globalization;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Tests;

internal static class FieldContourOverlay
{
    public static void RunSynthetic(string scenario, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var image = scenario switch
        {
            "localized-attachment" => BuildLocalizedAttachmentImage(),
            "real-tab" => BuildRealTabImage(),
            "rectangular-attachment" => BuildRectangularAttachmentImage(),
            _ => throw new ArgumentException($"Unknown synthetic contour scenario: {scenario}", nameof(scenario))
        };

        var sourcePath = Path.Combine(outputDirectory, scenario + "-source.png");
        Cv2.ImWrite(sourcePath, image);
        Console.WriteLine($"source={sourcePath}");
        WriteOverlay(image, scenario, outputDirectory);
    }

    public static void Run(string imagePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (image.Empty())
        {
            throw new InvalidOperationException($"Image read failed: {imagePath}");
        }

        WriteOverlay(image, Path.GetFileNameWithoutExtension(imagePath), outputDirectory);
    }

    public static void RunDebug(string imagePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (image.Empty())
        {
            throw new InvalidOperationException($"Image read failed: {imagePath}");
        }

        var name = Path.GetFileNameWithoutExtension(imagePath);
        WriteOverlay(image, name, outputDirectory);
        WriteDebugCrops(image, name, outputDirectory);
    }

    public static void RunArtifactCandidates(string imagePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (image.Empty())
        {
            throw new InvalidOperationException($"Image read failed: {imagePath}");
        }

        var name = Path.GetFileNameWithoutExtension(imagePath);
        WriteArtifactCandidateOverlays(image, name, outputDirectory);
    }

    public static void RunArtifactStages(string imagePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (image.Empty())
        {
            throw new InvalidOperationException($"Image read failed: {imagePath}");
        }

        var name = Path.GetFileNameWithoutExtension(imagePath);
        WriteArtifactStageOverlays(image, name, outputDirectory);
    }

    public static void RunArtifactDiff(string imagePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (image.Empty())
        {
            throw new InvalidOperationException($"Image read failed: {imagePath}");
        }

        var name = Path.GetFileNameWithoutExtension(imagePath);
        WriteArtifactDiffOverlays(image, name, outputDirectory);
    }

    private static void WriteOverlay(Mat image, string name, string outputDirectory)
    {
        var parameters = VisionParameters.Default with
        {
            BinaryThreshold = 0,
            MinPartAreaPixels = 10_000
        };

        var feature = new ContourFeatureExtractor().Extract(image, parameters);
        using var overlay = image.Channels() == 1
            ? image.CvtColor(ColorConversionCodes.GRAY2BGR)
            : image.Clone();
        var points = feature.ContourPoints
            .Select(point => new Point(
                Math.Clamp((int)Math.Round(point.X), 0, overlay.Width - 1),
                Math.Clamp((int)Math.Round(point.Y), 0, overlay.Height - 1)))
            .ToArray();
        if (points.Length >= 3)
        {
            Cv2.Polylines(overlay, new[] { points }, isClosed: true, Scalar.LimeGreen, 5, LineTypes.AntiAlias);
            Cv2.DrawMarker(
                overlay,
                new Point((int)Math.Round(feature.CenterXPixel), (int)Math.Round(feature.CenterYPixel)),
                Scalar.Yellow,
                MarkerTypes.Cross,
                56,
                5,
                LineTypes.AntiAlias);
        }

        var overlayPath = Path.Combine(outputDirectory, name + "-contour.png");
        Cv2.ImWrite(overlayPath, overlay);
        Console.WriteLine($"overlay={overlayPath}");
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"center=({feature.CenterXPixel:F1},{feature.CenterYPixel:F1}) area={feature.AreaPixels:F0} " +
            $"width={feature.WidthPixels:F0} height={feature.HeightPixels:F0} circularity={feature.Circularity:F3} " +
            $"radius_signal={feature.RadiusSignalPixels:F2} normalized_radius_signal={feature.NormalizedRadiusSignalPixels:F2} " +
            $"strategy={feature.Strategy.ShapeClass}/{feature.Strategy.Method}"));
    }

    private static void WriteDebugCrops(Mat image, string name, string outputDirectory)
    {
        var crop = BuildRightSideCrop(image.Size());
        using var originalCrop = new Mat(image, crop);
        var originalPath = Path.Combine(outputDirectory, name + "-right-original.png");
        Cv2.ImWrite(originalPath, originalCrop);

        using var gray = image.Channels() == 1
            ? image.Clone()
            : image.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0.0);
        using var binary = new Mat();
        Cv2.Threshold(blurred, binary, 0.0, 255.0, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        using var smallKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(binary, binary, MorphTypes.Open, smallKernel);
        Cv2.MorphologyEx(binary, binary, MorphTypes.Close, smallKernel);
        using var binaryCrop = new Mat(binary, crop);
        var binaryPath = Path.Combine(outputDirectory, name + "-right-binary.png");
        Cv2.ImWrite(binaryPath, binaryCrop);

        var overlayPath = Path.Combine(outputDirectory, name + "-contour.png");
        using var overlay = Cv2.ImRead(overlayPath, ImreadModes.Color);
        if (!overlay.Empty())
        {
            using var overlayCrop = new Mat(overlay, crop);
            var overlayCropPath = Path.Combine(outputDirectory, name + "-right-contour.png");
            Cv2.ImWrite(overlayCropPath, overlayCrop);
            Console.WriteLine($"right_overlay={overlayCropPath}");
        }

        Console.WriteLine($"right_original={originalPath}");
        Console.WriteLine($"right_binary={binaryPath}");
    }

    private static Rect BuildRightSideCrop(Size imageSize)
    {
        var width = Math.Min(1200, imageSize.Width);
        var height = Math.Min(1700, imageSize.Height);
        var x = Math.Clamp(3500, 0, Math.Max(imageSize.Width - width, 0));
        var y = Math.Clamp(1200, 0, Math.Max(imageSize.Height - height, 0));
        return new Rect(x, y, width, height);
    }

    private static void WriteArtifactCandidateOverlays(Mat image, string name, string outputDirectory)
    {
        using var gray = image.Channels() == 1
            ? image.Clone()
            : image.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0.0);
        using var binary = new Mat();
        Cv2.Threshold(blurred, binary, 0.0, 255.0, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        using var smallKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(binary, binary, MorphTypes.Open, smallKernel);
        Cv2.MorphologyEx(binary, binary, MorphTypes.Close, smallKernel);

        var sourceContour = FindLargestContour(binary);
        var sourceBounds = Cv2.BoundingRect(sourceContour);
        var minimumDimension = Math.Min(sourceBounds.Width, sourceBounds.Height);
        var kernelSizes = new[]
        {
            15,
            31,
            51,
            81,
            121,
            181,
            241,
            321,
            421,
            561,
            701
        };
        var maximumKernelSize = kernelSizes.Max();
        var roi = ExpandRect(sourceBounds, Math.Max(maximumKernelSize * 2, 12), image.Size());
        var localSourceContour = sourceContour
            .Select(point => new Point(point.X - roi.X, point.Y - roi.Y))
            .ToArray();
        using var sourceMask = new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.Black);
        Cv2.FillPoly(sourceMask, new[] { localSourceContour }, Scalar.White);

        foreach (var kernelSize in kernelSizes)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(kernelSize, kernelSize));
            using var opened = new Mat();
            Cv2.MorphologyEx(sourceMask, opened, MorphTypes.Open, kernel);
            var localOpenedContour = FindLargestContourOrNull(opened);
            if (localOpenedContour is null)
            {
                continue;
            }

            var openedContour = localOpenedContour
                .Select(point => new Point(point.X + roi.X, point.Y + roi.Y))
                .ToArray();
            using var overlay = image.Channels() == 1
                ? image.CvtColor(ColorConversionCodes.GRAY2BGR)
                : image.Clone();
            Cv2.Polylines(overlay, new[] { sourceContour }, isClosed: true, Scalar.Red, 3, LineTypes.AntiAlias);
            Cv2.Polylines(overlay, new[] { openedContour }, isClosed: true, Scalar.LimeGreen, 5, LineTypes.AntiAlias);
            var metrics = string.Create(
                CultureInfo.InvariantCulture,
                $"k={kernelSize} area={Math.Abs(Cv2.ContourArea(openedContour)):F0} " +
                $"min_dimension={minimumDimension}");
            Cv2.PutText(
                overlay,
                metrics,
                new Point(80, 120),
                HersheyFonts.HersheySimplex,
                2.0,
                Scalar.Yellow,
                5,
                LineTypes.AntiAlias);
            var path = Path.Combine(outputDirectory, $"{name}-artifact-k{kernelSize}.png");
            Cv2.ImWrite(path, overlay);
            Console.WriteLine($"candidate={path}");
        }
    }

    private static void WriteArtifactStageOverlays(Mat image, string name, string outputDirectory)
    {
        using var gray = image.Channels() == 1
            ? image.Clone()
            : image.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0.0);
        using var binary = new Mat();
        Cv2.Threshold(blurred, binary, 0.0, 255.0, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        using var smallKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(binary, binary, MorphTypes.Open, smallKernel);
        Cv2.MorphologyEx(binary, binary, MorphTypes.Close, smallKernel);

        var sourceContour = FindLargestContour(binary);
        var sourceBounds = Cv2.BoundingRect(sourceContour);
        var minimumDimension = Math.Min(sourceBounds.Width, sourceBounds.Height);
        var kernelSizes = new[]
        {
            NormalizeOddKernelSize((int)Math.Round(minimumDimension * 0.024)),
            NormalizeOddKernelSize((int)Math.Round(minimumDimension * 0.042)),
            NormalizeOddKernelSize((int)Math.Round(minimumDimension * 0.068)),
            NormalizeOddKernelSize((int)Math.Round(minimumDimension * 0.110))
        }
            .Distinct()
            .OrderBy(size => size)
            .ToArray();
        var maximumKernelSize = kernelSizes.Max();
        var roi = ExpandRect(sourceBounds, Math.Max(maximumKernelSize * 2, 12), image.Size());
        var localSourceContour = sourceContour
            .Select(point => new Point(point.X - roi.X, point.Y - roi.Y))
            .ToArray();
        using var sourceMask = new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.Black);
        Cv2.FillPoly(sourceMask, new[] { localSourceContour }, Scalar.White);

        foreach (var kernelSize in kernelSizes)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(kernelSize, kernelSize));
            using var opened = new Mat();
            Cv2.MorphologyEx(sourceMask, opened, MorphTypes.Open, kernel);
            var localOpenedContour = FindLargestContourOrNull(opened);
            if (localOpenedContour is null)
            {
                continue;
            }

            using var openedBodyMask = new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.Black);
            Cv2.FillPoly(openedBodyMask, new[] { localOpenedContour }, Scalar.White);
            var recoveryKernelSize = NormalizeOddKernelSize(Math.Max(3, kernelSize / 5));
            using var recoveryKernel = Cv2.GetStructuringElement(
                MorphShapes.Ellipse,
                new Size(recoveryKernelSize, recoveryKernelSize));
            using var recoveryMask = new Mat();
            using var recovered = new Mat();
            Cv2.Dilate(openedBodyMask, recoveryMask, recoveryKernel);
            Cv2.BitwiseAnd(sourceMask, recoveryMask, recovered);
            var localRecoveredContour = FindLargestContourOrNull(recovered);

            var openedContour = localOpenedContour
                .Select(point => new Point(point.X + roi.X, point.Y + roi.Y))
                .ToArray();
            var recoveredContour = localRecoveredContour?
                .Select(point => new Point(point.X + roi.X, point.Y + roi.Y))
                .ToArray();
            WriteCandidateMetrics("opened", kernelSize, sourceContour, openedContour, minimumDimension);
            if (recoveredContour is not null)
            {
                WriteCandidateMetrics("recovered", kernelSize, sourceContour, recoveredContour, minimumDimension);
            }

            using var overlay = image.Channels() == 1
                ? image.CvtColor(ColorConversionCodes.GRAY2BGR)
                : image.Clone();
            Cv2.Polylines(overlay, new[] { sourceContour }, isClosed: true, Scalar.Red, 3, LineTypes.AntiAlias);
            Cv2.Polylines(overlay, new[] { openedContour }, isClosed: true, Scalar.Cyan, 5, LineTypes.AntiAlias);
            if (recoveredContour is not null)
            {
                Cv2.Polylines(overlay, new[] { recoveredContour }, isClosed: true, Scalar.Magenta, 3, LineTypes.AntiAlias);
            }

            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"k={kernelSize} recover={recoveryKernelSize} red=source cyan=opened magenta=recovered");
            Cv2.PutText(
                overlay,
                text,
                new Point(80, 120),
                HersheyFonts.HersheySimplex,
                1.7,
                Scalar.Yellow,
                5,
                LineTypes.AntiAlias);
            var path = Path.Combine(outputDirectory, $"{name}-stages-k{kernelSize}.png");
            Cv2.ImWrite(path, overlay);
            Console.WriteLine($"stage={path}");
        }
    }

    private static void WriteArtifactDiffOverlays(Mat image, string name, string outputDirectory)
    {
        using var gray = image.Channels() == 1
            ? image.Clone()
            : image.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0.0);
        using var binary = new Mat();
        Cv2.Threshold(blurred, binary, 0.0, 255.0, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        using var smallKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(binary, binary, MorphTypes.Open, smallKernel);
        Cv2.MorphologyEx(binary, binary, MorphTypes.Close, smallKernel);

        var sourceContour = FindLargestContour(binary);
        var sourceBounds = Cv2.BoundingRect(sourceContour);
        var minimumDimension = Math.Min(sourceBounds.Width, sourceBounds.Height);
        var kernelSize = NormalizeOddKernelSize((int)Math.Round(minimumDimension * 0.068));
        var maximumKernelSize = NormalizeOddKernelSize((int)Math.Round(minimumDimension * 0.110));
        var roi = ExpandRect(sourceBounds, Math.Max(maximumKernelSize * 2, 12), image.Size());
        var localSourceContour = sourceContour
            .Select(point => new Point(point.X - roi.X, point.Y - roi.Y))
            .ToArray();
        using var sourceMask = new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.Black);
        Cv2.FillPoly(sourceMask, new[] { localSourceContour }, Scalar.White);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(kernelSize, kernelSize));
        using var opened = new Mat();
        Cv2.MorphologyEx(sourceMask, opened, MorphTypes.Open, kernel);
        var localOpenedContour = FindLargestContourOrNull(opened);
        if (localOpenedContour is null)
        {
            throw new InvalidOperationException("No opened contour found.");
        }

        using var openedBodyMask = new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.Black);
        Cv2.FillPoly(openedBodyMask, new[] { localOpenedContour }, Scalar.White);
        var recoveryKernelSize = NormalizeOddKernelSize(Math.Max(3, kernelSize / 5));
        using var recoveryKernel = Cv2.GetStructuringElement(
            MorphShapes.Ellipse,
            new Size(recoveryKernelSize, recoveryKernelSize));
        using var recoveryMask = new Mat();
        using var recovered = new Mat();
        Cv2.Dilate(openedBodyMask, recoveryMask, recoveryKernel);
        Cv2.BitwiseAnd(sourceMask, recoveryMask, recovered);
        var localRecoveredContour = FindLargestContourOrNull(recovered);
        if (localRecoveredContour is null)
        {
            throw new InvalidOperationException("No recovered contour found.");
        }

        using var openedNot = new Mat();
        using var diff = new Mat();
        Cv2.BitwiseNot(openedBodyMask, openedNot);
        Cv2.BitwiseAnd(recovered, openedNot, diff);

        using var overlay = image.Channels() == 1
            ? image.CvtColor(ColorConversionCodes.GRAY2BGR)
            : image.Clone();
        var openedContour = localOpenedContour
            .Select(point => new Point(point.X + roi.X, point.Y + roi.Y))
            .ToArray();
        var recoveredContour = localRecoveredContour
            .Select(point => new Point(point.X + roi.X, point.Y + roi.Y))
            .ToArray();
        Cv2.Polylines(overlay, new[] { sourceContour }, isClosed: true, Scalar.Red, 3, LineTypes.AntiAlias);
        Cv2.Polylines(overlay, new[] { openedContour }, isClosed: true, Scalar.Cyan, 5, LineTypes.AntiAlias);
        Cv2.Polylines(overlay, new[] { recoveredContour }, isClosed: true, Scalar.Magenta, 3, LineTypes.AntiAlias);

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var count = Cv2.ConnectedComponentsWithStats(
            diff,
            labels,
            stats,
            centroids,
            PixelConnectivity.Connectivity8,
            MatType.CV_32S);
        for (var label = 1; label < count; label++)
        {
            var area = stats.At<int>(label, (int)ConnectedComponentsTypes.Area);
            if (area < Math.Max(20, minimumDimension * minimumDimension * 0.000004))
            {
                continue;
            }

            var x = stats.At<int>(label, (int)ConnectedComponentsTypes.Left);
            var y = stats.At<int>(label, (int)ConnectedComponentsTypes.Top);
            var width = stats.At<int>(label, (int)ConnectedComponentsTypes.Width);
            var height = stats.At<int>(label, (int)ConnectedComponentsTypes.Height);
            var rect = new Rect(x + roi.X, y + roi.Y, width, height);
            var aspect = Math.Max(width, height) / Math.Max(Math.Min(width, height), 1.0);
            var centroidX = centroids.At<double>(label, 0) + roi.X;
            var centroidY = centroids.At<double>(label, 1) + roi.Y;
            var contact = CountComponentContactPixels(labels, openedBodyMask, label, x, y, width, height);
            Cv2.Rectangle(overlay, rect, Scalar.Yellow, 4, LineTypes.AntiAlias);
            Cv2.PutText(
                overlay,
                label.ToString(CultureInfo.InvariantCulture),
                new Point(rect.X, Math.Max(40, rect.Y - 10)),
                HersheyFonts.HersheySimplex,
                1.4,
                Scalar.Yellow,
                4,
                LineTypes.AntiAlias);
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"diff label={label} area={area} rect=({rect.X},{rect.Y},{rect.Width},{rect.Height}) " +
                $"aspect={aspect:F2} contact={contact} centroid=({centroidX:F1},{centroidY:F1})"));
        }

        Cv2.PutText(
            overlay,
            $"k={kernelSize} recover={recoveryKernelSize} yellow=recovered-opened components",
            new Point(80, 120),
            HersheyFonts.HersheySimplex,
            1.6,
            Scalar.Yellow,
            5,
            LineTypes.AntiAlias);
        var path = Path.Combine(outputDirectory, $"{name}-diff-k{kernelSize}.png");
        Cv2.ImWrite(path, overlay);
        Console.WriteLine($"diff_overlay={path}");
    }

    private static int CountComponentContactPixels(
        Mat labels,
        Mat bodyMask,
        int label,
        int x,
        int y,
        int width,
        int height)
    {
        var contact = 0;
        var left = Math.Max(0, x - 1);
        var top = Math.Max(0, y - 1);
        var right = Math.Min(labels.Width - 1, x + width);
        var bottom = Math.Min(labels.Height - 1, y + height);
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                if (labels.At<int>(row, column) != label)
                {
                    continue;
                }

                var touchesBody = false;
                for (var neighborY = Math.Max(top, row - 1); neighborY <= Math.Min(bottom, row + 1); neighborY++)
                {
                    for (var neighborX = Math.Max(left, column - 1); neighborX <= Math.Min(right, column + 1); neighborX++)
                    {
                        if (bodyMask.At<byte>(neighborY, neighborX) == 0)
                        {
                            continue;
                        }

                        touchesBody = true;
                        break;
                    }

                    if (touchesBody)
                    {
                        break;
                    }
                }

                if (touchesBody)
                {
                    contact++;
                }
            }
        }

        return contact;
    }

    private static void WriteCandidateMetrics(
        string stage,
        int kernelSize,
        Point[] sourceContour,
        Point[] candidateContour,
        double minimumDimension)
    {
        var source = CalculateContourMetrics(sourceContour);
        var candidate = CalculateContourMetrics(candidateContour);
        var areaRatio = candidate.Area / Math.Max(source.Area, 0.0001);
        var centerShift = Distance(source.CenterX, source.CenterY, candidate.CenterX, candidate.CenterY);
        var widthDiff = CalculateRatioDifference(candidate.Width, source.Width);
        var heightDiff = CalculateRatioDifference(candidate.Height, source.Height);
        var perimeterReduction = (source.Perimeter - candidate.Perimeter) / Math.Max(source.Perimeter, 0.0001);
        var circularityGain = (candidate.Circularity - source.Circularity) / Math.Max(source.Circularity, 0.0001);
        var areaLoss = (source.Area - candidate.Area) / Math.Max(source.Area, 0.0001);
        var score = perimeterReduction <= 0.0 && circularityGain <= 0.0
            ? 0.0
            : perimeterReduction * 4.0 + Math.Max(circularityGain, 0.0) - areaLoss * 2.0;
        var safe =
            areaRatio is >= 0.900 and <= 1.002 &&
            centerShift <= Math.Max(6.0, minimumDimension * 0.080) &&
            widthDiff <= 0.140 &&
            heightDiff <= 0.080 &&
            candidate.Perimeter < source.Perimeter &&
            candidate.Circularity >= source.Circularity * 0.98;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"candidate_metrics stage={stage} k={kernelSize} safe={safe} score={score:F4} " +
            $"area_ratio={areaRatio:F4} center_shift={centerShift:F2} width_diff={widthDiff:F4} " +
            $"height_diff={heightDiff:F4} circularity={candidate.Circularity:F4} " +
            $"perimeter_reduction={perimeterReduction:F4} circularity_gain={circularityGain:F4} " +
            $"area_loss={areaLoss:F4} area={candidate.Area:F0}"));
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

    private static Point[] FindLargestContour(Mat binary)
    {
        var contour = FindLargestContourOrNull(binary);
        return contour ?? throw new InvalidOperationException("No contour found.");
    }

    private static Rect ExpandRect(Rect rect, int padding, Size bounds)
    {
        var x = Math.Max(0, rect.X - padding);
        var y = Math.Max(0, rect.Y - padding);
        var right = Math.Min(bounds.Width, rect.Right + padding);
        var bottom = Math.Min(bounds.Height, rect.Bottom + padding);
        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static int NormalizeOddKernelSize(int size)
    {
        var normalized = Math.Max(size, 3);
        return normalized % 2 == 0 ? normalized + 1 : normalized;
    }

    private static Point[]? FindLargestContourOrNull(Mat binary)
    {
        Cv2.FindContours(
            binary,
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

    private static Mat BuildLocalizedAttachmentImage()
    {
        var image = new Mat(new Size(720, 620), MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(
            image,
            new Point(345, 310),
            new Size(165, 136),
            angle: -4,
            startAngle: 0,
            endAngle: 360,
            Scalar.FromRgb(178, 178, 178),
            thickness: -1);
        Cv2.Ellipse(
            image,
            new Point(516, 352),
            new Size(34, 84),
            angle: -3,
            startAngle: 0,
            endAngle: 360,
            Scalar.FromRgb(238, 238, 238),
            thickness: -1);
        Cv2.Line(image, new Point(499, 272), new Point(497, 434), Scalar.FromRgb(78, 78, 78), thickness: 4);
        return image;
    }

    private static Mat BuildRealTabImage()
    {
        var image = new Mat(new Size(720, 620), MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(
            image,
            new Point(330, 310),
            new Size(142, 116),
            angle: 0,
            startAngle: 0,
            endAngle: 360,
            Scalar.FromRgb(178, 178, 178),
            thickness: -1);
        Cv2.Rectangle(image, new Rect(454, 298, 36, 24), Scalar.FromRgb(178, 178, 178), thickness: -1);
        Cv2.Ellipse(
            image,
            new Point(492, 310),
            new Size(16, 22),
            angle: 0,
            startAngle: 0,
            endAngle: 360,
            Scalar.FromRgb(178, 178, 178),
            thickness: -1);
        return image;
    }

    private static Mat BuildRectangularAttachmentImage()
    {
        var image = new Mat(new Size(760, 640), MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(image, new Rect(210, 190, 290, 240), Scalar.White, thickness: -1);
        Cv2.Rectangle(image, new Rect(500, 274, 78, 32), Scalar.White, thickness: -1);
        Cv2.Circle(image, new Point(580, 290), 28, Scalar.White, thickness: -1);
        return image;
    }

    private sealed record ContourMetrics(
        double Area,
        double Perimeter,
        double CenterX,
        double CenterY,
        double Width,
        double Height,
        double Circularity);
}
