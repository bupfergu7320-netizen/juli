using System.Globalization;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Services;

internal sealed class ChangeoverTemplateSelfCheck
{
    private const double MaximumTemplateSelfCheckOffsetMm = 0.05;
    private const double MaximumTemplateSelfCheckRotationDegrees = 0.2;

    private readonly OpenCvVisionService _visionService;

    public ChangeoverTemplateSelfCheck(OpenCvVisionService visionService)
    {
        _visionService = visionService;
    }

    public ChangeoverTemplateSelfCheckResult Validate(
        Mat templateImage,
        PartTemplate template,
        VisionParameters parameters,
        string rawImagePath)
    {
        ArgumentNullException.ThrowIfNull(templateImage);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(parameters);

        var output = _visionService.Inspect(
            templateImage,
            template,
            parameters,
            partNo: "template-self-check",
            rawImagePath: rawImagePath);
        var result = output.Result;
        if (result.Decision != InspectionDecision.Ok)
        {
            return ChangeoverTemplateSelfCheckResult.Fail(
                $"Template self-check failed: {result.Message}",
                output);
        }

        if (result.Measurement is not { } measurement)
        {
            return ChangeoverTemplateSelfCheckResult.Fail(
                "Template self-check failed: no measurement was produced.",
                output);
        }

        var maxOffsetMm = Math.Max(
            Math.Abs(measurement.XOffsetMm),
            Math.Abs(measurement.YOffsetMm));
        var maxCompensationMm = Math.Max(
            Math.Abs(measurement.XCompensationMm),
            Math.Abs(measurement.YCompensationMm));
        var rotation = Math.Max(
            Math.Abs(measurement.AngleOffsetDegrees),
            Math.Abs(measurement.RotationCompensationDegrees));
        if (maxOffsetMm > MaximumTemplateSelfCheckOffsetMm ||
            maxCompensationMm > MaximumTemplateSelfCheckOffsetMm ||
            rotation > MaximumTemplateSelfCheckRotationDegrees)
        {
            return ChangeoverTemplateSelfCheckResult.Fail(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Template self-check failed: expected near-zero XYR, got offset=({0:F3},{1:F3})mm, compensation=({2:F3},{3:F3})mm, R offset={4:F3}deg, R compensation={5:F3}deg.",
                    measurement.XOffsetMm,
                    measurement.YOffsetMm,
                    measurement.XCompensationMm,
                    measurement.YCompensationMm,
                    measurement.AngleOffsetDegrees,
                    measurement.RotationCompensationDegrees),
                output);
        }

        if (output.TemplateSimilarity is { IsReliable: true, IsSamePart: false } similarity)
        {
            return ChangeoverTemplateSelfCheckResult.Fail(
                $"Template self-check failed: shape score below threshold. {similarity.Message}",
                output);
        }

        return ChangeoverTemplateSelfCheckResult.Pass(output);
    }
}

internal sealed record ChangeoverTemplateSelfCheckResult(
    bool Passed,
    string Message,
    OpenCvInspectionOutput Output)
{
    public static ChangeoverTemplateSelfCheckResult Pass(OpenCvInspectionOutput output)
    {
        return new ChangeoverTemplateSelfCheckResult(true, "Template self-check passed.", output);
    }

    public static ChangeoverTemplateSelfCheckResult Fail(string message, OpenCvInspectionOutput output)
    {
        return new ChangeoverTemplateSelfCheckResult(false, message, output);
    }
}
