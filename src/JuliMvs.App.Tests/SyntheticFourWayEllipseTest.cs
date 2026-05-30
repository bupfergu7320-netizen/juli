using System.Globalization;
using JuliMvs.App.Services;
using JuliMvs.Core;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Tests;

internal static class SyntheticFourWayEllipseTest
{
    private const int ImageWidth = 1400;
    private const int ImageHeight = 1000;
    private const int MajorRadiusPixels = 270;
    private const int MinorRadiusPixels = 226;

    private sealed record GeneratedCase(
        string Name,
        string Path,
        double RotationDegrees,
        double XOffsetPixels,
        double YOffsetPixels,
        bool HasDefect);

    private sealed record AlignmentOverlayMetrics(
        double CenterErrorPixels,
        int MissingPixels,
        int DifferencePixels,
        string OverlayPath);

    public static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var overlayDirectory = Path.Combine(outputDirectory, "overlays");
        Directory.CreateDirectory(overlayDirectory);
        var templatePath = Path.Combine(outputDirectory, "template_four_way_ellipse.png");
        var templateCenter = new Point2d(ImageWidth / 2.0, ImageHeight / 2.0);
        using (var templateImage = CreateEllipseImage(templateCenter, rotationDegrees: 0.0, defectIndex: -1))
        {
            Cv2.ImWrite(templatePath, templateImage);
        }

        var cases = GenerateCases(outputDirectory, templateCenter);
        var parameters = VisionParameters.Default with
        {
            FourWaySymmetricEnabled = true,
            BinaryThreshold = 0,
            MinPartAreaPixels = 10_000
        };
        var extractor = new ContourFeatureExtractor();
        using var templateForTest = Cv2.ImRead(templatePath, ImreadModes.Color);
        var templateFeature = extractor.Extract(templateForTest, parameters);
        var template = new PartTemplate(
            Guid.NewGuid(),
            "SYNTHETIC-FOUR-WAY",
            "FOUR-WAY-ELLIPSE",
            templatePath,
            DateTimeOffset.Now,
            templateFeature.CenterXPixel,
            templateFeature.CenterYPixel,
            templateFeature.CenterXPixel,
            templateFeature.CenterYPixel,
            string.Empty,
            string.Empty,
            templateFeature.AxisFeature.MeanAngleDegrees,
            templateFeature.WidthPixels,
            templateFeature.HeightPixels,
            templateFeature.AreaPixels,
            1.0,
            ImageRoi.Empty,
            parameters,
            templateFeature.WidthPixels,
            templateFeature.HeightPixels);

        var resolver = new ProductionAutoAngleResolver();
        resolver.WarmupTemplate(templateFeature, includeFrontBack: false);
        using var defectDetector = new ProductionMissingMaterialDetector();
        defectDetector.WarmupTemplate(template, templateFeature);

        var rows = new List<string>
        {
            "case,file,expected,decision,reason,rotation_deg,x_offset_px,y_offset_px,resolved_r_deg,angle_offset_deg,alignment_angle_deg,score,axis_ratio,pca_deg,ellipse_deg,axis_spread_deg,angle_error_180_deg,center_error_px,missing_pixels,difference_pixels,overlay"
        };
        var okPass = 0;
        var okFail = 0;
        var defectPass = 0;
        var defectFail = 0;
        var okAngleErrors = new List<double>();
        var okExpected = cases.Count(testCase => !testCase.HasDefect);
        var defectExpected = cases.Count(testCase => testCase.HasDefect);

