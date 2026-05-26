namespace JuliMvs.Core.Vision;

public static class RAxisCenterCalibrationSolver
{
    public static RAxisCenterCalibration Solve(IEnumerable<RAxisCenterCalibrationPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var pointList = points.ToList();
        if (pointList.Count < 3)
        {
            throw new ArgumentException("R轴中心标定至少需要3个点。", nameof(points));
        }

        EnsureNonCollinear(pointList);

        var candidates = new[]
            {
                FitRotationModel(pointList, angleDirection: 1),
                FitRotationModel(pointList, angleDirection: -1)
            }
            .Where(candidate => double.IsFinite(candidate.Radius) && candidate.Radius > 1e-6)
            .Select(candidate =>
            {
                var (rms, max) = CalculateErrors(pointList, candidate);
                return new RotationFit(candidate, rms, max);
            })
            .OrderBy(fit => fit.Rms)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("R轴中心标定点退化；请使用不同R角度采集的点位。");
        }

        var best = candidates[0];
        return new RAxisCenterCalibration
        {
            Enabled = true,
            CalibrationId = Guid.NewGuid().ToString("N"),
            CenterXMm = best.Parameters.CenterX,
            CenterYMm = best.Parameters.CenterY,
            RadiusMm = best.Parameters.Radius,
            RmsErrorMm = best.Rms,
            MaxErrorMm = best.Max,
            MachineAngleDirection = best.Parameters.AngleDirection,
            CreatedAt = DateTimeOffset.Now,
            Points = pointList
        };
    }

    public static IReadOnlyList<RAxisCenterCalibrationResidual> CalculateResiduals(
        RAxisCenterCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        if (calibration.Points.Count < 3)
        {
            return [];
        }

        var parameters = FitRotationModel(calibration.Points, calibration.GetMachineAngleDirection());
        return CalculateResiduals(calibration.Points, parameters)
            .OrderBy(residual => residual.AngleDegrees)
            .ToArray();
    }

    private static void EnsureNonCollinear(IReadOnlyList<RAxisCenterCalibrationPoint> points)
    {
        var maxArea = 0.0;
        for (var i = 0; i < points.Count - 2; i++)
        {
            for (var j = i + 1; j < points.Count - 1; j++)
            {
                for (var k = j + 1; k < points.Count; k++)
                {
                    var area = Math.Abs(
                        (points[j].ObservedCenterXMm - points[i].ObservedCenterXMm) *
                        (points[k].ObservedCenterYMm - points[i].ObservedCenterYMm) -
                        (points[k].ObservedCenterXMm - points[i].ObservedCenterXMm) *
                        (points[j].ObservedCenterYMm - points[i].ObservedCenterYMm));
                    maxArea = Math.Max(maxArea, area);
                }
            }
        }

        if (maxArea < 1e-8)
        {
            throw new InvalidOperationException("R轴中心标定点退化；请使用不同R角度采集的点位。");
        }
    }

    private static RotationParameters FitRotationModel(
        IReadOnlyList<RAxisCenterCalibrationPoint> points,
        int angleDirection)
    {
        var normal = new double[4, 4];
        var vector = new double[4];

        foreach (var point in points)
        {
            var radians = angleDirection * point.AngleDegrees * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            AddNormalEquation(normal, vector, [1.0, 0.0, cos, -sin], point.ObservedCenterXMm);
            AddNormalEquation(normal, vector, [0.0, 1.0, sin, cos], point.ObservedCenterYMm);
        }

        var parameters = Solve4x4(normal, vector);
        var radius = Math.Sqrt(parameters[2] * parameters[2] + parameters[3] * parameters[3]);
        return new RotationParameters(
            parameters[0],
            parameters[1],
            parameters[2],
            parameters[3],
            radius,
            angleDirection);
    }

    private static void AddNormalEquation(
        double[,] normal,
        double[] vector,
        IReadOnlyList<double> terms,
        double target)
    {
        for (var row = 0; row < 4; row++)
        {
            vector[row] += terms[row] * target;
            for (var column = 0; column < 4; column++)
            {
                normal[row, column] += terms[row] * terms[column];
            }
        }
    }

    private static (double Rms, double Max) CalculateErrors(
        IReadOnlyList<RAxisCenterCalibrationPoint> points,
        RotationParameters parameters)
    {
        var sumSquared = 0.0;
        var max = 0.0;
        foreach (var residual in CalculateResiduals(points, parameters))
        {
            sumSquared += residual.DistanceMm * residual.DistanceMm;
            max = Math.Max(max, residual.DistanceMm);
        }

        return (Math.Sqrt(sumSquared / points.Count), max);
    }

    private static IReadOnlyList<RAxisCenterCalibrationResidual> CalculateResiduals(
        IReadOnlyList<RAxisCenterCalibrationPoint> points,
        RotationParameters parameters)
    {
        var residuals = new List<RAxisCenterCalibrationResidual>(points.Count);
        foreach (var point in points)
        {
            var radians = parameters.AngleDirection * point.AngleDegrees * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            var predictedX = parameters.CenterX + cos * parameters.RadiusX - sin * parameters.RadiusY;
            var predictedY = parameters.CenterY + sin * parameters.RadiusX + cos * parameters.RadiusY;
            var dx = point.ObservedCenterXMm - predictedX;
            var dy = point.ObservedCenterYMm - predictedY;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            residuals.Add(new RAxisCenterCalibrationResidual(
                point.AngleDegrees,
                point.ObservedCenterXMm,
                point.ObservedCenterYMm,
                predictedX,
                predictedY,
                dx,
                dy,
                distance));
        }

        return residuals;
    }

    private static double[] Solve4x4(double[,] matrix, double[] vector)
    {
        var a = new double[4, 5];
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                a[row, column] = matrix[row, column];
            }

            a[row, 4] = vector[row];
        }

        for (var pivot = 0; pivot < 4; pivot++)
        {
            var bestRow = pivot;
            var bestValue = Math.Abs(a[pivot, pivot]);
            for (var row = pivot + 1; row < 4; row++)
            {
                var value = Math.Abs(a[row, pivot]);
                if (value > bestValue)
                {
                    bestValue = value;
                    bestRow = row;
                }
            }

            if (bestValue < 1e-12)
            {
                throw new InvalidOperationException("R轴中心标定点退化；请使用不同R角度采集的点位。");
            }

            if (bestRow != pivot)
            {
                for (var column = pivot; column < 5; column++)
                {
                    (a[pivot, column], a[bestRow, column]) = (a[bestRow, column], a[pivot, column]);
                }
            }

            var pivotValue = a[pivot, pivot];
            for (var column = pivot; column < 5; column++)
            {
                a[pivot, column] /= pivotValue;
            }

            for (var row = 0; row < 4; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                var factor = a[row, pivot];
                for (var column = pivot; column < 5; column++)
                {
                    a[row, column] -= factor * a[pivot, column];
                }
            }
        }

        return [a[0, 4], a[1, 4], a[2, 4], a[3, 4]];
    }

    private sealed record RotationParameters(
        double CenterX,
        double CenterY,
        double RadiusX,
        double RadiusY,
        double Radius,
        int AngleDirection);

    private sealed record RotationFit(RotationParameters Parameters, double Rms, double Max);
}

public sealed record RAxisCenterCalibrationResidual(
    double AngleDegrees,
    double ObservedXMm,
    double ObservedYMm,
    double PredictedXMm,
    double PredictedYMm,
    double ErrorXMm,
    double ErrorYMm,
    double DistanceMm);
