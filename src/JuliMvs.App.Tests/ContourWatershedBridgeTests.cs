using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Tests;

internal static class ContourWatershedBridgeTests
{
    public static void Run()
    {
        VerifyOpenedWatershedBridgeCutRejectsRightFixture();
    }

    private static void VerifyOpenedWatershedBridgeCutRejectsRightFixture()
    {
        using var clean = new Mat(new Size(1000, 900), MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(
            clean,
            new Point(490, 452),
            new Size(350, 330),
            angle: -2,
            startAngle: 0,
            endAngle: 360,
            Scalar.FromRgb(190, 190, 190),
            thickness: -1);

        using var polluted = clean.Clone();
        Cv2.Rectangle(polluted, new Rect(830, 568, 16, 150), Scalar.FromRgb(190, 190, 190), thickness: -1);
        Cv2.Rectangle(polluted, new Rect(842, 684, 88, 38), Scalar.FromRgb(190, 190, 190), thickness: -1);
        Cv2.Circle(polluted, new Point(930, 703), 28, Scalar.FromRgb(190, 190, 190), thickness: -1);

        var parameters = VisionParameters.Default with
        {
            MinPartAreaPixels = 10_000
        };
        var extractor = new ContourFeatureExtractor();
        var cleanFeature = extractor.Extract(clean, parameters);
        var pollutedFeature = extractor.Extract(polluted, parameters);

        AssertNear(cleanFeature.CenterXPixel, pollutedFeature.CenterXPixel, 4.0, "opened watershed center x");
        AssertNear(cleanFeature.CenterYPixel, pollutedFeature.CenterYPixel, 4.0, "opened watershed center y");
        AssertNear(cleanFeature.AreaPixels, pollutedFeature.AreaPixels, cleanFeature.AreaPixels * 0.01, "opened watershed area");
        AssertLessThan(pollutedFeature.WidthPixels, cleanFeature.WidthPixels * 1.015, "opened watershed width");
    }

    private static void AssertNear(double expected, double actual, double tolerance, string name)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"{name}: expected {expected:F3}, actual {actual:F3}, tolerance {tolerance:F3}");
        }
    }

    private static void AssertLessThan(double actual, double maximum, string name)
    {
        if (actual >= maximum)
        {
            throw new InvalidOperationException($"{name}: actual {actual:F3}, maximum {maximum:F3}");
        }
    }
}
