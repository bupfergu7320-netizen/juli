namespace JuliMvs.Core.Camera;

public sealed record CameraAcquisitionSettings
{
    public static CameraAcquisitionSettings Default { get; } = new();

    public double ExposureTimeMicroseconds { get; init; } = 8000;

    public double Gain { get; init; }

    public double CaptureDelaySeconds { get; init; } = 0.3;

    public int AutoExposureTarget { get; init; } = 255;

    public bool AutoExposureEnabled { get; init; }
}
