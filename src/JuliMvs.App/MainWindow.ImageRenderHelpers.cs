using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    private static Mat DrawCalibrationPreview(Mat image, PartDetection detection, string pointName)
    {
        var preview = image.Channels() == 1 ? image.CvtColor(ColorConversionCodes.GRAY2BGR) : image.Clone();
        var center = new OpenCvSharp.Point((int)Math.Round(detection.CenterXPixel), (int)Math.Round(detection.CenterYPixel));
        Cv2.DrawMarker(preview, center, Scalar.Yellow, MarkerTypes.Cross, 42, 3);
        Cv2.Circle(preview, center, 16, Scalar.LimeGreen, 3);
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
        Cv2.Circle(preview, center, 24, Scalar.Red, 4);
        Cv2.DrawMarker(preview, center, Scalar.Red, MarkerTypes.Cross, 58, 4);
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
            $"Board center: row 4 col 4, RMS={result.RmsErrorPixels:F3}px",
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
        Cv2.DrawContours(preview, [detection.Contour], -1, Scalar.LimeGreen, 3);
        Cv2.DrawMarker(preview, center, Scalar.Yellow, MarkerTypes.Cross, 44, 3);
        Cv2.Circle(preview, center, 16, Scalar.Yellow, 3);
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
}
