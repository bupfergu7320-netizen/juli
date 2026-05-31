namespace JuliMvs.Core.Camera;

public sealed record CameraFrame(
    int Width,
    int Height,
    long FrameNumber,
    string PixelFormat,
    double? ActualExposureTimeMicroseconds,
    byte[] Buffer,
    DateTimeOffset CapturedAt);
