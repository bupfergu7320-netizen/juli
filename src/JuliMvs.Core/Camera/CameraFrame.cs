namespace JuliMvs.Core.Camera;

public sealed record CameraFrame(
    int Width,
    int Height,
    long FrameNumber,
    string PixelFormat,
    byte[] Buffer,
    DateTimeOffset CapturedAt);
