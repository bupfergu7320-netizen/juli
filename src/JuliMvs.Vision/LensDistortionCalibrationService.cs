using JuliMvs.Core.Vision;
using OpenCvSharp;

namespace JuliMvs.Vision;

public sealed class LensDistortionCalibrationService
{
    public const int MinimumImageCount = 6;
    public const double MaximumAcceptedBoardRmsPixels = 2.0;
    public const double MaximumAcceptedBoardXyDifferencePercent = 5.0;

    public LensDistortionCalibrationInput CreateInput(
        CalibrationBoardDetectionResult detection,
        Size imageSize,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(detection);
        if (detection.DetectedPointCount != detection.ExpectedPointCount)
        {
            throw new InvalidOperationException("标定板圆点未完整识别，不能用于畸变标定。");
        }

        if (detection.RmsErrorPixels > MaximumAcceptedBoardRmsPixels)
        {
            throw new InvalidOperationException(
                $"标定板圆点排序误差过大，不能用于畸变标定。当前RMS={detection.RmsErrorPixels:F3}px，要求<={MaximumAcceptedBoardRmsPixels:F3}px。请删除该图并重拍，重点检查标定板是否旋转过大、圆点是否出视野或诊断图连线是否交叉。");
        }

        if (detection.XyDifferencePercent > MaximumAcceptedBoardXyDifferencePercent)
        {
            throw new InvalidOperationException(
                $"标定板X/Y比例差异过大，不能用于畸变标定。当前差异={detection.XyDifferencePercent:F3}%，要求<={MaximumAcceptedBoardXyDifferencePercent:F3}%。请删除该图并重拍，重点检查标定板倾斜、透视角度和诊断图连线。");
        }

        return new LensDistortionCalibrationInput(
            detection.Rows,
            detection.Columns,
            detection.SpacingMm,
            imageSize.Width,
            imageSize.Height,
            detection.Points.Select(point => new Point2f((float)point.X, (float)point.Y)).ToArray(),
            sourceName);
    }

    public LensDistortionCalibration Calibrate(IReadOnlyList<LensDistortionCalibrationInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count < MinimumImageCount)
        {
            throw new InvalidOperationException($"畸变标定至少需要{MinimumImageCount}张有效标定板图片。");
        }

        var first = inputs[0];
        if (inputs.Any(input =>
                input.Rows != first.Rows ||
                input.Columns != first.Columns ||
                Math.Abs(input.SpacingMm - first.SpacingMm) > 0.000001))
        {
            throw new InvalidOperationException("所有畸变标定图片必须使用相同规格的标定板。");
        }

        if (inputs.Any(input => input.ImageWidth != first.ImageWidth || input.ImageHeight != first.ImageHeight))
        {
            throw new InvalidOperationException("所有畸变标定图片必须使用相同相机分辨率。");
        }

        var expectedPointCount = first.Rows * first.Columns;
        if (inputs.Any(input => input.ImagePoints.Count != expectedPointCount))
        {
            throw new InvalidOperationException("畸变标定图片圆点数量不一致。");
        }

        var objectPointTemplate = BuildObjectPoints(first.Rows, first.Columns, first.SpacingMm);
        var objectPoints = inputs.Select(_ => objectPointTemplate).ToArray();
        var imagePoints = inputs.Select(input => input.ImagePoints).ToArray();
        var imageSize = new Size(first.ImageWidth, first.ImageHeight);
        var cameraMatrix = new double[3, 3];
        var distortionCoefficients = new double[5];

        var rms = Cv2.CalibrateCamera(
            objectPoints,
            imagePoints,
            imageSize,
            cameraMatrix,
            distortionCoefficients,
            out _,
            out _,
            CalibrationFlags.None);

        return new LensDistortionCalibration
        {
            Enabled = true,
            CalibrationId = Guid.NewGuid().ToString("N"),
            ImageWidth = first.ImageWidth,
            ImageHeight = first.ImageHeight,
            CameraMatrix = Flatten(cameraMatrix),
            DistortionCoefficients = distortionCoefficients,
            RmsReprojectionErrorPixels = rms,
            CapturedImageCount = inputs.Count,
            CreatedAt = DateTimeOffset.Now
        };
    }

    private static IReadOnlyList<Point3f> BuildObjectPoints(int rows, int columns, double spacingMm)
    {
        var points = new List<Point3f>(rows * columns);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                points.Add(new Point3f((float)(column * spacingMm), (float)(row * spacingMm), 0f));
            }
        }

        return points;
    }

    private static double[] Flatten(double[,] matrix)
    {
        return
        [
            matrix[0, 0],
            matrix[0, 1],
            matrix[0, 2],
            matrix[1, 0],
            matrix[1, 1],
            matrix[1, 2],
            matrix[2, 0],
            matrix[2, 1],
            matrix[2, 2]
        ];
    }
}

public sealed record LensDistortionCalibrationInput(
    int Rows,
    int Columns,
    double SpacingMm,
    int ImageWidth,
    int ImageHeight,
    IReadOnlyList<Point2f> ImagePoints,
    string SourceName);
