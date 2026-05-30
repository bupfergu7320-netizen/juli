using System.Globalization;
using JuliMvs.App.Services;
using JuliMvs.Core;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Tests;

internal static class SyntheticFieldImageTest
{
    private sealed record GeneratedCase(
        string Name,
        string Path,
        double RotationDegrees,
        double XOffsetPixels,
        double YOffsetPixels,
        bool HasDefect);

    public static void Run(string firstImagePath, string secondImagePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var templateSource = secondImagePath;
        using var templateSourceImage = Cv2.ImRead(templateSource, ImreadModes.Color);
        using var alternateSourceImage = Cv2.ImRead(firstImagePath, ImreadModes.Color);
        if (templateSourceImage.Empty())
        {
            throw new InvalidOperationException($"模板源图读取失败: {templateSource}");
        }

        if (alternateSourceImage.Empty())
        {
            throw new InvalidOperationException($"来料源图读取失败: {firstImagePath}");
        }

        using var templateSourceMask = ExtractLargestPartMask(templateSourceImage);
        using var alternateSourceMask = ExtractLargestPartMask(alternateSourceImage);
        var templateSourceCenter = CalculateMaskCenter(templateSourceMask);
        var alternateSourceCenter = CalculateMaskCenter(alternateSourceMask);
        var templateBackgroundColor = EstimateBackgroundColor(templateSourceImage, templateSourceMask);
        var alternateBackgroundColor = EstimateBackgroundColor(alternateSourceImage, alternateSourceMask);
        var outputSize = templateSourceImage.Size();
        var templateCenter = new Point2d(templateSourceImage.Width / 2.0, templateSourceImage.Height / 2.0);
        var templateImagePath = Path.Combine(outputDirectory, "template_000.png");
        using (var templateImage = BuildSyntheticImage(
            templateSourceImage,
            templateSourceMask,
            templateBackgroundColor,
            templateSourceCenter,
            templateCenter,
            outputSize,
            rotationDegrees: 0.0,
            defectIndex: -1))
        {
            Cv2.ImWrite(templateImagePath, templateImage);
        }

        var cases = new List<GeneratedCase>();
        var rotations = new[]
        {
            -42.0, -35.0, -28.0, -21.0, -14.0,
            -7.0, 0.0, 7.0, 14.0, 21.0,
            28.0, 35.0, 42.0, 49.0, 56.0,
            63.0, 70.0, 77.0, 84.0, 91.0,
            98.0, 105.0, 112.0, 119.0, 126.0
        };
        for (var index = 0; index < rotations.Length; index++)
        {
            var xOffset = ((index % 5) - 2) * 34.0;
            var yOffset = ((index / 5) - 2) * 26.0;
            var center = new Point2d(templateCenter.X + xOffset, templateCenter.Y + yOffset);
            var path = Path.Combine(outputDirectory, $"ok_{index + 1:00}_r{rotations[index]:+000;-000;+000}_x{xOffset:+000;-000;+000}_y{yOffset:+000;-000;+000}.png");
            using var image = BuildSyntheticImage(
                templateSourceImage,
                templateSourceMask,
                templateBackgroundColor,
                templateSourceCenter,
                center,
                outputSize,
                rotations[index],
                defectIndex: -1);
            Cv2.ImWrite(path, image);
            cases.Add(new GeneratedCase($"OK-{index + 1:00}", path, rotations[index], xOffset, yOffset, HasDefect: false));
        }

        for (var index = 0; index < 5; index++)
        {
            var rotation = rotations[index * 4 + 2];
            var xOffset = (index - 2) * 30.0;
            var yOffset = (2 - index) * 24.0;
            var center = new Point2d(templateCenter.X + xOffset, templateCenter.Y + yOffset);
            var path = Path.Combine(outputDirectory, $"ng_defect_{index + 1:00}_r{rotation:+000;-000;+000}.png");
            using var image = BuildSyntheticImage(
                templateSourceImage,
                templateSourceMask,
                templateBackgroundColor,
                templateSourceCenter,
                center,
                outputSize,
                rotation,
                defectIndex: index);
            Cv2.ImWrite(path, image);
            cases.Add(new GeneratedCase($"DEFECT-{index + 1:00}", path, rotation, xOffset, yOffset, HasDefect: true));
        }

        var parameters = VisionParameters.Default with
        {
            FourWaySymmetricEnabled = true,
            BinaryThreshold = 0,
            MinPartAreaPixels = 10_000
        };
        var extractor = new ContourFeatureExtractor();
        using var templateImageForTest = Cv2.ImRead(templateImagePath, ImreadModes.Color);
        var templateFeature = extractor.Extract(templateImageForTest, parameters);
        var template = new PartTemplate(
            Guid.NewGuid(),
            "SYNTHETIC-TEST",
            "FOUR-WAY-SYMMETRIC",
            templateImagePath,
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
        using var defectDetector = new ProductionMissingMaterialDetector();
        var rows = new List<string>
        {
            "case,file,expected,decision,reason,rotation_deg,x_offset_px,y_offset_px,resolved_r_deg,angle_offset_deg,score,current_axis_ratio,current_pca_deg,current_ellipse_deg,current_axis_spread_deg"
        };

        var okPass = 0;
        var okFail = 0;
        var defectPass = 0;
        var defectFail = 0;
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
            var score = 0.0;
            if (angle.IsReliable)
            {
                resolvedR = angle.ResolvedAngleDegrees;
                angleOffset = AngleMath.NormalizeDeltaDegrees(
                    angle.ResolvedAngleDegrees,
                    template.ReferenceAngleDegrees);
                score = angle.MatchScore;
                var defect = defectDetector.Evaluate(currentFeature, templateFeature, template, angle);
                if (defect.IsPass)
                {
                    decision = "OK";
                    reason = defect.Message;
                }
                else
                {
                    decision = "NG";
                    reason = defect.Message;
                }
            }

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
            else
            {
                if (decision == "OK")
                {
                    okPass++;
                }
                else
                {
                    okFail++;
                }
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
                Format(score),
                Format(currentFeature.AxisFeature.MeanRatio),
                Format(currentFeature.AxisFeature.RegionAngleDegrees),
                Format(currentFeature.AxisFeature.EllipseAngleDegrees),
                Format(currentFeature.AxisFeature.MaximumAngleSpreadDegrees)));
        }

