using JuliMvs.Core;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using OpenCvSharp;

namespace JuliMvs.Vision;

public sealed class OpenCvVisionService
{
    private const double TemplateAngleRefineWindowDegrees = 6.0;
    private const double TemplateAngleAmbiguitySeparationDegrees = 12.0;
    private const int TemplateAnglePatchMinimumPixels = 140;
    private const int TemplateAnglePatchMaximumPixels = 420;
    private const int TemplateAnglePatchPaddingPixels = 24;
    private const int AutoFeaturePatchMinimumPixels = 64;
    private const int AutoFeaturePatchMaximumPixels = 192;
    private const int AutoFeatureCandidateGrid = 9;
    private const int AutoFeatureMaxCandidates = 4;
    private const double AutoFeatureMinimumRadiusRatio = 0.18;
    private const double AutoFeatureMaximumRadiusRatio = 0.72;
    private const double AutoFeaturePatchSizeRatio = 0.32;
    private const double AutoFeatureMinimumTextureScore = 4.0;
    private const double AutoFeatureSeparatedScoreSlack = 0.13;
    private const double AutoFeatureSeparatedMarginMinimum = 0.01;
    private const double AutoFeatureNearTieScoreMargin = 0.02;
    private const double AutoFeatureLargeAngleMinimumDegrees = 30.0;
    private const int PolarRingAngularSamples = 720;
    private const int PolarRingRadialSamples = 64;
    private const double PolarRingInnerRadiusRatio = 0.25;
    private const double PolarRingOuterRadiusRatio = 0.82;
    private const double PolarRingAlternativeSeparationDegrees = 18.0;
    private const double PolarRingMinimumSignal = 0.015;
    private const double AutoPcaReliableRatio = 1.12;
    private const double AutoPcaConfidenceScale = 0.35;
    private const double ContourPolarMinimumRadiusSignal = 0.002;

    private sealed record CandidateDetection(string Source, PartDetection Detection);

    private sealed record ContourAngleProfile(
        double PcaAngleDegrees,
        double PcaRatio,
        double Circularity,
        bool IsPcaReliable);

    private sealed record ResolvedAngle(
        double AngleDegrees,
        bool AllowsFullRotation,
        string Source,
        double Score,
        double AlternativeScore,
        double SearchRangeDegrees,
        IReadOnlyList<AngleCandidateDiagnostic>? Candidates = null,
        string? Detail = null);

    private sealed record TemplateAngleModel(
        Guid TemplateId,
        string? ImagePath,
        string SourceDistortionCalibrationId,
        ImageRoi Roi,
        Mat Patch,
        Size SearchSize,
        double SourceCenterXPixel,
        double SourceCenterYPixel);

    private sealed record AutoFeatureAngleModel(
        Guid TemplateId,
        string? ImagePath,
        string SourceDistortionCalibrationId,
        ImageRoi Roi,
        IReadOnlyList<AutoFeatureCandidate> Features);

    private sealed record PolarRingAngleModel(
        Guid TemplateId,
        string? ImagePath,
        string SourceDistortionCalibrationId,
        ImageRoi Roi,
        float[] Signature,
        double Signal,
        double RadiusPixels);

    private sealed record ContourPolarAngleModel(
        Guid TemplateId,
        string? ImagePath,
        string SourceDistortionCalibrationId,
        ImageRoi Roi,
        float[] Signature,
        double Signal,
        ContourAngleProfile Profile);

    private sealed record ContourMirrorFaceDebugComputation(
        double FrontScore,
        double BackScore,
        double ScoreDifference,
        bool IsReliable,
        FrontBackDebugDecision SuggestedDecision,
        double FrontAngleOffsetDegrees,
        double BackAngleOffsetDegrees,
        double FrontAlternativeScore,
        double BackAlternativeScore,
        double CurrentSignal,
        double TemplateSignal,
        double SearchRangeDegrees,
        string Message);

    private sealed record AutoFeatureCandidate(
        int Index,
        Mat Patch,
        double CenterXPixel,
        double CenterYPixel,
        double OffsetXPixel,
        double OffsetYPixel,
        double RadiusPixels,
        double TemplateAngleDegrees,
        double QualityScore);

    private sealed record AutoFeatureVote(
        int FeatureIndex,
        double AngleOffsetDegrees,
        double ResolvedAngleDegrees,
        double Score,
        double AlternativeScore,
        double QualityScore,
        IReadOnlyList<(double AngleDegrees, double Score)> Candidates);

    private sealed record UndistortMap(
        string CalibrationId,
        int ImageWidth,
        int ImageHeight,
        Mat MapX,
        Mat MapY) : IDisposable
    {
        public void Dispose()
        {
            MapX.Dispose();
            MapY.Dispose();
        }
    }

    private sealed record AutoFeatureAngleCluster(
        double AngleOffsetDegrees,
        double ResolvedAngleDegrees,
        double Score,
        double RankScore,
        double AlternativeScore,
        int SupportCount,
        int BestFeatureIndex);

    private readonly object _templateAngleModelSync = new();
    private readonly object _undistortMapSync = new();
    private TemplateAngleModel? _cachedTemplateAngleModel;
    private AutoFeatureAngleModel? _cachedAutoFeatureAngleModel;
    private PolarRingAngleModel? _cachedPolarRingAngleModel;
    private ContourPolarAngleModel? _cachedContourPolarAngleModel;
    private UndistortMap? _cachedUndistortMap;
    private readonly PoseInvariantTemplateMatcher _templateMatcher = new();

    public PartTemplate CreateTemplate(
        Mat image,
        string batchNo,
        string productName,
        VisionParameters parameters,
        string? imagePath = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);

        EnsureProductionCalibrationReady(image, parameters);
        var detection = DetectPart(image, parameters);
        if (detection is null)
        {
            throw new InvalidOperationException("No valid workpiece contour was found in the template image.");
        }

        var referenceMachineCenter = parameters.CameraCalibration.PixelToMachine(
            detection.CenterXPixel,
            detection.CenterYPixel);
        var referenceAngleDegrees = ResolveTemplateReferenceAngle(detection, parameters);

