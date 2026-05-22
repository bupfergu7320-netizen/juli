namespace JuliMvs.Core;

public static class AngleMath
{
    public static double NormalizeDegrees180(double angleDegrees)
    {
        var normalized = angleDegrees % 180.0;
        if (normalized <= -90.0)
        {
            normalized += 180.0;
        }

        if (normalized > 90.0)
        {
            normalized -= 180.0;
        }

        return normalized;
    }

    public static double NormalizeDegrees360(double angleDegrees)
    {
        var normalized = angleDegrees % 360.0;
        if (normalized <= -180.0)
        {
            normalized += 360.0;
        }

        if (normalized > 180.0)
        {
            normalized -= 360.0;
        }

        return normalized;
    }

    public static double NormalizeDeltaDegrees(double currentDegrees, double referenceDegrees)
    {
        return NormalizeDegrees180(currentDegrees - referenceDegrees);
    }

    public static double NormalizeDeltaDegrees360(double currentDegrees, double referenceDegrees)
    {
        return NormalizeDegrees360(currentDegrees - referenceDegrees);
    }

    public static bool IsAngleWithinTolerance(double offsetDegrees, double toleranceDegrees)
    {
        return Math.Abs(offsetDegrees) <= Math.Abs(toleranceDegrees);
    }

}
