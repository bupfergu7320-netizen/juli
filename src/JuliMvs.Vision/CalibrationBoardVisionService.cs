using OpenCvSharp;

namespace JuliMvs.Vision;

public sealed class CalibrationBoardVisionService
{
    public CalibrationBoardDetectionResult DetectCircleGrid(
        Mat image,
        int rows,
        int columns,
        double spacingMm)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (rows < 2 || columns < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "Calibration board rows and columns must be at least 2.");
        }

        if (spacingMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spacingMm), "Calibration board spacing must be greater than 0.");
        }

        var expectedCount = checked(rows * columns);
        using var gray = ToGray(image);
        var boardMasks = FindBoardMasks(gray);
        try
        {
            var attempts = boardMasks
                .SelectMany(mask => FindDotCandidates(gray, mask, expectedCount))
                .OrderByDescending(attempt => attempt.Candidates.Count)
                .ThenBy(attempt => attempt.BoardMask.IsFullImage ? 1 : 0)
                .ThenByDescending(attempt => attempt.Score)
                .ToList();

            var orderingError = string.Empty;
            var candidateResults = new List<BoardDetectionCandidate>();
            foreach (var attempt in attempts)
            {
                var selected = attempt.Candidates
                    .OrderByDescending(candidate => candidate.Area)
                    .Take(expectedCount)
                    .Select(candidate => candidate.Center)
                    .ToList();
                if (selected.Count < expectedCount)
                {
                    continue;
                }

                try
                {
                    var grid = OrderGridPoints(selected, rows, columns);
                    candidateResults.Add(new BoardDetectionCandidate(
                        grid,
                        CalculateGridMetrics(grid, spacingMm),
                        attempt.Name,
                        attempt.BoardMask.Region,
                        attempt.BoardMask.IsFullImage,
                        attempt.Candidates.Count,
                        attempt.Score));
                }
                catch (InvalidOperationException ex)
                {
                    orderingError = ex.Message;
                }
            }

            if (candidateResults.Count > 0)
            {
                var best = candidateResults
                    .OrderBy(candidate => candidate.Metrics.RmsErrorPixels)
                    .ThenBy(candidate => candidate.Metrics.XyDifferencePercent)
                    .ThenBy(candidate => candidate.IsFullImage ? 1 : 0)
                    .ThenByDescending(candidate => candidate.CandidateCount)
                    .ThenByDescending(candidate => candidate.AttemptScore)
                    .First();

                return BuildResult(
                    image,
                    best.Grid,
                    rows,
                    columns,
                    spacingMm,
                    expectedCount,
                    best.DetectionMode,
                    best.BoardRegion,
                    best.Metrics);
            }

            var bestAttempt = attempts.FirstOrDefault();
            var bestPoints = bestAttempt?.Candidates
                .OrderByDescending(candidate => candidate.Area)
                .Take(Math.Max(expectedCount, 80))
                .Select(candidate => candidate.Center)
                .ToList() ?? [];
            var detectedCount = Math.Min(bestAttempt?.Candidates.Count ?? 0, expectedCount);
            var diagnostic = DrawFailurePreview(
                image,
                bestPoints,
                expectedCount,
                bestAttempt?.Name ?? "未找到候选圆点",
                bestAttempt?.BoardMask.Region);
            var extra = string.IsNullOrWhiteSpace(orderingError)
                ? string.Empty
                : $" 排序失败: {orderingError}";
            throw new CalibrationBoardDetectionException(
                $"标定板圆点识别不足: {detectedCount}/{expectedCount}。请检查标定板是否完整进入画面、曝光、清晰度和倾斜角度。{extra}",
                detectedCount,
                expectedCount,
                diagnostic);
        }
        finally
        {
            foreach (var boardMask in boardMasks)
            {
                boardMask.Dispose();
            }
        }
    }

    private static CalibrationBoardDetectionResult BuildResult(
        Mat image,
        IReadOnlyList<IReadOnlyList<Point2d>> grid,
        int rows,
        int columns,
        double spacingMm,
        int expectedCount,
        string detectionMode,
        Rect boardRegion)
    {
        var metrics = CalculateGridMetrics(grid, spacingMm);
        var orderedPoints = grid.SelectMany(row => row).ToList();
        var diagnostic = DrawSuccessPreview(
            image,
            grid,
            metrics.PixelPerMmX,
            metrics.PixelPerMmY,
            metrics.PixelPerMm,
            metrics.XyDifferencePercent,
            metrics.RmsErrorPixels,
            metrics.BoardAngleDegrees,
            detectionMode,
            boardRegion);

        return new CalibrationBoardDetectionResult(
            rows,
            columns,
            spacingMm,
            expectedCount,
            orderedPoints.Count,
            metrics.PixelPerMmX,
            metrics.PixelPerMmY,
            metrics.PixelPerMm,
            metrics.XyDifferencePercent,
            metrics.RmsErrorPixels,
            metrics.BoardAngleDegrees,
            orderedPoints,
            diagnostic,
            detectionMode);
    }

    private static CalibrationBoardDetectionResult BuildResult(
        Mat image,
        IReadOnlyList<IReadOnlyList<Point2d>> grid,
        int rows,
        int columns,
        double spacingMm,
        int expectedCount,
        string detectionMode,
        Rect boardRegion,
        GridMetrics metrics)
    {
        var orderedPoints = grid.SelectMany(row => row).ToList();
        var diagnostic = DrawSuccessPreview(
            image,
            grid,
            metrics.PixelPerMmX,
            metrics.PixelPerMmY,
            metrics.PixelPerMm,
            metrics.XyDifferencePercent,
            metrics.RmsErrorPixels,
            metrics.BoardAngleDegrees,
            detectionMode,
            boardRegion);

        return new CalibrationBoardDetectionResult(
            rows,
            columns,
            spacingMm,
            expectedCount,
            orderedPoints.Count,
            metrics.PixelPerMmX,
            metrics.PixelPerMmY,
            metrics.PixelPerMm,
            metrics.XyDifferencePercent,
            metrics.RmsErrorPixels,
            metrics.BoardAngleDegrees,
            orderedPoints,
            diagnostic,
            detectionMode);
    }

    private static GridMetrics CalculateGridMetrics(
        IReadOnlyList<IReadOnlyList<Point2d>> grid,
        double spacingMm)
    {
        var rows = grid.Count;
        var columns = grid[0].Count;
        var horizontalDistances = new List<double>();
        var verticalDistances = new List<double>();
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns - 1; column++)
            {
                horizontalDistances.Add(Distance(grid[row][column], grid[row][column + 1]));
            }
        }

        for (var row = 0; row < rows - 1; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                verticalDistances.Add(Distance(grid[row][column], grid[row + 1][column]));
            }
        }

        var averageHorizontalPixels = horizontalDistances.Average();
        var averageVerticalPixels = verticalDistances.Average();
        var pixelPerMmX = averageHorizontalPixels / spacingMm;
        var pixelPerMmY = averageVerticalPixels / spacingMm;
        var pixelPerMm = (pixelPerMmX + pixelPerMmY) / 2.0;
        var xyDifferencePercent = Math.Abs(pixelPerMmX - pixelPerMmY) / Math.Max(pixelPerMm, 0.0001) * 100.0;
        var rmsErrorPixels = CalculateDistanceRms(
            horizontalDistances,
            averageHorizontalPixels,
            verticalDistances,
            averageVerticalPixels);
        var boardAngleDegrees = CalculateBoardAngleDegrees(grid);

        return new GridMetrics(
            pixelPerMmX,
            pixelPerMmY,
            pixelPerMm,
            xyDifferencePercent,
            rmsErrorPixels,
            boardAngleDegrees);
    }

    private static IReadOnlyList<BoardMask> FindBoardMasks(Mat gray)
    {
        var masks = new List<BoardMask>();
        var fullMask = new Mat(gray.Size(), MatType.CV_8UC1, Scalar.White);
        masks.Add(new BoardMask("整图", fullMask, new Rect(0, 0, gray.Width, gray.Height), IsFullImage: true));

        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(7, 7), 0);

        using var otsu = new Mat();
        Cv2.Threshold(blurred, otsu, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        AddBrightBoardMasks(gray, otsu, "亮标定板-Otsu", masks);

        using var fixedBright = new Mat();
        Cv2.Threshold(blurred, fixedBright, 150, 255, ThresholdTypes.Binary);
        AddBrightBoardMasks(gray, fixedBright, "亮标定板-固定阈值", masks);

        return masks
            .GroupBy(mask => $"{mask.Region.X / 20}:{mask.Region.Y / 20}:{mask.Region.Width / 20}:{mask.Region.Height / 20}:{mask.IsFullImage}")
            .Select(group => group.First())
            .OrderBy(mask => mask.IsFullImage ? 1 : 0)
            .ThenByDescending(mask => mask.Region.Width * mask.Region.Height)
            .ToList();
    }

    private static void AddBrightBoardMasks(Mat gray, Mat brightBinary, string name, List<BoardMask> masks)
    {
        using var work = brightBinary.Clone();
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(17, 17));
        Cv2.MorphologyEx(work, work, MorphTypes.Close, closeKernel);
        Cv2.FindContours(work, out var contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

        var imageArea = Math.Max(1, gray.Width * gray.Height);
        var candidates = new List<(Point[] Contour, Rect Region, double Area, double Score)>();
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < imageArea * 0.015)
            {
                continue;
            }

            var region = Cv2.BoundingRect(contour);
            if (region.Width < gray.Width * 0.12 || region.Height < gray.Height * 0.12)
            {
                continue;
            }

            var aspect = region.Width / (double)Math.Max(region.Height, 1);
            if (aspect < 0.35 || aspect > 4.0)
            {
                continue;
            }

            var fillRatio = area / Math.Max(region.Width * region.Height, 1);
            if (fillRatio < 0.25)
            {
                continue;
            }

            var centerX = region.X + region.Width / 2.0;
            var centerY = region.Y + region.Height / 2.0;
            var centeredScore = 1.0 -
                (Math.Abs(centerX - gray.Width / 2.0) / gray.Width +
                 Math.Abs(centerY - gray.Height / 2.0) / gray.Height);
            candidates.Add((contour, region, area, area * Math.Max(centeredScore, 0.2)));
        }

        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Score).Take(4))
        {
            var mask = new Mat(gray.Size(), MatType.CV_8UC1, Scalar.Black);
            Cv2.DrawContours(mask, [candidate.Contour], -1, Scalar.White, -1);
            using var erodeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
            Cv2.Erode(mask, mask, erodeKernel);
            masks.Add(new BoardMask(name, mask, candidate.Region, IsFullImage: false));
        }
    }

    private static IReadOnlyList<DetectionAttempt> FindDotCandidates(Mat gray, BoardMask boardMask, int expectedCount)
    {
        var attempts = new List<DetectionAttempt>();
        foreach (var threshold in BuildDarkDotThresholds(gray))
        {
            using var masked = new Mat();
            Cv2.BitwiseAnd(threshold.Binary, boardMask.Mask, masked);
            var candidates = FindDotCandidatesFromBinary(gray, masked)
                .OrderByDescending(candidate => candidate.Area)
                .Take(Math.Max(expectedCount * 3, expectedCount))
                .ToList();
            var score = candidates.Count == 0
                ? 0
                : candidates.Count + candidates.Average(candidate => candidate.Circularity);
            attempts.Add(new DetectionAttempt(
                $"{boardMask.Name}+{threshold.Name}",
                boardMask,
                candidates,
                score));
            threshold.Dispose();
        }

        return attempts;
    }

    private static IReadOnlyList<ThresholdImage> BuildDarkDotThresholds(Mat gray)
    {
        var thresholds = new List<ThresholdImage>();
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        var otsuInv = new Mat();
        Cv2.Threshold(blurred, otsuInv, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        thresholds.Add(new ThresholdImage("黑点-Otsu", otsuInv));

        var fixed90 = new Mat();
        Cv2.Threshold(blurred, fixed90, 90, 255, ThresholdTypes.BinaryInv);
        thresholds.Add(new ThresholdImage("黑点-阈值90", fixed90));

        var fixed120 = new Mat();
        Cv2.Threshold(blurred, fixed120, 120, 255, ThresholdTypes.BinaryInv);
        thresholds.Add(new ThresholdImage("黑点-阈值120", fixed120));

        var adaptiveInv = new Mat();
        Cv2.AdaptiveThreshold(
            blurred,
            adaptiveInv,
            255,
            AdaptiveThresholdTypes.GaussianC,
            ThresholdTypes.BinaryInv,
            51,
            5);
        thresholds.Add(new ThresholdImage("黑点-自适应", adaptiveInv));

        return thresholds;
    }

    private static IReadOnlyList<DotCandidate> FindDotCandidatesFromBinary(Mat gray, Mat binary)
    {
        using var work = binary.Clone();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(work, work, MorphTypes.Open, kernel);
        Cv2.FindContours(work, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var imageArea = Math.Max(1, gray.Width * gray.Height);
        var minArea = Math.Max(20.0, imageArea * 0.000005);
        var maxArea = imageArea * 0.01;
        var candidates = new List<DotCandidate>();

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < minArea || area > maxArea)
            {
                continue;
            }

            var perimeter = Cv2.ArcLength(contour, true);
            if (perimeter <= 0)
            {
                continue;
            }

            var circularity = 4.0 * Math.PI * area / (perimeter * perimeter);
            if (circularity < 0.45)
            {
                continue;
            }

            var rect = Cv2.BoundingRect(contour);
            var aspect = rect.Width / (double)Math.Max(rect.Height, 1);
            if (aspect < 0.45 || aspect > 1.8)
            {
                continue;
            }

            var moments = Cv2.Moments(contour);
            if (Math.Abs(moments.M00) < double.Epsilon)
            {
                continue;
            }

            candidates.Add(new DotCandidate(
                new Point2d(moments.M10 / moments.M00, moments.M01 / moments.M00),
                area,
                circularity));
        }

        return candidates
            .OrderByDescending(candidate => candidate.Circularity)
            .ThenByDescending(candidate => candidate.Area)
            .ToList();
    }

    private static List<List<Point2d>> OrderGridPoints(IReadOnlyList<Point2d> points, int rows, int columns)
    {
        if (points.Count < rows * columns)
        {
            throw new InvalidOperationException("标定板圆点数量不足，无法排序。");
        }

        return OrderGridPointsByBoardAxes(points, rows, columns);
    }

    private static List<List<Point2d>> OrderGridPointsByBoardAxes(
        IReadOnlyList<Point2d> points,
        int rows,
        int columns)
    {
        GridOrdering? best = null;
        foreach (var axis in BuildGridAxisCandidates(points))
        {
            var ordering = TryOrderGridWithAxis(points, rows, columns, axis);
            if (ordering is null)
            {
                continue;
            }

            if (best is null ||
                ordering.Score < best.Score - 0.05 ||
                (Math.Abs(ordering.Score - best.Score) <= 0.05 &&
                 ordering.HorizontalAnglePenalty < best.HorizontalAnglePenalty))
            {
                best = ordering;
            }
        }

        return best?.Grid ??
               throw new InvalidOperationException("标定板圆点排序失败，请重拍标定板。");
    }

    private static IReadOnlyList<Point2d> BuildGridAxisCandidates(IReadOnlyList<Point2d> points)
    {
        var axes = new List<Point2d>();
        AddAxisCandidate(axes, new Point2d(1, 0));
        AddAxisCandidate(axes, new Point2d(0, 1));

        var nearestDistances = points
            .Select(point => points
                .Select(other => Distance(point, other))
                .Where(distance => distance > 0.000001)
                .DefaultIfEmpty(double.NaN)
                .Min())
            .Where(distance => !double.IsNaN(distance))
            .OrderBy(distance => distance)
            .ToList();
        if (nearestDistances.Count == 0)
        {
            return axes;
        }

        var spacing = nearestDistances[nearestDistances.Count / 2];
        var minimumNeighborDistance = spacing * 0.65;
        var maximumNeighborDistance = spacing * 1.35;
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var distance = Distance(points[i], points[j]);
                if (distance < minimumNeighborDistance || distance > maximumNeighborDistance)
                {
                    continue;
                }

                AddAxisCandidate(
                    axes,
                    new Point2d(
                        (points[j].X - points[i].X) / distance,
                        (points[j].Y - points[i].Y) / distance));
            }
        }

        return axes;
    }

    private static void AddAxisCandidate(List<Point2d> axes, Point2d axis)
    {
        var normalized = NormalizeAxis(axis);
        var angle = NormalizeAxisAngleDegrees(normalized);
        if (axes.Any(existing => AxisAngleDifferenceDegrees(NormalizeAxisAngleDegrees(existing), angle) < 1.0))
        {
            return;
        }

        axes.Add(normalized);
    }

    private static GridOrdering? TryOrderGridWithAxis(
        IReadOnlyList<Point2d> points,
        int rows,
        int columns,
        Point2d columnAxis)
    {
        var normalizedColumnAxis = NormalizeAxis(columnAxis);
        var rowNormal = new Point2d(-normalizedColumnAxis.Y, normalizedColumnAxis.X);
        var projected = points
            .Select(point => new ProjectedGridPoint(
                point,
                Dot(point, normalizedColumnAxis),
                Dot(point, rowNormal)))
            .OrderBy(point => point.RowProjection)
            .ToList();

        if (projected.Count < rows * columns)
        {
            return null;
        }

        var grid = new List<List<Point2d>>(rows);
        for (var row = 0; row < rows; row++)
        {
            var rowPoints = projected
                .Skip(row * columns)
                .Take(columns)
                .OrderBy(point => point.ColumnProjection)
                .Select(point => point.Point)
                .ToList();
            if (rowPoints.Count != columns)
            {
                return null;
            }

            grid.Add(rowPoints);
        }

        var horizontalDistances = new List<double>();
        var verticalDistances = new List<double>();
        var axisErrorSum = 0.0;
        var axisErrorCount = 0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns - 1; column++)
            {
                var delta = Subtract(grid[row][column + 1], grid[row][column]);
                horizontalDistances.Add(Length(delta));
                axisErrorSum += Math.Abs(Dot(delta, rowNormal));
                axisErrorCount++;
            }
        }

        for (var row = 0; row < rows - 1; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var delta = Subtract(grid[row + 1][column], grid[row][column]);
                verticalDistances.Add(Length(delta));
                axisErrorSum += Math.Abs(Dot(delta, normalizedColumnAxis));
                axisErrorCount++;
            }
        }

        if (horizontalDistances.Count == 0 || verticalDistances.Count == 0)
        {
            return null;
        }

        var averageHorizontalPixels = horizontalDistances.Average();
        var averageVerticalPixels = verticalDistances.Average();
        var distanceRms = CalculateDistanceRms(
            horizontalDistances,
            averageHorizontalPixels,
            verticalDistances,
            averageVerticalPixels);
        var axisError = axisErrorSum / Math.Max(axisErrorCount, 1);
        var score = distanceRms + axisError;
        var horizontalAnglePenalty = Math.Abs(NormalizeSignedAngleDegrees(
            Math.Atan2(normalizedColumnAxis.Y, normalizedColumnAxis.X) * 180.0 / Math.PI));
        return new GridOrdering(grid, score, horizontalAnglePenalty);
    }

    private static Point2d NormalizeAxis(Point2d axis)
    {
        var length = Length(axis);
        if (length <= 0.000001)
        {
            return new Point2d(1, 0);
        }

        var normalized = new Point2d(axis.X / length, axis.Y / length);
        return normalized.X < -0.000001 || (Math.Abs(normalized.X) <= 0.000001 && normalized.Y < 0)
            ? new Point2d(-normalized.X, -normalized.Y)
            : normalized;
    }

    private static double NormalizeAxisAngleDegrees(Point2d axis)
    {
        var angle = Math.Atan2(axis.Y, axis.X) * 180.0 / Math.PI;
        while (angle < 0)
        {
            angle += 180.0;
        }

        while (angle >= 180.0)
        {
            angle -= 180.0;
        }

        return angle;
    }

    private static double AxisAngleDifferenceDegrees(double first, double second)
    {
        var difference = Math.Abs(first - second);
        return Math.Min(difference, 180.0 - difference);
    }

    private static double NormalizeSignedAngleDegrees(double angle)
    {
        while (angle <= -90.0)
        {
            angle += 180.0;
        }

        while (angle > 90.0)
        {
            angle -= 180.0;
        }

        return angle;
    }

    private static Point2d Subtract(Point2d first, Point2d second)
    {
        return new Point2d(first.X - second.X, first.Y - second.Y);
    }

    private static Point2d Add(Point2d first, Point2d second)
    {
        return new Point2d(first.X + second.X, first.Y + second.Y);
    }

    private static Point2d Scale(Point2d point, double scale)
    {
        return new Point2d(point.X * scale, point.Y * scale);
    }

    private static double Dot(Point2d first, Point2d second)
    {
        return first.X * second.X + first.Y * second.Y;
    }

    private static double Length(Point2d point)
    {
        return Math.Sqrt(point.X * point.X + point.Y * point.Y);
    }

    private static Mat DrawSuccessPreview(
        Mat image,
        IReadOnlyList<IReadOnlyList<Point2d>> grid,
        double pixelPerMmX,
        double pixelPerMmY,
        double pixelPerMm,
        double xyDifferencePercent,
        double rmsErrorPixels,
        double boardAngleDegrees,
        string detectionMode,
        Rect boardRegion)
    {
        var preview = EnsureBgr(image);
        for (var row = 0; row < grid.Count; row++)
        {
            for (var column = 0; column < grid[row].Count; column++)
            {
                var point = ToPoint(grid[row][column]);
                Cv2.Circle(preview, point, 8, Scalar.LimeGreen, 2);
                Cv2.DrawMarker(preview, point, Scalar.Yellow, MarkerTypes.Cross, 18, 2);
                if (column < grid[row].Count - 1)
                {
                    Cv2.Line(preview, point, ToPoint(grid[row][column + 1]), new Scalar(255, 191, 0), 2);
                }

                if (row < grid.Count - 1)
                {
                    Cv2.Line(preview, point, ToPoint(grid[row + 1][column]), new Scalar(255, 191, 0), 2);
                }
            }
        }

        DrawRotatedGridOutline(preview, grid);

        var textLines = new[]
        {
            $"Circle grid {grid.Count}x{grid[0].Count}",
            $"Mode={detectionMode}",
            $"X={pixelPerMmX:F4} px/mm Y={pixelPerMmY:F4} px/mm",
            $"AVG={pixelPerMm:F4} px/mm Diff={xyDifferencePercent:F2}%",
            $"RMS={rmsErrorPixels:F3}px Angle={boardAngleDegrees:F3}deg"
        };
        for (var i = 0; i < textLines.Length; i++)
        {
            Cv2.PutText(
                preview,
                textLines[i],
                new Point(24, 38 + i * 34),
                HersheyFonts.HersheySimplex,
                0.75,
                Scalar.White,
                2);
        }

        return preview;
    }

    private static void DrawRotatedGridOutline(Mat preview, IReadOnlyList<IReadOnlyList<Point2d>> grid)
    {
        if (grid.Count < 2 || grid[0].Count < 2)
        {
            return;
        }

        var lastRow = grid.Count - 1;
        var lastColumn = grid[0].Count - 1;
        var topLeft = grid[0][0];
        var topRight = grid[0][lastColumn];
        var bottomLeft = grid[lastRow][0];
        var bottomRight = grid[lastRow][lastColumn];
        var columnAxis = NormalizeVector(Add(Subtract(topRight, topLeft), Subtract(bottomRight, bottomLeft)));
        var rowAxis = NormalizeVector(Add(Subtract(bottomLeft, topLeft), Subtract(bottomRight, topRight)));
        var marginX = CalculateAverageHorizontalSpacing(grid) * 0.75;
        var marginY = CalculateAverageVerticalSpacing(grid) * 0.75;
        var outline = new[]
        {
            ToPoint(Subtract(Subtract(topLeft, Scale(columnAxis, marginX)), Scale(rowAxis, marginY))),
            ToPoint(Subtract(Add(topRight, Scale(columnAxis, marginX)), Scale(rowAxis, marginY))),
            ToPoint(Add(Add(bottomRight, Scale(columnAxis, marginX)), Scale(rowAxis, marginY))),
            ToPoint(Add(Subtract(bottomLeft, Scale(columnAxis, marginX)), Scale(rowAxis, marginY)))
        };
        Cv2.Polylines(preview, [outline], true, Scalar.LimeGreen, 4, LineTypes.AntiAlias);
    }

    private static double CalculateAverageHorizontalSpacing(IReadOnlyList<IReadOnlyList<Point2d>> grid)
    {
        var distances = new List<double>();
        for (var row = 0; row < grid.Count; row++)
        {
            for (var column = 0; column < grid[row].Count - 1; column++)
            {
                distances.Add(Distance(grid[row][column], grid[row][column + 1]));
            }
        }

        return distances.Count == 0 ? 1.0 : distances.Average();
    }

    private static double CalculateAverageVerticalSpacing(IReadOnlyList<IReadOnlyList<Point2d>> grid)
    {
        var distances = new List<double>();
        for (var row = 0; row < grid.Count - 1; row++)
        {
            for (var column = 0; column < grid[row].Count; column++)
            {
                distances.Add(Distance(grid[row][column], grid[row + 1][column]));
            }
        }

        return distances.Count == 0 ? 1.0 : distances.Average();
    }

    private static Point2d NormalizeVector(Point2d vector)
    {
        var length = Length(vector);
        return length <= 0.000001
            ? new Point2d(1, 0)
            : new Point2d(vector.X / length, vector.Y / length);
    }

    private static Mat DrawFailurePreview(
        Mat image,
        IReadOnlyList<Point2d> points,
        int expectedCount,
        string detectionMode,
        Rect? boardRegion)
    {
        var preview = EnsureBgr(image);
        if (boardRegion.HasValue)
        {
            Cv2.Rectangle(preview, boardRegion.Value, Scalar.Cyan, 3);
        }

        foreach (var point in points)
        {
            Cv2.Circle(preview, ToPoint(point), 8, Scalar.Red, 2);
        }

        Cv2.PutText(
            preview,
            $"Detected {points.Count}/{expectedCount}",
            new Point(24, 42),
            HersheyFonts.HersheySimplex,
            1.0,
            Scalar.Red,
            2);
        Cv2.PutText(
            preview,
            detectionMode,
            new Point(24, 82),
            HersheyFonts.HersheySimplex,
            0.8,
            Scalar.Red,
            2);
        return preview;
    }

    private static Mat ToGray(Mat image)
    {
        if (image.Channels() == 1)
        {
            return image.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private static Mat EnsureBgr(Mat image)
    {
        if (image.Channels() == 3)
        {
            return image.Clone();
        }

        var bgr = new Mat();
        Cv2.CvtColor(image, bgr, ColorConversionCodes.GRAY2BGR);
        return bgr;
    }

    private static double Distance(Point2d first, Point2d second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double CalculateDistanceRms(
        IReadOnlyList<double> horizontalDistances,
        double averageHorizontalPixels,
        IReadOnlyList<double> verticalDistances,
        double averageVerticalPixels)
    {
        var errors = horizontalDistances
            .Select(distance => distance - averageHorizontalPixels)
            .Concat(verticalDistances.Select(distance => distance - averageVerticalPixels))
            .ToList();
        return Math.Sqrt(errors.Sum(error => error * error) / Math.Max(errors.Count, 1));
    }

    private static double CalculateBoardAngleDegrees(IReadOnlyList<IReadOnlyList<Point2d>> grid)
    {
        var angles = new List<double>();
        foreach (var row in grid)
        {
            var first = row.First();
            var last = row.Last();
            angles.Add(Math.Atan2(last.Y - first.Y, last.X - first.X) * 180.0 / Math.PI);
        }

        return angles.Average();
    }

    private static Point ToPoint(Point2d point)
    {
        return new Point((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    private sealed record DotCandidate(Point2d Center, double Area, double Circularity);

    private sealed record ProjectedGridPoint(Point2d Point, double ColumnProjection, double RowProjection);

    private sealed record GridOrdering(List<List<Point2d>> Grid, double Score, double HorizontalAnglePenalty);

    private sealed record DetectionAttempt(
        string Name,
        BoardMask BoardMask,
        IReadOnlyList<DotCandidate> Candidates,
        double Score);

    private sealed record BoardDetectionCandidate(
        List<List<Point2d>> Grid,
        GridMetrics Metrics,
        string DetectionMode,
        Rect BoardRegion,
        bool IsFullImage,
        int CandidateCount,
        double AttemptScore);

    private sealed record GridMetrics(
        double PixelPerMmX,
        double PixelPerMmY,
        double PixelPerMm,
        double XyDifferencePercent,
        double RmsErrorPixels,
        double BoardAngleDegrees);

    private sealed record ThresholdImage(string Name, Mat Binary) : IDisposable
    {
        public void Dispose()
        {
            Binary.Dispose();
        }
    }

    private sealed record BoardMask(string Name, Mat Mask, Rect Region, bool IsFullImage) : IDisposable
    {
        public void Dispose()
        {
            Mask.Dispose();
        }
    }
}

public sealed class CalibrationBoardDetectionException : InvalidOperationException, IDisposable
{
    public CalibrationBoardDetectionException(
        string message,
        int detectedPointCount,
        int expectedPointCount,
        Mat diagnosticImage)
        : base(message)
    {
        DetectedPointCount = detectedPointCount;
        ExpectedPointCount = expectedPointCount;
        DiagnosticImage = diagnosticImage;
    }

    public int DetectedPointCount { get; }

    public int ExpectedPointCount { get; }

    public Mat DiagnosticImage { get; }

    public void Dispose()
    {
        DiagnosticImage.Dispose();
    }
}

public sealed class CalibrationBoardDetectionResult : IDisposable
{
    public CalibrationBoardDetectionResult(
        int rows,
        int columns,
        double spacingMm,
        int expectedPointCount,
        int detectedPointCount,
        double pixelPerMmX,
        double pixelPerMmY,
        double pixelPerMm,
        double xyDifferencePercent,
        double rmsErrorPixels,
        double boardAngleDegrees,
        IReadOnlyList<Point2d> points,
        Mat diagnosticImage,
        string detectionMode)
    {
        Rows = rows;
        Columns = columns;
        SpacingMm = spacingMm;
        ExpectedPointCount = expectedPointCount;
        DetectedPointCount = detectedPointCount;
        PixelPerMmX = pixelPerMmX;
        PixelPerMmY = pixelPerMmY;
        PixelPerMm = pixelPerMm;
        XyDifferencePercent = xyDifferencePercent;
        RmsErrorPixels = rmsErrorPixels;
        BoardAngleDegrees = boardAngleDegrees;
        Points = points;
        DiagnosticImage = diagnosticImage;
        DetectionMode = detectionMode;
    }

    public int Rows { get; }

    public int Columns { get; }

    public double SpacingMm { get; }

    public int ExpectedPointCount { get; }

    public int DetectedPointCount { get; }

    public double PixelPerMmX { get; }

    public double PixelPerMmY { get; }

    public double PixelPerMm { get; }

    public double XyDifferencePercent { get; }

    public double RmsErrorPixels { get; }

    public double BoardAngleDegrees { get; }

    public IReadOnlyList<Point2d> Points { get; }

    public Mat DiagnosticImage { get; }

    public string DetectionMode { get; }

    public void Dispose()
    {
        DiagnosticImage.Dispose();
    }
}