        return new PartTemplate(
            Guid.NewGuid(),
            batchNo,
            productName,
            imagePath,
            DateTimeOffset.Now,
            detection.CenterXPixel,
            detection.CenterYPixel,
            referenceMachineCenter.XMm,
            referenceMachineCenter.YMm,
            parameters.CameraCalibration.CalibrationId,
            GetCurrentDistortionCalibrationId(parameters),
            referenceAngleDegrees,
            detection.WidthMm,
            detection.HeightMm,
            detection.AreaPixels,
            MatchScoreBaseline: 1.0,
            parameters.Roi,
            parameters,
            detection.WidthPixels,
            detection.HeightPixels);
    }

    public OpenCvInspectionOutput Inspect(
        Mat image,
        PartTemplate template,
        VisionParameters? parameters = null,
        string? partNo = null,
        string? rawImagePath = null,
        bool buildDiagnosticImage = true)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(template);

        var activeParameters = parameters ?? template.Parameters;
        EnsureProductionSetup(image, template, activeParameters);
        using var workingImage = PrepareImage(image, activeParameters);
        var diagnostic = buildDiagnosticImage ? EnsureBgr(workingImage) : new Mat();
        var detection = DetectPartPrepared(
            workingImage,
            activeParameters,
            template,
            out var candidateDiagnostics,
            out var candidateDetections,
            buildDiagnostics: buildDiagnosticImage);
        var resolvedPartNo = string.IsNullOrWhiteSpace(partNo)
            ? DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff")
            : partNo;

        if (detection is null)
        {
            var error = InspectionResult.Error(
                template.BatchNo,
                resolvedPartNo,
                NgReason.MatchFailed,
                "No valid workpiece contour was found.");
            if (buildDiagnosticImage)
            {
                Cv2.PutText(diagnostic, "MATCH FAILED", new Point(24, 48), HersheyFonts.HersheySimplex, 1.2, Scalar.Red, 2);
            }

            return new OpenCvInspectionOutput(error, diagnostic, candidateDiagnostics: candidateDiagnostics);
        }

        if (buildDiagnosticImage)
        {
            DrawCandidateContours(diagnostic, candidateDetections, detection);
            var contourForDraw = OffsetContour(detection.Contour, detection.Offset);
            Cv2.DrawContours(diagnostic, new[] { contourForDraw }, -1, Scalar.LimeGreen, 2);
            DrawCenterMarkers(diagnostic, detection, template);
        }

        var referenceCenter = GetReferenceCenterMachine(template, activeParameters);
        var currentCenter = activeParameters.CameraCalibration.PixelToMachine(detection.CenterXPixel, detection.CenterYPixel);
        var resolvedAngle = ResolveInspectionAngle(
            workingImage,
            detection,
            template,
            activeParameters,
            buildDiagnostics: buildDiagnosticImage);
        var angleDiagnostic = BuildAngleDiagnostic(activeParameters, detection.AngleDegrees, resolvedAngle);
        var similarity = CalculateTemplateSimilarity(
            workingImage,
            detection,
            template,
            activeParameters,
            resolvedAngle.AngleDegrees);
        var matchScore = similarity?.FinalScore ?? CalculateMatchScore(detection, template);
        var alignmentSnapshot = XyrAlignmentSolver.Solve(
            new PartPose2D(currentCenter.XMm, currentCenter.YMm, resolvedAngle.AngleDegrees, matchScore),
            new PartPose2D(
                referenceCenter.XMm,
                referenceCenter.YMm,
                template.ReferenceAngleDegrees,
                template.MatchScoreBaseline),
            activeParameters.RAxisCenterCalibration,
            activeParameters.InvertRotationCompensation ? -1 : 1,
            resolvedAngle.AllowsFullRotation);
        var angleOffset = alignmentSnapshot.AngleOffsetDegrees;
        var rotateCompensation = alignmentSnapshot.HomeRActionDegrees;

        var decision = DecideSingleShot(
            angleOffset,
            rotateCompensation,
            activeParameters.AngleToleranceDegrees,
            activeParameters.ShapeScoreThreshold,
            angleDiagnostic,
            similarity,
            matchScore,
            out var reason,
            out var message);
        var frontBackDecisionDiagnostic = TryApplyBackSideNg(
            detection,
            template,
            activeParameters,
            buildDiagnostics: buildDiagnosticImage,
            ref decision,
            ref reason,
            ref message);
        var rotateCompensationForOutput = decision == InspectionDecision.Ok
            ? rotateCompensation
            : 0.0;
        var xyCompensation = decision == InspectionDecision.Ok
            ? new MachinePoint(
                alignmentSnapshot.HomeXActionMm,
                alignmentSnapshot.HomeYActionMm)
            : new MachinePoint(0.0, 0.0);
        var measurement = new InspectionMeasurement(
            detection.CenterXPixel,
            detection.CenterYPixel,
            alignmentSnapshot.XOffsetMm,
            alignmentSnapshot.YOffsetMm,
            xyCompensation.XMm,
            xyCompensation.YMm,
            resolvedAngle.AngleDegrees,
            angleOffset,
            rotateCompensationForOutput,
            detection.WidthMm,
            detection.HeightMm,
            detection.AreaPixels,
            matchScore);

        if (buildDiagnosticImage)
        {
            DrawOverlay(diagnostic, decision, message, measurement, angleDiagnostic, similarity);
        }

        var result = InspectionResult.FromMeasurement(
            template.BatchNo,
            resolvedPartNo,
            decision,
            reason,
            message,
            measurement,
            rawImagePath);

        return new OpenCvInspectionOutput(
            result,
            diagnostic,
            alignmentSnapshot,
            candidateDiagnostics,
            angleDiagnostic,
            similarity,
            frontBackDecisionDiagnostic);
    }

    public PartDetection? DetectPart(Mat image, VisionParameters parameters)
    {
        using var workingImage = PrepareImage(image, parameters);
        return DetectPartPrepared(workingImage, parameters, template: null, out _, out _, buildDiagnostics: false);
    }

    public FrontBackDebugResult? AnalyzeFrontBackDebug(
        Mat image,
        PartTemplate template,
        VisionParameters? parameters = null,
        string? fixedOverlayDiagnosticPath = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(template);

        var activeParameters = parameters ?? template.Parameters;
        if (!HasTemplatePixelShape(template))
        {
            return new FrontBackDebugResult(
                0.0,
                0.0,
                0.0,
                false,
                FrontBackDebugDecision.Unavailable,
                "-",
                "-",
                "Template has no saved pixel shape; front/back debug score is unavailable.");
        }

        using var workingImage = PrepareImage(image, activeParameters);
        var detection = DetectPartPrepared(
            workingImage,
            activeParameters,
            template,
            out _,
            out _);
        if (detection is null)
        {
            return new FrontBackDebugResult(
                0.0,
                0.0,
                0.0,
                false,
                FrontBackDebugDecision.Unavailable,
                "-",
                "-",
                "No valid workpiece contour was found; front/back debug score is unavailable.");
        }

        var resolvedAngle = ResolveInspectionAngle(workingImage, detection, template, activeParameters);
        var contourMirror = AnalyzeContourMirrorFaceDebug(detection, template, activeParameters);
        var debug = _templateMatcher.CheckFrontBackDebug(
            workingImage,
            detection,
            template,
            activeParameters,
            resolvedAngle.AngleDegrees,
            contourMirror?.BackAngleOffsetDegrees,
            fixedOverlayDiagnosticPath);
        if (debug is null)
        {
            return null;
        }

        return debug with
        {
            ContourMirror = contourMirror is null
                ? null
                : ToContourMirrorFaceDebugResult(contourMirror)
        };
    }

    public Mat PrepareImage(Mat image, VisionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!parameters.LensDistortionCalibration.CanApplyTo(image.Width, image.Height))
        {
            return image.Clone();
        }

        return Undistort(image, parameters.LensDistortionCalibration);
    }

    private static PartDetection? DetectPartPrepared(
        Mat image,
        VisionParameters parameters,
        PartTemplate? template,
        out IReadOnlyList<ContourCandidateDiagnostic> candidateDiagnostics,
        out IReadOnlyList<PartDetection> candidateDetections,
        bool buildDiagnostics = true)
    {
        using var roiImage = ExtractRoi(image, parameters.Roi, out var offset);
        using var gray = ToGray(roiImage);
        using var blurred = Blur(gray, parameters.BlurKernelSize);
        using var binary = Threshold(blurred, parameters.BinaryThreshold);
        using var inverted = new Mat();
        Cv2.BitwiseNot(binary, inverted);

        var candidates = FindDetectionCandidates(binary, "binary", offset, parameters)
            .Concat(FindDetectionCandidates(inverted, "inverted", offset, parameters))
            .ToList();
        var selected = SelectDetectionCandidate(candidates, template);

        candidateDiagnostics = buildDiagnostics
            ? BuildCandidateDiagnostics(candidates, template, selected)
            : Array.Empty<ContourCandidateDiagnostic>();
        candidateDetections = buildDiagnostics
            ? candidates.Select(candidate => candidate.Detection).ToArray()
            : Array.Empty<PartDetection>();
        return selected?.Detection;
    }

    private Mat Undistort(Mat image, LensDistortionCalibration calibration)
    {
        var map = GetUndistortMap(calibration, image.Width, image.Height);
        var corrected = new Mat();
        Cv2.Remap(image, corrected, map.MapX, map.MapY, InterpolationFlags.Linear);
        return corrected;
    }

    private UndistortMap GetUndistortMap(
        LensDistortionCalibration calibration,
        int imageWidth,
        int imageHeight)
    {
        lock (_undistortMapSync)
        {
            if (_cachedUndistortMap is { } cached &&
                cached.ImageWidth == imageWidth &&
                cached.ImageHeight == imageHeight &&
                string.Equals(cached.CalibrationId, calibration.CalibrationId, StringComparison.Ordinal))
            {
                return cached;
            }

            _cachedUndistortMap?.Dispose();
            _cachedUndistortMap = BuildUndistortMap(calibration, imageWidth, imageHeight);
            return _cachedUndistortMap;
        }
    }

    private static UndistortMap BuildUndistortMap(
        LensDistortionCalibration calibration,
        int imageWidth,
        int imageHeight)
    {
        using var cameraMatrix = Mat.FromArray(new[,]
        {
            { calibration.CameraMatrix[0], calibration.CameraMatrix[1], calibration.CameraMatrix[2] },
            { calibration.CameraMatrix[3], calibration.CameraMatrix[4], calibration.CameraMatrix[5] },
            { calibration.CameraMatrix[6], calibration.CameraMatrix[7], calibration.CameraMatrix[8] }
        });
        using var distortion = Mat.FromArray(calibration.DistortionCoefficients);
        var mapX = new Mat();
        var mapY = new Mat();
        Cv2.InitUndistortRectifyMap(
            cameraMatrix,
            distortion,
            new Mat(),
            cameraMatrix,
            new Size(imageWidth, imageHeight),
            MatType.CV_32FC1,
            mapX,
            mapY);
        return new UndistortMap(calibration.CalibrationId, imageWidth, imageHeight, mapX, mapY);
    }

    private static InspectionDecision DecideSingleShot(
        double angleOffset,
        double rotationCompensationDegrees,
        double angleToleranceDegrees,
        double shapeScoreThreshold,
        AngleResolutionDiagnostic angleDiagnostic,
        TemplateSimilarityResult? similarity,
        double matchScore,
        out NgReason reason,
        out string message)
    {
        if (!angleDiagnostic.IsReliable)
        {
            reason = NgReason.MatchFailed;
            message = $"Angle NG: {angleDiagnostic.Message}";
            return InspectionDecision.Ng;
        }

        if (similarity is { IsReliable: true, IsSamePart: false } ||
            similarity is null && matchScore < shapeScoreThreshold)
        {
            reason = NgReason.ShapeOutOfTolerance;
            message = similarity is null
                ? $"Shape NG: score={matchScore:F3} below {shapeScoreThreshold:F3}."
                : $"Shape NG: {similarity.Message}.";
            return InspectionDecision.Ng;
        }

        var rOutOfTolerance = !AngleMath.IsAngleWithinTolerance(angleOffset, angleToleranceDegrees);
        if (rOutOfTolerance)
        {
            reason = NgReason.None;
            message = $"OK, X/Y/R output valid: R={rotationCompensationDegrees:F3}deg";
            return InspectionDecision.Ok;
        }

        reason = NgReason.None;
        message = "OK, X/Y/R output valid";
        return InspectionDecision.Ok;
    }

    private ContourMirrorFaceDebugResult? TryApplyBackSideNg(
        PartDetection detection,
        PartTemplate template,
        VisionParameters parameters,
        bool buildDiagnostics,
        ref InspectionDecision decision,
        ref NgReason reason,
        ref string message)
    {
        if (!parameters.BackSideNgEnabled || decision != InspectionDecision.Ok)
        {
            return null;
        }

        var computation = AnalyzeContourMirrorFaceDebug(detection, template, parameters);
        if (computation is null)
        {
            return null;
        }

        if (IsBackSideNg(computation, parameters))
        {
            decision = InspectionDecision.Ng;
            reason = NgReason.BackSideDetected;
            message =
                $"BackSide NG: ContourMirror diff={computation.ScoreDifference:F3} < 0, " +
                $"front={computation.FrontScore:F3}, back={computation.BackScore:F3}.";
        }

        return buildDiagnostics
            ? ToContourMirrorFaceDebugResult(computation)
            : null;
    }

    private static bool IsBackSideNg(
        ContourMirrorFaceDebugComputation computation,
        VisionParameters parameters)
    {
        _ = parameters;
        return computation.ScoreDifference < 0.0;
    }

    private static ContourMirrorFaceDebugResult ToContourMirrorFaceDebugResult(
        ContourMirrorFaceDebugComputation computation)
    {
        return new ContourMirrorFaceDebugResult(
            computation.FrontScore,
            computation.BackScore,
            computation.ScoreDifference,
            computation.IsReliable,
            computation.SuggestedDecision,
            computation.FrontAngleOffsetDegrees,
            computation.BackAngleOffsetDegrees,
            computation.FrontAlternativeScore,
            computation.BackAlternativeScore,
            computation.CurrentSignal,
            computation.TemplateSignal,
            computation.SearchRangeDegrees,
            computation.Message);
    }

    private static IEnumerable<CandidateDetection> FindDetectionCandidates(
        Mat binary,
        string source,
        Point offset,
        VisionParameters parameters)
    {
        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0)
        {
            return [];
        }

        var imageArea = binary.Width * binary.Height;
        return contours
            .Select(contour => new { Contour = contour, Area = Cv2.ContourArea(contour) })
            .Where(x => x.Area >= parameters.MinPartAreaPixels)
            .Where(x => x.Area <= parameters.MaxPartAreaPixels)
            .Where(x => x.Area <= imageArea * 0.95)
            .Select(x => new CandidateDetection(source, CreateDetection(x.Contour, x.Area, offset, parameters)));
    }

    private static CandidateDetection? SelectDetectionCandidate(
        IReadOnlyCollection<CandidateDetection> candidates,
        PartTemplate? template)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (template is null)
        {
            return candidates
                .OrderByDescending(candidate => candidate.Detection.AreaPixels)
                .First();
        }

        return candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = CalculateMatchScore(candidate.Detection, template),
                CenterDistancePixels = DistanceToTemplateCenterPixels(candidate.Detection, template)
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.CenterDistancePixels)
            .ThenByDescending(candidate => candidate.Candidate.Detection.AreaPixels)
            .Select(candidate => candidate.Candidate)
            .First();
    }

    private static IReadOnlyList<ContourCandidateDiagnostic> BuildCandidateDiagnostics(
        IReadOnlyList<CandidateDetection> candidates,
        PartTemplate? template,
        CandidateDetection? selected)
    {
        var diagnostics = candidates
            .Select((candidate, index) =>
            {
                var detection = candidate.Detection;
                return new ContourCandidateDiagnostic(
                    Rank: 0,
                    CandidateIndex: index + 1,
                    Source: candidate.Source,
                    IsSelected: ReferenceEquals(candidate, selected),
                    Score: template is null ? 0.0 : CalculateMatchScore(detection, template),
                    CenterXPixel: detection.CenterXPixel,
                    CenterYPixel: detection.CenterYPixel,
                    WidthPixels: detection.WidthPixels,
                    HeightPixels: detection.HeightPixels,
                    WidthMm: detection.WidthMm,
                    HeightMm: detection.HeightMm,
                    AreaPixels: detection.AreaPixels,
                    FillRatio: CalculateFillRatio(detection.AreaPixels, detection.WidthPixels, detection.HeightPixels),
                    CenterDistancePixels: template is null ? 0.0 : DistanceToTemplateCenterPixels(detection, template));
            })
            .ToArray();

        var ordered = template is null
            ? diagnostics.OrderByDescending(candidate => candidate.AreaPixels)
            : diagnostics
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.CenterDistancePixels)
                .ThenByDescending(candidate => candidate.AreaPixels);

        return ordered
            .Select((candidate, rank) => candidate with { Rank = rank + 1 })
            .ToArray();
    }

    private static PartDetection CreateDetection(
        Point[] contour,
        double area,
        Point offset,
        VisionParameters parameters)
    {
        var shape = MeasurePartShape(contour);
        var widthPixels = Math.Max(shape.Size.Width, shape.Size.Height);
        var heightPixels = Math.Min(shape.Size.Width, shape.Size.Height);
        var sizeMm = CalculateSizeMm(shape, offset, parameters, widthPixels, heightPixels);
        var centerXPixel = shape.Center.X + offset.X;
        var centerYPixel = shape.Center.Y + offset.Y;

        return new PartDetection(
            contour,
            offset,
            centerXPixel,
            centerYPixel,
            NormalizeMajorAxisAngle(shape),
            widthPixels,
            heightPixels,
            sizeMm.WidthMm,
            sizeMm.HeightMm,
            area);
    }

    private static Mat ExtractRoi(Mat image, ImageRoi roi, out Point offset)
    {
        if (roi.IsEmpty)
        {
            offset = new Point(0, 0);
            return image.Clone();
        }

        var rect = new Rect(
            Math.Clamp(roi.X, 0, image.Width - 1),
            Math.Clamp(roi.Y, 0, image.Height - 1),
            Math.Clamp(roi.Width, 1, image.Width - roi.X),
            Math.Clamp(roi.Height, 1, image.Height - roi.Y));

        offset = new Point(rect.X, rect.Y);
        return new Mat(image, rect).Clone();
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

    private static Mat Blur(Mat gray, int kernelSize)
    {
        var normalizedKernel = kernelSize < 3 ? 3 : kernelSize;
        if (normalizedKernel % 2 == 0)
        {
            normalizedKernel++;
        }

        var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(normalizedKernel, normalizedKernel), 0);
        return blurred;
    }

    private static Mat Threshold(Mat gray, int binaryThreshold)
    {
        var binary = new Mat();
        if (binaryThreshold <= 0)
        {
            Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        }
        else
        {
            Cv2.Threshold(gray, binary, binaryThreshold, 255, ThresholdTypes.Binary);
        }

        Cv2.MorphologyEx(
            binary,
            binary,
            MorphTypes.Close,
            Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));
        return binary;
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

    private static RotatedRect MeasurePartShape(Point[] contour)
    {
        return Cv2.MinAreaRect(contour);
    }

    private static (double WidthMm, double HeightMm) CalculateSizeMm(
        RotatedRect shape,
        Point offset,
        VisionParameters parameters,
        double widthPixels,
        double heightPixels)
    {
        if (!parameters.CameraCalibration.Enabled)
        {
            return (widthPixels, heightPixels);
        }

        var points = shape
            .Points()
            .Select(point => parameters.CameraCalibration.PixelToMachine(point.X + offset.X, point.Y + offset.Y))
            .ToArray();

        var sideA = (Distance(points[0], points[1]) + Distance(points[2], points[3])) / 2.0;
        var sideB = (Distance(points[1], points[2]) + Distance(points[3], points[0])) / 2.0;
        return (Math.Max(sideA, sideB), Math.Min(sideA, sideB));
    }

    private static double Distance(MachinePoint a, MachinePoint b)
    {
        var dx = a.XMm - b.XMm;
        var dy = a.YMm - b.YMm;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double NormalizeMajorAxisAngle(RotatedRect rect)
    {
        var angle = (double)rect.Angle;
        if (rect.Size.Width < rect.Size.Height)
        {
            angle += 90.0;
        }

        return AngleMath.NormalizeDegrees180(angle);
    }

    private static ContourAngleProfile CalculateContourAngleProfile(PartDetection detection)
    {
        if (detection.Contour.Length < 3)
        {
            return new ContourAngleProfile(
                detection.AngleDegrees,
                PcaRatio: 1.0,
                Circularity: 0.0,
                IsPcaReliable: false);
        }

        var meanX = detection.Contour.Average(point => (double)point.X);
        var meanY = detection.Contour.Average(point => (double)point.Y);
        var covarianceXx = 0.0;
        var covarianceXy = 0.0;
        var covarianceYy = 0.0;
        foreach (var point in detection.Contour)
        {
            var dx = point.X - meanX;
            var dy = point.Y - meanY;
            covarianceXx += dx * dx;
            covarianceXy += dx * dy;
            covarianceYy += dy * dy;
        }

        covarianceXx /= detection.Contour.Length;
        covarianceXy /= detection.Contour.Length;
        covarianceYy /= detection.Contour.Length;

        var trace = covarianceXx + covarianceYy;
        var delta = Math.Sqrt(
            Math.Max(
                ((covarianceXx - covarianceYy) * (covarianceXx - covarianceYy)) +
                (4.0 * covarianceXy * covarianceXy),
                0.0));
        var major = (trace + delta) / 2.0;
        var minor = Math.Max((trace - delta) / 2.0, 0.000001);
        var pcaRatio = major / minor;
        var angle = Math.Atan2(2.0 * covarianceXy, covarianceXx - covarianceYy) * 90.0 / Math.PI;
        var perimeter = Cv2.ArcLength(detection.Contour, closed: true);
        var circularity = perimeter > 0.0001
            ? Math.Clamp(4.0 * Math.PI * detection.AreaPixels / (perimeter * perimeter), 0.0, 1.0)
            : 0.0;

        return new ContourAngleProfile(
            AngleMath.NormalizeDegrees180(angle),
            pcaRatio,
            circularity,
            pcaRatio >= AutoPcaReliableRatio);
    }

    private static double CalculatePcaAngleScore(double pcaRatio)
    {
        return Math.Clamp((pcaRatio - 1.0) / AutoPcaConfidenceScale, 0.0, 1.0);
    }

    private static string FormatContourAngleProfile(ContourAngleProfile profile)
    {
        return $"pcaRatio={profile.PcaRatio:F3}; circularity={profile.Circularity:F3}";
    }

    private static double ResolveTemplateReferenceAngle(PartDetection detection, VisionParameters parameters)
    {
        if (parameters.AngleDetectionMode != AngleDetectionMode.AutoPcaOrPolarRing)
        {
            return detection.AngleDegrees;
        }

        var profile = CalculateContourAngleProfile(detection);
        return profile.IsPcaReliable
            ? profile.PcaAngleDegrees
            : detection.AngleDegrees;
    }

    private static double CalculateMatchScore(PartDetection detection, PartTemplate template)
    {
        var widthRatio = Math.Abs(detection.WidthMm - template.WidthMm) / Math.Max(template.WidthMm, 0.0001);
        var heightRatio = Math.Abs(detection.HeightMm - template.HeightMm) / Math.Max(template.HeightMm, 0.0001);
        var areaRatio = Math.Abs(detection.AreaPixels - template.AreaPixels) / Math.Max(template.AreaPixels, 0.0001);
        if (!HasTemplatePixelShape(template))
        {
            var legacyPenalty = (widthRatio * 0.4) + (heightRatio * 0.4) + (areaRatio * 0.2);
            return Math.Clamp(1.0 - legacyPenalty, 0.0, 1.0);
        }

        var detectionFillRatio = CalculateFillRatio(
            detection.AreaPixels,
            detection.WidthPixels,
            detection.HeightPixels);
        var templateFillRatio = CalculateFillRatio(
            template.AreaPixels,
            template.ReferenceWidthPixels,
            template.ReferenceHeightPixels);
        var fillRatioDiff = Math.Abs(detectionFillRatio - templateFillRatio) / Math.Max(templateFillRatio, 0.0001);
        var penalty =
            (widthRatio * 0.3) +
            (heightRatio * 0.3) +
            (areaRatio * 0.2) +
            (fillRatioDiff * 0.2);
        return Math.Clamp(1.0 - penalty, 0.0, 1.0);
    }

    private TemplateSimilarityResult? CalculateTemplateSimilarity(
        Mat preparedImage,
        PartDetection detection,
        PartTemplate template,
        VisionParameters parameters,
        double resolvedAngleDegrees)
    {
        if (!HasTemplatePixelShape(template))
        {
            return null;
        }

        return _templateMatcher.TryCompare(
            preparedImage,
            detection,
            template,
            parameters,
            resolvedAngleDegrees);
    }

    private ResolvedAngle ResolveInspectionAngle(
        Mat preparedImage,
        PartDetection detection,
        PartTemplate template,
        VisionParameters parameters,
        bool buildDiagnostics = true)
    {
        if (parameters.AngleDetectionMode == AngleDetectionMode.AutoPcaOrPolarRing)
        {
            return ResolveAutoPcaOrPolarRingAngle(preparedImage, detection, template, parameters, buildDiagnostics);
        }

        if (parameters.AngleDetectionMode == AngleDetectionMode.OuterContour)
        {
            return new ResolvedAngle(
                detection.AngleDegrees,
                AllowsFullRotation: false,
                "outer-contour",
                Score: 1.0,
                AlternativeScore: 0.0,
                SearchRangeDegrees: 180.0,
                Candidates: null);
        }

        if (parameters.AngleDetectionMode == AngleDetectionMode.AutoFeatureRotation)
        {
            if (string.IsNullOrWhiteSpace(template.ImagePath))
            {
                return new ResolvedAngle(
                    detection.AngleDegrees,
                    AllowsFullRotation: false,
                    "outer-contour-no-template-image",
                    Score: 0.0,
                    AlternativeScore: 0.0,
                    SearchRangeDegrees: 180.0,
                    Candidates: null);
            }

            var autoModel = TryGetAutoFeatureAngleModel(template, parameters);
            if (autoModel is { Features.Count: > 0 })
            {
                return MatchAutoFeatureRotation(preparedImage, detection, autoModel, template.ReferenceAngleDegrees, parameters, buildDiagnostics);
            }

            return new ResolvedAngle(
                detection.AngleDegrees,
                AllowsFullRotation: true,
                "auto-feature-rotation-no-feature",
                Score: 0.0,
                AlternativeScore: 0.0,
                SearchRangeDegrees: Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0),
                Candidates: []);
        }

        if (parameters.AngleDetectionMode == AngleDetectionMode.PolarRingRotation)
        {
            if (string.IsNullOrWhiteSpace(template.ImagePath))
            {
                return new ResolvedAngle(
                    detection.AngleDegrees,
                    AllowsFullRotation: false,
                    "outer-contour-no-template-image",
                    Score: 0.0,
                    AlternativeScore: 0.0,
                    SearchRangeDegrees: 180.0,
                    Candidates: null);
            }

            var polarModel = TryGetPolarRingAngleModel(template, parameters);
            if (polarModel is not null)
            {
                return MatchPolarRingRotation(preparedImage, detection, polarModel, template.ReferenceAngleDegrees, parameters, buildDiagnostics);
            }

            return new ResolvedAngle(
                detection.AngleDegrees,
                AllowsFullRotation: true,
                "polar-ring-rotation-no-signal",
                Score: 0.0,
                AlternativeScore: 0.0,
                SearchRangeDegrees: Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0),
                Candidates: []);
        }

        var model = TryGetTemplateAngleModel(template, parameters);
        if (model is null)
        {
            return new ResolvedAngle(
                detection.AngleDegrees,
                AllowsFullRotation: false,
                parameters.AngleDetectionMode == AngleDetectionMode.AutoFeatureRotation
                    ? "outer-contour-no-auto-feature"
                    : "outer-contour-no-template-image",
                Score: 0.0,
                AlternativeScore: 0.0,
                SearchRangeDegrees: 180.0,
                Candidates: null);
        }

        return MatchTemplateRotation(preparedImage, detection, model, template.ReferenceAngleDegrees, parameters, buildDiagnostics);
    }

    private ResolvedAngle ResolveAutoPcaOrPolarRingAngle(
        Mat preparedImage,
        PartDetection detection,
        PartTemplate template,
        VisionParameters parameters,
        bool buildDiagnostics)
    {
        var profile = CalculateContourAngleProfile(detection);
        var profileDetail = FormatContourAngleProfile(profile);
        if (profile.IsPcaReliable)
        {
            var templateContourModel = TryGetContourPolarAngleModel(template, parameters);
            if (templateContourModel is { Profile.IsPcaReliable: false })
            {
                return MatchContourPolarRotation(
                    detection,
                    templateContourModel,
                    template.ReferenceAngleDegrees,
                    parameters,
                    profileDetail,
                    buildDiagnostics);
            }

            return new ResolvedAngle(
                profile.PcaAngleDegrees,
                AllowsFullRotation: false,
                "auto-pca-contour",
                Score: CalculatePcaAngleScore(profile.PcaRatio),
                AlternativeScore: 0.0,
                SearchRangeDegrees: 180.0,
                Candidates: null,
                Detail: profileDetail);
        }

        var contourModel = TryGetContourPolarAngleModel(template, parameters);
        if (contourModel is not null)
        {
            return MatchContourPolarRotation(detection, contourModel, template.ReferenceAngleDegrees, parameters, profileDetail, buildDiagnostics);
        }

        var polarModel = TryGetPolarRingAngleModel(template, parameters);
        if (polarModel is not null)
        {
            var polarResult = MatchPolarRingRotation(preparedImage, detection, polarModel, template.ReferenceAngleDegrees, parameters, buildDiagnostics);
            return polarResult with
            {
                Source = polarResult.Source == "polar-ring-rotation"
                    ? "auto-pca-polar-ring"
                    : polarResult.Source,
                Detail = profileDetail
            };
        }

        return new ResolvedAngle(
            detection.AngleDegrees,
            AllowsFullRotation: true,
            string.IsNullOrWhiteSpace(template.ImagePath)
                ? "auto-pca-polar-ring-no-template-image"
                : "auto-pca-polar-ring-no-signal",
            Score: 0.0,
            AlternativeScore: 0.0,
            SearchRangeDegrees: Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0),
            Candidates: [],
            Detail: profileDetail);
    }

    private ContourMirrorFaceDebugComputation? AnalyzeContourMirrorFaceDebug(
        PartDetection detection,
        PartTemplate template,
        VisionParameters parameters)
    {
        var model = TryGetContourPolarAngleModel(template, parameters);
        if (model is null)
        {
            return null;
        }

        var currentSignature = BuildContourPolarSignature(detection);
        var currentSignal = CalculateSignatureSignal(currentSignature);
        if (currentSignal < ContourPolarMinimumRadiusSignal)
        {
            return new ContourMirrorFaceDebugComputation(
                0.0,
                0.0,
                0.0,
                false,
                FrontBackDebugDecision.Unavailable,
                0.0,
                0.0,
                0.0,
                0.0,
                currentSignal,
                model.Signal,
                Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0),
                $"Contour mirror debug unavailable: current contour signal={currentSignal:F3}.");
        }

        var searchRange = Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0);
        var front = SearchPolarRingAngle(
            currentSignature,
            model.Signature,
            template.ReferenceAngleDegrees,
            searchRange);
        var mirrored = MirrorCircularSignature(currentSignature);
        var back = SearchPolarRingAngle(
            mirrored,
            model.Signature,
            template.ReferenceAngleDegrees,
            searchRange);

        var difference = front.Score - back.Score;
        const double minimumScore = 0.58;
        const double reliableMargin = 0.06;
        var isReliable = Math.Max(front.Score, back.Score) >= minimumScore &&
            Math.Abs(difference) >= reliableMargin;
        var suggestedDecision = isReliable
            ? difference > 0.0 ? FrontBackDebugDecision.Front : FrontBackDebugDecision.Back
            : FrontBackDebugDecision.Uncertain;
        var message =
            $"Contour mirror front/back debug: front={front.Score:F3} at {front.AngleOffsetDegrees:F3}deg, " +
            $"back={back.Score:F3} at {back.AngleOffsetDegrees:F3}deg, diff={difference:F3}, " +
            $"reliable={isReliable}, threshold score>={minimumScore:F3}, margin>={reliableMargin:F3}. " +
            "When BackSideNgEnabled is true, diff<0 is backside NG.";

        return new ContourMirrorFaceDebugComputation(
            front.Score,
            back.Score,
            difference,
            isReliable,
            suggestedDecision,
            front.AngleOffsetDegrees,
            back.AngleOffsetDegrees,
            front.SecondBestScore,
            back.SecondBestScore,
            currentSignal,
            model.Signal,
            searchRange,
            message);
    }

    private AngleResolutionDiagnostic BuildAngleDiagnostic(
        VisionParameters parameters,
        double contourAngleDegrees,
        ResolvedAngle resolvedAngle)
    {
        var usesTemplateRotation = resolvedAngle.Source.StartsWith("template-rotation", StringComparison.Ordinal) ||
            resolvedAngle.Source.StartsWith("auto-feature-rotation", StringComparison.Ordinal) ||
            resolvedAngle.Source.StartsWith("polar-ring-rotation", StringComparison.Ordinal) ||
            resolvedAngle.Source.StartsWith("auto-pca-polar-ring", StringComparison.Ordinal) ||
            resolvedAngle.Source.StartsWith("auto-pca-contour-polar", StringComparison.Ordinal);
        var isAutoFeatureRotation = resolvedAngle.Source.StartsWith("auto-feature-rotation", StringComparison.Ordinal);
        var hasSeparatedAutoFeatureMatch =
            isAutoFeatureRotation &&
            resolvedAngle.Score >= GetSeparatedAutoFeatureMinimumScore(parameters) &&
            resolvedAngle.Score - resolvedAngle.AlternativeScore >= GetSeparatedAutoFeatureMinimumMargin(parameters);
        var hasMinimumScore = resolvedAngle.Score >= parameters.TemplateAngleMinimumScore || hasSeparatedAutoFeatureMatch;
        var hasEnoughMargin = resolvedAngle.Score - resolvedAngle.AlternativeScore >= parameters.TemplateAngleMinimumScoreMargin;
        var isContourPolarRotation = resolvedAngle.Source.StartsWith("auto-pca-contour-polar", StringComparison.Ordinal);
        var allowHighScoreContourPolar = isContourPolarRotation &&
            resolvedAngle.Score >= Math.Max(parameters.TemplateAngleMinimumScore, 0.70);
        var isReliable = !usesTemplateRotation || (hasMinimumScore && (hasEnoughMargin || allowHighScoreContourPolar));

        var message = resolvedAngle.Source switch
        {
            "outer-contour" => "Using outer contour angle.",
            "outer-contour-no-template-image" => "Template image is unavailable; using outer contour angle.",
            "outer-contour-no-auto-feature" => "No reliable auto angle feature was found; using outer contour angle.",
            "auto-pca-contour" => $"Using PCA contour angle: {resolvedAngle.Detail}.",
            "auto-pca-polar-ring-no-template-image" => $"Template image is unavailable; auto PCA was not reliable: {resolvedAngle.Detail}.",
            "auto-pca-polar-ring-no-signal" => $"No reliable contour/polar angle signal was found after auto PCA check: {resolvedAngle.Detail}.",
            "auto-feature-rotation-no-feature" => "No reliable auto angle feature was found in the template image.",
            "auto-feature-rotation-no-match" => "Auto angle feature could not be matched in the current image.",
            "polar-ring-rotation-no-signal" => "No reliable polar ring angle signal was found.",
            "auto-pca-contour-polar" when !hasEnoughMargin && allowHighScoreContourPolar =>
                $"Using high-score contour polar angle match with close alternatives: score={resolvedAngle.Score:F3}, alternative={resolvedAngle.AlternativeScore:F3}, margin={resolvedAngle.Score - resolvedAngle.AlternativeScore:F3}. {resolvedAngle.Detail}.",
            "auto-pca-contour-polar" => $"Using contour polar angle match: {resolvedAngle.Detail}.",
            "auto-pca-polar-ring" => $"Using image polar ring angle match: {resolvedAngle.Detail}.",
            _ when hasSeparatedAutoFeatureMatch && resolvedAngle.Score < parameters.TemplateAngleMinimumScore =>
                $"auto-feature angle is separated: score={resolvedAngle.Score:F3}, alternative={resolvedAngle.AlternativeScore:F3}, margin={resolvedAngle.Score - resolvedAngle.AlternativeScore:F3}.",
            _ when !hasMinimumScore =>
                $"rotation score {resolvedAngle.Score:F3} is below {parameters.TemplateAngleMinimumScore:F3}.",
            _ when !hasEnoughMargin =>
                $"rotation is ambiguous: score={resolvedAngle.Score:F3}, alternative={resolvedAngle.AlternativeScore:F3}, margin={resolvedAngle.Score - resolvedAngle.AlternativeScore:F3}.",
            _ => $"rotation angle score={resolvedAngle.Score:F3}, alternative={resolvedAngle.AlternativeScore:F3}."
        };

        return new AngleResolutionDiagnostic(
            parameters.AngleDetectionMode,
            contourAngleDegrees,
            resolvedAngle.AngleDegrees,
            resolvedAngle.AllowsFullRotation,
            isReliable,
            resolvedAngle.Source,
            resolvedAngle.Score,
            resolvedAngle.AlternativeScore,
            resolvedAngle.Score - resolvedAngle.AlternativeScore,
            message,
            resolvedAngle.Candidates);
    }

    private static double GetSeparatedAutoFeatureMinimumScore(VisionParameters parameters)
    {
        return Math.Max(0.25, parameters.TemplateAngleMinimumScore - AutoFeatureSeparatedScoreSlack);
    }

    private static double GetSeparatedAutoFeatureMinimumMargin(VisionParameters parameters)
    {
        return Math.Max(AutoFeatureSeparatedMarginMinimum, parameters.TemplateAngleMinimumScoreMargin * 2.0);
    }

    private TemplateAngleModel? TryGetTemplateAngleModel(PartTemplate template, VisionParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
        {
            return null;
        }

        var currentDistortionId = GetCurrentDistortionCalibrationId(parameters);
        lock (_templateAngleModelSync)
        {
            if (_cachedTemplateAngleModel is { } cached &&
                cached.TemplateId == template.Id &&
                string.Equals(cached.ImagePath, template.ImagePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cached.SourceDistortionCalibrationId, currentDistortionId, StringComparison.Ordinal) &&
                cached.Roi == parameters.Roi)
            {
                return cached;
            }

            _cachedTemplateAngleModel?.Patch.Dispose();
            _cachedTemplateAngleModel = null;
            _cachedPolarRingAngleModel = null;
            _cachedContourPolarAngleModel = null;

            using var templateImage = Cv2.ImRead(template.ImagePath, ImreadModes.Color);
            if (templateImage.Empty())
            {
                return null;
            }

            using var preparedTemplate = PrepareImage(templateImage, parameters);
            var model = BuildTemplateAngleModel(preparedTemplate, template, parameters, currentDistortionId);
            _cachedTemplateAngleModel = model;
            return model;
        }
    }

    private AutoFeatureAngleModel? TryGetAutoFeatureAngleModel(PartTemplate template, VisionParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
        {
            return null;
        }

        var currentDistortionId = GetCurrentDistortionCalibrationId(parameters);
        lock (_templateAngleModelSync)
        {
            if (_cachedAutoFeatureAngleModel is { } cached &&
                cached.TemplateId == template.Id &&
                string.Equals(cached.ImagePath, template.ImagePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cached.SourceDistortionCalibrationId, currentDistortionId, StringComparison.Ordinal) &&
                cached.Roi == parameters.Roi)
            {
                return cached;
            }

            foreach (var feature in _cachedAutoFeatureAngleModel?.Features ?? [])
            {
                feature.Patch.Dispose();
            }

            _cachedAutoFeatureAngleModel = null;

            using var templateImage = Cv2.ImRead(template.ImagePath, ImreadModes.Color);
            if (templateImage.Empty())
            {
                return null;
            }

            using var preparedTemplate = PrepareImage(templateImage, parameters);
            var features = ExtractAutoFeatureCandidates(preparedTemplate, template);
            var model = new AutoFeatureAngleModel(
                template.Id,
                template.ImagePath,
                currentDistortionId,
                parameters.Roi,
                features);
            _cachedAutoFeatureAngleModel = model;
            return model;
        }
    }

    private ContourPolarAngleModel? TryGetContourPolarAngleModel(PartTemplate template, VisionParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
        {
            return null;
        }

        var currentDistortionId = GetCurrentDistortionCalibrationId(parameters);
        lock (_templateAngleModelSync)
        {
            if (_cachedContourPolarAngleModel is { } cached &&
                cached.TemplateId == template.Id &&
                string.Equals(cached.ImagePath, template.ImagePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cached.SourceDistortionCalibrationId, currentDistortionId, StringComparison.Ordinal) &&
                cached.Roi == parameters.Roi)
            {
                return cached;
            }

            _cachedContourPolarAngleModel = null;

            using var templateImage = Cv2.ImRead(template.ImagePath, ImreadModes.Color);
            if (templateImage.Empty())
            {
                return null;
            }

            using var preparedTemplate = PrepareImage(templateImage, parameters);
            var templateDetection = DetectPartPrepared(
                preparedTemplate,
                parameters,
                template,
                out _,
                out _);
            if (templateDetection is null)
            {
                return null;
            }

            var model = BuildContourPolarAngleModel(template, parameters, currentDistortionId, templateDetection);
            _cachedContourPolarAngleModel = model;
            return model;
        }
    }

    private PolarRingAngleModel? TryGetPolarRingAngleModel(PartTemplate template, VisionParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
        {
            return null;
        }

        var currentDistortionId = GetCurrentDistortionCalibrationId(parameters);
        lock (_templateAngleModelSync)
        {
            if (_cachedPolarRingAngleModel is { } cached &&
                cached.TemplateId == template.Id &&
                string.Equals(cached.ImagePath, template.ImagePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cached.SourceDistortionCalibrationId, currentDistortionId, StringComparison.Ordinal) &&
                cached.Roi == parameters.Roi)
            {
                return cached;
            }

            _cachedPolarRingAngleModel = null;

            using var templateImage = Cv2.ImRead(template.ImagePath, ImreadModes.Color);
            if (templateImage.Empty())
            {
                return null;
            }

            using var preparedTemplate = PrepareImage(templateImage, parameters);
            var model = BuildPolarRingAngleModel(preparedTemplate, template, parameters, currentDistortionId);
            _cachedPolarRingAngleModel = model;
            return model;
        }
    }

    private static ContourPolarAngleModel? BuildContourPolarAngleModel(
        PartTemplate template,
        VisionParameters parameters,
        string currentDistortionId,
        PartDetection templateDetection)
    {
        var signature = BuildContourPolarSignature(templateDetection);
        var signal = CalculateSignatureSignal(signature);
        if (signal < ContourPolarMinimumRadiusSignal)
        {
            return null;
        }

        return new ContourPolarAngleModel(
            template.Id,
            template.ImagePath,
            currentDistortionId,
            parameters.Roi,
            signature,
            signal,
            CalculateContourAngleProfile(templateDetection));
    }

    private static TemplateAngleModel BuildTemplateAngleModel(
        Mat preparedTemplate,
        PartTemplate template,
        VisionParameters parameters,
        string currentDistortionId)
    {
        var patch = ExtractTemplateAnglePatch(
            preparedTemplate,
            template.ReferenceCenterXPixel,
            template.ReferenceCenterYPixel,
            template.ReferenceWidthPixels,
            template.ReferenceHeightPixels,
            out var searchSize);

        return new TemplateAngleModel(
            template.Id,
            template.ImagePath,
            currentDistortionId,
            parameters.Roi,
            patch,
            searchSize,
            template.ReferenceCenterXPixel,
            template.ReferenceCenterYPixel);
    }

    private static PolarRingAngleModel? BuildPolarRingAngleModel(
        Mat preparedTemplate,
        PartTemplate template,
        VisionParameters parameters,
        string currentDistortionId)
    {
        var radius = CalculatePolarRingRadius(template.ReferenceWidthPixels, template.ReferenceHeightPixels);
        using var gray = ToGray(preparedTemplate);
        var signature = BuildPolarRingSignature(gray, template.ReferenceCenterXPixel, template.ReferenceCenterYPixel, radius);
        var signal = CalculateSignatureSignal(signature);
        if (signal < PolarRingMinimumSignal)
        {
            return null;
        }

        return new PolarRingAngleModel(
            template.Id,
            template.ImagePath,
            currentDistortionId,
            parameters.Roi,
            signature,
            signal,
            radius);
    }

    private static Mat ExtractTemplateAnglePatch(
        Mat image,
        double centerX,
        double centerY,
        double referenceWidthPixels,
        double referenceHeightPixels,
        out Size searchSize)
    {
        var patchSize = CalculateTemplateAnglePatchSize(referenceWidthPixels, referenceHeightPixels);
        var sourceRect = BuildCenteredRect(centerX, centerY, patchSize + TemplateAnglePatchPaddingPixels * 2, image.Width, image.Height);
        var featureSize = patchSize + TemplateAnglePatchPaddingPixels * 2;
        using var sourcePatch = new Mat(image, sourceRect);
        using var gray = ToGray(sourcePatch);
        var normalized = NormalizeTemplateAngleImage(gray);

        if (normalized.Width == featureSize && normalized.Height == featureSize)
        {
            searchSize = new Size(featureSize, featureSize);
            return normalized;
        }

        var resized = new Mat();
        Cv2.Resize(normalized, resized, new Size(featureSize, featureSize), 0, 0, InterpolationFlags.Area);
        normalized.Dispose();
        searchSize = new Size(featureSize, featureSize);
        return resized;
    }

    private static ResolvedAngle MatchTemplateRotation(
        Mat preparedImage,
        PartDetection detection,
        TemplateAngleModel model,
        double referenceAngleDegrees,
        VisionParameters parameters,
        bool buildDiagnostics)
    {
        using var searchPatch = ExtractSearchAnglePatch(
            preparedImage,
            detection.CenterXPixel,
            detection.CenterYPixel,
            model.SearchSize);
        var coarseStep = NormalizeAngleStep(parameters.TemplateAngleCoarseStepDegrees, 5.0);
        var fineStep = NormalizeAngleStep(parameters.TemplateAngleFineStepDegrees, 0.5);
        var searchRange = Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 180.0, 360.0);
        var coarse = SearchTemplateAngle(searchPatch, model.Patch, -searchRange, searchRange, coarseStep);
        var fineStart = coarse.AngleDegrees - TemplateAngleRefineWindowDegrees;
        var fineEnd = coarse.AngleDegrees + TemplateAngleRefineWindowDegrees;
        var fine = SearchTemplateAngle(searchPatch, model.Patch, fineStart, fineEnd, fineStep);
        var candidates = buildDiagnostics
            ? BuildAngleCandidates(referenceAngleDegrees, fine.TopCandidates, coarse.TopCandidates)
            : null;

        return new ResolvedAngle(
            AngleMath.NormalizeDegrees360(referenceAngleDegrees + fine.AngleDegrees),
            AllowsFullRotation: searchRange > 180.0,
            "template-rotation",
            fine.Score,
            Math.Max(coarse.SecondBestScore, fine.SecondBestScore),
            searchRange,
            candidates);
    }

    private static ResolvedAngle MatchPolarRingRotation(
        Mat preparedImage,
        PartDetection detection,
        PolarRingAngleModel model,
        double referenceAngleDegrees,
        VisionParameters parameters,
        bool buildDiagnostics)
    {
        using var gray = ToGray(preparedImage);
        var currentSignature = BuildPolarRingSignature(gray, detection.CenterXPixel, detection.CenterYPixel, model.RadiusPixels);
        var currentSignal = CalculateSignatureSignal(currentSignature);
        if (currentSignal < PolarRingMinimumSignal)
        {
            return new ResolvedAngle(
                detection.AngleDegrees,
                AllowsFullRotation: true,
                "polar-ring-rotation-no-signal",
                Score: 0.0,
                AlternativeScore: 0.0,
                SearchRangeDegrees: Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0),
                Candidates: []);
        }

        var result = SearchPolarRingAngle(
            currentSignature,
            model.Signature,
            referenceAngleDegrees,
            parameters.TemplateAngleSearchRangeDegrees,
            buildDiagnostics);

        return new ResolvedAngle(
            AngleMath.NormalizeDegrees360(referenceAngleDegrees + result.AngleOffsetDegrees),
            AllowsFullRotation: true,
            "polar-ring-rotation",
            result.Score,
            result.SecondBestScore,
            Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0),
            result.Candidates);
    }

    private static ResolvedAngle MatchContourPolarRotation(
        PartDetection detection,
        ContourPolarAngleModel model,
        double referenceAngleDegrees,
        VisionParameters parameters,
        string profileDetail,
        bool buildDiagnostics)
    {
        var currentSignature = BuildContourPolarSignature(detection);
        var currentSignal = CalculateSignatureSignal(currentSignature);
        if (currentSignal < ContourPolarMinimumRadiusSignal)
        {
            return new ResolvedAngle(
                detection.AngleDegrees,
                AllowsFullRotation: true,
                "auto-pca-polar-ring-no-signal",
                Score: 0.0,
                AlternativeScore: 0.0,
                SearchRangeDegrees: Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0),
                Candidates: [],
                Detail: $"{profileDetail}; contourSignal={currentSignal:F3}");
        }

        var result = SearchPolarRingAngle(
            currentSignature,
            model.Signature,
            referenceAngleDegrees,
            parameters.TemplateAngleSearchRangeDegrees,
            buildDiagnostics);

        return new ResolvedAngle(
            AngleMath.NormalizeDegrees360(referenceAngleDegrees + result.AngleOffsetDegrees),
            AllowsFullRotation: true,
            "auto-pca-contour-polar",
            result.Score,
            result.SecondBestScore,
            Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0),
            result.Candidates,
            $"{profileDetail}; contourSignal={currentSignal:F3}; templateSignal={model.Signal:F3}");
    }

    private static ResolvedAngle MatchAutoFeatureRotation(
        Mat preparedImage,
        PartDetection detection,
        AutoFeatureAngleModel model,
        double referenceAngleDegrees,
        VisionParameters parameters,
        bool buildDiagnostics)
    {
        var coarseStep = NormalizeAngleStep(parameters.TemplateAngleCoarseStepDegrees, 5.0);
        var fineStep = NormalizeAngleStep(parameters.TemplateAngleFineStepDegrees, 0.5);
        var searchRange = Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0);
        using var currentGray = ToGray(preparedImage);
        using var currentNormalized = NormalizeFeatureMatchImage(currentGray);

        var votes = new List<AutoFeatureVote>();
        foreach (var feature in model.Features.Take(AutoFeatureMaxCandidates))
        {
            var coarse = SearchAutoFeatureAngle(
                currentNormalized,
                feature,
                detection.CenterXPixel,
                detection.CenterYPixel,
                -searchRange,
                searchRange,
                coarseStep);
            var fine = SearchAutoFeatureAngle(
                currentNormalized,
                feature,
                detection.CenterXPixel,
                detection.CenterYPixel,
                coarse.AngleDegrees - TemplateAngleRefineWindowDegrees,
                coarse.AngleDegrees + TemplateAngleRefineWindowDegrees,
                fineStep);

            if (fine.Score > 0)
            {
                votes.Add(new AutoFeatureVote(
                    feature.Index,
                    fine.AngleDegrees,
                    AngleMath.NormalizeDegrees360(referenceAngleDegrees + fine.AngleDegrees),
                    fine.Score,
                    Math.Max(coarse.SecondBestScore, fine.SecondBestScore),
                    feature.QualityScore,
                    fine.TopCandidates));
            }
        }

        if (votes.Count == 0)
        {
            return new ResolvedAngle(
                detection.AngleDegrees,
                AllowsFullRotation: true,
                "auto-feature-rotation-no-match",
                Score: 0.0,
                AlternativeScore: 0.0,
                SearchRangeDegrees: searchRange,
                Candidates: []);
        }

        var clusters = BuildAutoFeatureAngleClusters(votes, referenceAngleDegrees);
        var bestCluster = SelectAutoFeatureAngleCluster(clusters, parameters.TemplateAngleMinimumScore);
        var selectedScore = CalculateAutoFeatureClusterConfidence(bestCluster);
        var alternativeScore = clusters
            .Where(cluster => Math.Abs(AngleMath.NormalizeDeltaDegrees360(cluster.AngleOffsetDegrees, bestCluster.AngleOffsetDegrees)) >=
                TemplateAngleAmbiguitySeparationDegrees)
            .Select(CalculateAutoFeatureClusterConfidence)
            .DefaultIfEmpty(0.0)
            .Max();
        var candidates = buildDiagnostics
            ? votes
                .SelectMany(vote => vote.Candidates.Select(candidate => new
                {
                    vote.FeatureIndex,
                    candidate.AngleDegrees,
                    ResolvedAngleDegrees = AngleMath.NormalizeDegrees360(referenceAngleDegrees + candidate.AngleDegrees),
                    candidate.Score
                }))
                .OrderByDescending(candidate => candidate.Score)
                .Take(8)
                .Select((vote, index) => new AngleCandidateDiagnostic(
                    index + 1,
                    vote.AngleDegrees,
                    vote.ResolvedAngleDegrees,
                    Math.Clamp(vote.Score, 0.0, 1.0),
                    $"auto-feature-{vote.FeatureIndex}"))
                .ToArray()
            : null;

        return new ResolvedAngle(
            bestCluster.ResolvedAngleDegrees,
            AllowsFullRotation: true,
            "auto-feature-rotation",
            selectedScore,
            alternativeScore,
            searchRange,
            candidates);
    }

    private static double CalculateAutoFeatureVoteRankScore(AutoFeatureVote vote)
    {
        return Math.Clamp(vote.Score * vote.QualityScore, 0.0, 1.0);
    }

    private static double CalculateAutoFeatureClusterConfidence(AutoFeatureAngleCluster cluster)
    {
        return Math.Clamp(cluster.RankScore, 0.0, 1.0);
    }

    private static IReadOnlyList<AutoFeatureAngleCluster> BuildAutoFeatureAngleClusters(
        IReadOnlyList<AutoFeatureVote> votes,
        double referenceAngleDegrees)
    {
        var clusters = new List<List<AutoFeatureVote>>();
        foreach (var vote in votes
            .SelectMany(vote => vote.Candidates.Select(candidate => vote with
            {
                AngleOffsetDegrees = candidate.AngleDegrees,
                ResolvedAngleDegrees = AngleMath.NormalizeDegrees360(referenceAngleDegrees + candidate.AngleDegrees),
                Score = candidate.Score
            }))
            .OrderByDescending(vote => vote.Score))
        {
            var cluster = clusters.FirstOrDefault(existing =>
                existing.Any(existingVote => Math.Abs(AngleMath.NormalizeDeltaDegrees360(vote.AngleOffsetDegrees, existingVote.AngleOffsetDegrees)) <
                    TemplateAngleAmbiguitySeparationDegrees));
            if (cluster is null)
            {
                clusters.Add([vote]);
            }
            else
            {
                cluster.Add(vote);
            }
        }

        return clusters
            .Select(cluster =>
            {
                var best = cluster.OrderByDescending(vote => vote.Score).First();
                var supportCount = cluster.Select(vote => vote.FeatureIndex).Distinct().Count();
                var rankScore = cluster
                    .GroupBy(vote => vote.FeatureIndex)
                    .Select(group => group.Max(CalculateAutoFeatureVoteRankScore))
                    .Sum();
                return new AutoFeatureAngleCluster(
                    best.AngleOffsetDegrees,
                    best.ResolvedAngleDegrees,
                    best.Score,
                    rankScore,
                    best.AlternativeScore,
                    supportCount,
                    best.FeatureIndex);
            })
            .OrderByDescending(cluster => cluster.RankScore)
            .ThenByDescending(cluster => cluster.Score)
            .ToArray();
    }

    private static AutoFeatureAngleCluster SelectAutoFeatureAngleCluster(
        IReadOnlyList<AutoFeatureAngleCluster> clusters,
        double minimumReliableScore)
    {
        var best = clusters[0];
        var rawScoreBest = clusters
            .OrderByDescending(cluster => cluster.Score)
            .ThenByDescending(cluster => cluster.RankScore)
            .First();
        if (Math.Abs(best.AngleOffsetDegrees) >= AutoFeatureLargeAngleMinimumDegrees)
        {
            if (best.RankScore < minimumReliableScore &&
                Math.Abs(rawScoreBest.AngleOffsetDegrees) < AutoFeatureLargeAngleMinimumDegrees &&
                rawScoreBest.Score > best.Score)
            {
                return rawScoreBest;
            }

            return best;
        }

        var largeAngleNearTie = clusters
            .Where(cluster => Math.Abs(cluster.AngleOffsetDegrees) >= AutoFeatureLargeAngleMinimumDegrees)
            .Where(cluster => cluster.Score >= best.Score)
            .OrderByDescending(cluster => cluster.SupportCount)
            .ThenByDescending(cluster => cluster.RankScore)
            .ThenByDescending(cluster => cluster.Score)
            .FirstOrDefault();

        return largeAngleNearTie ?? best;
    }

    private static Mat ExtractSearchAnglePatch(Mat image, double centerX, double centerY, Size searchSize)
    {
        var sourceRect = BuildCenteredRect(centerX, centerY, Math.Max(searchSize.Width, searchSize.Height), image.Width, image.Height);
        using var sourcePatch = new Mat(image, sourceRect);
        using var gray = ToGray(sourcePatch);
        using var normalized = NormalizeTemplateAngleImage(gray);
        var resized = new Mat();
        Cv2.Resize(normalized, resized, searchSize, 0, 0, InterpolationFlags.Area);
        return resized;
    }

    private static IReadOnlyList<AutoFeatureCandidate> ExtractAutoFeatureCandidates(
        Mat preparedTemplate,
        PartTemplate template)
    {
        using var gray = ToGray(preparedTemplate);
        using var normalized = NormalizeFeatureMatchImage(gray);
        using var partMask = BuildFilledPartMask(gray, template);
        using var edges = new Mat();
        Cv2.Canny(normalized, edges, 40, 120);

        var radius = Math.Max(Math.Min(template.ReferenceWidthPixels, template.ReferenceHeightPixels) / 2.0, AutoFeaturePatchMinimumPixels);
        var patchSize = CalculateAutoFeaturePatchSize(template.ReferenceWidthPixels, template.ReferenceHeightPixels);
        var step = Math.Max(patchSize / 2, (int)Math.Round(radius * 2.0 / AutoFeatureCandidateGrid));
        var startX = (int)Math.Round(template.ReferenceCenterXPixel - radius * AutoFeatureMaximumRadiusRatio);
        var endX = (int)Math.Round(template.ReferenceCenterXPixel + radius * AutoFeatureMaximumRadiusRatio);
        var startY = (int)Math.Round(template.ReferenceCenterYPixel - radius * AutoFeatureMaximumRadiusRatio);
        var endY = (int)Math.Round(template.ReferenceCenterYPixel + radius * AutoFeatureMaximumRadiusRatio);
        var scored = new List<(Rect Rect, double CenterX, double CenterY, double Radius, double Score)>();

        for (var y = startY; y <= endY; y += step)
        {
            for (var x = startX; x <= endX; x += step)
            {
                var centerX = x;
                var centerY = y;
                var dx = centerX - template.ReferenceCenterXPixel;
                var dy = centerY - template.ReferenceCenterYPixel;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < radius * AutoFeatureMinimumRadiusRatio ||
                    distance > radius * AutoFeatureMaximumRadiusRatio)
                {
                    continue;
                }

                if (!TryBuildCenteredRectInsideImage(
                    centerX,
                    centerY,
                    patchSize,
                    preparedTemplate.Width,
                    preparedTemplate.Height,
                    out var rect))
                {
                    continue;
                }

                using var patch = new Mat(normalized, rect);
                using var maskPatch = new Mat(partMask, rect);
                var partCoverage = Cv2.CountNonZero(maskPatch) / Math.Max(rect.Width * rect.Height, 1.0);
                if (partCoverage < 0.92)
                {
                    continue;
                }

                using var edgePatch = new Mat(edges, rect);
                var mean = Cv2.Mean(patch).Val0;
                using var meanMat = new Mat(patch.Size(), patch.Type(), Scalar.All(mean));
                using var diff = new Mat();
                Cv2.Absdiff(patch, meanMat, diff);
                var contrast = Cv2.Mean(diff).Val0;
                var edgeDensity = Cv2.CountNonZero(edgePatch) / Math.Max(rect.Width * rect.Height, 1.0);
                var overexposed = CountThreshold(patch, 245) / Math.Max(rect.Width * rect.Height, 1.0);
                var underexposed = CountUnderThreshold(patch, 10) / Math.Max(rect.Width * rect.Height, 1.0);
                var score = contrast * (1.0 + edgeDensity * 8.0) * (1.0 - Math.Min(overexposed + underexposed, 0.85));
                if (score >= AutoFeatureMinimumTextureScore)
                {
                    scored.Add((rect, centerX, centerY, distance, score));
                }
            }
        }

        var features = new List<AutoFeatureCandidate>();
        foreach (var item in scored.OrderByDescending(item => item.Score))
        {
            if (features.Any(feature => DistancePixels(feature.CenterXPixel, feature.CenterYPixel, item.CenterX, item.CenterY) < patchSize))
            {
                continue;
            }

            var patch = new Mat(normalized, item.Rect).Clone();
            var dx = item.CenterX - template.ReferenceCenterXPixel;
            var dy = item.CenterY - template.ReferenceCenterYPixel;
            features.Add(new AutoFeatureCandidate(
                features.Count + 1,
                patch,
                item.CenterX,
                item.CenterY,
                dx,
                dy,
                item.Radius,
                Math.Atan2(dy, dx) * 180.0 / Math.PI,
                Math.Clamp(item.Score / 32.0, 0.1, 1.0)));

            if (features.Count >= AutoFeatureMaxCandidates)
            {
                break;
            }
        }

        return features;
    }

    private static (double AngleDegrees, double Score, double SecondBestScore, IReadOnlyList<(double AngleDegrees, double Score)> TopCandidates) SearchAutoFeatureAngle(
        Mat currentNormalized,
        AutoFeatureCandidate feature,
        double currentCenterX,
        double currentCenterY,
        double startDegrees,
        double endDegrees,
        double stepDegrees)
    {
        var bestAngle = startDegrees;
        var bestScore = double.NegativeInfinity;
        var candidates = new List<(double AngleDegrees, double Score)>();

        for (var angle = startDegrees; angle <= endDegrees + 1e-9; angle += stepDegrees)
        {
            var radians = -angle * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            var predictedX = currentCenterX + cos * feature.OffsetXPixel - sin * feature.OffsetYPixel;
            var predictedY = currentCenterY + sin * feature.OffsetXPixel + cos * feature.OffsetYPixel;
            var searchPadding = Math.Max(24, feature.Patch.Width / 2);
            var searchSize = feature.Patch.Width + searchPadding * 2;
            if (!TryBuildCenteredRectInsideImage(
                predictedX,
                predictedY,
                searchSize,
                currentNormalized.Width,
                currentNormalized.Height,
                out var searchRect))
            {
                continue;
            }

            using var rotated = RotateTemplatePatch(feature.Patch, angle);
            using var search = new Mat(currentNormalized, searchRect);
            var score = MatchTemplateScore(search, rotated);
            AddAngleCandidate(candidates, angle, score);
            if (score > bestScore)
            {
                bestScore = score;
                bestAngle = angle;
            }
        }

        if (candidates.Count == 0)
        {
            return (bestAngle, 0.0, 0.0, []);
        }

        var topCandidates = candidates
            .OrderByDescending(candidate => candidate.Score)
            .Take(5)
            .Select(candidate => (candidate.AngleDegrees, Score: Math.Clamp(candidate.Score, 0.0, 1.0)))
            .ToArray();
        var bestCandidate = topCandidates[0];
        var secondBestScore = topCandidates
            .Skip(1)
            .Where(candidate => Math.Abs(AngleMath.NormalizeDeltaDegrees360(candidate.AngleDegrees, bestCandidate.AngleDegrees)) >=
                TemplateAngleAmbiguitySeparationDegrees)
            .Select(candidate => candidate.Score)
            .DefaultIfEmpty(0.0)
            .Max();

        return (
            bestCandidate.AngleDegrees,
            Math.Clamp(bestCandidate.Score, 0.0, 1.0),
            Math.Clamp(secondBestScore, 0.0, 1.0),
            topCandidates);
    }

    private static double MatchTemplateScore(Mat search, Mat templatePatch)
    {
        if (search.Width < templatePatch.Width || search.Height < templatePatch.Height)
        {
            return 0.0;
        }

        using var result = new Mat();
        Cv2.MatchTemplate(search, templatePatch, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out double _, out var maxVal);
        return Math.Clamp(maxVal, 0.0, 1.0);
    }

    private static (double AngleOffsetDegrees, double Score, double SecondBestScore, IReadOnlyList<AngleCandidateDiagnostic> Candidates) SearchPolarRingAngle(
        float[] currentSignature,
        float[] templateSignature,
        double referenceAngleDegrees,
        double configuredSearchRangeDegrees,
        bool buildDiagnostics = true)
    {
        var sampleCount = Math.Min(currentSignature.Length, templateSignature.Length);
        if (sampleCount == 0)
        {
            return (0.0, 0.0, 0.0, []);
        }

        var searchRange = Math.Clamp(configuredSearchRangeDegrees, 1.0, 360.0);
        var effectiveSearchRange = Math.Min(searchRange, 180.0);
        var maxShift = (int)Math.Ceiling(sampleCount * effectiveSearchRange / 360.0);
        var bestShift = 0;
        var bestScore = double.NegativeInfinity;
        var candidates = new List<(double AngleDegrees, double Score)>();

        for (var shift = -maxShift; shift <= maxShift; shift++)
        {
            var angle = -ShiftToDegrees(shift, sampleCount);
            if (Math.Abs(angle) > searchRange + 1e-9)
            {
                continue;
            }

            var score = CalculateCircularCorrelation(currentSignature, templateSignature, shift);
            AddAngleCandidate(candidates, angle, score);
            if (score > bestScore)
            {
                bestScore = score;
                bestShift = shift;
            }
        }

        var bestAngle = -ShiftToDegrees(bestShift, sampleCount);
        var secondBestScore = candidates
            .Where(candidate => Math.Abs(AngleMath.NormalizeDeltaDegrees360(candidate.AngleDegrees, bestAngle)) >=
                PolarRingAlternativeSeparationDegrees)
            .Select(candidate => candidate.Score)
            .DefaultIfEmpty(0.0)
            .Max();
        var diagnostics = buildDiagnostics
            ? candidates
                .OrderByDescending(candidate => candidate.Score)
                .Take(8)
                .Select((candidate, index) => new AngleCandidateDiagnostic(
                    index + 1,
                    candidate.AngleDegrees,
                    AngleMath.NormalizeDegrees360(referenceAngleDegrees + candidate.AngleDegrees),
                    Math.Clamp(candidate.Score, 0.0, 1.0),
                    "polar-ring"))
                .ToArray()
            : Array.Empty<AngleCandidateDiagnostic>();

        return (
            bestAngle,
            Math.Clamp(bestScore, 0.0, 1.0),
            Math.Clamp(secondBestScore, 0.0, 1.0),
            diagnostics);
    }

    private static (double AngleDegrees, double Score, double SecondBestScore, IReadOnlyList<(double AngleDegrees, double Score)> TopCandidates) SearchTemplateAngle(
        Mat searchPatch,
        Mat templatePatch,
        double startDegrees,
        double endDegrees,
        double stepDegrees)
    {
        using var invertedSearch = new Mat();
        Cv2.BitwiseNot(searchPatch, invertedSearch);
        using var distance = new Mat();
        Cv2.DistanceTransform(invertedSearch, distance, DistanceTypes.L2, DistanceTransformMasks.Mask3);

        var bestAngle = startDegrees;
        var bestScore = double.NegativeInfinity;
        var secondBestScore = 0.0;
        var candidates = new List<(double AngleDegrees, double Score)>();

        for (var angle = startDegrees; angle <= endDegrees + 1e-9; angle += stepDegrees)
        {
            using var rotated = RotateTemplatePatch(templatePatch, angle);
            var score = CalculateChamferScore(distance, rotated);
            AddAngleCandidate(candidates, angle, score);
            if (score > bestScore)
            {
                secondBestScore = bestScore;
                bestScore = score;
                bestAngle = angle;
            }
            else if (Math.Abs(AngleMath.NormalizeDeltaDegrees360(angle, bestAngle)) >= TemplateAngleAmbiguitySeparationDegrees &&
                score > secondBestScore)
            {
                secondBestScore = score;
            }
        }

        return (
            bestAngle,
            Math.Clamp(bestScore, 0.0, 1.0),
            Math.Clamp(secondBestScore, 0.0, 1.0),
            candidates
                .OrderByDescending(candidate => candidate.Score)
                .Take(5)
                .Select(candidate => (candidate.AngleDegrees, Math.Clamp(candidate.Score, 0.0, 1.0)))
                .ToArray());
    }

    private static void AddAngleCandidate(
        List<(double AngleDegrees, double Score)> candidates,
        double angleDegrees,
        double score)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            if (Math.Abs(AngleMath.NormalizeDeltaDegrees360(angleDegrees, candidates[i].AngleDegrees)) <
                TemplateAngleAmbiguitySeparationDegrees)
            {
                if (score > candidates[i].Score)
                {
                    candidates[i] = (angleDegrees, score);
                }

                return;
            }
        }

        candidates.Add((angleDegrees, score));
    }

    private static IReadOnlyList<AngleCandidateDiagnostic> BuildAngleCandidates(
        double referenceAngleDegrees,
        IReadOnlyList<(double AngleDegrees, double Score)> fineCandidates,
        IReadOnlyList<(double AngleDegrees, double Score)> coarseCandidates)
    {
        var candidates = fineCandidates
            .Select(candidate => new
            {
                candidate.AngleDegrees,
                candidate.Score,
                Stage = "fine"
            })
            .Concat(coarseCandidates.Select(candidate => new
            {
                candidate.AngleDegrees,
                candidate.Score,
                Stage = "coarse"
            }))
            .GroupBy(candidate => Math.Round(candidate.AngleDegrees / TemplateAngleAmbiguitySeparationDegrees))
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .Take(5)
            .Select((candidate, index) => new AngleCandidateDiagnostic(
                index + 1,
                candidate.AngleDegrees,
                AngleMath.NormalizeDegrees360(referenceAngleDegrees + candidate.AngleDegrees),
                Math.Clamp(candidate.Score, 0.0, 1.0),
                candidate.Stage))
            .ToArray();

        return candidates;
    }

    private static double CalculateChamferScore(Mat distanceToSearchEdge, Mat rotatedTemplatePatch)
    {
        using var binaryTemplate = new Mat();
        Cv2.Threshold(rotatedTemplatePatch, binaryTemplate, 32, 255, ThresholdTypes.Binary);
        var templateEdgeCount = Cv2.CountNonZero(binaryTemplate);
        if (templateEdgeCount < 8)
        {
            return 0.0;
        }

        var averageDistance = Cv2.Mean(distanceToSearchEdge, binaryTemplate).Val0;
        return Math.Exp(-averageDistance / 6.0);
    }

    private static Mat RotateTemplatePatch(Mat templatePatch, double angleDegrees)
    {
        using var rotation = Cv2.GetRotationMatrix2D(
            new Point2f(templatePatch.Width / 2f, templatePatch.Height / 2f),
            angleDegrees,
            1.0);
        var rotated = new Mat();
        Cv2.WarpAffine(
            templatePatch,
            rotated,
            rotation,
            templatePatch.Size(),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            Scalar.All(0));
        return rotated;
    }

    private static Mat NormalizeTemplateAngleImage(Mat gray)
    {
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0);
        using var equalized = new Mat();
        Cv2.EqualizeHist(blurred, equalized);
        var edges = new Mat();
        Cv2.Canny(equalized, edges, 40, 120);
        return edges;
    }

    private static Mat NormalizeFeatureMatchImage(Mat gray)
    {
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0);
        using var equalized = new Mat();
        Cv2.EqualizeHist(blurred, equalized);
        var normalized = new Mat();
        Cv2.Normalize(equalized, normalized, 0, 255, NormTypes.MinMax);
        return normalized;
    }

    private static float[] BuildPolarRingSignature(Mat gray, double centerX, double centerY, double radiusPixels)
    {
        using var normalized = NormalizeFeatureMatchImage(gray);
        using var gradient = BuildGradientMagnitude(normalized);
        var signature = new float[PolarRingAngularSamples];
        var innerRadius = Math.Max(2.0, radiusPixels * PolarRingInnerRadiusRatio);
        var outerRadius = Math.Max(innerRadius + 2.0, radiusPixels * PolarRingOuterRadiusRatio);
        var radialStep = (outerRadius - innerRadius) / Math.Max(PolarRingRadialSamples - 1, 1);

        for (var angleIndex = 0; angleIndex < signature.Length; angleIndex++)
        {
            var angle = angleIndex * 2.0 * Math.PI / signature.Length;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var sum = 0.0;
            var weightSum = 0.0;

            for (var radiusIndex = 0; radiusIndex < PolarRingRadialSamples; radiusIndex++)
            {
                var radius = innerRadius + radiusIndex * radialStep;
                var x = centerX + cos * radius;
                var y = centerY + sin * radius;
                if (!TrySampleByte(normalized, x, y, out var intensity) ||
                    !TrySampleFloat(gradient, x, y, out var edge))
                {
                    continue;
                }

                var radialPosition = radiusIndex / Math.Max(PolarRingRadialSamples - 1.0, 1.0);
                var radialWeight = 0.65 + radialPosition * 0.7;
                sum += radialWeight * ((intensity / 255.0) + (edge / 255.0) * 1.4);
                weightSum += radialWeight;
            }

            signature[angleIndex] = weightSum > 0.0001
                ? (float)(sum / weightSum)
                : 0f;
        }

        NormalizeSignatureInPlace(signature);
        return signature;
    }

    private static float[] BuildContourPolarSignature(PartDetection detection)
    {
        var signature = new float[PolarRingAngularSamples];
        if (detection.Contour.Length < 3)
        {
            return signature;
        }

        var centerX = detection.CenterXPixel - detection.Offset.X;
        var centerY = detection.CenterYPixel - detection.Offset.Y;
        for (var i = 0; i < detection.Contour.Length; i++)
        {
            var start = detection.Contour[i];
            var end = detection.Contour[(i + 1) % detection.Contour.Length];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy)));
            for (var step = 0; step <= steps; step++)
            {
                var t = step / (double)steps;
                var x = start.X + dx * t;
                var y = start.Y + dy * t;
                var radiusX = x - centerX;
                var radiusY = y - centerY;
                var radius = Math.Sqrt(radiusX * radiusX + radiusY * radiusY);
                if (radius <= 0.0001)
                {
                    continue;
                }

                var angle = Math.Atan2(radiusY, radiusX);
                if (angle < 0)
                {
                    angle += Math.PI * 2.0;
                }

                var index = Math.Clamp(
                    (int)Math.Round(angle * signature.Length / (Math.PI * 2.0)) % signature.Length,
                    0,
                    signature.Length - 1);
                signature[index] = Math.Max(signature[index], (float)radius);
            }
        }

        FillMissingCircularSignatureBins(signature);
        NormalizeSignatureInPlace(signature);
        return signature;
    }

    private static float[] MirrorCircularSignature(float[] signature)
    {
        var mirrored = new float[signature.Length];
        if (signature.Length == 0)
        {
            return mirrored;
        }

        mirrored[0] = signature[0];
        for (var i = 1; i < signature.Length; i++)
        {
            mirrored[i] = signature[signature.Length - i];
        }

        return mirrored;
    }

    private static void FillMissingCircularSignatureBins(float[] signature)
    {
        if (signature.Length == 0 || signature.All(value => value <= 0f))
        {
            return;
        }

        for (var i = 0; i < signature.Length; i++)
        {
            if (signature[i] > 0f)
            {
                continue;
            }

            var previousIndex = FindNearestCircularSignatureValue(signature, i, -1);
            var nextIndex = FindNearestCircularSignatureValue(signature, i, 1);
            signature[i] = previousIndex >= 0 && nextIndex >= 0
                ? (signature[previousIndex] + signature[nextIndex]) / 2f
                : previousIndex >= 0
                    ? signature[previousIndex]
                    : signature[nextIndex];
        }
    }

    private static int FindNearestCircularSignatureValue(float[] signature, int startIndex, int direction)
    {
        for (var offset = 1; offset < signature.Length; offset++)
        {
            var index = PositiveModulo(startIndex + offset * direction, signature.Length);
            if (signature[index] > 0f)
            {
                return index;
            }
        }

        return -1;
    }

    private static Mat BuildGradientMagnitude(Mat gray)
    {
        using var sobelX = new Mat();
        using var sobelY = new Mat();
        Cv2.Sobel(gray, sobelX, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(gray, sobelY, MatType.CV_32F, 0, 1, 3);
        using var magnitude = new Mat();
        Cv2.Magnitude(sobelX, sobelY, magnitude);
        var normalized = new Mat();
        Cv2.Normalize(magnitude, normalized, 0, 255, NormTypes.MinMax);
        return normalized;
    }

    private static void NormalizeSignatureInPlace(float[] signature)
    {
        if (signature.Length == 0)
        {
            return;
        }

        var mean = signature.Average(value => (double)value);
        var variance = signature
            .Select(value => (value - mean) * (value - mean))
            .DefaultIfEmpty(0.0)
            .Average();
        var stdDev = Math.Sqrt(Math.Max(variance, 0.0));
        if (stdDev < 0.000001)
        {
            Array.Fill(signature, 0f);
            return;
        }

        for (var i = 0; i < signature.Length; i++)
        {
            signature[i] = (float)((signature[i] - mean) / stdDev);
        }

        SmoothCircularSignatureInPlace(signature);
    }

    private static void SmoothCircularSignatureInPlace(float[] signature)
    {
        if (signature.Length < 3)
        {
            return;
        }

        var copy = signature.ToArray();
        for (var i = 0; i < signature.Length; i++)
        {
            var previous = copy[(i - 1 + copy.Length) % copy.Length];
            var current = copy[i];
            var next = copy[(i + 1) % copy.Length];
            signature[i] = (float)((previous + current * 2.0 + next) / 4.0);
        }
    }

    private static double CalculateSignatureSignal(float[] signature)
    {
        if (signature.Length == 0)
        {
            return 0.0;
        }

        return Math.Sqrt(signature.Select(value => value * value).Average());
    }

    private static double CalculateCircularCorrelation(float[] currentSignature, float[] templateSignature, int shift)
    {
        var sampleCount = Math.Min(currentSignature.Length, templateSignature.Length);
        if (sampleCount == 0)
        {
            return 0.0;
        }

        var dot = 0.0;
        var currentNorm = 0.0;
        var templateNorm = 0.0;
        for (var i = 0; i < sampleCount; i++)
        {
            var templateIndex = PositiveModulo(i - shift, sampleCount);
            var current = currentSignature[i];
            var template = templateSignature[templateIndex];
            dot += current * template;
            currentNorm += current * current;
            templateNorm += template * template;
        }

        if (currentNorm < 0.000001 || templateNorm < 0.000001)
        {
            return 0.0;
        }

        var correlation = dot / Math.Sqrt(currentNorm * templateNorm);
        return Math.Clamp((correlation + 1.0) / 2.0, 0.0, 1.0);
    }

    private static double ShiftToDegrees(int shift, int sampleCount)
    {
        return AngleMath.NormalizeDegrees360(shift * 360.0 / Math.Max(sampleCount, 1));
    }

    private static int PositiveModulo(int value, int modulo)
    {
        var result = value % modulo;
        return result < 0 ? result + modulo : result;
    }

    private static double CalculatePolarRingRadius(double referenceWidthPixels, double referenceHeightPixels)
    {
        var sourceSize = Math.Min(
            referenceWidthPixels > 0.0001 ? referenceWidthPixels : TemplateAnglePatchMaximumPixels,
            referenceHeightPixels > 0.0001 ? referenceHeightPixels : TemplateAnglePatchMaximumPixels);
        return Math.Clamp(sourceSize / 2.0, TemplateAnglePatchMinimumPixels / 2.0, TemplateAnglePatchMaximumPixels / 2.0);
    }

    private static bool TrySampleByte(Mat image, double x, double y, out double value)
    {
        value = 0.0;
        if (x < 0 || y < 0 || x >= image.Width - 1 || y >= image.Height - 1)
        {
            return false;
        }

        var left = (int)Math.Floor(x);
        var top = (int)Math.Floor(y);
        var fx = x - left;
        var fy = y - top;
        var topLeft = image.At<byte>(top, left);
        var topRight = image.At<byte>(top, left + 1);
        var bottomLeft = image.At<byte>(top + 1, left);
        var bottomRight = image.At<byte>(top + 1, left + 1);
        value = Bilinear(topLeft, topRight, bottomLeft, bottomRight, fx, fy);
        return true;
    }

    private static bool TrySampleFloat(Mat image, double x, double y, out double value)
    {
        value = 0.0;
        if (x < 0 || y < 0 || x >= image.Width - 1 || y >= image.Height - 1)
        {
            return false;
        }

        var left = (int)Math.Floor(x);
        var top = (int)Math.Floor(y);
        var fx = x - left;
        var fy = y - top;
        var topLeft = image.At<float>(top, left);
        var topRight = image.At<float>(top, left + 1);
        var bottomLeft = image.At<float>(top + 1, left);
        var bottomRight = image.At<float>(top + 1, left + 1);
        value = Bilinear(topLeft, topRight, bottomLeft, bottomRight, fx, fy);
        return true;
    }

    private static double Bilinear(
        double topLeft,
        double topRight,
        double bottomLeft,
        double bottomRight,
        double fx,
        double fy)
    {
        var top = topLeft + (topRight - topLeft) * fx;
        var bottom = bottomLeft + (bottomRight - bottomLeft) * fx;
        return top + (bottom - top) * fy;
    }

    private static Mat BuildFilledPartMask(Mat gray, PartTemplate template)
    {
        using var blurred = Blur(gray, 5);
        using var binary = Threshold(blurred, 0);
        using var inverted = new Mat();
        Cv2.BitwiseNot(binary, inverted);

        var binaryMask = SelectMaskContainingTemplateCenter(binary, template);
        var invertedMask = SelectMaskContainingTemplateCenter(inverted, template);
        if (Cv2.CountNonZero(invertedMask) > Cv2.CountNonZero(binaryMask))
        {
            binaryMask.Dispose();
            return invertedMask;
        }

        invertedMask.Dispose();
        return binaryMask;
    }

    private static Mat SelectMaskContainingTemplateCenter(Mat binary, PartTemplate template)
    {
        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var mask = new Mat(binary.Size(), MatType.CV_8UC1, Scalar.Black);
        if (contours.Length == 0)
        {
            return mask;
        }

        var center = new Point2f((float)template.ReferenceCenterXPixel, (float)template.ReferenceCenterYPixel);
        var selected = contours
            .Select(contour => new
            {
                Contour = contour,
                Area = Cv2.ContourArea(contour),
                ContainsCenter = Cv2.PointPolygonTest(contour, center, false) >= 0
            })
            .Where(candidate => candidate.Area > 0)
            .OrderByDescending(candidate => candidate.ContainsCenter)
            .ThenByDescending(candidate => candidate.Area)
            .FirstOrDefault();
        if (selected is null)
        {
            return mask;
        }

        Cv2.DrawContours(mask, [selected.Contour], -1, Scalar.White, -1);
        return mask;
    }

    private static int CalculateTemplateAnglePatchSize(double referenceWidthPixels, double referenceHeightPixels)
    {
        var sourceSize = Math.Min(
            referenceWidthPixels > 0.0001 ? referenceWidthPixels : TemplateAnglePatchMaximumPixels,
            referenceHeightPixels > 0.0001 ? referenceHeightPixels : TemplateAnglePatchMaximumPixels);
        var scaled = (int)Math.Round(sourceSize * 0.75);
        return Math.Clamp(scaled, TemplateAnglePatchMinimumPixels, TemplateAnglePatchMaximumPixels);
    }

    private static int CalculateAutoFeaturePatchSize(double referenceWidthPixels, double referenceHeightPixels)
    {
        var sourceSize = Math.Min(
            referenceWidthPixels > 0.0001 ? referenceWidthPixels : AutoFeaturePatchMaximumPixels,
            referenceHeightPixels > 0.0001 ? referenceHeightPixels : AutoFeaturePatchMaximumPixels);
        var scaled = (int)Math.Round(sourceSize * AutoFeaturePatchSizeRatio);
        return Math.Clamp(scaled, AutoFeaturePatchMinimumPixels, AutoFeaturePatchMaximumPixels);
    }

    private static Rect BuildCenteredRect(double centerX, double centerY, int requestedSize, int imageWidth, int imageHeight)
    {
        var size = Math.Clamp(requestedSize, 1, Math.Min(imageWidth, imageHeight));
        var x = (int)Math.Round(centerX - size / 2.0);
        var y = (int)Math.Round(centerY - size / 2.0);
        x = Math.Clamp(x, 0, Math.Max(0, imageWidth - size));
        y = Math.Clamp(y, 0, Math.Max(0, imageHeight - size));
        return new Rect(x, y, size, size);
    }

    private static bool TryBuildCenteredRectInsideImage(
        double centerX,
        double centerY,
        int requestedSize,
        int imageWidth,
        int imageHeight,
        out Rect rect)
    {
        var size = Math.Clamp(requestedSize, 1, Math.Min(imageWidth, imageHeight));
        var x = (int)Math.Round(centerX - size / 2.0);
        var y = (int)Math.Round(centerY - size / 2.0);
        rect = new Rect(
            Math.Clamp(x, 0, Math.Max(0, imageWidth - size)),
            Math.Clamp(y, 0, Math.Max(0, imageHeight - size)),
            size,
            size);
        return x >= 0 &&
            y >= 0 &&
            x + size <= imageWidth &&
            y + size <= imageHeight;
    }

    private static bool IsRectInsideImage(Rect rect, int imageWidth, int imageHeight)
    {
        return rect.X >= 0 &&
            rect.Y >= 0 &&
            rect.Right <= imageWidth &&
            rect.Bottom <= imageHeight;
    }

    private static double CountThreshold(Mat image, byte threshold)
    {
        using var mask = new Mat();
        Cv2.Threshold(image, mask, threshold, 255, ThresholdTypes.Binary);
        return Cv2.CountNonZero(mask);
    }

    private static double CountUnderThreshold(Mat image, byte threshold)
    {
        using var mask = new Mat();
        Cv2.Threshold(image, mask, threshold, 255, ThresholdTypes.BinaryInv);
        return Cv2.CountNonZero(mask);
    }

    private static double DistancePixels(double ax, double ay, double bx, double by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double NormalizeAngleStep(double configuredStepDegrees, double fallbackStepDegrees)
    {
        return configuredStepDegrees > 0.0001
            ? configuredStepDegrees
            : fallbackStepDegrees;
    }

    private static bool HasTemplatePixelShape(PartTemplate template)
    {
        return template.ReferenceWidthPixels > 0.0001 && template.ReferenceHeightPixels > 0.0001;
    }

    private static double CalculateFillRatio(double areaPixels, double widthPixels, double heightPixels)
    {
        var boxArea = Math.Max(widthPixels * heightPixels, 0.0001);
        return Math.Clamp(areaPixels / boxArea, 0.0, 1.0);
    }

    private static double DistanceToTemplateCenterPixels(PartDetection detection, PartTemplate template)
    {
        var dx = detection.CenterXPixel - template.ReferenceCenterXPixel;
        var dy = detection.CenterYPixel - template.ReferenceCenterYPixel;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double ApplyOutputDirection(double action, bool invertDirection)
    {
        return invertDirection ? -action : action;
    }

    public static ProductionSetupDecision ValidateProductionSetup(PartTemplate template, VisionParameters parameters)
    {
        var machineSetup = ValidateMachineCalibration(parameters);
        return machineSetup.IsReady
            ? ValidateTemplateCalibration(template, parameters)
            : machineSetup;
    }

    public static ProductionSetupDecision ValidateMachineCalibration(VisionParameters parameters)
    {
        if (!parameters.CameraCalibration.Enabled)
        {
            return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.CameraCalibrationMissing);
        }

        var currentDistortionId = GetCurrentDistortionCalibrationId(parameters);
        if (!string.Equals(
                parameters.CameraCalibration.SourceDistortionCalibrationId,
                currentDistortionId,
                StringComparison.Ordinal))
        {
            return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.CameraCalibrationDistortionMismatch);
        }

        if (!parameters.RAxisCenterCalibration.Enabled)
        {
            return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.RAxisCenterMissing);
        }

        if (!string.Equals(
                parameters.RAxisCenterCalibration.SourceCameraCalibrationId,
                parameters.CameraCalibration.CalibrationId,
                StringComparison.Ordinal))
        {
            return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.RAxisCenterCameraMismatch);
        }

        return ProductionSetupDecision.Ready;
    }

    private static ProductionSetupDecision ValidateTemplateCalibration(PartTemplate template, VisionParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(template.SourceCameraCalibrationId))
        {
            return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.TemplateCameraCalibrationMissing);
        }

        if (!string.Equals(
                template.SourceCameraCalibrationId,
                parameters.CameraCalibration.CalibrationId,
                StringComparison.Ordinal))
        {
            return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.TemplateCameraCalibrationMismatch);
        }

        if (!string.Equals(
                template.SourceDistortionCalibrationId,
                GetCurrentDistortionCalibrationId(parameters),
                StringComparison.Ordinal))
        {
            return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.TemplateDistortionCalibrationMismatch);
        }

        return ProductionSetupDecision.Ready;
    }

    private static void EnsureProductionCalibrationReady(Mat image, VisionParameters parameters)
    {
        if (parameters.LensDistortionCalibration.Enabled &&
            !parameters.LensDistortionCalibration.CanApplyTo(image.Width, image.Height))
        {
            throw new InvalidOperationException(
                $"Lens distortion calibration applies to {parameters.LensDistortionCalibration.ImageWidth}x{parameters.LensDistortionCalibration.ImageHeight}, but current image is {image.Width}x{image.Height}.");
        }

        var setup = ValidateMachineCalibration(parameters);
        if (!setup.IsReady)
        {
            throw new InvalidOperationException(GetProductionSetupBlockMessage(setup.Reason));
        }
    }

    private static string GetProductionSetupBlockMessage(ProductionSetupBlockReason reason)
    {
        return reason switch
        {
            ProductionSetupBlockReason.CameraCalibrationMissing =>
                "Camera calibration is missing.",
            ProductionSetupBlockReason.CameraCalibrationDistortionMismatch =>
                "Camera calibration does not match the current distortion calibration.",
            ProductionSetupBlockReason.RAxisCenterMissing =>
                "R-axis center calibration is missing.",
            ProductionSetupBlockReason.RAxisCenterCameraMismatch =>
                "R-axis center calibration does not match the current camera calibration.",
            ProductionSetupBlockReason.TemplateCameraCalibrationMissing =>
                "Template camera calibration source is missing.",
            ProductionSetupBlockReason.TemplateCameraCalibrationMismatch =>
                "Template camera calibration source does not match the current camera calibration.",
            ProductionSetupBlockReason.TemplateDistortionCalibrationMismatch =>
                "Template distortion calibration source does not match the current distortion calibration.",
            _ => "Production setup is not ready."
        };
    }

    private static MachinePoint GetReferenceCenterMachine(PartTemplate template, VisionParameters parameters)
    {
        var setup = ValidateTemplateCalibration(template, parameters);
        if (!setup.IsReady)
        {
            throw new InvalidOperationException(GetProductionSetupBlockMessage(setup.Reason));
        }

        return new MachinePoint(template.ReferenceCenterXMm, template.ReferenceCenterYMm);
    }

    public static string? GetProductionSetupError(PartTemplate template, VisionParameters parameters)
    {
        var setup = ValidateProductionSetup(template, parameters);
        return setup.IsReady ? null : GetProductionSetupBlockMessage(setup.Reason);
    }

    public static string? GetMachineCalibrationError(VisionParameters parameters)
    {
        var setup = ValidateMachineCalibration(parameters);
        return setup.IsReady ? null : GetProductionSetupBlockMessage(setup.Reason);
    }

    private static void EnsureProductionSetup(Mat image, PartTemplate template, VisionParameters parameters)
    {
        EnsureProductionCalibrationReady(image, parameters);
        var setup = ValidateTemplateCalibration(template, parameters);
        if (!setup.IsReady)
        {
            throw new InvalidOperationException(GetProductionSetupBlockMessage(setup.Reason));
        }
    }

    private static string GetCurrentDistortionCalibrationId(VisionParameters parameters)
    {
        return parameters.LensDistortionCalibration.Enabled
            ? parameters.LensDistortionCalibration.CalibrationId
            : string.Empty;
    }

    private static Point[] OffsetContour(IEnumerable<Point> contour, Point offset)
    {
        return contour.Select(point => new Point(point.X + offset.X, point.Y + offset.Y)).ToArray();
    }

    private static void DrawCandidateContours(
        Mat diagnostic,
        IReadOnlyList<PartDetection> candidates,
        PartDetection selected)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (ReferenceEquals(candidate, selected))
            {
                continue;
            }

            var contour = OffsetContour(candidate.Contour, candidate.Offset);
            var color = new Scalar(0, 180, 255);
            Cv2.DrawContours(diagnostic, new[] { contour }, -1, color, 1);
            Cv2.PutText(
                diagnostic,
                $"C{index + 1}",
                new Point((int)Math.Round(candidate.CenterXPixel) + 8, (int)Math.Round(candidate.CenterYPixel) - 8),
                HersheyFonts.HersheySimplex,
                0.55,
                color,
                1);
        }
    }

    private static void DrawCenterMarkers(Mat diagnostic, PartDetection detection, PartTemplate template)
    {
        Cv2.DrawMarker(
            diagnostic,
            new Point((int)Math.Round(template.ReferenceCenterXPixel), (int)Math.Round(template.ReferenceCenterYPixel)),
            new Scalar(255, 191, 0),
            MarkerTypes.Cross,
            22,
            2);
        Cv2.DrawMarker(
            diagnostic,
            new Point((int)Math.Round(detection.CenterXPixel), (int)Math.Round(detection.CenterYPixel)),
            Scalar.Yellow,
            MarkerTypes.TiltedCross,
            22,
            2);
    }

    private static void DrawOverlay(
        Mat diagnostic,
        InspectionDecision decision,
        string message,
        InspectionMeasurement measurement,
        AngleResolutionDiagnostic angleDiagnostic,
        TemplateSimilarityResult? similarity)
    {
        var color = decision switch
        {
            InspectionDecision.Ok => Scalar.LimeGreen,
            InspectionDecision.Ng => Scalar.Red,
            _ => Scalar.OrangeRed
        };

        if (decision == InspectionDecision.Ng)
        {
            DrawLargeNgMarker(diagnostic);
        }

        Cv2.PutText(diagnostic, decision.ToString(), new Point(24, 44), HersheyFonts.HersheySimplex, 1.1, color, 2);
        Cv2.PutText(diagnostic, message, new Point(24, 84), HersheyFonts.HersheySimplex, 0.7, color, 2);
        Cv2.PutText(
            diagnostic,
            $"XY offset=({measurement.XOffsetMm:F3},{measurement.YOffsetMm:F3})mm comp=({measurement.XCompensationMm:F3},{measurement.YCompensationMm:F3})mm",
            new Point(24, 124),
            HersheyFonts.HersheySimplex,
            0.65,
            Scalar.White,
            2);
        Cv2.PutText(
            diagnostic,
            $"R offset={measurement.AngleOffsetDegrees:F3}deg comp={measurement.RotationCompensationDegrees:F3}deg",
            new Point(24, 160),
            HersheyFonts.HersheySimplex,
            0.65,
            Scalar.White,
            2);
        Cv2.PutText(
            diagnostic,
            $"W={measurement.WidthMm:F3}mm H={measurement.HeightMm:F3}mm score={measurement.MatchScore:F3}",
            new Point(24, 196),
            HersheyFonts.HersheySimplex,
            0.65,
            Scalar.White,
            2);
        Cv2.PutText(
            diagnostic,
            $"Angle={angleDiagnostic.Source} score={angleDiagnostic.Score:F3} margin={angleDiagnostic.ScoreMargin:F3}",
            new Point(24, 232),
            HersheyFonts.HersheySimplex,
            0.65,
            Scalar.White,
            2);
        if (similarity is not null)
        {
            Cv2.PutText(
                diagnostic,
                $"Shape=size {similarity.SizeScore:F3} contour {similarity.ShapeScore:F3} iou {similarity.MaskIoU:F3} edge {similarity.EdgeDistanceScore:F3}",
                new Point(24, 268),
                HersheyFonts.HersheySimplex,
                0.65,
                Scalar.White,
                2);
        }
    }

    private static void DrawLargeNgMarker(Mat diagnostic)
    {
        const string text = "NG";
        var fontScale = Math.Clamp(diagnostic.Width / 900.0, 3.6, 7.0);
        var thickness = Math.Max(8, (int)Math.Round(fontScale * 2.0));
        var padding = Math.Max(24, (int)Math.Round(fontScale * 8.0));
        var baseline = 0;
        var textSize = Cv2.GetTextSize(text, HersheyFonts.HersheySimplex, fontScale, thickness, out baseline);
        var origin = new Point(padding, padding + textSize.Height);
        var boxEnd = new Point(
            origin.X + textSize.Width + padding,
            origin.Y + baseline + padding);

        Cv2.Rectangle(diagnostic, new Point(0, 0), boxEnd, Scalar.Black, -1);
        Cv2.Rectangle(diagnostic, new Point(0, 0), boxEnd, Scalar.Red, Math.Max(4, thickness / 2));
        Cv2.PutText(diagnostic, text, origin, HersheyFonts.HersheySimplex, fontScale, Scalar.White, thickness + 8);
        Cv2.PutText(diagnostic, text, origin, HersheyFonts.HersheySimplex, fontScale, Scalar.Red, thickness);
    }
}
