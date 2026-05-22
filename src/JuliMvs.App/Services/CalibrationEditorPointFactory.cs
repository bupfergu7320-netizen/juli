using System.Globalization;
using JuliMvs.Core.Vision;

namespace JuliMvs.App.Services;

internal static class CalibrationEditorPointFactory
{
    public static List<EditableCalibrationPoint> CreateDefaultNinePointCalibrationPoints(double stepMm)
    {
        var xLeft = -stepMm;
        var xCenter = 0.0;
        var xRight = stepMm;
        var yTop = stepMm;
        var yCenter = 0.0;
        var yBottom = -stepMm;
        return
        [
            new EditableCalibrationPoint("\u7b2c1\u70b9", 0, 0, xLeft, yTop, false, xLeft, yTop),
            new EditableCalibrationPoint("\u7b2c2\u70b9", 0, 0, xCenter, yTop, false, xCenter, yTop),
            new EditableCalibrationPoint("\u7b2c3\u70b9", 0, 0, xRight, yTop, false, xRight, yTop),
            new EditableCalibrationPoint("\u7b2c4\u70b9", 0, 0, xLeft, yCenter, false, xLeft, yCenter),
            new EditableCalibrationPoint("\u7b2c5\u70b9", 0, 0, xCenter, yCenter, false, xCenter, yCenter),
            new EditableCalibrationPoint("\u7b2c6\u70b9", 0, 0, xRight, yCenter, false, xRight, yCenter),
            new EditableCalibrationPoint("\u7b2c7\u70b9", 0, 0, xLeft, yBottom, false, xLeft, yBottom),
            new EditableCalibrationPoint("\u7b2c8\u70b9", 0, 0, xCenter, yBottom, false, xCenter, yBottom),
            new EditableCalibrationPoint("\u7b2c9\u70b9", 0, 0, xRight, yBottom, false, xRight, yBottom)
        ];
    }

    public static List<EditableRAxisCenterCalibrationPoint> CreateDefaultRAxisCenterCalibrationPoints()
    {
        double[] angles = [0, 45, 90, 135, 180, 225, 270, 315];
        return angles
            .Select(angle => new EditableRAxisCenterCalibrationPoint(
                BuildRAxisCenterPointName(angle),
                angle,
                0,
                0,
                0,
                0,
                false))
            .ToList();
    }

    public static string BuildRAxisCenterPointName(double angleDegrees)
    {
        return $"R{angleDegrees.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture)}\u5ea6";
    }

    public static string GetCalibrationPointName(int index, int requiredPointCount)
    {
        return index >= 0 && index < requiredPointCount
            ? $"\u7b2c{index + 1}\u70b9"
            : $"\u70b9{index + 1}";
    }

    public static int GetPointNumber(
        IReadOnlyList<EditableCalibrationPoint> points,
        EditableCalibrationPoint selectedPoint)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (ReferenceEquals(points[i], selectedPoint))
            {
                return i + 1;
            }
        }

        return 1;
    }

    public static CameraCalibration CalculateCameraCalibration(
        CalibrationEditorSolver solver,
        IReadOnlyList<EditableCalibrationPoint> points,
        int requiredPointCount)
    {
        return solver.CalculateCamera(
            points.Select(ToCameraCalibrationEditorPoint).ToList(),
            requiredPointCount);
    }

    public static RAxisCenterCalibration CalculateRAxisCenterCalibration(
        CalibrationEditorSolver solver,
        IReadOnlyList<EditableRAxisCenterCalibrationPoint> points,
        CameraCalibration cameraCalibration,
        string captureTarget)
    {
        return solver.CalculateRAxisCenter(
            points.Select(ToRAxisCenterCalibrationEditorPoint).ToList(),
            cameraCalibration,
            captureTarget);
    }

    public static CameraCalibrationEditorPoint ToCameraCalibrationEditorPoint(EditableCalibrationPoint point)
    {
        return new CameraCalibrationEditorPoint(
            point.Name,
            point.PixelX,
            point.PixelY,
            point.MachineXMm,
            point.MachineYMm,
            point.IsCaptured);
    }

    public static RAxisCenterCalibrationEditorPoint ToRAxisCenterCalibrationEditorPoint(
        EditableRAxisCenterCalibrationPoint point)
    {
        return new RAxisCenterCalibrationEditorPoint(
            point.Name,
            point.AngleDegrees,
            point.PixelX,
            point.PixelY,
            point.MachineXMm,
            point.MachineYMm,
            point.IsCaptured);
    }
}

internal sealed class EditableCalibrationPoint
{
    public EditableCalibrationPoint()
        : this(string.Empty, 0, 0, 0, 0, false)
    {
    }

    public EditableCalibrationPoint(
        string name,
        double pixelX,
        double pixelY,
        double machineXMm,
        double machineYMm,
        bool isCaptured,
        double? suggestedMachineXMm = null,
        double? suggestedMachineYMm = null)
    {
        Name = name;
        PixelX = pixelX;
        PixelY = pixelY;
        MachineXMm = machineXMm;
        MachineYMm = machineYMm;
        IsCaptured = isCaptured;
        SuggestedMachineXMm = suggestedMachineXMm ?? machineXMm;
        SuggestedMachineYMm = suggestedMachineYMm ?? machineYMm;
    }

    public string Name { get; set; }

    public double PixelX { get; set; }

    public double PixelY { get; set; }

    public double MachineXMm { get; set; }

    public double MachineYMm { get; set; }

    public bool IsCaptured { get; set; }

    public double SuggestedMachineXMm { get; set; }

    public double SuggestedMachineYMm { get; set; }

    public string SuggestedMachineXText => SuggestedMachineXMm.ToString("0.###", CultureInfo.InvariantCulture);

    public string SuggestedMachineYText => SuggestedMachineYMm.ToString("0.###", CultureInfo.InvariantCulture);

    public string CaptureStatus => IsCaptured ? "\u5df2\u91c7\u96c6" : "\u672a\u91c7\u96c6";
}

internal sealed class EditableRAxisCenterCalibrationPoint
{
    public EditableRAxisCenterCalibrationPoint()
        : this(string.Empty, 0, 0, 0, 0, 0, false)
    {
    }

    public EditableRAxisCenterCalibrationPoint(
        string name,
        double angleDegrees,
        double pixelX,
        double pixelY,
        double machineXMm,
        double machineYMm,
        bool isCaptured)
    {
        Name = name;
        AngleDegrees = angleDegrees;
        PixelX = pixelX;
        PixelY = pixelY;
        MachineXMm = machineXMm;
        MachineYMm = machineYMm;
        IsCaptured = isCaptured;
    }

    public string Name { get; set; }

    public double AngleDegrees { get; set; }

    public double PixelX { get; set; }

    public double PixelY { get; set; }

    public double MachineXMm { get; set; }

    public double MachineYMm { get; set; }

    public bool IsCaptured { get; set; }

    public string CaptureStatus => IsCaptured ? "\u5df2\u91c7\u96c6" : "\u672a\u91c7\u96c6";
}
