using System.Diagnostics;
using System.Globalization;
using JuliMvs.App.Services;
using JuliMvs.Core;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Tests;

internal static class FieldFallbackExperiment
{
    public static void Run(string templateImagePath, string currentImagePath)
    {
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
            "FIELD-FALLBACK",
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

        Console.WriteLine($"template={templateImagePath}");
        Console.WriteLine($"current={currentImagePath}");
        PrintFeature("template_feature", templateFeature);
        PrintFeature("current_feature", currentFeature);
        var radiusMatch = ContourFeatureExtractor.MatchRadiusSignatureWithAlternatives(
            currentFeature.RadiusSignature,
            templateFeature.RadiusSignature,
            alternativeExclusionDegrees: 12.0);
        Console.WriteLine(
            $"radius_only: angle={AngleMath.NormalizeDegrees360(radiusMatch.AngleDegrees):F2} " +
            $"error={radiusMatch.ErrorPixels:F2} alt={radiusMatch.AlternativeErrorPixels:F2} " +
            $"separation={radiusMatch.AlternativeErrorPixels - radiusMatch.ErrorPixels:F2} " +
            $"normalized_error={radiusMatch.ErrorNormalized:F4}");

        var resolver = new ProductionAutoAngleResolver();
        using var missingDetector = new ProductionMissingMaterialDetector();

        var defaultWatch = Stopwatch.StartNew();
        var defaultAngle = resolver.Resolve(currentFeature, templateFeature, template, fourWaySymmetric: false);
        defaultWatch.Stop();
        PrintAngle("default", defaultAngle, defaultWatch.ElapsedMilliseconds);

        var matcher = new ContourShapeMatcher();
        Console.WriteLine("fallback_threshold,max_error,reliable,score,error_px,second_px,separation_px,angle_offset_deg,center_x,center_y,angle_reliable,reliability,missing,elapsed_ms,message");
        foreach (var maximumErrorPixels in new[] { 12.0, 16.0, 20.0, 24.0, 30.0, 36.0 })
        {
            var options = ContourShapeMatcherOptions.Default with
            {
                MaximumErrorPixels = maximumErrorPixels,
                MaximumAreaDifferenceRatio = 0.20,
                MinimumSeparationPixels = 1.0
            };
            var watch = Stopwatch.StartNew();
            var relaxedFront = matcher.Match(currentFeature, templateFeature, options);
            var angle = relaxedFront.IsReliable
                ? resolver.Resolve(currentFeature, templateFeature, template, relaxedFront, fourWaySymmetric: false)
                : ProductionAutoAngleResult.Unreliable(relaxedFront.Message);
            var reliability = angle.IsReliable
                ? ProductionContourReliabilityGuard.Evaluate(
                    currentFeature,
                    templateFeature,
                    template,
                    angle.MatchScore)
                : ProductionContourReliabilityResult.Fail(angle.Message);
            var missing = angle.IsReliable && reliability.IsReliable && angle.SkipMissingMaterialDetection
                ? ProductionMissingMaterialResult.Pass("缺料检测跳过: Shape失败后宽容XYR兜底")
                : angle.IsReliable && reliability.IsReliable
                ? missingDetector.Evaluate(currentFeature, templateFeature, template, angle)
                : ProductionMissingMaterialResult.Pass("skip");
            if (angle.IsReliable && reliability.IsReliable && angle.SkipMissingMaterialDetection)
            {
                missing = missingDetector.EvaluateCoarseVisibleEdgeMissing(
                    currentFeature,
                    templateFeature,
                    template);
            }

            watch.Stop();

            Console.WriteLine(string.Join(
                ',',
                maximumErrorPixels.ToString("0.##", CultureInfo.InvariantCulture),
                options.MaximumErrorPixels.ToString("0.##", CultureInfo.InvariantCulture),
                relaxedFront.IsReliable ? "true" : "false",
                relaxedFront.Score.ToString("0.000", CultureInfo.InvariantCulture),
                relaxedFront.ErrorPixels.ToString("0.00", CultureInfo.InvariantCulture),
                relaxedFront.AlternativeErrorPixels.ToString("0.00", CultureInfo.InvariantCulture),
                relaxedFront.SeparationPixels.ToString("0.00", CultureInfo.InvariantCulture),
                relaxedFront.AngleOffsetDegrees.ToString("0.00", CultureInfo.InvariantCulture),
                relaxedFront.CenterXPixel.ToString("0.0", CultureInfo.InvariantCulture),
                relaxedFront.CenterYPixel.ToString("0.0", CultureInfo.InvariantCulture),
                angle.IsReliable ? "true" : "false",
                reliability.IsReliable
                    ? string.IsNullOrWhiteSpace(reliability.Warning) ? "OK" : Simplify(reliability.Warning)
                    : Simplify(reliability.Reason),
                missing.IsPass ? Simplify(missing.Message) : Simplify(missing.Message),
                watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                Simplify(angle.IsReliable ? angle.Message : relaxedFront.Message)));
        }
    }

    private static void PrintFeature(string label, ContourFeatureExtraction feature)
    {
        Console.WriteLine(
            $"{label}: center=({feature.CenterXPixel:F1},{feature.CenterYPixel:F1}) " +
            $"area={feature.AreaPixels:F0} width={feature.WidthPixels:F0} height={feature.HeightPixels:F0} " +
            $"axis_ratio={feature.AxisRatio:F3} pca_ratio={feature.PcaRatio:F3} circularity={feature.Circularity:F3} " +
            $"radius_signal={feature.RadiusSignalPixels:F2} strategy={feature.Strategy.ShapeClass}/{feature.Strategy.Method}");
    }

    private static void PrintAngle(string label, ProductionAutoAngleResult angle, long elapsedMilliseconds)
    {
        Console.WriteLine(
            $"{label}: reliable={angle.IsReliable} score={angle.MatchScore:F3} " +
            $"resolved={angle.ResolvedAngleDegrees:F2} align={angle.AlignmentAngleDegrees:F2} " +
            $"center=({angle.CenterXPixel:F1},{angle.CenterYPixel:F1}) elapsed_ms={elapsedMilliseconds} " +
            $"message={Simplify(angle.Message)}");
    }

    private static string Simplify(string value)
    {
        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace(",", "，", StringComparison.Ordinal);
    }
}
