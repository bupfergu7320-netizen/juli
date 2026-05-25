namespace JuliMvs.Core.Vision;

public static class CameraCalibrationSolver
{
    public static CameraCalibration Solve(IEnumerable<CalibrationPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var pointList = points.ToList();
        if (pointList.Count < 3)
        {
            throw new ArgumentException("At least three calibration points are required.", nameof(points));
        }

        var xCoefficients = SolveLeastSquares(pointList, point => point.MachineXMm);
        var yCoefficients = SolveLeastSquares(pointList, point => point.MachineYMm);
        var rms = CalculateRms(pointList, xCoefficients, yCoefficients);

        return new CameraCalibration
        {
            Enabled = true,
            CalibrationId = Guid.NewGuid().ToString("N"),
            X0 = xCoefficients[0],
            XPixelCoefficient = xCoefficients[1],
            YPixelCoefficient = xCoefficients[2],
            Y0 = yCoefficients[0],
            YPixelXCoefficient = yCoefficients[1],
            YPixelYCoefficient = yCoefficients[2],
            RmsErrorMm = rms,
            CreatedAt = DateTimeOffset.Now,
            Points = pointList
        };
    }

    private static double[] SolveLeastSquares(
        IReadOnlyList<CalibrationPoint> points,
        Func<CalibrationPoint, double> targetSelector)
    {
        var normal = new double[3, 3];
        var vector = new double[3];

        foreach (var point in points)
        {
            var terms = new[] { 1.0, point.PixelX, point.PixelY };
            var target = targetSelector(point);
            for (var row = 0; row < 3; row++)
            {
                vector[row] += terms[row] * target;
                for (var column = 0; column < 3; column++)
                {
                    normal[row, column] += terms[row] * terms[column];
                }
            }
        }

        return Solve3x3(normal, vector);
    }

    private static double CalculateRms(
        IReadOnlyList<CalibrationPoint> points,
        IReadOnlyList<double> xCoefficients,
        IReadOnlyList<double> yCoefficients)
    {
        var sumSquared = 0.0;
        foreach (var point in points)
        {
            var x = Evaluate(xCoefficients, point.PixelX, point.PixelY);
            var y = Evaluate(yCoefficients, point.PixelX, point.PixelY);
            var dx = x - point.MachineXMm;
            var dy = y - point.MachineYMm;
            sumSquared += dx * dx + dy * dy;
        }

        return Math.Sqrt(sumSquared / points.Count);
    }

    private static double Evaluate(IReadOnlyList<double> coefficients, double pixelX, double pixelY)
    {
        return coefficients[0] + coefficients[1] * pixelX + coefficients[2] * pixelY;
    }

    private static double[] Solve3x3(double[,] matrix, double[] vector)
    {
        var a = new double[3, 4];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                a[row, column] = matrix[row, column];
            }

            a[row, 3] = vector[row];
        }

        for (var pivot = 0; pivot < 3; pivot++)
        {
            var bestRow = pivot;
            var bestValue = Math.Abs(a[pivot, pivot]);
            for (var row = pivot + 1; row < 3; row++)
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
                throw new InvalidOperationException("标定点退化，不能共线；请使用不在同一直线上的点位。");
            }

            if (bestRow != pivot)
            {
                for (var column = pivot; column < 4; column++)
                {
                    (a[pivot, column], a[bestRow, column]) = (a[bestRow, column], a[pivot, column]);
                }
            }

            var pivotValue = a[pivot, pivot];
            for (var column = pivot; column < 4; column++)
            {
                a[pivot, column] /= pivotValue;
            }

            for (var row = 0; row < 3; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                var factor = a[row, pivot];
                for (var column = pivot; column < 4; column++)
                {
                    a[row, column] -= factor * a[pivot, column];
                }
            }
        }

        return [a[0, 3], a[1, 3], a[2, 3]];
    }
}