        foreach (var testCase in cases)
        {
            using var image = Cv2.ImRead(testCase.Path, ImreadModes.Color);
            var currentFeature = extractor.Extract(image, parameters);
            var angle = resolver.Resolve(
                currentFeature,
                templateFeature,
                template,
                fourWaySymmetric: true);

            var decision = "NG";
            var reason = angle.Message;
            var resolvedR = 0.0;
            var angleOffset = 0.0;
            var alignmentAngle = 0.0;
            var score = 0.0;
            var angleError = double.NaN;
            if (angle.IsReliable)
            {
                resolvedR = angle.ResolvedAngleDegrees;
                angleOffset = AngleMath.NormalizeDeltaDegrees(
                    angle.ResolvedAngleDegrees,
                    template.ReferenceAngleDegrees);
                alignmentAngle = angle.AlignmentAngleDegrees;
                score = angle.MatchScore;
                angleError = Math.Abs(Diff180(angleOffset, testCase.RotationDegrees));
                var defect = defectDetector.Evaluate(currentFeature, templateFeature, template, angle);
                decision = defect.IsPass ? "OK" : "NG";
                reason = defect.Message;
            }

            var overlayMetrics = WriteAlignmentOverlay(
                overlayDirectory,
                testCase,
                template,
                templateFeature,
                currentFeature,
                angle,
                decision,
                angleOffset,
                angleError);

            if (testCase.HasDefect)
            {
                if (decision == "NG")
                {
                    defectPass++;
                }
                else
                {
                    defectFail++;
                }
            }
            else if (decision == "OK")
            {
                okPass++;
                okAngleErrors.Add(angleError);
            }
            else
            {
                okFail++;
            }

            rows.Add(string.Join(
                ',',
                Csv(testCase.Name),
                Csv(Path.GetFileName(testCase.Path)),
                Csv(testCase.HasDefect ? "NG" : "OK"),
                Csv(decision),
                Csv(Simplify(reason)),
                Format(testCase.RotationDegrees),
                Format(testCase.XOffsetPixels),
                Format(testCase.YOffsetPixels),
                Format(resolvedR),
                Format(angleOffset),
                Format(alignmentAngle),
                Format(score),
                Format(currentFeature.AxisFeature.MeanRatio),
                Format(currentFeature.AxisFeature.RegionAngleDegrees),
                Format(currentFeature.AxisFeature.EllipseAngleDegrees),
                Format(currentFeature.AxisFeature.MaximumAngleSpreadDegrees),
                Format(angleError),
                Format(overlayMetrics.CenterErrorPixels),
                overlayMetrics.MissingPixels.ToString(CultureInfo.InvariantCulture),
                overlayMetrics.DifferencePixels.ToString(CultureInfo.InvariantCulture),
                Csv(Path.GetFileName(overlayMetrics.OverlayPath))));
        }

        var reportPath = Path.Combine(outputDirectory, "synthetic_four_way_results.csv");
        File.WriteAllLines(reportPath, rows);
        var summaryPath = Path.Combine(outputDirectory, "summary.txt");
        var maxOkAngleError = okAngleErrors.Count == 0 ? double.NaN : okAngleErrors.Max();
        var avgOkAngleError = okAngleErrors.Count == 0 ? double.NaN : okAngleErrors.Average();
        File.WriteAllLines(
            summaryPath,
            new[]
            {
                $"output_directory={outputDirectory}",
                $"template_image={templatePath}",
                $"template_axis_ratio={Format(templateFeature.AxisFeature.MeanRatio)}",
                $"template_pca_deg={Format(templateFeature.AxisFeature.RegionAngleDegrees)}",
                $"template_ellipse_deg={Format(templateFeature.AxisFeature.EllipseAngleDegrees)}",
                $"template_axis_spread_deg={Format(templateFeature.AxisFeature.MaximumAngleSpreadDegrees)}",
                $"ok_expected={okExpected}",
                $"ok_pass={okPass}",
                $"ok_fail={okFail}",
                $"ok_angle_error_avg_deg={Format(avgOkAngleError)}",
                $"ok_angle_error_max_deg={Format(maxOkAngleError)}",
                $"defect_expected={defectExpected}",
                $"defect_pass_ng={defectPass}",
                $"defect_fail_ok={defectFail}",
                $"overlay_directory={overlayDirectory}",
                $"csv={reportPath}"
            });

