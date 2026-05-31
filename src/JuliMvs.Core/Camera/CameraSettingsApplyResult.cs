namespace JuliMvs.Core.Camera;

public sealed record CameraSettingsApplyResult(
    CameraRuntimeSettings RuntimeSettings,
    IReadOnlyList<string> Warnings);

