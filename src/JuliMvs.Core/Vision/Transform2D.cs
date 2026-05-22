using JuliMvs.Core;

namespace JuliMvs.Core.Vision;

public sealed record Transform2D(
    double M00,
    double M01,
    double M02,
    double M10,
    double M11,
    double M12)
{
    public static Transform2D Identity { get; } = new(1.0, 0.0, 0.0, 0.0, 1.0, 0.0);

    public static Transform2D Translate(double xMm, double yMm)
    {
        return new Transform2D(1.0, 0.0, xMm, 0.0, 1.0, yMm);
    }

    public static Transform2D Rotate(double angleDegrees)
    {
        var radians = DegreesToRadians(angleDegrees);
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Transform2D(cos, -sin, 0.0, sin, cos, 0.0);
    }

    public static Transform2D RotateAround(MachinePoint center, double angleDegrees)
    {
        var radians = DegreesToRadians(angleDegrees);
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var tx = center.XMm - (cos * center.XMm) + (sin * center.YMm);
        var ty = center.YMm - (sin * center.XMm) - (cos * center.YMm);
        return new Transform2D(cos, -sin, tx, sin, cos, ty);
    }

    public static Transform2D FromVectorAngleToRigid(
        double sourceXMm,
        double sourceYMm,
        double sourceAngleDegrees,
        double targetXMm,
        double targetYMm,
        double targetAngleDegrees)
    {
        var deltaDegrees = AngleMath.NormalizeDeltaDegrees(targetAngleDegrees, sourceAngleDegrees);
        var radians = DegreesToRadians(deltaDegrees);
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var tx = targetXMm - (cos * sourceXMm) + (sin * sourceYMm);
        var ty = targetYMm - (sin * sourceXMm) - (cos * sourceYMm);
        return new Transform2D(cos, -sin, tx, sin, cos, ty);
    }

    public MachinePoint TransformPoint(MachinePoint point)
    {
        return new MachinePoint(
            (M00 * point.XMm) + (M01 * point.YMm) + M02,
            (M10 * point.XMm) + (M11 * point.YMm) + M12);
    }

    public Transform2D Then(Transform2D next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return new Transform2D(
            (next.M00 * M00) + (next.M01 * M10),
            (next.M00 * M01) + (next.M01 * M11),
            (next.M00 * M02) + (next.M01 * M12) + next.M02,
            (next.M10 * M00) + (next.M11 * M10),
            (next.M10 * M01) + (next.M11 * M11),
            (next.M10 * M02) + (next.M11 * M12) + next.M12);
    }

    public Transform2D Invert()
    {
        var determinant = (M00 * M11) - (M01 * M10);
        if (Math.Abs(determinant) < 1e-12)
        {
            throw new InvalidOperationException("Transform is not invertible.");
        }

        var inv00 = M11 / determinant;
        var inv01 = -M01 / determinant;
        var inv10 = -M10 / determinant;
        var inv11 = M00 / determinant;
        return new Transform2D(
            inv00,
            inv01,
            -((inv00 * M02) + (inv01 * M12)),
            inv10,
            inv11,
            -((inv10 * M02) + (inv11 * M12)));
    }

    private static double DegreesToRadians(double angleDegrees)
    {
        return angleDegrees * Math.PI / 180.0;
    }
}
