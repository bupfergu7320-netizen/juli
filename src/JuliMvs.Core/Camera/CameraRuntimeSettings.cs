namespace JuliMvs.Core.Camera;

public sealed record CameraRuntimeSettings(
    double? ExposureTimeMicroseconds,
    double? Gain,
    string? ExposureAuto,
    int? AutoExposureTarget,
    int? AutoTargetValue,
    int? Brightness);

