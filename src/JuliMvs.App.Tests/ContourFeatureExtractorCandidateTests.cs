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
        VerifyFixtureTouchingBorderDoesNotBecomePartContour();
        VerifyDarkPartOnBrightBackgroundUsesPartContour();
        VerifyReferenceFeatureSelectsPartOverLargerInteriorFixture();
        VerifyDetachedSmallCylinderDoesNotMovePartCenter();
        VerifyConnectedFixtureArtifactIsTrimmedFromPartContour();
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
}
