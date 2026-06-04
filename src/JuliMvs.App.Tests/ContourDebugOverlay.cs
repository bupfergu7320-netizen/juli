using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Tests;

internal static class ContourDebugOverlay
{
    public static void Run(string imagePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var image = ReadImage(imagePath);
        if (image.Empty())
        {
            throw new InvalidOperationException($"Image read failed: {imagePath}");
        }

        var parameters = VisionParameters.Default with
        {
            BinaryThreshold = 0,
            MinPartAreaPixels = 10_000
        };
        var extractor = new ContourFeatureExtractor();
        var feature = extractor.Extract(image, parameters);
        var denseContourPoints = extractor.ExtractDenseContourPoints(image, parameters);
        using var overlay = BuildOverlay(image, feature, denseContourPoints);
        var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(imagePath)}-contour.png");
        WriteImage(outputPath, overlay);
        Console.WriteLine($"overlay={outputPath}");
        Console.WriteLine(
            $"center=({feature.CenterXPixel:F1},{feature.CenterYPixel:F1}) area={feature.AreaPixels:F0} " +
            $"width={feature.WidthPixels:F0} height={feature.HeightPixels:F0} circularity={feature.Circularity:F3} " +
            $"radius_signal={feature.RadiusSignalPixels:F2} strategy={feature.Strategy.ShapeClass}/{feature.Strategy.Method}");
    }

    private static Mat ReadImage(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }

    private static void WriteImage(string path, Mat image)
    {
        Cv2.ImEncode(Path.GetExtension(path), image, out var buffer);
        File.WriteAllBytes(path, buffer);
    }

    private static Mat BuildOverlay(
        Mat source,
        ContourFeatureExtraction feature,
        IReadOnlyList<Point2d> denseContourPoints)
    {
        var overlay = source.Channels() == 1
            ? source.CvtColor(ColorConversionCodes.GRAY2BGR)
            : source.Clone();
        var points = denseContourPoints
            .Select(point => new Point(
                Math.Clamp((int)Math.Round(point.X), 0, overlay.Width - 1),
                Math.Clamp((int)Math.Round(point.Y), 0, overlay.Height - 1)))
            .ToArray();
        if (points.Length >= 3)
        {
            Cv2.Polylines(overlay, new[] { points }, isClosed: true, new Scalar(0, 255, 0), thickness: 5, LineTypes.AntiAlias);
        }

        Cv2.DrawMarker(
            overlay,
            new Point((int)Math.Round(feature.CenterXPixel), (int)Math.Round(feature.CenterYPixel)),
            new Scalar(0, 255, 255),
            MarkerTypes.Cross,
            56,
            5,
            LineTypes.AntiAlias);
        return overlay;
    }
}
