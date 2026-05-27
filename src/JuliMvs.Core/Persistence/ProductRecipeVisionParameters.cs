using JuliMvs.Core.Vision;

namespace JuliMvs.Core.Persistence;

public static class ProductRecipeVisionParameters
{
    private const double MinimumRuntimeTemplateAngleScoreMargin = 0.08;

    public static VisionParameters ForSave(VisionParameters parameters)
    {
        return parameters with
        {
            FrontBumpFeature = FrontBumpFeature.Disabled,
            LensDistortionCalibration = LensDistortionCalibration.Disabled,
            CameraCalibration = CameraCalibration.Disabled,
            RAxisCenterCalibration = RAxisCenterCalibration.Disabled,
            AngleDetectionMode = AngleDetectionMode.AutoPcaOrPolarRing,
            TemplateAngleMinimumScoreMargin = Math.Max(
                parameters.TemplateAngleMinimumScoreMargin,
                MinimumRuntimeTemplateAngleScoreMargin),
            InvertXCompensation = false,
            InvertYCompensation = false,
            InvertRotationCompensation = false
        };
    }

    public static VisionParameters ForSave(PartTemplate template, VisionParameters runtimeParameters)
    {
        var templateParameters = template.Parameters with
        {
            LensDistortionCalibration = runtimeParameters.LensDistortionCalibration,
            CameraCalibration = runtimeParameters.CameraCalibration,
            RAxisCenterCalibration = runtimeParameters.RAxisCenterCalibration,
            InvertXCompensation = runtimeParameters.InvertXCompensation,
            InvertYCompensation = runtimeParameters.InvertYCompensation,
            InvertRotationCompensation = runtimeParameters.InvertRotationCompensation
        };
        return ForSave(templateParameters);
    }

    public static VisionParameters ApplyToRuntime(
        VisionParameters currentRuntimeParameters,
        VisionParameters recipeParameters)
    {
        return recipeParameters with
        {
            LensDistortionCalibration = currentRuntimeParameters.LensDistortionCalibration,
            CameraCalibration = currentRuntimeParameters.CameraCalibration,
            RAxisCenterCalibration = currentRuntimeParameters.RAxisCenterCalibration,
            AngleDetectionMode = AngleDetectionMode.AutoPcaOrPolarRing,
            TemplateAngleMinimumScoreMargin = Math.Max(
                recipeParameters.TemplateAngleMinimumScoreMargin,
                MinimumRuntimeTemplateAngleScoreMargin),
            InvertXCompensation = currentRuntimeParameters.InvertXCompensation,
            InvertYCompensation = currentRuntimeParameters.InvertYCompensation,
            InvertRotationCompensation = currentRuntimeParameters.InvertRotationCompensation
        };
    }
}
