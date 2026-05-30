namespace JuliMvs.Vision;

public enum AutoPartShapeClass
{
    StrongEllipse,
    IrregularRound,
    WeakEllipse,
    NearCircle
}

public enum AutoAngleMethod
{
    PcaAxis,
    ContourPolar,
    Disabled
}

public sealed record AutoAngleStrategyDecision(
    AutoPartShapeClass ShapeClass,
    AutoAngleMethod Method,
    bool AllowsRCorrection,
    double AxisRatio,
    double PcaRatio,
    double Circularity,
    double TemplateRadiusSignalPixels,
    string Message);

public static class AutoAngleStrategy
{
    public const double StrongEllipseAxisRatio = 1.20;
    public const double WeakEllipseAxisRatio = 1.03;
    public const double StrongPcaRatio = 1.20;
    public const double NearCircleMaximumAxisRatio = 1.03;
    public const double NearCircleMinimumCircularity = 0.94;
    public const double MinimumContourRadiusSignalPixels = 0.50;

    public static AutoAngleStrategyDecision Select(
        double widthPixels,
        double heightPixels,
        double pcaRatio,
        double circularity,
        double templateRadiusSignalPixels)
    {
        var axisRatio = CalculateAxisRatio(widthPixels, heightPixels);
        var hasContourDirection = templateRadiusSignalPixels >= MinimumContourRadiusSignalPixels;

        if (axisRatio >= StrongEllipseAxisRatio && pcaRatio >= StrongPcaRatio)
        {
            return new AutoAngleStrategyDecision(
                AutoPartShapeClass.StrongEllipse,
                AutoAngleMethod.PcaAxis,
                AllowsRCorrection: true,
                axisRatio,
                pcaRatio,
                circularity,
                templateRadiusSignalPixels,
                "自动判断为明显椭圆：使用长轴/PCA角度。");
        }

        if (hasContourDirection)
        {
            var shapeClass = axisRatio >= WeakEllipseAxisRatio
                ? AutoPartShapeClass.WeakEllipse
                : AutoPartShapeClass.IrregularRound;
            return new AutoAngleStrategyDecision(
                shapeClass,
                AutoAngleMethod.ContourPolar,
                AllowsRCorrection: true,
                axisRatio,
                pcaRatio,
                circularity,
                templateRadiusSignalPixels,
                shapeClass == AutoPartShapeClass.WeakEllipse
                    ? "自动判断为无强主方向微椭圆：使用Shape轮廓配准，候选不唯一时判NG。"
                    : "自动判断为不规则圆/带缺口：使用Shape轮廓配准。");
        }

        if (axisRatio >= WeakEllipseAxisRatio)
        {
            return new AutoAngleStrategyDecision(
                AutoPartShapeClass.WeakEllipse,
                AutoAngleMethod.ContourPolar,
                AllowsRCorrection: true,
                axisRatio,
                pcaRatio,
                circularity,
                templateRadiusSignalPixels,
                "自动判断为弱方向微椭圆：项目要求检测R，使用Shape轮廓配准；方向不可靠时判NG。");
        }

        return new AutoAngleStrategyDecision(
            AutoPartShapeClass.IrregularRound,
            AutoAngleMethod.ContourPolar,
            AllowsRCorrection: true,
            axisRatio,
            pcaRatio,
            circularity,
            templateRadiusSignalPixels,
            "自动判断为弱方向不规则圆：项目要求检测R，使用Shape轮廓配准；方向不可靠时判NG。");
    }

    private static double CalculateAxisRatio(double widthPixels, double heightPixels)
    {
        var major = Math.Max(Math.Abs(widthPixels), Math.Abs(heightPixels));
        var minor = Math.Max(Math.Min(Math.Abs(widthPixels), Math.Abs(heightPixels)), 0.0001);
        return major / minor;
    }
}
