using JuliMvs.Plc;

namespace JuliMvs.App.Services;

internal static class PlcOutputDirectionSettings
{
    public static bool IsSimpleXyTransform(PlcOutputTransform transform)
    {
        return IsSimpleAxisTransform(transform.Xx, transform.Xy, transform.XBias) &&
            IsSimpleAxisTransform(transform.Yy, transform.Yx, transform.YBias);
    }

    public static bool IsSimpleXInverted(PlcOutputTransform transform)
    {
        return IsSimpleAxisTransform(transform.Xx, transform.Xy, transform.XBias) &&
            transform.Xx < 0.0;
    }

    public static bool IsSimpleYInverted(PlcOutputTransform transform)
    {
        return IsSimpleAxisTransform(transform.Yy, transform.Yx, transform.YBias) &&
            transform.Yy < 0.0;
    }

    public static PlcOutputTransform ApplySimpleXyDirection(
        PlcOutputTransform current,
        bool invertX,
        bool invertY)
    {
        return current with
        {
            Xx = invertX ? -1.0 : 1.0,
            Xy = 0.0,
            XBias = 0.0,
            Yx = 0.0,
            Yy = invertY ? -1.0 : 1.0,
            YBias = 0.0
        };
    }

    public static string FormatSimpleDirectionText(PlcOutputTransform transform)
    {
        return
            $"X\u65b9\u5411={(transform.Xx < 0.0 ? "\u53d6\u53cd" : "\u4e0d\u53d6\u53cd")}, " +
            $"Y\u65b9\u5411={(transform.Yy < 0.0 ? "\u53d6\u53cd" : "\u4e0d\u53d6\u53cd")}";
    }

    private static bool IsSimpleAxisTransform(double mainCoefficient, double crossCoefficient, double bias)
    {
        return Math.Abs(Math.Abs(mainCoefficient) - 1.0) < 0.000001 &&
            Math.Abs(crossCoefficient) < 0.000001 &&
            Math.Abs(bias) < 0.000001;
    }
}
