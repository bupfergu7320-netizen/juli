namespace JuliMvs.Vision;

public sealed record ProductionSetupDecision(
    bool IsReady,
    ProductionSetupBlockReason Reason)
{
    public static ProductionSetupDecision Ready { get; } =
        new(true, ProductionSetupBlockReason.None);

    public static ProductionSetupDecision Blocked(ProductionSetupBlockReason reason)
    {
        return new ProductionSetupDecision(false, reason);
    }
}

public enum ProductionSetupBlockReason
{
    None,
    CameraCalibrationMissing,
    CameraCalibrationDistortionMismatch,
    RAxisCenterMissing,
    RAxisCenterCameraMismatch,
    TemplateImageMissing,
    TemplateCameraCalibrationMissing,
    TemplateCameraCalibrationMismatch,
    TemplateDistortionCalibrationMismatch
}
