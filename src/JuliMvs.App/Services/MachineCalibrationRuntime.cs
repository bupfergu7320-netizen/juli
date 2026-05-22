using JuliMvs.Core.Vision;
using JuliMvs.Vision;

namespace JuliMvs.App.Services;

internal sealed class MachineCalibrationRuntime
{
    private readonly string _rAxisCenterCaptureTarget;

    public MachineCalibrationRuntime(string rAxisCenterCaptureTarget)
    {
        _rAxisCenterCaptureTarget = rAxisCenterCaptureTarget;
    }

    public VisionParameters BuildRuntimeParameters(
        VisionParameters baseParameters,
        LensDistortionCalibration lensDistortionCalibration,
        CameraCalibration cameraCalibration,
        RAxisCenterCalibration rAxisCenterCalibration)
    {
        var effectiveCameraCalibration = GetEffectiveCameraCalibration(
            lensDistortionCalibration,
            cameraCalibration);
        var effectiveRAxisCenterCalibration = GetEffectiveRAxisCenterCalibration(
            effectiveCameraCalibration,
            rAxisCenterCalibration);

        return baseParameters with
        {
            LensDistortionCalibration = lensDistortionCalibration,
            CameraCalibration = effectiveCameraCalibration,
            RAxisCenterCalibration = effectiveRAxisCenterCalibration
        };
    }

    public CameraCalibration GetEffectiveCameraCalibration(
        LensDistortionCalibration lensDistortionCalibration,
        CameraCalibration cameraCalibration)
    {
        return IsCameraCalibrationValidForCurrentDistortion(lensDistortionCalibration, cameraCalibration)
            ? cameraCalibration
            : CameraCalibration.Disabled;
    }

    public RAxisCenterCalibration GetEffectiveRAxisCenterCalibration(
        CameraCalibration effectiveCameraCalibration,
        RAxisCenterCalibration rAxisCenterCalibration)
    {
        if (!IsRAxisCenterCalibrationValidForCurrentCameraCalibration(
            effectiveCameraCalibration,
            rAxisCenterCalibration))
        {
            return RAxisCenterCalibration.Disabled;
        }

        return rAxisCenterCalibration.MachineAngleDirection == 0
            ? rAxisCenterCalibration with { MachineAngleDirection = rAxisCenterCalibration.GetMachineAngleDirection() }
            : rAxisCenterCalibration;
    }

    public MachineCalibrationReadiness EvaluateMachineReadiness(VisionParameters runtimeParameters)
    {
        var decision = OpenCvVisionService.ValidateMachineCalibration(runtimeParameters);
        return decision.IsReady
            ? MachineCalibrationReadiness.Ready
            : new MachineCalibrationReadiness(
                false,
                ProductionSetupMessageFormatter.FormatBlockMessage(decision.Reason),
                decision.Reason);
    }

    private static bool IsCameraCalibrationValidForCurrentDistortion(
        LensDistortionCalibration lensDistortionCalibration,
        CameraCalibration cameraCalibration)
    {
        if (!cameraCalibration.Enabled)
        {
            return false;
        }

        return string.Equals(
            cameraCalibration.SourceDistortionCalibrationId,
            GetCurrentDistortionCalibrationId(lensDistortionCalibration),
            StringComparison.Ordinal);
    }

    private bool IsRAxisCenterCalibrationValidForCurrentCameraCalibration(
        CameraCalibration effectiveCameraCalibration,
        RAxisCenterCalibration rAxisCenterCalibration)
    {
        if (!rAxisCenterCalibration.Enabled || !effectiveCameraCalibration.Enabled)
        {
            return false;
        }

        return string.Equals(
                rAxisCenterCalibration.SourceCameraCalibrationId,
                effectiveCameraCalibration.CalibrationId,
                StringComparison.Ordinal) &&
            string.Equals(
                rAxisCenterCalibration.CaptureTarget,
                _rAxisCenterCaptureTarget,
                StringComparison.Ordinal);
    }

    private static string GetCurrentDistortionCalibrationId(LensDistortionCalibration lensDistortionCalibration)
    {
        return lensDistortionCalibration.Enabled ? lensDistortionCalibration.CalibrationId : string.Empty;
    }
}

internal sealed record MachineCalibrationReadiness(
    bool IsReady,
    string Message,
    ProductionSetupBlockReason Reason)
{
    public static MachineCalibrationReadiness Ready { get; } =
        new(true, string.Empty, ProductionSetupBlockReason.None);
}