        var reportPath = Path.Combine(outputDirectory, "synthetic_test_results.csv");
        File.WriteAllLines(reportPath, rows);
        var summaryPath = Path.Combine(outputDirectory, "summary.txt");
        File.WriteAllLines(
            summaryPath,
            new[]
            {
                $"template_source={templateSource}",
                $"alternate_source={firstImagePath}",
                $"output_directory={outputDirectory}",
                $"template_image={templateImagePath}",
                $"template_axis_ratio={Format(templateFeature.AxisFeature.MeanRatio)}",
                $"template_pca_deg={Format(templateFeature.AxisFeature.RegionAngleDegrees)}",
                $"template_ellipse_deg={Format(templateFeature.AxisFeature.EllipseAngleDegrees)}",
                $"template_axis_spread_deg={Format(templateFeature.AxisFeature.MaximumAngleSpreadDegrees)}",
                $"ok_expected=25",
                $"ok_pass={okPass}",
                $"ok_fail={okFail}",
                $"defect_expected=5",
                $"defect_pass_ng={defectPass}",
                $"defect_fail_ok={defectFail}",
                $"csv={reportPath}"
            });

        Console.WriteLine($"模板图: {templateImagePath}");
        Console.WriteLine($"结果CSV: {reportPath}");
        Console.WriteLine($"汇总: {summaryPath}");
        Console.WriteLine($"OK来料: 通过 {okPass}/25, 误NG {okFail}/25");
        Console.WriteLine($"缺陷来料: 检出 {defectPass}/5, 漏检 {defectFail}/5");
        Console.WriteLine($"模板轴比={Format(templateFeature.AxisFeature.MeanRatio)}, PCA={Format(templateFeature.AxisFeature.RegionAngleDegrees)}deg, fitEllipse={Format(templateFeature.AxisFeature.EllipseAngleDegrees)}deg, 差={Format(templateFeature.AxisFeature.MaximumAngleSpreadDegrees)}deg");
    }

    private static Mat ExtractLargestPartMask(Mat source)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(source, hsv, ColorConversionCodes.BGR2HSV);
        var channels = Cv2.Split(hsv);
        using var saturation = channels[1];
        using var value = channels[2];
        channels[0].Dispose();
        using var lowSaturation = new Mat();
        using var bright = new Mat();
        using var binary = new Mat();
        Cv2.Threshold(saturation, lowSaturation, 70, 255, ThresholdTypes.BinaryInv);
        Cv2.Threshold(value, bright, 80, 255, ThresholdTypes.Binary);
        Cv2.BitwiseAnd(lowSaturation, bright, binary);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(9, 9));
        Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel);
        Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel);
        var contour = FindBestPartContour(binary, source.Size());
        var mask = new Mat(source.Height, source.Width, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(mask, new[] { contour }, Scalar.White);
        return mask;
    }

    private static Point[] FindLargestContour(Mat mask)
    {
        Cv2.FindContours(
            mask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);
        return contours
            .OrderByDescending(contour => Cv2.ContourArea(contour))
            .FirstOrDefault()
            ?? throw new InvalidOperationException("未找到工件轮廓。");
    }

    private static Point[] FindBestPartContour(Mat mask, Size imageSize)
    {
        Cv2.FindContours(
            mask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        var imageArea = Math.Max(imageSize.Width * imageSize.Height, 1);
        var imageCenter = new Point2d(imageSize.Width / 2.0, imageSize.Height / 2.0);
        var bestScore = double.NegativeInfinity;
        Point[]? bestContour = null;
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < imageArea * 0.03 || area > imageArea * 0.45)
            {
                continue;
            }

            var rect = Cv2.BoundingRect(contour);
            var boxRatio = rect.Width / Math.Max(rect.Height, 1.0);
            if (boxRatio < 0.55 || boxRatio > 1.85)
            {
                continue;
            }

            var perimeter = Cv2.ArcLength(contour, closed: true);
            if (perimeter <= 0.0001)
            {
                continue;
            }

            var circularity = 4.0 * Math.PI * area / (perimeter * perimeter);
            if (circularity < 0.35)
            {
                continue;
            }

            var center = new Point2d(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0);
            var centerDistance = CalculateDistance(center.X, center.Y, imageCenter.X, imageCenter.Y);
            var normalizedDistance = centerDistance / Math.Max(imageSize.Width, imageSize.Height);
            var score = area * Math.Clamp(circularity, 0.0, 1.0) * (1.0 - Math.Min(normalizedDistance, 0.75));
            if (score > bestScore)
            {
                bestScore = score;
                bestContour = contour;
            }
        }

        if (bestContour is not null)
        {
            return bestContour;
        }

        return contours
            .OrderByDescending(contour => Cv2.ContourArea(contour))
            .FirstOrDefault()
            ?? throw new InvalidOperationException("未找到工件轮廓。");
    }

    private static Point2d CalculateMaskCenter(Mat mask)
    {
        var contour = FindLargestContour(mask);
        var moments = Cv2.Moments(contour);
        if (Math.Abs(moments.M00) < 0.0001)
        {
            throw new InvalidOperationException("工件面积过小，无法计算中心。");
        }

        return new Point2d(moments.M10 / moments.M00, moments.M01 / moments.M00);
    }

    private static Scalar EstimateBackgroundColor(Mat image, Mat mask)
    {
        using var inverted = new Mat();
        Cv2.BitwiseNot(mask, inverted);
        var mean = Cv2.Mean(image, inverted);
        return new Scalar(mean.Val0, mean.Val1, mean.Val2);
    }

    private static Mat BuildSyntheticImage(
        Mat source,
        Mat sourceMask,
        Scalar backgroundColor,
        Point2d sourceCenter,
        Point2d targetCenter,
        Size outputSize,
        double rotationDegrees,
        int defectIndex)
    {
        var canvas = new Mat(outputSize, MatType.CV_8UC3, backgroundColor);
        AddBackgroundTexture(canvas, defectIndex);

        using var part = new Mat();
        source.CopyTo(part, sourceMask);
        using var workingMask = sourceMask.Clone();
        if (defectIndex >= 0)
        {
            AddMissingBite(workingMask, sourceCenter, defectIndex);
            using var cleanedPart = new Mat();
            part.CopyTo(cleanedPart, workingMask);
            cleanedPart.CopyTo(part);
        }

        var matrix = Cv2.GetRotationMatrix2D(
            new Point2f((float)sourceCenter.X, (float)sourceCenter.Y),
            rotationDegrees,
            1.0);
        matrix.Set(0, 2, matrix.At<double>(0, 2) + targetCenter.X - sourceCenter.X);
        matrix.Set(1, 2, matrix.At<double>(1, 2) + targetCenter.Y - sourceCenter.Y);
        using var warpedPart = new Mat();
        using var warpedMask = new Mat();
        Cv2.WarpAffine(
            part,
            warpedPart,
            matrix,
            canvas.Size(),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            Scalar.All(0));
        Cv2.WarpAffine(
            workingMask,
            warpedMask,
            matrix,
            canvas.Size(),
            InterpolationFlags.Nearest,
            BorderTypes.Constant,
            Scalar.All(0));
        warpedPart.CopyTo(canvas, warpedMask);
        return canvas;
    }

    private static void AddBackgroundTexture(Mat canvas, int seed)
    {
        using var noise = new Mat(canvas.Size(), MatType.CV_8UC3);
        Cv2.Randu(noise, new Scalar(0, 0, 0), new Scalar(12, 12, 12));
        Cv2.Add(canvas, noise, canvas);
        if (seed >= 0)
        {
            Cv2.Line(canvas, new Point(90 + seed * 35, 120), new Point(700 + seed * 22, 980), new Scalar(10, 18, 12), 2);
        }
    }

    private static void AddMissingBite(Mat mask, Point2d center, int defectIndex)
    {
        var angles = new[] { -70.0, -25.0, 12.0, 48.0, 83.0 };
        var angle = angles[defectIndex % angles.Length] * Math.PI / 180.0;
        var direction = new Point2d(Math.Cos(angle), Math.Sin(angle));
        var edgePoint = FindEdgePoint(mask, center, direction);
        var biteDepth = new[] { 52.0, 46.0, 58.0, 50.0, 54.0 }[defectIndex % 5];
        var biteWidth = new[] { 128, 116, 140, 124, 132 }[defectIndex % 5];
        var biteCenter = new Point(
            (int)Math.Round(edgePoint.X - direction.X * biteDepth * 0.35),
            (int)Math.Round(edgePoint.Y - direction.Y * biteDepth * 0.35));
        Cv2.Ellipse(
            mask,
            biteCenter,
            new Size(biteWidth, (int)Math.Round(biteDepth)),
            angles[defectIndex % angles.Length],
            0,
            360,
            Scalar.Black,
            thickness: -1);
    }

    private static Point2d FindEdgePoint(Mat mask, Point2d center, Point2d direction)
    {
        var contour = FindLargestContour(mask);
        var bestProjection = double.NegativeInfinity;
        var bestPoint = new Point2d(center.X + direction.X * 300.0, center.Y + direction.Y * 300.0);
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

    private static double CalculateDistance(double leftX, double leftY, double rightX, double rightY)
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
