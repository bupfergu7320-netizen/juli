using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JuliMvs.App.Services;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App;

public partial class MainWindow
{
    private const int ProductionPreviewMaxPixels = 1600;

    private static BitmapImage CreateBitmapImageFromMat(Mat image)
    {
        Cv2.ImEncode(".bmp", image, out var buffer);
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(buffer);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapImage CreatePreviewBitmapImageFromMat(Mat image)
    {
        if (image.Width <= ProductionPreviewMaxPixels && image.Height <= ProductionPreviewMaxPixels)
        {
            return CreateBitmapImageFromMat(image);
        }

        var scale = Math.Min(
            (double)ProductionPreviewMaxPixels / image.Width,
            (double)ProductionPreviewMaxPixels / image.Height);
        var previewSize = new OpenCvSharp.Size(
            Math.Max(1, (int)Math.Round(image.Width * scale)),
            Math.Max(1, (int)Math.Round(image.Height * scale)));
        using var preview = new Mat();
        Cv2.Resize(image, preview, previewSize, 0, 0, InterpolationFlags.Area);
        return CreateBitmapImageFromMat(preview);
    }

    private static Mat DrawCalibrationPreview(Mat image, PartDetection detection, string pointName)
    {
        var preview = image.Channels() == 1 ? image.CvtColor(ColorConversionCodes.GRAY2BGR) : image.Clone();
        var center = new OpenCvSharp.Point((int)Math.Round(detection.CenterXPixel), (int)Math.Round(detection.CenterYPixel));
        DrawOutlinedMarker(preview, center, Scalar.Yellow, MarkerTypes.Cross, 48, 4);
        DrawOutlinedCircle(preview, center, 18, Scalar.LimeGreen, 4);
        Cv2.PutText(
            preview,
            pointName,
            new OpenCvSharp.Point(Math.Max(0, center.X + 18), Math.Max(32, center.Y - 18)),
            HersheyFonts.HersheySimplex,
            1.0,
            Scalar.Yellow,
            2);
        Cv2.PutText(
            preview,
            $"Pixel=({detection.CenterXPixel:F1},{detection.CenterYPixel:F1})",
            new OpenCvSharp.Point(24, 42),
            HersheyFonts.HersheySimplex,
            0.85,
            Scalar.White,
            2);
        return preview;
    }

    private static Mat DrawCalibrationBoardCenterPreview(
        Mat image,
        CalibrationBoardDetectionResult result,
        Point2d centerPoint,
        string pointName)
    {
        var preview = result.DiagnosticImage.Empty()
            ? image.Channels() == 1 ? image.CvtColor(ColorConversionCodes.GRAY2BGR) : image.Clone()
            : result.DiagnosticImage.Clone();
        var center = new OpenCvSharp.Point((int)Math.Round(centerPoint.X), (int)Math.Round(centerPoint.Y));
        DrawOutlinedCircle(preview, center, 26, Scalar.Red, 5);
        DrawOutlinedMarker(preview, center, Scalar.Red, MarkerTypes.Cross, 64, 5);
        Cv2.PutText(
            preview,
            $"{pointName} center dot",
            new OpenCvSharp.Point(Math.Max(0, center.X + 26), Math.Max(36, center.Y - 26)),
            HersheyFonts.HersheySimplex,
            1.0,
            Scalar.Red,
            3);
        Cv2.PutText(
            preview,
            $"Pixel=({centerPoint.X:F1},{centerPoint.Y:F1})",
            new OpenCvSharp.Point(24, 232),
            HersheyFonts.HersheySimplex,
            0.85,
            Scalar.Red,
            2);
        Cv2.PutText(
            preview,
            $"Board center row4 col4 RMS={result.RmsErrorPixels:F3}px",
            new OpenCvSharp.Point(24, 266),
            HersheyFonts.HersheySimplex,
            0.75,
            Scalar.Red,
            2);
        return preview;
    }

    private static Mat DrawDetectionPreview(Mat image, PartDetection detection)
    {
        var preview = image.Channels() == 1 ? image.CvtColor(ColorConversionCodes.GRAY2BGR) : image.Clone();
        var center = new OpenCvSharp.Point((int)Math.Round(detection.CenterXPixel), (int)Math.Round(detection.CenterYPixel));
        DrawOutlinedContour(preview, detection.Contour, Scalar.LimeGreen, 5);
        DrawOutlinedMarker(preview, center, Scalar.Yellow, MarkerTypes.Cross, 52, 5);
        DrawOutlinedCircle(preview, center, 20, Scalar.Yellow, 5);
        Cv2.PutText(
            preview,
            $"X={detection.CenterXPixel:F1} Y={detection.CenterYPixel:F1} R={detection.AngleDegrees:F2}",
            new OpenCvSharp.Point(24, 42),
            HersheyFonts.HersheySimplex,
            0.85,
            Scalar.White,
            2);
        return preview;
    }

    private static void DrawOutlinedContour(Mat image, OpenCvSharp.Point[] contour, Scalar color, int thickness)
    {
        Cv2.DrawContours(image, [contour], -1, Scalar.Black, thickness + 4, LineTypes.AntiAlias);
        Cv2.DrawContours(image, [contour], -1, color, thickness, LineTypes.AntiAlias);
    }

    private static void DrawOutlinedMarker(
        Mat image,
        OpenCvSharp.Point center,
        Scalar color,
        MarkerTypes markerType,
        int markerSize,
        int thickness)
    {
        Cv2.DrawMarker(image, center, Scalar.Black, markerType, markerSize, thickness + 4, LineTypes.AntiAlias);
        Cv2.DrawMarker(image, center, color, markerType, markerSize, thickness, LineTypes.AntiAlias);
    }

    private static void DrawOutlinedCircle(Mat image, OpenCvSharp.Point center, int radius, Scalar color, int thickness)
    {
        Cv2.Circle(image, center, radius, Scalar.Black, thickness + 4, LineTypes.AntiAlias);
        Cv2.Circle(image, center, radius, color, thickness, LineTypes.AntiAlias);
    }

}
