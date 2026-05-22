namespace JuliMvs.Core.Vision;

public sealed record CameraCalibration
{
    public bool Enabled { get; init; }

    public string CalibrationId { get; init; } = string.Empty;

    public double X0 { get; init; }

    public double XPixelCoefficient { get; init; }

    public double YPixelCoefficient { get; init; }

    public double Y0 { get; init; }

    public double YPixelXCoefficient { get; init; }

    public double YPixelYCoefficient { get; init; }

    public double RmsErrorMm { get; init; }

    public string SourceDistortionCalibrationId { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public IReadOnlyList<CalibrationPoint> Points { get; init; } = [];

    public static CameraCalibration Disabled { get; } = new();

    public MachinePoint PixelToMachine(double pixelX, double pixelY)
    {
        return new MachinePoint(
            X0 + XPixelCoefficient * pixelX + YPixelCoefficient * pixelY,
            Y0 + YPixelXCoefficient * pixelX + YPixelYCoefficient * pixelY);
    }
}

public sealed record MachinePoint(double XMm, double YMm);
