namespace JuliMvs.Core.Vision;

public sealed record PartTemplate(
    Guid Id,
    string BatchNo,
    string ProductName,
    string? ImagePath,
    DateTimeOffset CreatedAt,
    double ReferenceCenterXPixel,
    double ReferenceCenterYPixel,
    double ReferenceCenterXMm,
    double ReferenceCenterYMm,
    string SourceCameraCalibrationId,
    string SourceDistortionCalibrationId,
    double ReferenceAngleDegrees,
    double WidthMm,
    double HeightMm,
    double AreaPixels,
    double MatchScoreBaseline,
    ImageRoi Roi,
    VisionParameters Parameters,
    double ReferenceWidthPixels = 0.0,
    double ReferenceHeightPixels = 0.0);