        Console.WriteLine($"template={templatePath}");
        Console.WriteLine($"csv={reportPath}");
        Console.WriteLine($"summary={summaryPath}");
        Console.WriteLine($"overlays={overlayDirectory}");
        Console.WriteLine($"OK: {okPass}/{okExpected} pass, {okFail}/{okExpected} NG");
        Console.WriteLine($"DEFECT: {defectPass}/{defectExpected} NG, {defectFail}/{defectExpected} missed");
        Console.WriteLine($"OK angle error: avg={Format(avgOkAngleError)}deg, max={Format(maxOkAngleError)}deg");
        Console.WriteLine($"template axis ratio={Format(templateFeature.AxisFeature.MeanRatio)}, PCA={Format(templateFeature.AxisFeature.RegionAngleDegrees)}deg, fitEllipse={Format(templateFeature.AxisFeature.EllipseAngleDegrees)}deg, spread={Format(templateFeature.AxisFeature.MaximumAngleSpreadDegrees)}deg");
    }

    private static List<GeneratedCase> GenerateCases(string outputDirectory, Point2d templateCenter)
    {
        var cases = new List<GeneratedCase>();
        var okCases = GenerateOkCases();
        for (var index = 0; index < okCases.Count; index++)
        {
            var (rotation, xOffset, yOffset) = okCases[index];
            var center = new Point2d(templateCenter.X + xOffset, templateCenter.Y + yOffset);
            var path = Path.Combine(outputDirectory, $"ok_{index + 1:000}_r{rotation:+000;-000;+000}_x{xOffset:+000;-000;+000}_y{yOffset:+000;-000;+000}.png");
            using var image = CreateEllipseImage(center, rotation, defectIndex: -1);
            Cv2.ImWrite(path, image);
            cases.Add(new GeneratedCase($"OK-{index + 1:000}", path, rotation, xOffset, yOffset, HasDefect: false));
        }

        for (var index = 0; index < 5; index++)
        {
            var rotation = okCases[index * 20 + 10].RotationDegrees;
            var xOffset = (index - 2) * 42.0;
            var yOffset = (2 - index) * 30.0;
            var center = new Point2d(templateCenter.X + xOffset, templateCenter.Y + yOffset);
            var path = Path.Combine(outputDirectory, $"ng_defect_{index + 1:00}_r{rotation:+000;-000;+000}.png");
            using var image = CreateEllipseImage(center, rotation, defectIndex: index);
            Cv2.ImWrite(path, image);
            cases.Add(new GeneratedCase($"DEFECT-{index + 1:00}", path, rotation, xOffset, yOffset, HasDefect: true));
        }

        return cases;
    }

    private static List<(double RotationDegrees, double XOffsetPixels, double YOffsetPixels)> GenerateOkCases()
    {
        var cases = new List<(double RotationDegrees, double XOffsetPixels, double YOffsetPixels)>();
        var index = 0;
        for (var row = 0; row < 10; row++)
        {
            for (var column = 0; column < 10; column++)
            {
                var xOffset = -150.0 + column * 33.0 + ((row % 2) * 8.0);
                var yOffset = -115.0 + row * 25.0 + ((column % 3) - 1) * 4.0;
                var rotation = -88.0 + index * 3.7;
                rotation = AngleMath.NormalizeDegrees180(rotation);
                cases.Add((rotation, xOffset, yOffset));
                index++;
            }
        }

        return cases;
    }

    private static Mat CreateEllipseImage(Point2d center, double rotationDegrees, int defectIndex)
    {
        var image = new Mat(ImageHeight, ImageWidth, MatType.CV_8UC3, new Scalar(25, 25, 25));
        AddBackground(image);
        using var mask = new Mat(ImageHeight, ImageWidth, MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(
            mask,
            new Point((int)Math.Round(center.X), (int)Math.Round(center.Y)),
            new Size(MajorRadiusPixels, MinorRadiusPixels),
            rotationDegrees,
            0,
            360,
            Scalar.White,
            thickness: -1);

        using var smoothKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, smoothKernel);

        if (defectIndex >= 0)
        {
            AddMissingEdgeDefect(mask, center, rotationDegrees, defectIndex);
        }

        using var part = new Mat(ImageHeight, ImageWidth, MatType.CV_8UC3, new Scalar(205, 205, 205));
        AddPartTexture(part, mask);
        part.CopyTo(image, mask);
        return image;
    }

    private static void AddMissingEdgeDefect(Mat mask, Point2d center, double rotationDegrees, int defectIndex)
    {
        var localAngles = new[] { -62.0, -28.0, 14.0, 51.0, 76.0 };
        var depths = new[] { 42.0, 48.0, 54.0, 46.0, 50.0 };
        var widths = new[] { 112, 126, 138, 118, 130 };
        var worldAngle = (rotationDegrees + localAngles[defectIndex % localAngles.Length]) * Math.PI / 180.0;
        var direction = new Point2d(Math.Cos(worldAngle), Math.Sin(worldAngle));
        var edgePoint = FindEdgePoint(mask, center, direction);
        var biteCenter = new Point(
            (int)Math.Round(edgePoint.X - direction.X * depths[defectIndex] * 0.35),
            (int)Math.Round(edgePoint.Y - direction.Y * depths[defectIndex] * 0.35));
        Cv2.Ellipse(
            mask,
            biteCenter,
            new Size(widths[defectIndex], (int)Math.Round(depths[defectIndex])),
            rotationDegrees + localAngles[defectIndex],
            0,
            360,
            Scalar.Black,
            thickness: -1);
    }

    private static Point2d FindEdgePoint(Mat mask, Point2d center, Point2d direction)
    {
        Cv2.FindContours(
            mask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);
        var contour = contours
            .OrderByDescending(contour => Cv2.ContourArea(contour))
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Synthetic contour not found.");
        var bestProjection = double.NegativeInfinity;
        var bestPoint = center;
        foreach (var point in contour)
        {
            var projection = (point.X - center.X) * direction.X + (point.Y - center.Y) * direction.Y;
            if (projection > bestProjection)
            {
                bestProjection = projection;
                bestPoint = new Point2d(point.X, point.Y);
            }
        }

        return bestPoint;
    }

    private static void AddBackground(Mat image)
    {
    }

    private static void AddPartTexture(Mat part, Mat mask)
    {
    }

    private static AlignmentOverlayMetrics WriteAlignmentOverlay(
        string overlayDirectory,
        GeneratedCase testCase,
        PartTemplate template,
        ContourFeatureExtraction templateFeature,
        ContourFeatureExtraction currentFeature,
        ProductionAutoAngleResult angle,
        string decision,
        double angleOffset,
        double angleError)
    {
        var outputPath = Path.Combine(overlayDirectory, $"{Path.GetFileNameWithoutExtension(testCase.Path)}_overlay.png");
        var centerError = double.NaN;
        var missingPixels = 0;
        var differencePixels = 0;
        using var overlay = new Mat(ImageHeight, ImageWidth, MatType.CV_8UC3, new Scalar(18, 18, 18));
        DrawFilledContour(overlay, templateFeature.ContourPoints, new Scalar(28, 48, 28));
        DrawPolyline(overlay, templateFeature.ContourPoints, new Scalar(0, 220, 0), 2);

        if (angle.IsReliable)
        {
            var alignedCurrentPoints = AlignCurrentToTemplate(
                currentFeature,
                templateFeature,
                template,
                angle);
            DrawPolyline(overlay, alignedCurrentPoints, new Scalar(255, 160, 0), 2);
            var metrics = DrawMissingAreaOverlay(overlay, templateFeature.ContourPoints, alignedCurrentPoints);
            missingPixels = metrics.MissingPixels;
            differencePixels = metrics.DifferencePixels;
            centerError = Distance(
                angle.CenterXPixel,
                angle.CenterYPixel,
                currentFeature.CenterXPixel,
                currentFeature.CenterYPixel);
            Cv2.Circle(
                overlay,
                new Point((int)Math.Round(templateFeature.CenterXPixel), (int)Math.Round(templateFeature.CenterYPixel)),
                5,
                new Scalar(0, 255, 255),
                -1);
        }

        var statusColor = decision == "OK"
            ? new Scalar(0, 220, 0)
            : new Scalar(0, 0, 255);
        Cv2.PutText(
            overlay,
            $"{decision} {testCase.Name} realR={testCase.RotationDegrees:F1} outR={angleOffset:F2} err={angleError:F3}",
            new Point(22, 36),
            HersheyFonts.HersheySimplex,
            0.7,
            statusColor,
            2,
            LineTypes.AntiAlias);
        Cv2.PutText(
            overlay,
            $"realXY=({testCase.XOffsetPixels:F0},{testCase.YOffsetPixels:F0}) score={angle.MatchScore:F3} green=template blue=aligned red=missing",
            new Point(22, 68),
            HersheyFonts.HersheySimplex,
            0.58,
            new Scalar(220, 220, 220),
            1,
            LineTypes.AntiAlias);

        Cv2.ImWrite(outputPath, overlay);
        return new AlignmentOverlayMetrics(centerError, missingPixels, differencePixels, outputPath);
    }

    private static IReadOnlyList<Point2d> AlignCurrentToTemplate(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        PartTemplate template,
        ProductionAutoAngleResult angle)
    {
        var angleOffset = AngleMath.NormalizeDeltaDegrees360(
            angle.AlignmentAngleDegrees,
            template.ReferenceAngleDegrees);
        var radians = -angleOffset * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return currentFeature.ContourPoints
            .Select(point =>
            {
                var dx = point.X - angle.CenterXPixel;
                var dy = point.Y - angle.CenterYPixel;
                return new Point2d(
                    templateFeature.CenterXPixel + dx * cos - dy * sin,
                    templateFeature.CenterYPixel + dx * sin + dy * cos);
            })
            .ToArray();
    }

    private static (int MissingPixels, int DifferencePixels) DrawMissingAreaOverlay(
        Mat overlay,
        IReadOnlyList<Point2d> templatePoints,
        IReadOnlyList<Point2d> alignedCurrentPoints)
    {
        using var templateMask = BuildFullImageMask(templatePoints);
        using var currentMask = BuildFullImageMask(alignedCurrentPoints);
        using var coreMask = new Mat();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(25, 25));
        Cv2.Erode(templateMask, coreMask, kernel);
        using var invertedCurrent = new Mat();
        Cv2.BitwiseNot(currentMask, invertedCurrent);
        using var missing = new Mat();
        Cv2.BitwiseAnd(coreMask, invertedCurrent, missing);
        using var difference = new Mat();
        Cv2.BitwiseXor(templateMask, currentMask, difference);
        var missingPixels = Cv2.CountNonZero(missing);
        var differencePixels = Cv2.CountNonZero(difference);
        using var red = new Mat(ImageHeight, ImageWidth, MatType.CV_8UC3, new Scalar(0, 0, 255));
        red.CopyTo(overlay, missing);
        return (missingPixels, differencePixels);
    }

    private static Mat BuildFullImageMask(IReadOnlyList<Point2d> contourPoints)
    {
        var mask = new Mat(ImageHeight, ImageWidth, MatType.CV_8UC1, Scalar.Black);
        var points = contourPoints
            .Select(point => new Point(
                Math.Clamp((int)Math.Round(point.X), 0, ImageWidth - 1),
                Math.Clamp((int)Math.Round(point.Y), 0, ImageHeight - 1)))
            .ToArray();
        if (points.Length >= 3)
        {
            Cv2.FillPoly(mask, new[] { points }, Scalar.White);
        }

        return mask;
    }

    private static void DrawFilledContour(
        Mat image,
        IReadOnlyList<Point2d> contourPoints,
        Scalar color)
    {
        var points = ToPoints(contourPoints);
        if (points.Length >= 3)
        {
            Cv2.FillPoly(image, new[] { points }, color);
        }
    }

    private static void DrawPolyline(
        Mat image,
        IReadOnlyList<Point2d> contourPoints,
        Scalar color,
        int thickness)
    {
        var points = ToPoints(contourPoints);
        if (points.Length >= 2)
        {
            Cv2.Polylines(image, new[] { points }, isClosed: true, color, thickness, LineTypes.AntiAlias);
        }
    }

    private static Point[] ToPoints(IReadOnlyList<Point2d> contourPoints)
    {
        return contourPoints
            .Select(point => new Point(
                Math.Clamp((int)Math.Round(point.X), 0, ImageWidth - 1),
                Math.Clamp((int)Math.Round(point.Y), 0, ImageHeight - 1)))
            .ToArray();
    }

    private static double Diff180(double leftDegrees, double rightDegrees)
    {
        var diff = Math.Abs(AngleMath.NormalizeDeltaDegrees(leftDegrees, rightDegrees));
        return Math.Min(diff, Math.Abs(180.0 - diff));
    }

    private static double Distance(double leftX, double leftY, double rightX, double rightY)
    {
        var dx = leftX - rightX;
        var dy = leftY - rightY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string Simplify(string message)
    {
        return message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Csv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
