using OpenCvSharp;

namespace JuliMvs.Vision;

public sealed record PartDetection(
    Point[] Contour,
    Point Offset,
    double CenterXPixel,
    double CenterYPixel,
    double AngleDegrees,
    double WidthPixels,
    double HeightPixels,
    double WidthMm,
    double HeightMm,
    double AreaPixels);
