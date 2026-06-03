using System.Runtime.CompilerServices;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Tests;

internal static class ContourFeatureExtractorCandidateTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        if (IsDiagnosticCommand())
        {
            return;
        }

        VerifyFixtureTouchingBorderDoesNotBecomePartContour();
        VerifyDarkPartOnBrightBackgroundUsesPartContour();
        VerifyReferenceFeatureSelectsPartOverLargerInteriorFixture();
        VerifyDetachedSmallCylinderDoesNotMovePartCenter();
        VerifyConnectedFixtureArtifactIsTrimmedFromPartContour();
        VerifySmallConnectedArtifactDoesNotErodeMainRightEdge();
        VerifyNarrowBridgeFixturePollutionDoesNotPullRightContour();
        VerifyLocalizedBrightAttachmentSnapsBackToRealEdge();
        VerifySmallEdgeTabCanBeTrimmedAsAttachment();
        VerifyRectangularPartKeepsRealCorners();
        VerifyProductionReferenceRejectsLargerCurrentFixture();
    }

    private static bool IsDiagnosticCommand()
    {
        var args = Environment.GetCommandLineArgs();
        return args.Any(arg =>
            string.Equals(arg, "field-contour", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "field-contour-debug", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "field-contour-artifacts", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "field-contour-stages", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "field-contour-diff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "synthetic-contour", StringComparison.OrdinalIgnoreCase));
    }

    private static void VerifyFixtureTouchingBorderDoesNotBecomePartContour()
    {
        using var image = new Mat(new Size(420, 420), MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(image, new Rect(0, 30, 170, 360), Scalar.White, thickness: -1);
        Cv2.Ellipse(
            image,
            new Point(280, 210),
            new Size(72, 52),
            angle: 18,
            startAngle: 0,
            endAngle: 360,
            Scalar.White,
            thickness: -1);

        var feature = Extract(image);

        AssertNear(280.0, feature.CenterXPixel, 8.0, "fixture border center x");
        AssertNear(210.0, feature.CenterYPixel, 8.0, "fixture border center y");
        AssertLessThan(feature.AreaPixels, 18_000.0, "fixture border area");
    }

    private static void VerifyDarkPartOnBrightBackgroundUsesPartContour()
    {
        using var image = new Mat(new Size(420, 420), MatType.CV_8UC1, Scalar.White);
        Cv2.Ellipse(
            image,
            new Point(210, 210),
            new Size(82, 56),
            angle: -24,
            startAngle: 0,
            endAngle: 360,
            Scalar.Black,
            thickness: -1);

        var feature = Extract(image);

        AssertNear(210.0, feature.CenterXPixel, 5.0, "dark part center x");
        AssertNear(210.0, feature.CenterYPixel, 5.0, "dark part center y");
        AssertLessThan(feature.WidthPixels, 190.0, "dark part width");
        AssertLessThan(feature.AreaPixels, 20_000.0, "dark part area");
    }

    private static void VerifyReferenceFeatureSelectsPartOverLargerInteriorFixture()
    {
        using var template = new Mat(new Size(420, 420), MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(
            template,
            new Point(210, 210),
            new Size(70, 50),
            angle: 12,
            startAngle: 0,
            endAngle: 360,
            Scalar.White,
            thickness: -1);

        using var current = new Mat(new Size(420, 420), MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(current, new Rect(60, 50, 90, 220), Scalar.White, thickness: -1);
        Cv2.Ellipse(
            current,
            new Point(235, 205),
            new Size(70, 50),
            angle: 12,
            startAngle: 0,
            endAngle: 360,
            Scalar.White,
            thickness: -1);

        var parameters = TestParameters();
        var extractor = new ContourFeatureExtractor();
        var referenceFeature = extractor.Extract(template, parameters);
        var currentFeature = extractor.Extract(current, parameters, referenceFeature: referenceFeature);

        AssertNear(235.0, currentFeature.CenterXPixel, 7.0, "referenced part center x");
        AssertNear(205.0, currentFeature.CenterYPixel, 7.0, "referenced part center y");
        AssertLessThan(currentFeature.AreaPixels, 16_000.0, "referenced part area");
    }

    private static void VerifyDetachedSmallCylinderDoesNotMovePartCenter()
    {
        using var image = new Mat(new Size(420, 420), MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(
            image,
            new Point(210, 210),
            new Size(74, 54),
            angle: 30,
            startAngle: 0,
            endAngle: 360,
            Scalar.White,
            thickness: -1);
        Cv2.Circle(image, new Point(330, 120), 24, Scalar.White, thickness: -1);

        var feature = Extract(image);

        AssertNear(210.0, feature.CenterXPixel, 6.0, "small cylinder center x");
        AssertNear(210.0, feature.CenterYPixel, 6.0, "small cylinder center y");
        AssertLessThan(feature.AreaPixels, 17_000.0, "small cylinder area");
    }

    private static void VerifyConnectedFixtureArtifactIsTrimmedFromPartContour()
    {
        using var clean = new Mat(new Size(520, 520), MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(clean, new Point(260, 260), 125, Scalar.White, thickness: -1);
        using var polluted = clean.Clone();
        Cv2.Rectangle(polluted, new Rect(360, 338, 95, 18), Scalar.White, thickness: -1);
        Cv2.Circle(polluted, new Point(446, 347), 22, Scalar.White, thickness: -1);

        var cleanFeature = Extract(clean);
        var pollutedFeature = Extract(polluted);

        AssertNear(cleanFeature.CenterXPixel, pollutedFeature.CenterXPixel, 3.5, "connected artifact center x");
        AssertNear(cleanFeature.CenterYPixel, pollutedFeature.CenterYPixel, 3.5, "connected artifact center y");
        AssertNear(cleanFeature.AreaPixels, pollutedFeature.AreaPixels, cleanFeature.AreaPixels * 0.035, "connected artifact area");
        AssertLessThan(pollutedFeature.RadiusSignalPixels, 5.0, "connected artifact radius signal");
    }

    private static void VerifySmallConnectedArtifactDoesNotErodeMainRightEdge()
    {
        using var clean = new Mat(new Size(980, 920), MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(
            clean,
            new Point(486, 462),
            new Size(356, 332),
            angle: -2,
            startAngle: 0,
            endAngle: 360,
            Scalar.FromRgb(190, 190, 190),
            thickness: -1);

        using var polluted = clean.Clone();
        Cv2.Rectangle(polluted, new Rect(817, 583, 64, 34), Scalar.FromRgb(190, 190, 190), thickness: -1);
        Cv2.Circle(polluted, new Point(875, 602), 24, Scalar.FromRgb(190, 190, 190), thickness: -1);

        var cleanFeature = Extract(clean);
        var pollutedFeature = Extract(polluted);
        var cleanRightEdge = FindRightEdgeNearCenter(cleanFeature);
        var pollutedRightEdge = FindRightEdgeNearCenter(pollutedFeature);

        AssertNear(cleanRightEdge, pollutedRightEdge, 2.5, "small connected artifact right edge preserved");
        AssertNear(cleanFeature.CenterXPixel, pollutedFeature.CenterXPixel, 3.0, "small connected artifact center x");
        AssertNear(cleanFeature.CenterYPixel, pollutedFeature.CenterYPixel, 3.0, "small connected artifact center y");
        AssertNear(cleanFeature.AreaPixels, pollutedFeature.AreaPixels, cleanFeature.AreaPixels * 0.006, "small connected artifact area");
        AssertLessThan(pollutedFeature.WidthPixels, cleanFeature.WidthPixels * 1.010, "small connected artifact width");
    }

    private static void VerifyNarrowBridgeFixturePollutionDoesNotPullRightContour()
    {
        using var clean = new Mat(new Size(980, 920), MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(
            clean,
            new Point(486, 462),
            new Size(356, 332),
            angle: -2,
            startAngle: 0,
            endAngle: 360,
            Scalar.FromRgb(190, 190, 190),
            thickness: -1);

        using var polluted = clean.Clone();
        Cv2.Rectangle(polluted, new Rect(828, 572, 12, 136), Scalar.FromRgb(190, 190, 190), thickness: -1);
        Cv2.Rectangle(polluted, new Rect(818, 694, 72, 28), Scalar.FromRgb(190, 190, 190), thickness: -1);
        Cv2.Circle(polluted, new Point(886, 710), 22, Scalar.FromRgb(190, 190, 190), thickness: -1);

        var cleanFeature = Extract(clean);
        var pollutedFeature = Extract(polluted);
        var cleanRightEdge = FindRightEdgeNearCenter(cleanFeature);
        var pollutedRightEdge = FindRightEdgeNearCenter(pollutedFeature);

        AssertNear(cleanRightEdge, pollutedRightEdge, 4.0, "narrow bridge pollution right edge");
        AssertNear(cleanFeature.CenterXPixel, pollutedFeature.CenterXPixel, 3.5, "narrow bridge pollution center x");
        AssertNear(cleanFeature.CenterYPixel, pollutedFeature.CenterYPixel, 3.5, "narrow bridge pollution center y");
        AssertNear(cleanFeature.AreaPixels, pollutedFeature.AreaPixels, cleanFeature.AreaPixels * 0.008, "narrow bridge pollution area");
        AssertLessThan(pollutedFeature.WidthPixels, cleanFeature.WidthPixels * 1.012, "narrow bridge pollution width");
    }

    private static void VerifyLocalizedBrightAttachmentSnapsBackToRealEdge()
    {
        using var clean = new Mat(new Size(720, 620), MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(
            clean,
            new Point(345, 310),
            new Size(165, 136),
            angle: -4,
            startAngle: 0,
            endAngle: 360,
            Scalar.FromRgb(178, 178, 178),
            thickness: -1);

        using var polluted = clean.Clone();
        Cv2.Ellipse(
            polluted,
            new Point(516, 352),
            new Size(34, 84),
            angle: -3,
            startAngle: 0,
            endAngle: 360,
            Scalar.FromRgb(238, 238, 238),
            thickness: -1);
        Cv2.Line(polluted, new Point(499, 272), new Point(497, 434), Scalar.FromRgb(78, 78, 78), thickness: 4);

        var cleanFeature = Extract(clean);
        var pollutedFeature = Extract(polluted);

        AssertNear(cleanFeature.CenterXPixel, pollutedFeature.CenterXPixel, 4.0, "localized attachment center x");
        AssertNear(cleanFeature.CenterYPixel, pollutedFeature.CenterYPixel, 4.0, "localized attachment center y");
        AssertNear(cleanFeature.AreaPixels, pollutedFeature.AreaPixels, cleanFeature.AreaPixels * 0.040, "localized attachment area");
        AssertLessThan(pollutedFeature.WidthPixels, cleanFeature.WidthPixels * 1.035, "localized attachment width");
    }

    private static void VerifySmallEdgeTabCanBeTrimmedAsAttachment()
    {
        using var clean = new Mat(new Size(720, 620), MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(
            clean,
            new Point(330, 310),
            new Size(142, 116),
            angle: 0,
            startAngle: 0,
            endAngle: 360,
            Scalar.FromRgb(178, 178, 178),
            thickness: -1);

        using var part = clean.Clone();
        Cv2.Rectangle(part, new Rect(454, 298, 36, 24), Scalar.FromRgb(178, 178, 178), thickness: -1);
        Cv2.Ellipse(
            part,
            new Point(492, 310),
            new Size(16, 22),
            angle: 0,
            startAngle: 0,
            endAngle: 360,
            Scalar.FromRgb(178, 178, 178),
            thickness: -1);

        var cleanFeature = Extract(clean);
        var feature = Extract(part);

        AssertNear(cleanFeature.CenterXPixel, feature.CenterXPixel, 4.0, "edge tab center x trimmed");
        AssertNear(cleanFeature.CenterYPixel, feature.CenterYPixel, 4.0, "edge tab center y trimmed");
        AssertNear(cleanFeature.AreaPixels, feature.AreaPixels, cleanFeature.AreaPixels * 0.040, "edge tab area trimmed");
        AssertLessThan(feature.WidthPixels, cleanFeature.WidthPixels * 1.035, "edge tab width trimmed");
    }

    private static void VerifyRectangularPartKeepsRealCorners()
    {
        using var image = new Mat(new Size(760, 640), MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(image, new Rect(210, 190, 290, 240), Scalar.White, thickness: -1);

        var feature = Extract(image);

        AssertNear(354.5, feature.CenterXPixel, 3.0, "rectangular part center x");
        AssertNear(309.5, feature.CenterYPixel, 3.0, "rectangular part center y");
        AssertNear(290.0, feature.WidthPixels, 5.0, "rectangular part width");
        AssertNear(240.0, feature.HeightPixels, 5.0, "rectangular part height");
        AssertNear(69_600.0, feature.AreaPixels, 2_500.0, "rectangular part area");
    }

    private static void VerifyProductionReferenceRejectsLargerCurrentFixture()
    {
        using var template = new Mat(new Size(520, 520), MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(template, new Point(420, 260), 65, Scalar.White, thickness: -1);

        using var current = new Mat(new Size(520, 520), MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(
            current,
            new Point(210, 260),
            new Size(135, 120),
            angle: 0,
            startAngle: 0,
            endAngle: 360,
            Scalar.White,
            thickness: -1);
        Cv2.Circle(current, new Point(420, 260), 65, Scalar.White, thickness: -1);

        var parameters = TestParameters();
        var extractor = new ContourFeatureExtractor();
        var referenceFeature = extractor.Extract(template, parameters);
        var unreferencedFeature = extractor.Extract(current, parameters);
        var referencedFeature = extractor.Extract(current, parameters, referenceFeature: referenceFeature);

        AssertNear(420.0, referencedFeature.CenterXPixel, 6.0, "production reference center x");
        AssertNear(260.0, referencedFeature.CenterYPixel, 6.0, "production reference center y");
        AssertLessThan(referencedFeature.AreaPixels, 16_000.0, "production reference area");
        AssertGreaterThan(Math.Abs(unreferencedFeature.CenterXPixel - referencedFeature.CenterXPixel), 120.0, "production reference changed candidate");
    }

    private static double FindRightEdgeNearCenter(ContourFeatureExtraction feature)
    {
        return feature.ContourPoints
            .Where(point => Math.Abs(point.Y - feature.CenterYPixel) <= 16.0)
            .Select(point => point.X)
            .DefaultIfEmpty(double.NegativeInfinity)
            .Max();
    }

    private static ContourFeatureExtraction Extract(Mat image)
    {
        return new ContourFeatureExtractor().Extract(image, TestParameters());
    }

    private static VisionParameters TestParameters()
    {
        return VisionParameters.Default with
        {
            BinaryThreshold = 0,
            BlurKernelSize = 3,
            MinPartAreaPixels = 1_000.0
        };
    }

    private static void AssertNear(double expected, double actual, double tolerance, string name)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"{name}: expected {expected:0.###} +/- {tolerance:0.###}, actual {actual:0.###}.");
        }
    }

    private static void AssertLessThan(double actual, double maximum, string name)
    {
        if (actual >= maximum)
        {
            throw new InvalidOperationException(
                $"{name}: expected < {maximum:0.###}, actual {actual:0.###}.");
        }
    }

    private static void AssertGreaterThan(double actual, double minimum, string name)
    {
        if (actual <= minimum)
        {
            throw new InvalidOperationException(
                $"{name}: expected > {minimum:0.###}, actual {actual:0.###}.");
        }
    }
}
