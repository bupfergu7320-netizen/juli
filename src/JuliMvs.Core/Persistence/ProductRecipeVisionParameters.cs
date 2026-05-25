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
            TemplateAngleMinimumScoreMargin = Math.Max(
                parameters.TemplateAngleMinimumScoreMargin,
                MinimumRuntimeTemplateAngleScoreMargin),
            InvertXCompensation = false,
            InvertYCompensation = false,
            InvertRotationCompensation = false
        };
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
            TemplateAngleMinimumScoreMargin = Math.Max(
                recipeParameters.TemplateAngleMinimumScoreMargin,
                MinimumRuntimeTemplateAngleScoreMargin),
            InvertXCompensation = currentRuntimeParameters.InvertXCompensation,
            InvertYCompensation = currentRuntimeParameters.InvertYCompensation,
            InvertRotationCompensation = currentRuntimeParameters.InvertRotationCompensation
        };
    }
}
