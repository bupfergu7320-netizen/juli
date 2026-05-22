namespace JuliMvs.Core.Vision;

public sealed record LensDistortionCalibration
{
    public bool Enabled { get; init; }

    public string CalibrationId { get; init; } = string.Empty;

    public int ImageWidth { get; init; }

    public int ImageHeight { get; init; }

    public double[] CameraMatrix { get; init; } = [];

    public double[] DistortionCoefficients { get; init; } = [];

    public double RmsReprojectionErrorPixels { get; init; }

    public int CapturedImageCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public static LensDistortionCalibration Disabled { get; } = new();

    public bool CanApplyTo(int imageWidth, int imageHeight)
    {
        return Enabled &&
               ImageWidth == imageWidth &&
               ImageHeight == imageHeight &&
               CameraMatrix.Length == 9 &&
               DistortionCoefficients.Length > 0;
    }
}
