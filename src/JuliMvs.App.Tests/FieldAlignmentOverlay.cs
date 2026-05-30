using System.Globalization;
using JuliMvs.App.Services;
using JuliMvs.Core;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Tests;

internal static class FieldAlignmentOverlay
{
    public static void Run(string templateImagePath, string currentImagePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var parameters = VisionParameters.Default with
        {
            BinaryThreshold = 0,
            MinPartAreaPixels = 10_000
        };
        var extractor = new ContourFeatureExtractor();
        using var templateImage = Cv2.ImRead(templateImagePath, ImreadModes.Color);
        using var currentImage = Cv2.ImRead(currentImagePath, ImreadModes.Color);
        if (templateImage.Empty())
        {
            throw new InvalidOperationException($"Template image read failed: {templateImagePath}");
        }

        if (currentImage.Empty())
        {
            throw new InvalidOperationException($"Current image read failed: {currentImagePath}");
        }

        var templateFeature = extractor.Extract(templateImage, parameters);
        var currentFeature = extractor.Extract(currentImage, parameters);
        var template = new PartTemplate(
            Guid.NewGuid(),
            "FIELD-ALIGN",
            "FIELD",
            templateImagePath,
            DateTimeOffset.Now,
            templateFeature.CenterXPixel,
            templateFeature.CenterYPixel,
            templateFeature.CenterXPixel,
            templateFeature.CenterYPixel,
            string.Empty,
            string.Empty,
            templateFeature.PcaAngleDegrees,
            templateFeature.WidthPixels,
            templateFeature.HeightPixels,
            templateFeature.AreaPixels,
            1.0,
            ImageRoi.Empty,
            parameters,
            templateFeature.WidthPixels,
            templateFeature.HeightPixels);

        var resolver = new ProductionAutoAngleResolver();
        var angle = resolver.Resolve(currentFeature, templateFeature, template, fourWaySymmetric: false);
        if (!angle.IsReliable)
        {
            Console.WriteLine($"angle_reliable=false message={angle.Message}");
            return;
        }

        using var templateMask = BuildMask(templateFeature, templateImage.Size());
        using var alignedMask = AlignCurrentMask(currentFeature, templateFeature, templateImage.Size(), angle);
        using var overlay = BuildOverlay(templateImage, templateMask, alignedMask);
        var overlayPath = Path.Combine(outputDirectory, "field_alignment_overlay.png");
        Cv2.ImWrite(overlayPath, overlay);

        using var intersection = new Mat();
        using var union = new Mat();
        using var missing = new Mat();
        using var extra = new Mat();
        using var alignedNot = new Mat();
        using var templateNot = new Mat();
        Cv2.BitwiseAnd(templateMask, alignedMask, intersection);
        Cv2.BitwiseOr(templateMask, alignedMask, union);
        Cv2.BitwiseNot(alignedMask, alignedNot);
        Cv2.BitwiseNot(templateMask, templateNot);
        Cv2.BitwiseAnd(templateMask, alignedNot, missing);
        Cv2.BitwiseAnd(alignedMask, templateNot, extra);

        var templateArea = Cv2.CountNonZero(templateMask);
        var alignedArea = Cv2.CountNonZero(alignedMask);
        var intersectionArea = Cv2.CountNonZero(intersection);
        var unionArea = Cv2.CountNonZero(union);
        var missingArea = Cv2.CountNonZero(missing);
        var extraArea = Cv2.CountNonZero(extra);
        var iou = unionArea <= 0 ? 0.0 : (double)intersectionArea / unionArea;
        var missingRatio = templateArea <= 0 ? 0.0 : (double)missingArea / templateArea;
        var extraRatio = alignedArea <= 0 ? 0.0 : (double)extraArea / alignedArea;

        Console.WriteLine($"overlay={overlayPath}");
        Console.WriteLine(
            $"angle_reliable=true resolved={angle.ResolvedAngleDegrees:F2} align={angle.AlignmentAngleDegrees:F2} " +
            $"center=({angle.CenterXPixel:F1},{angle.CenterYPixel:F1}) score={angle.MatchScore:F3}");
        Console.WriteLine(
            $"template_center=({templateFeature.CenterXPixel:F1},{templateFeature.CenterYPixel:F1}) " +
            $"current_center=({currentFeature.CenterXPixel:F1},{currentFeature.CenterYPixel:F1})");
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"iou={iou:F4} missing_ratio={missingRatio:P2} extra_ratio={extraRatio:P2} " +
                $"template_area={templateArea} aligned_area={alignedArea} missing_area={missingArea} extra_area={extraArea}"));
        Console.WriteLine($"message={angle.Message}");
    }

    private static Mat BuildMask(ContourFeatureExtraction feature, Size size)
    {
        var mask = new Mat(size.Height, size.Width, MatType.CV_8UC1, Scalar.All(0));
        var points = feature.ContourPoints
            .Select(point => new Point(
                Math.Clamp((int)Math.Round(point.X), 0, size.Width - 1),
                Math.Clamp((int)Math.Round(point.Y), 0, size.Height - 1)))
            .ToArray();
        if (points.Length >= 3)
        {
            Cv2.FillPoly(mask, new[] { points }, Scalar.White);
        }

        return mask;
    }

    private static Mat AlignCurrentMask(
        ContourFeatureExtraction currentFeature,
        ContourFeatureExtraction templateFeature,
        Size size,
        ProductionAutoAngleResult angle)
    {
        var mask = new Mat(size.Height, size.Width, MatType.CV_8UC1, Scalar.All(0));
        var angleOffset = AngleMath.NormalizeDeltaDegrees360(
            angle.AlignmentAngleDegrees,
            templateFeature.PcaAngleDegrees);
        var radians = -angleOffset * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var points = currentFeature.ContourPoints
            .Select(point =>
            {
                var dx = point.X - angle.CenterXPixel;
                var dy = point.Y - angle.CenterYPixel;
                var x = templateFeature.CenterXPixel + dx * cos - dy * sin;
                var y = templateFeature.CenterYPixel + dx * sin + dy * cos;
                return new Point(
                    Math.Clamp((int)Math.Round(x), 0, size.Width - 1),
                    Math.Clamp((int)Math.Round(y), 0, size.Height - 1));
            })
            .ToArray();
        if (points.Length >= 3)
        {
            Cv2.FillPoly(mask, new[] { points }, Scalar.White);
        }

        return mask;
    }

    private static Mat BuildOverlay(Mat templateImage, Mat templateMask, Mat alignedMask)
    {
        var overlay = templateImage.Channels() == 1
            ? templateImage.CvtColor(ColorConversionCodes.GRAY2BGR)
            : templateImage.Clone();
        using var dim = new Mat();
        overlay.ConvertTo(dim, MatType.CV_8UC3, 0.38);
        overlay.Dispose();
        overlay = dim.Clone();

        using var green = new Mat(templateMask.Rows, templateMask.Cols, MatType.CV_8UC3, new Scalar(0, 210, 0));
        using var blue = new Mat(templateMask.Rows, templateMask.Cols, MatType.CV_8UC3, new Scalar(255, 120, 0));
        using var both = new Mat();
        Cv2.BitwiseAnd(templateMask, alignedMask, both);
        green.CopyTo(overlay, templateMask);
        blue.CopyTo(overlay, alignedMask);
        using var yellow = new Mat(templateMask.Rows, templateMask.Cols, MatType.CV_8UC3, new Scalar(0, 230, 230));
        yellow.CopyTo(overlay, both);

        Cv2.PutText(
            overlay,
            "green=template blue=aligned current yellow=overlap",
            new Point(80, 120),
            HersheyFonts.HersheySimplex,
            2.0,
            Scalar.White,
            5,
            LineTypes.AntiAlias);
        return overlay;
    }
}
