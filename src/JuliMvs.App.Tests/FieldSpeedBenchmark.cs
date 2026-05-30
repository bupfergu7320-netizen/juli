using System.Diagnostics;
using System.Globalization;
using JuliMvs.App.Services;
using JuliMvs.Core;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Tests;

internal static class FieldSpeedBenchmark
{
    public static void Run(string templateImagePath, IReadOnlyList<string> currentImagePaths)
    {
        if (currentImagePaths.Count == 0)
        {
            throw new InvalidOperationException("At least one current image is required.");
        }

        var parameters = VisionParameters.Default with
        {
            BinaryThreshold = 0,
            MinPartAreaPixels = 10_000
        };
        var extractor = new ContourFeatureExtractor();
        using var templateImage = Cv2.ImRead(templateImagePath, ImreadModes.Color);
        if (templateImage.Empty())
        {
            throw new InvalidOperationException($"Template image read failed: {templateImagePath}");
        }

        var templateExtractWatch = Stopwatch.StartNew();
        var templateFeature = extractor.Extract(templateImage, parameters);
        templateExtractWatch.Stop();
        var template = new PartTemplate(
            Guid.NewGuid(),
            "FIELD-BENCH",
            "BENCH",
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
        using var missingDetector = new ProductionMissingMaterialDetector();
        Console.WriteLine($"template={templateImagePath}");
        Console.WriteLine($"template_extract_ms={templateExtractWatch.ElapsedMilliseconds}");
        Console.WriteLine("file,extract_ms,shape_ms,missing_ms,total_ms,reliable,score,error_message");

        foreach (var imagePath in currentImagePaths)
        {
            using var currentImage = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (currentImage.Empty())
            {
                Console.WriteLine($"{Csv(Path.GetFileName(imagePath))},0,0,0,0,false,0,{Csv("read failed")}");
                continue;
            }

            var totalWatch = Stopwatch.StartNew();
            var extractWatch = Stopwatch.StartNew();
            var currentFeature = extractor.Extract(currentImage, parameters);
            extractWatch.Stop();

            var shapeWatch = Stopwatch.StartNew();
            var angle = resolver.Resolve(currentFeature, templateFeature, template, fourWaySymmetric: false);
            shapeWatch.Stop();

            var missingWatch = Stopwatch.StartNew();
            var missing = angle.IsReliable && angle.SkipMissingMaterialDetection
                ? ProductionMissingMaterialResult.Pass("缺料检测跳过: Shape失败后宽容XYR兜底")
                : angle.IsReliable
                ? missingDetector.Evaluate(currentFeature, templateFeature, template, angle)
                : ProductionMissingMaterialResult.Pass("shape unreliable");
            if (angle.IsReliable && angle.SkipMissingMaterialDetection)
            {
                missing = missingDetector.EvaluateCoarseVisibleEdgeMissing(
                    currentFeature,
                    templateFeature,
                    template);
            }

            missingWatch.Stop();
            totalWatch.Stop();

            var score = angle.IsReliable ? angle.MatchScore : 0.0;
            var message = angle.IsReliable ? missing.Message : angle.Message;
            Console.WriteLine(string.Join(
                ',',
                Csv(Path.GetFileName(imagePath)),
                extractWatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                shapeWatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                missingWatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                totalWatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                angle.IsReliable ? "true" : "false",
                score.ToString("0.000", CultureInfo.InvariantCulture),
                Csv(message)));
        }
    }

    private static string Csv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
