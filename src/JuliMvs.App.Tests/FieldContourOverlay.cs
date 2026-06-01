using System.Globalization;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Tests;

internal static class FieldContourOverlay
{
    public static void Run(string imagePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var parameters = VisionParameters.Default with
        {
            BinaryThreshold = 0,
            MinPartAreaPixels = 10_000
        };
        using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (image.Empty())
        {
            throw new InvalidOperationException($"Image read failed: {imagePath}");
        }

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

        var overlayPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(imagePath) + "-contour.png");
        Cv2.ImWrite(overlayPath, overlay);
        Console.WriteLine($"overlay={overlayPath}");
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"center=({feature.CenterXPixel:F1},{feature.CenterYPixel:F1}) area={feature.AreaPixels:F0} " +
            $"width={feature.WidthPixels:F0} height={feature.HeightPixels:F0} circularity={feature.Circularity:F3} " +
            $"radius_signal={feature.RadiusSignalPixels:F2} normalized_radius_signal={feature.NormalizedRadiusSignalPixels:F2} " +
            $"strategy={feature.Strategy.ShapeClass}/{feature.Strategy.Method}"));
    }
}
