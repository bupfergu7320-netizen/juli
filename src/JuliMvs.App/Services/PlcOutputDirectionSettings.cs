using JuliMvs.Plc;

namespace JuliMvs.App.Services;

internal static class PlcOutputDirectionSettings
{
    public static bool IsSimpleXyTransform(PlcOutputTransform transform)
    {
        return IsSimpleDirectXyTransform(transform) || IsSimpleSwappedXyTransform(transform);
    }

    public static bool IsSimpleXInverted(PlcOutputTransform transform)
    {
        return IsSimpleXySwapped(transform)
            ? transform.Yx < 0.0
            : transform.Xx < 0.0;
    }

    public static bool IsSimpleYInverted(PlcOutputTransform transform)
    {
        return IsSimpleXySwapped(transform)
            ? transform.Xy < 0.0
            : transform.Yy < 0.0;
    }

    public static bool IsSimpleXySwapped(PlcOutputTransform transform)
    {
        return IsSimpleSwappedXyTransform(transform);
    }

    public static PlcOutputTransform ApplySimpleXyDirection(
        PlcOutputTransform current,
        bool invertX,
        bool invertY,
        bool swapXy = false)
    {
        var xSign = invertX ? -1.0 : 1.0;
        var ySign = invertY ? -1.0 : 1.0;

        return current with
        {
            Xx = swapXy ? 0.0 : xSign,
            Xy = swapXy ? ySign : 0.0,
            XBias = 0.0,
            Yx = swapXy ? xSign : 0.0,
            Yy = swapXy ? 0.0 : ySign,
            YBias = 0.0
        };
    }

    public static string FormatSimpleDirectionText(PlcOutputTransform transform)
    {
        return
            $"X/Y输出={(IsSimpleXySwapped(transform) ? "交换" : "不交换")}, " +
            $"X方向={(IsSimpleXInverted(transform) ? "取反" : "不取反")}, " +
            $"Y方向={(IsSimpleYInverted(transform) ? "取反" : "不取反")}";
    }

    private static bool IsSimpleDirectXyTransform(PlcOutputTransform transform)
    {
        return IsSimpleAxisTransform(transform.Xx, transform.Xy, transform.XBias) &&
            IsSimpleAxisTransform(transform.Yy, transform.Yx, transform.YBias);
    }

    private static bool IsSimpleSwappedXyTransform(PlcOutputTransform transform)
    {
        return IsSimpleAxisTransform(transform.Xy, transform.Xx, transform.XBias) &&
            IsSimpleAxisTransform(transform.Yx, transform.Yy, transform.YBias);
    }

    private static bool IsSimpleAxisTransform(double mainCoefficient, double crossCoefficient, double bias)
    {
        return Math.Abs(Math.Abs(mainCoefficient) - 1.0) < 0.000001 &&
            Math.Abs(crossCoefficient) < 0.000001 &&
            Math.Abs(bias) < 0.000001;
    }
}
