namespace JuliMvs.Core.Vision;

public sealed record VisionParameters
{
    public ImageRoi Roi { get; init; } = ImageRoi.Empty;

    public int BinaryThreshold { get; init; } = 0;

    public int BlurKernelSize { get; init; } = 5;

    public double MinPartAreaPixels { get; init; } = 500.0;

    public double MaxPartAreaPixels { get; init; } = double.PositiveInfinity;

    public LensDistortionCalibration LensDistortionCalibration { get; init; } = LensDistortionCalibration.Disabled;

    public CameraCalibration CameraCalibration { get; init; } = CameraCalibration.Disabled;

    public RAxisCenterCalibration RAxisCenterCalibration { get; init; } = RAxisCenterCalibration.Disabled;

    public double AngleToleranceDegrees { get; init; } = 0.5;

    public double XPositionToleranceMm { get; init; } = 0.5;

    public double YPositionToleranceMm { get; init; } = 0.5;

    public double WidthToleranceMm { get; init; } = 0.5;

    public double HeightToleranceMm { get; init; } = 0.5;

    public double AreaTolerancePercent { get; init; } = 5.0;

    public double ShapeScoreThreshold { get; init; } = 0.85;

    public AngleDetectionMode AngleDetectionMode { get; init; } = AngleDetectionMode.AutoPcaOrPolarRing;

    public double TemplateAngleSearchRangeDegrees { get; init; } = 180.0;

    public double TemplateAngleCoarseStepDegrees { get; init; } = 5.0;

    public double TemplateAngleFineStepDegrees { get; init; } = 0.5;

    public double TemplateAngleMinimumScore { get; init; } = 0.35;

    public double TemplateAngleMinimumScoreMargin { get; init; } = 0.08;

    public bool InvertXCompensation { get; init; }

    public bool InvertYCompensation { get; init; }

    public bool InvertRotationCompensation { get; init; }

    public bool BackSideNgEnabled { get; init; }

    public double BackSideNgMinimumBackScore { get; init; } = 0.0;

    public double BackSideNgMaximumScoreDifference { get; init; } = 0.0;

    public static VisionParameters Default { get; } = new();
}
