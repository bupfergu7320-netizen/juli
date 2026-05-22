namespace JuliMvs.Core.Vision;

public sealed record RAxisCenterCalibration
{
    public bool Enabled { get; init; }

    public string CalibrationId { get; init; } = string.Empty;

    public double CenterXMm { get; init; }

    public double CenterYMm { get; init; }

    public double RadiusMm { get; init; }

    public double RmsErrorMm { get; init; }

    public double MaxErrorMm { get; init; }

    /// <summary>
    /// Direction of positive PLC R movement in machine XY coordinates.
    /// 1 means counter-clockwise, -1 means clockwise, 0 means infer from old calibration points.
    /// </summary>
    public int MachineAngleDirection { get; init; }

    public string SourceCameraCalibrationId { get; init; } = string.Empty;

    public string CaptureTarget { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public IReadOnlyList<RAxisCenterCalibrationPoint> Points { get; init; } = [];

    public static RAxisCenterCalibration Disabled { get; } = new();

    public MachinePoint Center => new(CenterXMm, CenterYMm);

    public int GetMachineAngleDirection()
    {
        if (MachineAngleDirection < 0)
        {
            return -1;
        }

        if (MachineAngleDirection > 0)
        {
            return 1;
        }

        return InferMachineAngleDirection(Points);
    }

    public static int InferMachineAngleDirection(IReadOnlyList<RAxisCenterCalibrationPoint> points)
    {
        if (points.Count < 2)
        {
            return 1;
        }

        var orderedPoints = points
            .OrderBy(point => Normalize360(point.AngleDegrees))
            .ToArray();
        var signedArea = 0.0;
        for (var i = 0; i < orderedPoints.Length; i++)
        {
            var current = orderedPoints[i];
            var next = orderedPoints[(i + 1) % orderedPoints.Length];
            signedArea += current.ObservedCenterXMm * next.ObservedCenterYMm -
                next.ObservedCenterXMm * current.ObservedCenterYMm;
        }

        return signedArea < 0.0 ? -1 : 1;
    }

    private static double Normalize360(double angleDegrees)
    {
        var normalized = angleDegrees % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }
}

public sealed record RAxisCenterCalibrationPoint(
    double AngleDegrees,
    double PixelX,
    double PixelY,
    double ObservedCenterXMm,
    double ObservedCenterYMm);
