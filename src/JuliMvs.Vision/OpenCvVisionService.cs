using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using JuliMvs.Core;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using OpenCvSharp;

namespace JuliMvs.Vision;

public sealed class OpenCvVisionService
{
	private sealed record CandidateDetection(string Source, PartDetection Detection);

	private sealed record ContourAngleProfile(double PcaAngleDegrees, double PcaRatio, double Circularity, bool IsPcaReliable);

	private sealed record ResolvedAngle(double AngleDegrees, bool AllowsFullRotation, string Source, double Score, double AlternativeScore, double SearchRangeDegrees, IReadOnlyList<AngleCandidateDiagnostic>? Candidates = null, string? Detail = null);

	private sealed record TemplateAngleModel(Guid TemplateId, string? ImagePath, string SourceDistortionCalibrationId, ImageRoi Roi, Mat Patch, Size SearchSize, double SourceCenterXPixel, double SourceCenterYPixel);

	private sealed record AutoFeatureAngleModel(Guid TemplateId, string? ImagePath, string SourceDistortionCalibrationId, ImageRoi Roi, IReadOnlyList<AutoFeatureCandidate> Features);

	private sealed record PolarRingAngleModel(Guid TemplateId, string? ImagePath, string SourceDistortionCalibrationId, ImageRoi Roi, float[] Signature, double Signal, double RadiusPixels);

	private sealed record ContourPolarAngleModel(Guid TemplateId, string? ImagePath, string SourceDistortionCalibrationId, ImageRoi Roi, float[] Signature, float[] RadiusSignature, double Signal, double RadiusSignal, ContourAngleProfile Profile);

	private sealed record ContourMirrorFaceDebugComputation(double FrontScore, double BackScore, double ScoreDifference, bool IsReliable, FrontBackDebugDecision SuggestedDecision, double FrontAngleOffsetDegrees, double BackAngleOffsetDegrees, double FrontAlternativeScore, double BackAlternativeScore, double CurrentSignal, double TemplateSignal, double SearchRangeDegrees, string Message);

	private sealed record ContourSampleMirrorFaceComputation(double FrontScore, double BackScore, double ScoreDifference, bool IsReliable, FrontBackDebugDecision SuggestedDecision, int SampleCount, double MinimumScoreDifference, double CurrentSignal, double TemplateSignal, double FrontAngleOffsetDegrees, double BackAngleOffsetDegrees, string Message);

	private sealed record AutoFeatureCandidate(int Index, Mat Patch, double CenterXPixel, double CenterYPixel, double OffsetXPixel, double OffsetYPixel, double RadiusPixels, double TemplateAngleDegrees, double QualityScore);

	private sealed record AutoFeatureVote(int FeatureIndex, double AngleOffsetDegrees, double ResolvedAngleDegrees, double Score, double AlternativeScore, double QualityScore, IReadOnlyList<(double AngleDegrees, double Score)> Candidates);

	private sealed record UndistortMap(string CalibrationId, int ImageWidth, int ImageHeight, Mat MapX, Mat MapY) : IDisposable
	{
		public void Dispose()
		{
			MapX.Dispose();
			MapY.Dispose();
		}
	}

	private sealed record AutoFeatureAngleCluster(double AngleOffsetDegrees, double ResolvedAngleDegrees, double Score, double RankScore, double AlternativeScore, int SupportCount, int BestFeatureIndex);

	private sealed class VisionStageTimingBuilder
	{
		private long _prepareImageMs;

		private long _detectPartMs;

		private long _resolveAngleMs;

		private long _templateSimilarityMs;

		private long _alignmentMs;

		private long _decisionMs;

		private long _frontBackMs;

		private long _overlayMs;

		public T MeasurePrepareImage<T>(Func<T> action)
		{
			return Measure(action, ref _prepareImageMs);
		}

		public T MeasureDetectPart<T>(Func<T> action)
		{
			return Measure(action, ref _detectPartMs);
		}

		public T MeasureResolveAngle<T>(Func<T> action)
		{
			return Measure(action, ref _resolveAngleMs);
		}

		public T MeasureTemplateSimilarity<T>(Func<T> action)
		{
			return Measure(action, ref _templateSimilarityMs);
		}

		public T MeasureAlignment<T>(Func<T> action)
		{
			return Measure(action, ref _alignmentMs);
		}

		public T MeasureDecision<T>(Func<T> action)
		{
			return Measure(action, ref _decisionMs);
		}

		public T MeasureFrontBack<T>(Func<T> action)
		{
			return Measure(action, ref _frontBackMs);
		}

		public void MeasureOverlay(Action action)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			action();
			stopwatch.Stop();
			_overlayMs += stopwatch.ElapsedMilliseconds;
		}

		public VisionStageTimings Build()
		{
			return new VisionStageTimings(_prepareImageMs, _detectPartMs, _resolveAngleMs, _templateSimilarityMs, _alignmentMs, _decisionMs, _frontBackMs, _overlayMs);
		}

		private static T Measure<T>(Func<T> action, ref long elapsedMilliseconds)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			T result = action();
			stopwatch.Stop();
			elapsedMilliseconds += stopwatch.ElapsedMilliseconds;
			return result;
		}
	}

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

	private const double AutoAngleDisabledScore = 1.0;

	private const double ContourPolarMinimumRadiusSignal = 0.002;

	private const double ContourRadiusMirrorMaximumAllowedErrorPixels = 15.0;

	private const double ContourRadiusMirrorMinimumSeparationPixels = 2.5;

	private const double ContourRadiusMirrorMinimumSignalPixels = 0.5;

	private readonly object _templateAngleModelSync = new object();

	private readonly object _undistortMapSync = new object();

	private TemplateAngleModel? _cachedTemplateAngleModel;

	private AutoFeatureAngleModel? _cachedAutoFeatureAngleModel;

	private PolarRingAngleModel? _cachedPolarRingAngleModel;

	private ContourPolarAngleModel? _cachedContourPolarAngleModel;

	private UndistortMap? _cachedUndistortMap;

	private readonly PoseInvariantTemplateMatcher _templateMatcher = new PoseInvariantTemplateMatcher();

	public PartTemplate CreateTemplate(Mat image, string batchNo, string productName, VisionParameters parameters, string? imagePath = null)
	{
		ArgumentNullException.ThrowIfNull(image, "image");
		ArgumentException.ThrowIfNullOrWhiteSpace(batchNo, "batchNo");
		ArgumentException.ThrowIfNullOrWhiteSpace(productName, "productName");
		EnsureProductionCalibrationReady(image, parameters);
		PartDetection detection = DetectPart(image, parameters);
		if ((object)detection == null)
		{
			throw new InvalidOperationException("模板图片中未找到有效工件轮廓。");
		}
		MachinePoint referenceMachineCenter = parameters.CameraCalibration.PixelToMachine(detection.CenterXPixel, detection.CenterYPixel);
		double referenceAngleDegrees = ResolveTemplateReferenceAngle(detection, parameters);
		return new PartTemplate(Guid.NewGuid(), batchNo, productName, imagePath, DateTimeOffset.Now, detection.CenterXPixel, detection.CenterYPixel, referenceMachineCenter.XMm, referenceMachineCenter.YMm, parameters.CameraCalibration.CalibrationId, GetCurrentDistortionCalibrationId(parameters), referenceAngleDegrees, detection.WidthMm, detection.HeightMm, detection.AreaPixels, 1.0, parameters.Roi, parameters, detection.WidthPixels, detection.HeightPixels);
	}

	public OpenCvInspectionOutput Inspect(Mat image, PartTemplate template, VisionParameters? parameters = null, string? partNo = null, string? rawImagePath = null, bool buildDiagnosticImage = true)
	{
		ArgumentNullException.ThrowIfNull(image, "image");
		ArgumentNullException.ThrowIfNull(template, "template");
		VisionParameters activeParameters = parameters ?? template.Parameters;
		EnsureProductionSetup(image, template, activeParameters);
		VisionStageTimingBuilder stageTimings = new VisionStageTimingBuilder();
		Mat workingImage = stageTimings.MeasurePrepareImage(() => PrepareImage(image, activeParameters));
		try
		{
			Mat diagnostic = (buildDiagnosticImage ? EnsureBgr(workingImage) : new Mat());
			IReadOnlyList<ContourCandidateDiagnostic> candidateDiagnostics = Array.Empty<ContourCandidateDiagnostic>();
			IReadOnlyList<PartDetection> candidateDetections = Array.Empty<PartDetection>();
			PartDetection detection = stageTimings.MeasureDetectPart(() => DetectPartPrepared(workingImage, activeParameters, template, out candidateDiagnostics, out candidateDetections, buildDiagnosticImage));
			string resolvedPartNo = (string.IsNullOrWhiteSpace(partNo) ? DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff") : partNo);
			if ((object)detection == null)
			{
				InspectionResult result = InspectionResult.Error(template.BatchNo, resolvedPartNo, NgReason.MatchFailed, "未找到有效工件轮廓。");
				if (buildDiagnosticImage)
				{
					Cv2.PutText(diagnostic, "MATCH FAILED", new Point(24, 48), HersheyFonts.HersheySimplex, 1.2, Scalar.Red, 2);
				}
				return new OpenCvInspectionOutput(result, diagnostic, null, candidateDiagnostics, null, null, null, stageTimings.Build());
			}
			if (buildDiagnosticImage)
			{
				DrawCandidateContours(diagnostic, candidateDetections, detection);
				Point[] contourForDraw = OffsetContour(detection.Contour, detection.Offset);
				DrawOutlinedContour(diagnostic, contourForDraw, Scalar.LimeGreen, 5);
				DrawCenterMarkers(diagnostic, detection, template);
			}
			MachinePoint referenceCenter = GetReferenceCenterMachine(template, activeParameters);
			MachinePoint currentCenter = activeParameters.CameraCalibration.PixelToMachine(detection.CenterXPixel, detection.CenterYPixel);
			ResolvedAngle resolvedAngle = stageTimings.MeasureResolveAngle(() => ResolveInspectionAngle(workingImage, detection, template, activeParameters, buildDiagnosticImage));
			AngleResolutionDiagnostic angleDiagnostic = BuildAngleDiagnostic(activeParameters, detection.AngleDegrees, resolvedAngle);
			TemplateSimilarityResult similarity = stageTimings.MeasureTemplateSimilarity(() => CalculateTemplateSimilarity(workingImage, detection, template, activeParameters, resolvedAngle.AngleDegrees));
			double matchScore = similarity?.FinalScore ?? CalculateMatchScore(detection, template);
			XyrAlignmentSnapshot alignmentSnapshot = stageTimings.MeasureAlignment(() => XyrAlignmentSolver.Solve(new PartPose2D(currentCenter.XMm, currentCenter.YMm, resolvedAngle.AngleDegrees, matchScore), new PartPose2D(referenceCenter.XMm, referenceCenter.YMm, template.ReferenceAngleDegrees, template.MatchScoreBaseline), activeParameters.RAxisCenterCalibration, (!activeParameters.InvertRotationCompensation) ? 1 : (-1), resolvedAngle.AllowsFullRotation));
			double angleOffset = alignmentSnapshot.AngleOffsetDegrees;
			double rotateCompensation = alignmentSnapshot.HomeRActionDegrees;
			NgReason reason = NgReason.None;
			string message = string.Empty;
			InspectionDecision decision = stageTimings.MeasureDecision(() => DecideSingleShot(angleOffset, rotateCompensation, activeParameters.AngleToleranceDegrees, activeParameters.ShapeScoreThreshold, angleDiagnostic, similarity, matchScore, out reason, out message));
			NgReason finalReason = reason;
			string finalMessage = message;
			ContourSampleMirrorFaceDebugResult contourSampleMirrorDecisionDiagnostic = stageTimings.MeasureFrontBack(() => TryApplyContourSampleMirrorBackSideNg(detection, resolvedAngle.AngleDegrees, template, activeParameters, ref decision, ref finalReason, ref finalMessage));
			reason = finalReason;
			message = finalMessage;
			double rotateCompensationForOutput = ((decision == InspectionDecision.Ok) ? rotateCompensation : 0.0);
			MachinePoint xyCompensation = ((decision == InspectionDecision.Ok) ? new MachinePoint(alignmentSnapshot.HomeXActionMm, alignmentSnapshot.HomeYActionMm) : new MachinePoint(0.0, 0.0));
			InspectionMeasurement measurement = new InspectionMeasurement(detection.CenterXPixel, detection.CenterYPixel, alignmentSnapshot.XOffsetMm, alignmentSnapshot.YOffsetMm, xyCompensation.XMm, xyCompensation.YMm, resolvedAngle.AngleDegrees, angleOffset, rotateCompensationForOutput, detection.WidthMm, detection.HeightMm, detection.AreaPixels, matchScore);
			if (buildDiagnosticImage)
			{
				stageTimings.MeasureOverlay(delegate
				{
					DrawOverlay(diagnostic, decision, message, measurement, angleDiagnostic, similarity);
				});
			}
			return new OpenCvInspectionOutput(InspectionResult.FromMeasurement(template.BatchNo, resolvedPartNo, decision, reason, message, measurement, rawImagePath), diagnostic, alignmentSnapshot, candidateDiagnostics, angleDiagnostic, similarity, contourSampleMirrorDecisionDiagnostic, stageTimings.Build());
		}
		finally
		{
			if (workingImage != null)
			{
				((IDisposable)workingImage).Dispose();
			}
		}
	}

	public PartDetection? DetectPart(Mat image, VisionParameters parameters)
	{
		using Mat workingImage = PrepareImage(image, parameters);
		IReadOnlyList<ContourCandidateDiagnostic> candidateDiagnostics;
		IReadOnlyList<PartDetection> candidateDetections;
		return DetectPartPrepared(workingImage, parameters, null, out candidateDiagnostics, out candidateDetections, buildDiagnostics: false);
	}

	public FrontBackDebugResult? AnalyzeFrontBackDebug(Mat image, PartTemplate template, VisionParameters? parameters = null, string? fixedOverlayDiagnosticPath = null)
	{
		ArgumentNullException.ThrowIfNull(image, "image");
		ArgumentNullException.ThrowIfNull(template, "template");
		VisionParameters activeParameters = parameters ?? template.Parameters;
		if (!HasTemplatePixelShape(template))
		{
			return new FrontBackDebugResult(0.0, 0.0, 0.0, IsReliable: false, FrontBackDebugDecision.Unavailable, "-", "-", "模板缺少已保存的像素形状，无法计算正反面调试分数。");
		}
		using Mat workingImage = PrepareImage(image, activeParameters);
		IReadOnlyList<ContourCandidateDiagnostic> candidateDiagnostics;
		IReadOnlyList<PartDetection> candidateDetections;
		PartDetection detection = DetectPartPrepared(workingImage, activeParameters, template, out candidateDiagnostics, out candidateDetections);
		if ((object)detection == null)
		{
			return new FrontBackDebugResult(0.0, 0.0, 0.0, IsReliable: false, FrontBackDebugDecision.Unavailable, "-", "-", "未找到有效工件轮廓，无法计算正反面调试分数。");
		}
		ResolvedAngle resolvedAngle = ResolveInspectionAngle(workingImage, detection, template, activeParameters);
		ContourMirrorFaceDebugComputation contourMirror = AnalyzeContourMirrorFaceDebug(detection, template, activeParameters);
		FrontBackDebugResult debug = _templateMatcher.CheckFrontBackDebug(workingImage, detection, template, activeParameters, resolvedAngle.AngleDegrees, contourMirror?.BackAngleOffsetDegrees, fixedOverlayDiagnosticPath);
		if ((object)debug == null)
		{
			return null;
		}
		return debug with
		{
			ContourMirror = (((object)contourMirror == null) ? null : ToContourMirrorFaceDebugResult(contourMirror))
		};
	}

	public void WarmupProductionTemplate(PartTemplate template, VisionParameters parameters)
	{
		ArgumentNullException.ThrowIfNull(template, "template");
		ArgumentNullException.ThrowIfNull(parameters, "parameters");
		ProductionSetupDecision setup = ValidateProductionSetup(template, parameters);
		if (!setup.IsReady)
		{
			throw new InvalidOperationException(GetProductionSetupBlockMessage(setup.Reason));
		}
		switch (parameters.AngleDetectionMode)
		{
		case AngleDetectionMode.AutoPcaOrPolarRing:
			_ = ((object)TryGetContourPolarAngleModel(template, parameters)) ?? ((object)TryGetPolarRingAngleModel(template, parameters));
			break;
		case AngleDetectionMode.AutoFeatureRotation:
			TryGetAutoFeatureAngleModel(template, parameters);
			break;
		case AngleDetectionMode.PolarRingRotation:
			TryGetPolarRingAngleModel(template, parameters);
			break;
		case AngleDetectionMode.TemplateRotation:
			TryGetTemplateAngleModel(template, parameters);
			break;
		default:
			break;
		}
		WarmupUndistortMap(parameters);
		_templateMatcher.Warmup(template, parameters);
	}

	private void WarmupUndistortMap(VisionParameters parameters)
	{
		LensDistortionCalibration calibration = parameters.LensDistortionCalibration;
		if (calibration.CanApplyTo(calibration.ImageWidth, calibration.ImageHeight))
		{
			GetUndistortMap(calibration, calibration.ImageWidth, calibration.ImageHeight);
		}
	}

	public Mat PrepareImage(Mat image, VisionParameters parameters)
	{
		ArgumentNullException.ThrowIfNull(image, "image");
		if (!parameters.LensDistortionCalibration.CanApplyTo(image.Width, image.Height))
		{
			return image.Clone();
		}
		return Undistort(image, parameters.LensDistortionCalibration);
	}

	private static PartDetection? DetectPartPrepared(Mat image, VisionParameters parameters, PartTemplate? template, out IReadOnlyList<ContourCandidateDiagnostic> candidateDiagnostics, out IReadOnlyList<PartDetection> candidateDetections, bool buildDiagnostics = true)
	{
		Point offset;
		using Mat roiImage = ExtractRoi(image, parameters.Roi, out offset);
		using Mat gray = ToGray(roiImage);
		using Mat blurred = Blur(gray, parameters.BlurKernelSize);
		using Mat binary = Threshold(blurred, parameters.BinaryThreshold);
		using Mat inverted = new Mat();
		Cv2.BitwiseNot(binary, inverted);
		List<CandidateDetection> candidates = FindDetectionCandidates(binary, "binary", offset, parameters).Concat(FindDetectionCandidates(inverted, "inverted", offset, parameters)).ToList();
		CandidateDetection selected = SelectDetectionCandidate(candidates, template);
		IReadOnlyList<ContourCandidateDiagnostic> readOnlyList2;
		if (!buildDiagnostics)
		{
			IReadOnlyList<ContourCandidateDiagnostic> readOnlyList = Array.Empty<ContourCandidateDiagnostic>();
			readOnlyList2 = readOnlyList;
		}
		else
		{
			readOnlyList2 = BuildCandidateDiagnostics(candidates, template, selected);
		}
		candidateDiagnostics = readOnlyList2;
		candidateDetections = (buildDiagnostics ? candidates.Select((CandidateDetection candidate) => candidate.Detection).ToArray() : Array.Empty<PartDetection>());
		return selected?.Detection;
	}

	private Mat Undistort(Mat image, LensDistortionCalibration calibration)
	{
		UndistortMap map = GetUndistortMap(calibration, image.Width, image.Height);
		Mat corrected = new Mat();
		Cv2.Remap(image, corrected, map.MapX, map.MapY);
		return corrected;
	}

	private UndistortMap GetUndistortMap(LensDistortionCalibration calibration, int imageWidth, int imageHeight)
	{
		lock (_undistortMapSync)
		{
			UndistortMap cached = _cachedUndistortMap;
			if ((object)cached != null && cached.ImageWidth == imageWidth && cached.ImageHeight == imageHeight && string.Equals(cached.CalibrationId, calibration.CalibrationId, StringComparison.Ordinal))
			{
				return cached;
			}
			_cachedUndistortMap?.Dispose();
			_cachedUndistortMap = BuildUndistortMap(calibration, imageWidth, imageHeight);
			return _cachedUndistortMap;
		}
	}

	private static UndistortMap BuildUndistortMap(LensDistortionCalibration calibration, int imageWidth, int imageHeight)
	{
		using Mat<double> cameraMatrix = Mat.FromArray(new double[3, 3]
		{
			{
				calibration.CameraMatrix[0],
				calibration.CameraMatrix[1],
				calibration.CameraMatrix[2]
			},
			{
				calibration.CameraMatrix[3],
				calibration.CameraMatrix[4],
				calibration.CameraMatrix[5]
			},
			{
				calibration.CameraMatrix[6],
				calibration.CameraMatrix[7],
				calibration.CameraMatrix[8]
			}
		});
		using Mat<double> distortion = Mat.FromArray(calibration.DistortionCoefficients);
		Mat mapX = new Mat();
		Mat mapY = new Mat();
		Cv2.InitUndistortRectifyMap(cameraMatrix, distortion, new Mat(), cameraMatrix, new Size(imageWidth, imageHeight), MatType.CV_32FC1, mapX, mapY);
		return new UndistortMap(calibration.CalibrationId, imageWidth, imageHeight, mapX, mapY);
	}

	private static InspectionDecision DecideSingleShot(double angleOffset, double rotationCompensationDegrees, double angleToleranceDegrees, double shapeScoreThreshold, AngleResolutionDiagnostic angleDiagnostic, TemplateSimilarityResult? similarity, double matchScore, out NgReason reason, out string message)
	{
		if (!angleDiagnostic.IsReliable)
		{
			reason = NgReason.MatchFailed;
			message = "角度NG: " + angleDiagnostic.Message;
			return InspectionDecision.Ng;
		}
		if (((object)similarity != null && similarity.IsReliable && !similarity.IsSamePart) || ((object)similarity == null && matchScore < shapeScoreThreshold))
		{
			reason = NgReason.ShapeOutOfTolerance;
			message = (((object)similarity == null) ? $"轮廓NG: 分数={matchScore:F3}，低于阈值{shapeScoreThreshold:F3}。" : ("轮廓NG: " + similarity.Message + "。"));
			return InspectionDecision.Ng;
		}
		if (angleDiagnostic.Source == "auto-angle-disabled")
		{
			reason = NgReason.None;
			message = "OK，自动判断不需要R角度，X/Y输出有效，R=0";
			return InspectionDecision.Ok;
		}
		if (!AngleMath.IsAngleWithinTolerance(angleOffset, angleToleranceDegrees))
		{
			reason = NgReason.None;
			message = $"OK，X/Y/R输出有效: R={rotationCompensationDegrees:F3}deg";
			return InspectionDecision.Ok;
		}
		reason = NgReason.None;
		message = "OK，X/Y/R输出有效";
		return InspectionDecision.Ok;
	}

	private ContourSampleMirrorFaceDebugResult? TryApplyContourSampleMirrorBackSideNg(PartDetection detection, double resolvedAngleDegrees, PartTemplate template, VisionParameters parameters, ref InspectionDecision decision, ref NgReason reason, ref string message)
	{
		if (!parameters.BackSideNgEnabled || decision != InspectionDecision.Ok)
		{
			return null;
		}
		ContourSampleMirrorFaceComputation computation = AnalyzeContourSampleMirrorFace(detection, resolvedAngleDegrees, template, parameters);
		if ((object)computation == null)
		{
			computation = new ContourSampleMirrorFaceComputation(0.0, 0.0, 0.0, IsReliable: false, FrontBackDebugDecision.Unavailable, 0, ContourRadiusMirrorMinimumSeparationPixels, 0.0, 0.0, 0.0, 0.0, "反面NG: 外轮廓半径模型不可用，请重建当前型号模板。");
		}
		if (computation.SuggestedDecision != FrontBackDebugDecision.Front)
		{
			decision = InspectionDecision.Ng;
			reason = NgReason.BackSideDetected;
			message = computation.Message;
		}
		return ToContourSampleMirrorFaceDebugResult(computation);
	}

	private static ContourMirrorFaceDebugResult ToContourMirrorFaceDebugResult(ContourMirrorFaceDebugComputation computation)
	{
		return new ContourMirrorFaceDebugResult(computation.FrontScore, computation.BackScore, computation.ScoreDifference, computation.IsReliable, computation.SuggestedDecision, computation.FrontAngleOffsetDegrees, computation.BackAngleOffsetDegrees, computation.FrontAlternativeScore, computation.BackAlternativeScore, computation.CurrentSignal, computation.TemplateSignal, computation.SearchRangeDegrees, computation.Message);
	}

	private static ContourSampleMirrorFaceDebugResult ToContourSampleMirrorFaceDebugResult(ContourSampleMirrorFaceComputation computation)
	{
		return new ContourSampleMirrorFaceDebugResult(computation.FrontScore, computation.BackScore, computation.ScoreDifference, computation.IsReliable, computation.SuggestedDecision, computation.SampleCount, computation.MinimumScoreDifference, computation.CurrentSignal, computation.TemplateSignal, computation.FrontAngleOffsetDegrees, computation.BackAngleOffsetDegrees, computation.Message);
	}

	private ContourSampleMirrorFaceComputation? AnalyzeContourSampleMirrorFace(PartDetection detection, double resolvedAngleDegrees, PartTemplate template, VisionParameters parameters)
	{
		ContourPolarAngleModel model = TryGetContourPolarAngleModel(template, parameters);
		if ((object)model == null)
		{
			return null;
		}
		float[] currentSignature = BuildContourRadiusSignature(detection, smooth: true);
		float[] templateSignature = model.RadiusSignature;
		float[] mirroredTemplateSignature = MirrorCircularSignature(templateSignature);
		double currentSignal = CalculateRadiusSignalPixels(currentSignature);
		double templateSignal = model.RadiusSignal;
		if (currentSignal < ContourRadiusMirrorMinimumSignalPixels || templateSignal < ContourRadiusMirrorMinimumSignalPixels)
		{
			return new ContourSampleMirrorFaceComputation(0.0, 0.0, 0.0, IsReliable: false, FrontBackDebugDecision.Uncertain, currentSignature.Length, ContourRadiusMirrorMinimumSeparationPixels, currentSignal, templateSignal, 0.0, 0.0, $"反面NG: 外轮廓半径变化过弱，当前={currentSignal:F3}px，模板={templateSignal:F3}px，无法稳定区分正反面。");
		}
		var front = SearchCircularRadiusError(currentSignature, templateSignature);
		var back = SearchCircularRadiusError(currentSignature, mirroredTemplateSignature);
		double frontAngleOffset = 0.0 - ShiftToSignedDegrees(front.Shift, currentSignature.Length);
		double backAngleOffset = 0.0 - ShiftToSignedDegrees(back.Shift, currentSignature.Length);
		double difference = back.ErrorPixels - front.ErrorPixels;
		double minimumDifference = ContourRadiusMirrorMinimumSeparationPixels;
		double bestError = Math.Min(front.ErrorPixels, back.ErrorPixels);
		double separation = Math.Abs(difference);
		bool isReliable = bestError <= ContourRadiusMirrorMaximumAllowedErrorPixels && separation >= minimumDifference;
		FrontBackDebugDecision suggestedDecision = isReliable ? ((difference > 0.0) ? FrontBackDebugDecision.Front : FrontBackDebugDecision.Back) : FrontBackDebugDecision.Uncertain;
		string message = suggestedDecision switch
		{
			FrontBackDebugDecision.Front => $"外轮廓半径正反面OK: 正面误差={front.ErrorPixels:F2}px, 镜像误差={back.ErrorPixels:F2}px, 分离={separation:F2}px, 最大允许误差={ContourRadiusMirrorMaximumAllowedErrorPixels:F2}px, 分离阈值={minimumDifference:F2}px。", 
			FrontBackDebugDecision.Back => $"反面NG: 外轮廓半径更接近镜像模板，正面误差={front.ErrorPixels:F2}px, 镜像误差={back.ErrorPixels:F2}px, 分离={separation:F2}px, 分离阈值={minimumDifference:F2}px。", 
			_ when bestError > ContourRadiusMirrorMaximumAllowedErrorPixels => $"反面NG: 外轮廓半径匹配误差过大，正面误差={front.ErrorPixels:F2}px, 镜像误差={back.ErrorPixels:F2}px, 最大允许误差={ContourRadiusMirrorMaximumAllowedErrorPixels:F2}px。请检查轮廓提取、遮挡或工件变形。", 
			_ => $"反面NG: 外轮廓半径判断不确定，正面误差={front.ErrorPixels:F2}px, 镜像误差={back.ErrorPixels:F2}px, 分离={separation:F2}px, 分离阈值={minimumDifference:F2}px。", 
		};
		return new ContourSampleMirrorFaceComputation(front.ErrorPixels, back.ErrorPixels, difference, isReliable, suggestedDecision, currentSignature.Length, minimumDifference, currentSignal, templateSignal, frontAngleOffset, backAngleOffset, message);
	}

	private static IEnumerable<CandidateDetection> FindDetectionCandidates(Mat binary, string source, Point offset, VisionParameters parameters)
	{
		Cv2.FindContours(binary, out Point[][] contours, out HierarchyIndex[] _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
		if (contours.Length == 0)
		{
			return Array.Empty<CandidateDetection>();
		}
		int imageArea = binary.Width * binary.Height;
		return from contour in contours
			select new
			{
				Contour = contour,
				Area = Cv2.ContourArea(contour)
			} into x
			where x.Area >= parameters.MinPartAreaPixels
			where x.Area <= parameters.MaxPartAreaPixels
			where x.Area <= (double)imageArea * 0.95
			select new CandidateDetection(source, CreateDetection(x.Contour, x.Area, offset, parameters));
	}

	private static CandidateDetection? SelectDetectionCandidate(IReadOnlyCollection<CandidateDetection> candidates, PartTemplate? template)
	{
		if (candidates.Count == 0)
		{
			return null;
		}
		if ((object)template == null)
		{
			return candidates.OrderByDescending((CandidateDetection candidate) => candidate.Detection.AreaPixels).First();
		}
		return (from candidate in candidates
			select new
			{
				Candidate = candidate,
				Score = CalculateMatchScore(candidate.Detection, template),
				CenterDistancePixels = DistanceToTemplateCenterPixels(candidate.Detection, template)
			} into candidate
			orderby candidate.Score descending, candidate.CenterDistancePixels, candidate.Candidate.Detection.AreaPixels descending
			select candidate.Candidate).First();
	}

	private static IReadOnlyList<ContourCandidateDiagnostic> BuildCandidateDiagnostics(IReadOnlyList<CandidateDetection> candidates, PartTemplate? template, CandidateDetection? selected)
	{
		ContourCandidateDiagnostic[] diagnostics = candidates.Select(delegate(CandidateDetection candidate, int index)
		{
			PartDetection detection = candidate.Detection;
			return new ContourCandidateDiagnostic(0, index + 1, candidate.Source, (object)candidate == selected, ((object)template == null) ? 0.0 : CalculateMatchScore(detection, template), detection.CenterXPixel, detection.CenterYPixel, detection.WidthPixels, detection.HeightPixels, detection.WidthMm, detection.HeightMm, detection.AreaPixels, CalculateFillRatio(detection.AreaPixels, detection.WidthPixels, detection.HeightPixels), ((object)template == null) ? 0.0 : DistanceToTemplateCenterPixels(detection, template));
		}).ToArray();
		return (((object)template == null) ? diagnostics.OrderByDescending((ContourCandidateDiagnostic candidate) => candidate.AreaPixels) : (from candidate in diagnostics
			orderby candidate.Score descending, candidate.CenterDistancePixels, candidate.AreaPixels descending
			select candidate)).Select((ContourCandidateDiagnostic candidate, int rank) => candidate with
		{
			Rank = rank + 1
		}).ToArray();
	}

	private static PartDetection CreateDetection(Point[] contour, double area, Point offset, VisionParameters parameters)
	{
		RotatedRect shape = MeasurePartShape(contour);
		float widthPixels = Math.Max(shape.Size.Width, shape.Size.Height);
		float heightPixels = Math.Min(shape.Size.Width, shape.Size.Height);
		(double, double) sizeMm = CalculateSizeMm(shape, offset, parameters, widthPixels, heightPixels);
		float centerXPixel = shape.Center.X + (float)offset.X;
		float centerYPixel = shape.Center.Y + (float)offset.Y;
		return new PartDetection(contour, offset, centerXPixel, centerYPixel, NormalizeMajorAxisAngle(shape), widthPixels, heightPixels, sizeMm.Item1, sizeMm.Item2, area);
	}

	private static Mat ExtractRoi(Mat image, ImageRoi roi, out Point offset)
	{
		if (roi.IsEmpty)
		{
			offset = new Point(0, 0);
			return image.Clone();
		}
		Rect rect = new Rect(Math.Clamp(roi.X, 0, image.Width - 1), Math.Clamp(roi.Y, 0, image.Height - 1), Math.Clamp(roi.Width, 1, image.Width - roi.X), Math.Clamp(roi.Height, 1, image.Height - roi.Y));
		offset = new Point(rect.X, rect.Y);
		return new Mat(image, rect).Clone();
	}

	private static Mat ToGray(Mat image)
	{
		if (image.Channels() == 1)
		{
			return image.Clone();
		}
		Mat gray = new Mat();
		Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
		return gray;
	}

	private static Mat Blur(Mat gray, int kernelSize)
	{
		int normalizedKernel = ((kernelSize < 3) ? 3 : kernelSize);
		if (normalizedKernel % 2 == 0)
		{
			normalizedKernel++;
		}
		Mat blurred = new Mat();
		Cv2.GaussianBlur(gray, blurred, new Size(normalizedKernel, normalizedKernel), 0.0);
		return blurred;
	}

	private static Mat Threshold(Mat gray, int binaryThreshold)
	{
		Mat binary = new Mat();
		if (binaryThreshold <= 0)
		{
			Cv2.Threshold(gray, binary, 0.0, 255.0, ThresholdTypes.Otsu);
		}
		else
		{
			Cv2.Threshold(gray, binary, binaryThreshold, 255.0, ThresholdTypes.Binary);
		}
		Cv2.MorphologyEx(binary, binary, MorphTypes.Close, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));
		return binary;
	}

	private static Mat EnsureBgr(Mat image)
	{
		if (image.Channels() == 3)
		{
			return image.Clone();
		}
		Mat bgr = new Mat();
		Cv2.CvtColor(image, bgr, ColorConversionCodes.GRAY2BGR);
		return bgr;
	}

	private static RotatedRect MeasurePartShape(Point[] contour)
	{
		return Cv2.MinAreaRect(contour);
	}

	private static (double WidthMm, double HeightMm) CalculateSizeMm(RotatedRect shape, Point offset, VisionParameters parameters, double widthPixels, double heightPixels)
	{
		if (!parameters.CameraCalibration.Enabled)
		{
			return (WidthMm: widthPixels, HeightMm: heightPixels);
		}
		MachinePoint[] points = (from point in shape.Points()
			select parameters.CameraCalibration.PixelToMachine(point.X + (float)offset.X, point.Y + (float)offset.Y)).ToArray();
		double sideA = (Distance(points[0], points[1]) + Distance(points[2], points[3])) / 2.0;
		double sideB = (Distance(points[1], points[2]) + Distance(points[3], points[0])) / 2.0;
		return (WidthMm: Math.Max(sideA, sideB), HeightMm: Math.Min(sideA, sideB));
	}

	private static double Distance(MachinePoint a, MachinePoint b)
	{
		double num = a.XMm - b.XMm;
		double dy = a.YMm - b.YMm;
		return Math.Sqrt(num * num + dy * dy);
	}

	private static double NormalizeMajorAxisAngle(RotatedRect rect)
	{
		double angle = rect.Angle;
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
			return new ContourAngleProfile(detection.AngleDegrees, 1.0, 0.0, IsPcaReliable: false);
		}
		double meanX = ((IEnumerable<Point>)detection.Contour).Average((Func<Point, double>)((Point point2) => point2.X));
		double meanY = ((IEnumerable<Point>)detection.Contour).Average((Func<Point, double>)((Point point2) => point2.Y));
		double covarianceXx = 0.0;
		double covarianceXy = 0.0;
		double covarianceYy = 0.0;
		Point[] contour = detection.Contour;
		for (int num = 0; num < contour.Length; num++)
		{
			Point point = contour[num];
			double dx = (double)point.X - meanX;
			double dy = (double)point.Y - meanY;
			covarianceXx += dx * dx;
			covarianceXy += dx * dy;
			covarianceYy += dy * dy;
		}
		covarianceXx /= (double)detection.Contour.Length;
		covarianceXy /= (double)detection.Contour.Length;
		covarianceYy /= (double)detection.Contour.Length;
		double num2 = covarianceXx + covarianceYy;
		double delta = Math.Sqrt(Math.Max((covarianceXx - covarianceYy) * (covarianceXx - covarianceYy) + 4.0 * covarianceXy * covarianceXy, 0.0));
		double major = (num2 + delta) / 2.0;
		double minor = Math.Max((num2 - delta) / 2.0, 1E-06);
		double pcaRatio = major / minor;
		double angleDegrees = Math.Atan2(2.0 * covarianceXy, covarianceXx - covarianceYy) * 90.0 / Math.PI;
		double perimeter = Cv2.ArcLength(detection.Contour, closed: true);
		return new ContourAngleProfile(Circularity: (perimeter > 0.0001) ? Math.Clamp(Math.PI * 4.0 * detection.AreaPixels / (perimeter * perimeter), 0.0, 1.0) : 0.0, PcaAngleDegrees: AngleMath.NormalizeDegrees180(angleDegrees), PcaRatio: pcaRatio, IsPcaReliable: pcaRatio >= 1.12);
	}

	private static double CalculatePcaAngleScore(double pcaRatio)
	{
		return Math.Clamp((pcaRatio - 1.0) / 0.35, 0.0, 1.0);
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
		ContourAngleProfile profile = CalculateContourAngleProfile(detection);
		if (!profile.IsPcaReliable)
		{
			return detection.AngleDegrees;
		}
		return profile.PcaAngleDegrees;
	}

	private static double CalculateMatchScore(PartDetection detection, PartTemplate template)
	{
		double widthRatio = Math.Abs(detection.WidthMm - template.WidthMm) / Math.Max(template.WidthMm, 0.0001);
		double heightRatio = Math.Abs(detection.HeightMm - template.HeightMm) / Math.Max(template.HeightMm, 0.0001);
		double areaRatio = Math.Abs(detection.AreaPixels - template.AreaPixels) / Math.Max(template.AreaPixels, 0.0001);
		if (!HasTemplatePixelShape(template))
		{
			double legacyPenalty = widthRatio * 0.4 + heightRatio * 0.4 + areaRatio * 0.2;
			return Math.Clamp(1.0 - legacyPenalty, 0.0, 1.0);
		}
		double num = CalculateFillRatio(detection.AreaPixels, detection.WidthPixels, detection.HeightPixels);
		double templateFillRatio = CalculateFillRatio(template.AreaPixels, template.ReferenceWidthPixels, template.ReferenceHeightPixels);
		double fillRatioDiff = Math.Abs(num - templateFillRatio) / Math.Max(templateFillRatio, 0.0001);
		double penalty = widthRatio * 0.3 + heightRatio * 0.3 + areaRatio * 0.2 + fillRatioDiff * 0.2;
		return Math.Clamp(1.0 - penalty, 0.0, 1.0);
	}

	private TemplateSimilarityResult? CalculateTemplateSimilarity(Mat preparedImage, PartDetection detection, PartTemplate template, VisionParameters parameters, double resolvedAngleDegrees)
	{
		if (!HasTemplatePixelShape(template))
		{
			return null;
		}
		return _templateMatcher.TryCompare(preparedImage, detection, template, parameters, resolvedAngleDegrees);
	}

	private ResolvedAngle ResolveInspectionAngle(Mat preparedImage, PartDetection detection, PartTemplate template, VisionParameters parameters, bool buildDiagnostics = true)
	{
		if (parameters.AngleDetectionMode == AngleDetectionMode.AutoPcaOrPolarRing)
		{
			return ResolveAutoPcaOrPolarRingAngle(preparedImage, detection, template, parameters, buildDiagnostics);
		}
		if (parameters.AngleDetectionMode == AngleDetectionMode.OuterContour)
		{
			return new ResolvedAngle(detection.AngleDegrees, AllowsFullRotation: false, "outer-contour", 1.0, 0.0, 180.0);
		}
		if (parameters.AngleDetectionMode == AngleDetectionMode.AutoFeatureRotation)
		{
			if (string.IsNullOrWhiteSpace(template.ImagePath))
			{
				return new ResolvedAngle(detection.AngleDegrees, AllowsFullRotation: false, "outer-contour-no-template-image", 0.0, 0.0, 180.0);
			}
			AutoFeatureAngleModel autoModel = TryGetAutoFeatureAngleModel(template, parameters);
			if ((object)autoModel != null)
			{
				IReadOnlyList<AutoFeatureCandidate> features = autoModel.Features;
				if (features != null && features.Count > 0)
				{
					return MatchAutoFeatureRotation(preparedImage, detection, autoModel, template.ReferenceAngleDegrees, parameters, buildDiagnostics);
				}
			}
			return new ResolvedAngle(detection.AngleDegrees, AllowsFullRotation: true, "auto-feature-rotation-no-feature", 0.0, 0.0, Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0), Array.Empty<AngleCandidateDiagnostic>());
		}
		if (parameters.AngleDetectionMode == AngleDetectionMode.PolarRingRotation)
		{
			if (string.IsNullOrWhiteSpace(template.ImagePath))
			{
				return new ResolvedAngle(detection.AngleDegrees, AllowsFullRotation: false, "outer-contour-no-template-image", 0.0, 0.0, 180.0);
			}
			PolarRingAngleModel polarModel = TryGetPolarRingAngleModel(template, parameters);
			if ((object)polarModel != null)
			{
				return MatchPolarRingRotation(preparedImage, detection, polarModel, template.ReferenceAngleDegrees, parameters, buildDiagnostics);
			}
			return new ResolvedAngle(detection.AngleDegrees, AllowsFullRotation: true, "polar-ring-rotation-no-signal", 0.0, 0.0, Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0), Array.Empty<AngleCandidateDiagnostic>());
		}
		TemplateAngleModel model = TryGetTemplateAngleModel(template, parameters);
		if ((object)model == null)
		{
			return new ResolvedAngle(detection.AngleDegrees, AllowsFullRotation: false, (parameters.AngleDetectionMode == AngleDetectionMode.AutoFeatureRotation) ? "outer-contour-no-auto-feature" : "outer-contour-no-template-image", 0.0, 0.0, 180.0);
		}
		return MatchTemplateRotation(preparedImage, detection, model, template.ReferenceAngleDegrees, parameters, buildDiagnostics);
	}

	private ResolvedAngle ResolveAutoPcaOrPolarRingAngle(Mat preparedImage, PartDetection detection, PartTemplate template, VisionParameters parameters, bool buildDiagnostics)
	{
		ContourAngleProfile profile = CalculateContourAngleProfile(detection);
		string profileDetail = FormatContourAngleProfile(profile);
		ContourPolarAngleModel contourModel = TryGetContourPolarAngleModel(template, parameters);
		ContourAngleProfile strategyProfile = contourModel?.Profile ?? profile;
		AutoAngleStrategyDecision strategy = AutoAngleStrategy.Select(
			template.ReferenceWidthPixels > 0.0001 ? template.ReferenceWidthPixels : detection.WidthPixels,
			template.ReferenceHeightPixels > 0.0001 ? template.ReferenceHeightPixels : detection.HeightPixels,
			strategyProfile.PcaRatio,
			strategyProfile.Circularity,
			contourModel?.RadiusSignal ?? 0.0);
		string strategyDetail = FormatAutoAngleStrategyDetail(strategy, profileDetail);
		if (strategy.Method == AutoAngleMethod.Disabled)
		{
			return new ResolvedAngle(template.ReferenceAngleDegrees, AllowsFullRotation: false, "auto-angle-disabled", AutoAngleDisabledScore, 0.0, 0.0, null, strategyDetail);
		}

		if (strategy.Method == AutoAngleMethod.PcaAxis)
		{
			return new ResolvedAngle(profile.PcaAngleDegrees, AllowsFullRotation: false, "auto-pca-contour", CalculatePcaAngleScore(profile.PcaRatio), 0.0, 180.0, null, strategyDetail);
		}

		if ((object)contourModel != null)
		{
			return MatchContourPolarRotation(detection, contourModel, template.ReferenceAngleDegrees, parameters, strategyDetail, buildDiagnostics);
		}
		PolarRingAngleModel polarModel = TryGetPolarRingAngleModel(template, parameters);
		if ((object)polarModel != null)
		{
			ResolvedAngle polarResult = MatchPolarRingRotation(preparedImage, detection, polarModel, template.ReferenceAngleDegrees, parameters, buildDiagnostics);
			return polarResult with
			{
				Source = ((polarResult.Source == "polar-ring-rotation") ? "auto-pca-polar-ring" : polarResult.Source),
				Detail = strategyDetail
			};
		}
		return new ResolvedAngle(template.ReferenceAngleDegrees, AllowsFullRotation: false, "auto-angle-disabled", AutoAngleDisabledScore, 0.0, 0.0, null, strategyDetail + " 未找到可用轮廓方向模型，R锁定为0。");
	}

	private static string FormatAutoAngleStrategyDetail(AutoAngleStrategyDecision strategy, string profileDetail)
	{
		return $"{strategy.Message} {profileDetail}; shape={strategy.ShapeClass}; method={strategy.Method}; axisRatio={strategy.AxisRatio:F3}; contourRadiusSignal={strategy.TemplateRadiusSignalPixels:F3}px";
	}

	private ContourMirrorFaceDebugComputation? AnalyzeContourMirrorFaceDebug(PartDetection detection, PartTemplate template, VisionParameters parameters)
	{
		ContourPolarAngleModel model = TryGetContourPolarAngleModel(template, parameters);
		if ((object)model == null)
		{
			return null;
		}
		float[] currentSignature = BuildContourPolarSignature(detection);
		double currentSignal = CalculateSignatureSignal(currentSignature);
		if (currentSignal < 0.002)
		{
			return new ContourMirrorFaceDebugComputation(0.0, 0.0, 0.0, IsReliable: false, FrontBackDebugDecision.Unavailable, 0.0, 0.0, 0.0, 0.0, currentSignal, model.Signal, Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0), $"轮廓镜像调试不可用: 当前轮廓信号={currentSignal:F3}。");
		}
		double searchRange = Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0);
		(double, double, double, IReadOnlyList<AngleCandidateDiagnostic>) front = SearchPolarRingAngle(currentSignature, model.Signature, template.ReferenceAngleDegrees, searchRange);
		(double, double, double, IReadOnlyList<AngleCandidateDiagnostic>) back = SearchPolarRingAngle(MirrorCircularSignature(currentSignature), model.Signature, template.ReferenceAngleDegrees, searchRange);
		double difference = front.Item2 - back.Item2;
		bool isReliable = Math.Max(front.Item2, back.Item2) >= 0.58 && Math.Abs(difference) >= 0.06;
		FrontBackDebugDecision suggestedDecision = ((!isReliable) ? FrontBackDebugDecision.Uncertain : ((difference > 0.0) ? FrontBackDebugDecision.Front : FrontBackDebugDecision.Back));
		string message = $"轮廓镜像正反面调试: 正面={front.Item2:F3} 角度={front.Item1:F3}deg, 反面={back.Item2:F3} 角度={back.Item1:F3}deg, 分差={difference:F3}, 可靠={isReliable}, 分数阈值>={0.58:F3}, 分差阈值>={0.06:F3}。 " + "启用反面NG时，分差<0判为反面NG。";
		return new ContourMirrorFaceDebugComputation(front.Item2, back.Item2, difference, isReliable, suggestedDecision, front.Item1, back.Item1, front.Item3, back.Item3, currentSignal, model.Signal, searchRange, message);
	}

	private AngleResolutionDiagnostic BuildAngleDiagnostic(VisionParameters parameters, double contourAngleDegrees, ResolvedAngle resolvedAngle)
	{
		bool num = resolvedAngle.Source.StartsWith("template-rotation", StringComparison.Ordinal) || resolvedAngle.Source.StartsWith("auto-feature-rotation", StringComparison.Ordinal) || resolvedAngle.Source.StartsWith("polar-ring-rotation", StringComparison.Ordinal) || resolvedAngle.Source.StartsWith("auto-pca-polar-ring", StringComparison.Ordinal) || resolvedAngle.Source.StartsWith("auto-pca-contour-polar", StringComparison.Ordinal);
		bool hasSeparatedAutoFeatureMatch = resolvedAngle.Source.StartsWith("auto-feature-rotation", StringComparison.Ordinal) && resolvedAngle.Score >= GetSeparatedAutoFeatureMinimumScore(parameters) && resolvedAngle.Score - resolvedAngle.AlternativeScore >= GetSeparatedAutoFeatureMinimumMargin(parameters);
		bool hasMinimumScore = resolvedAngle.Score >= parameters.TemplateAngleMinimumScore || hasSeparatedAutoFeatureMatch;
		bool hasEnoughMargin = resolvedAngle.Score - resolvedAngle.AlternativeScore >= parameters.TemplateAngleMinimumScoreMargin;
		bool allowHighScoreContourPolar = resolvedAngle.Source.StartsWith("auto-pca-contour-polar", StringComparison.Ordinal) && resolvedAngle.Score >= Math.Max(parameters.TemplateAngleMinimumScore, 0.7);
		bool isReliable = !num || (hasMinimumScore && (hasEnoughMargin || allowHighScoreContourPolar));
		string message = resolvedAngle.Source switch
		{
			"outer-contour" => "使用外轮廓角度。", 
			"outer-contour-no-template-image" => "模板图片不可用，使用外轮廓角度。", 
			"outer-contour-no-auto-feature" => "未找到可靠的自动角度特征，使用外轮廓角度。", 
			"auto-pca-contour" => "使用PCA轮廓角度: " + resolvedAngle.Detail + "。", 
			"auto-angle-disabled" => "自动判断不输出R角度: " + resolvedAngle.Detail + "。", 
			"auto-pca-polar-ring-no-template-image" => "模板图片不可用，自动PCA不可靠: " + resolvedAngle.Detail + "。", 
			"auto-pca-polar-ring-no-signal" => "自动PCA检查后未找到可靠的轮廓/极坐标角度信号: " + resolvedAngle.Detail + "。", 
			"auto-feature-rotation-no-feature" => "模板图片中未找到可靠的自动角度特征。", 
			"auto-feature-rotation-no-match" => "当前图片中未匹配到自动角度特征。", 
			"polar-ring-rotation-no-signal" => "未找到可靠的极坐标环角度信号。", 
			"auto-pca-contour-polar" => (!(!hasEnoughMargin && allowHighScoreContourPolar)) ? ("使用轮廓极坐标角度匹配: " + resolvedAngle.Detail + "。") : $"使用高分轮廓极坐标角度匹配，但候选接近: 分数={resolvedAngle.Score:F3}, 备选={resolvedAngle.AlternativeScore:F3}, 分差={resolvedAngle.Score - resolvedAngle.AlternativeScore:F3}。{resolvedAngle.Detail}。", 
			"auto-pca-polar-ring" => "使用图像极坐标环角度匹配: " + resolvedAngle.Detail + "。", 
			_ => (hasSeparatedAutoFeatureMatch && resolvedAngle.Score < parameters.TemplateAngleMinimumScore) ? $"自动特征角度已拉开分差: 分数={resolvedAngle.Score:F3}, 备选={resolvedAngle.AlternativeScore:F3}, 分差={resolvedAngle.Score - resolvedAngle.AlternativeScore:F3}。" : (hasMinimumScore ? (hasEnoughMargin ? $"旋转角度分数={resolvedAngle.Score:F3}, 备选={resolvedAngle.AlternativeScore:F3}。" : $"旋转角度不明确: 分数={resolvedAngle.Score:F3}, 备选={resolvedAngle.AlternativeScore:F3}, 分差={resolvedAngle.Score - resolvedAngle.AlternativeScore:F3}。") : $"旋转匹配分数{resolvedAngle.Score:F3}低于阈值{parameters.TemplateAngleMinimumScore:F3}。"), 
		};
		return new AngleResolutionDiagnostic(parameters.AngleDetectionMode, contourAngleDegrees, resolvedAngle.AngleDegrees, resolvedAngle.AllowsFullRotation, isReliable, resolvedAngle.Source, resolvedAngle.Score, resolvedAngle.AlternativeScore, resolvedAngle.Score - resolvedAngle.AlternativeScore, message, resolvedAngle.Candidates);
	}

	private static double GetSeparatedAutoFeatureMinimumScore(VisionParameters parameters)
	{
		return Math.Max(0.25, parameters.TemplateAngleMinimumScore - 0.13);
	}

	private static double GetSeparatedAutoFeatureMinimumMargin(VisionParameters parameters)
	{
		return Math.Max(0.01, parameters.TemplateAngleMinimumScoreMargin * 2.0);
	}

	private TemplateAngleModel? TryGetTemplateAngleModel(PartTemplate template, VisionParameters parameters)
	{
		if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
		{
			return null;
		}
		string currentDistortionId = GetCurrentDistortionCalibrationId(parameters);
		lock (_templateAngleModelSync)
		{
			TemplateAngleModel cached = _cachedTemplateAngleModel;
			if ((object)cached != null && cached.TemplateId == template.Id && string.Equals(cached.ImagePath, template.ImagePath, StringComparison.OrdinalIgnoreCase) && string.Equals(cached.SourceDistortionCalibrationId, currentDistortionId, StringComparison.Ordinal) && cached.Roi == parameters.Roi)
			{
				return cached;
			}
			_cachedTemplateAngleModel?.Patch.Dispose();
			_cachedTemplateAngleModel = null;
			_cachedPolarRingAngleModel = null;
			_cachedContourPolarAngleModel = null;
			using Mat templateImage = Cv2.ImRead(template.ImagePath);
			if (templateImage.Empty())
			{
				return null;
			}
			using Mat preparedTemplate = PrepareImage(templateImage, parameters);
			return _cachedTemplateAngleModel = BuildTemplateAngleModel(preparedTemplate, template, parameters, currentDistortionId);
		}
	}

	private AutoFeatureAngleModel? TryGetAutoFeatureAngleModel(PartTemplate template, VisionParameters parameters)
	{
		if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
		{
			return null;
		}
		string currentDistortionId = GetCurrentDistortionCalibrationId(parameters);
		lock (_templateAngleModelSync)
		{
			AutoFeatureAngleModel cached = _cachedAutoFeatureAngleModel;
			if ((object)cached != null && cached.TemplateId == template.Id && string.Equals(cached.ImagePath, template.ImagePath, StringComparison.OrdinalIgnoreCase) && string.Equals(cached.SourceDistortionCalibrationId, currentDistortionId, StringComparison.Ordinal) && cached.Roi == parameters.Roi)
			{
				return cached;
			}
			foreach (AutoFeatureCandidate item in _cachedAutoFeatureAngleModel?.Features ?? Array.Empty<AutoFeatureCandidate>())
			{
				item.Patch.Dispose();
			}
			_cachedAutoFeatureAngleModel = null;
			using Mat templateImage = Cv2.ImRead(template.ImagePath);
			if (templateImage.Empty())
			{
				return null;
			}
			using Mat preparedTemplate = PrepareImage(templateImage, parameters);
			IReadOnlyList<AutoFeatureCandidate> features = ExtractAutoFeatureCandidates(preparedTemplate, template);
			return _cachedAutoFeatureAngleModel = new AutoFeatureAngleModel(template.Id, template.ImagePath, currentDistortionId, parameters.Roi, features);
		}
	}

	private ContourPolarAngleModel? TryGetContourPolarAngleModel(PartTemplate template, VisionParameters parameters)
	{
		if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
		{
			return null;
		}
		string currentDistortionId = GetCurrentDistortionCalibrationId(parameters);
		lock (_templateAngleModelSync)
		{
			ContourPolarAngleModel cached = _cachedContourPolarAngleModel;
			if ((object)cached != null && cached.TemplateId == template.Id && string.Equals(cached.ImagePath, template.ImagePath, StringComparison.OrdinalIgnoreCase) && string.Equals(cached.SourceDistortionCalibrationId, currentDistortionId, StringComparison.Ordinal) && cached.Roi == parameters.Roi)
			{
				return cached;
			}
			_cachedContourPolarAngleModel = null;
			using Mat templateImage = Cv2.ImRead(template.ImagePath);
			if (templateImage.Empty())
			{
				return null;
			}
			using Mat preparedTemplate = PrepareImage(templateImage, parameters);
			IReadOnlyList<ContourCandidateDiagnostic> candidateDiagnostics;
			IReadOnlyList<PartDetection> candidateDetections;
			PartDetection templateDetection = DetectPartPrepared(preparedTemplate, parameters, template, out candidateDiagnostics, out candidateDetections);
			if ((object)templateDetection == null)
			{
				return null;
			}
			return _cachedContourPolarAngleModel = BuildContourPolarAngleModel(template, parameters, currentDistortionId, templateDetection);
		}
	}

	private PolarRingAngleModel? TryGetPolarRingAngleModel(PartTemplate template, VisionParameters parameters)
	{
		if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
		{
			return null;
		}
		string currentDistortionId = GetCurrentDistortionCalibrationId(parameters);
		lock (_templateAngleModelSync)
		{
			PolarRingAngleModel cached = _cachedPolarRingAngleModel;
			if ((object)cached != null && cached.TemplateId == template.Id && string.Equals(cached.ImagePath, template.ImagePath, StringComparison.OrdinalIgnoreCase) && string.Equals(cached.SourceDistortionCalibrationId, currentDistortionId, StringComparison.Ordinal) && cached.Roi == parameters.Roi)
			{
				return cached;
			}
			_cachedPolarRingAngleModel = null;
			using Mat templateImage = Cv2.ImRead(template.ImagePath);
			if (templateImage.Empty())
			{
				return null;
			}
			using Mat preparedTemplate = PrepareImage(templateImage, parameters);
			return _cachedPolarRingAngleModel = BuildPolarRingAngleModel(preparedTemplate, template, parameters, currentDistortionId);
		}
	}

	private static ContourPolarAngleModel? BuildContourPolarAngleModel(PartTemplate template, VisionParameters parameters, string currentDistortionId, PartDetection templateDetection)
	{
		float[] signature = BuildContourPolarSignature(templateDetection);
		double signal = CalculateSignatureSignal(signature);
		if (signal < 0.002)
		{
			return null;
		}
		float[] radiusSignature = BuildContourRadiusSignature(templateDetection, smooth: true);
		double radiusSignal = CalculateRadiusSignalPixels(radiusSignature);
		return new ContourPolarAngleModel(template.Id, template.ImagePath, currentDistortionId, parameters.Roi, signature, radiusSignature, signal, radiusSignal, CalculateContourAngleProfile(templateDetection));
	}

	private static TemplateAngleModel BuildTemplateAngleModel(Mat preparedTemplate, PartTemplate template, VisionParameters parameters, string currentDistortionId)
	{
		Size searchSize;
		Mat patch = ExtractTemplateAnglePatch(preparedTemplate, template.ReferenceCenterXPixel, template.ReferenceCenterYPixel, template.ReferenceWidthPixels, template.ReferenceHeightPixels, out searchSize);
		return new TemplateAngleModel(template.Id, template.ImagePath, currentDistortionId, parameters.Roi, patch, searchSize, template.ReferenceCenterXPixel, template.ReferenceCenterYPixel);
	}

	private static PolarRingAngleModel? BuildPolarRingAngleModel(Mat preparedTemplate, PartTemplate template, VisionParameters parameters, string currentDistortionId)
	{
		double radius = CalculatePolarRingRadius(template.ReferenceWidthPixels, template.ReferenceHeightPixels);
		using Mat gray = ToGray(preparedTemplate);
		float[] signature = BuildPolarRingSignature(gray, template.ReferenceCenterXPixel, template.ReferenceCenterYPixel, radius);
		double signal = CalculateSignatureSignal(signature);
		if (signal < 0.015)
		{
			return null;
		}
		return new PolarRingAngleModel(template.Id, template.ImagePath, currentDistortionId, parameters.Roi, signature, signal, radius);
	}

	private static Mat ExtractTemplateAnglePatch(Mat image, double centerX, double centerY, double referenceWidthPixels, double referenceHeightPixels, out Size searchSize)
	{
		int patchSize = CalculateTemplateAnglePatchSize(referenceWidthPixels, referenceHeightPixels);
		Rect sourceRect = BuildCenteredRect(centerX, centerY, patchSize + 48, image.Width, image.Height);
		int featureSize = patchSize + 48;
		using Mat sourcePatch = new Mat(image, sourceRect);
		using Mat gray = ToGray(sourcePatch);
		Mat normalized = NormalizeTemplateAngleImage(gray);
		if (normalized.Width == featureSize && normalized.Height == featureSize)
		{
			searchSize = new Size(featureSize, featureSize);
			return normalized;
		}
		Mat resized = new Mat();
		Cv2.Resize(normalized, resized, new Size(featureSize, featureSize), 0.0, 0.0, InterpolationFlags.Area);
		normalized.Dispose();
		searchSize = new Size(featureSize, featureSize);
		return resized;
	}

	private static ResolvedAngle MatchTemplateRotation(Mat preparedImage, PartDetection detection, TemplateAngleModel model, double referenceAngleDegrees, VisionParameters parameters, bool buildDiagnostics)
	{
		using Mat searchPatch = ExtractSearchAnglePatch(preparedImage, detection.CenterXPixel, detection.CenterYPixel, model.SearchSize);
		double coarseStep = NormalizeAngleStep(parameters.TemplateAngleCoarseStepDegrees, 5.0);
		double fineStep = NormalizeAngleStep(parameters.TemplateAngleFineStepDegrees, 0.5);
		double searchRange = Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 180.0, 360.0);
		(double, double, double, IReadOnlyList<(double, double)>) coarse = SearchTemplateAngle(searchPatch, model.Patch, 0.0 - searchRange, searchRange, coarseStep);
		double fineStart = coarse.Item1 - 6.0;
		double fineEnd = coarse.Item1 + 6.0;
		(double, double, double, IReadOnlyList<(double, double)>) fine = SearchTemplateAngle(searchPatch, model.Patch, fineStart, fineEnd, fineStep);
		IReadOnlyList<AngleCandidateDiagnostic> candidates = (buildDiagnostics ? BuildAngleCandidates(referenceAngleDegrees, fine.Item4, coarse.Item4) : null);
		return new ResolvedAngle(AngleMath.NormalizeDegrees360(referenceAngleDegrees + fine.Item1), searchRange > 180.0, "template-rotation", fine.Item2, Math.Max(coarse.Item3, fine.Item3), searchRange, candidates);
	}

	private static ResolvedAngle MatchPolarRingRotation(Mat preparedImage, PartDetection detection, PolarRingAngleModel model, double referenceAngleDegrees, VisionParameters parameters, bool buildDiagnostics)
	{
		using Mat gray = ToGray(preparedImage);
		float[] currentSignature = BuildPolarRingSignature(gray, detection.CenterXPixel, detection.CenterYPixel, model.RadiusPixels);
		if (CalculateSignatureSignal(currentSignature) < 0.015)
		{
			return new ResolvedAngle(detection.AngleDegrees, AllowsFullRotation: true, "polar-ring-rotation-no-signal", 0.0, 0.0, Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0), Array.Empty<AngleCandidateDiagnostic>());
		}
		(double, double, double, IReadOnlyList<AngleCandidateDiagnostic>) result = SearchPolarRingAngle(currentSignature, model.Signature, referenceAngleDegrees, parameters.TemplateAngleSearchRangeDegrees, buildDiagnostics);
		return new ResolvedAngle(AngleMath.NormalizeDegrees360(referenceAngleDegrees + result.Item1), AllowsFullRotation: true, "polar-ring-rotation", result.Item2, result.Item3, Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0), result.Item4);
	}

	private static ResolvedAngle MatchContourPolarRotation(PartDetection detection, ContourPolarAngleModel model, double referenceAngleDegrees, VisionParameters parameters, string profileDetail, bool buildDiagnostics)
	{
		float[] currentSignature = BuildContourPolarSignature(detection);
		double currentSignal = CalculateSignatureSignal(currentSignature);
		if (currentSignal < 0.002)
		{
			return new ResolvedAngle(detection.AngleDegrees, AllowsFullRotation: true, "auto-pca-polar-ring-no-signal", 0.0, 0.0, Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0), Array.Empty<AngleCandidateDiagnostic>(), $"{profileDetail}; contourSignal={currentSignal:F3}");
		}
		(double, double, double, IReadOnlyList<AngleCandidateDiagnostic>) result = SearchPolarRingAngle(currentSignature, model.Signature, referenceAngleDegrees, parameters.TemplateAngleSearchRangeDegrees, buildDiagnostics);
		return new ResolvedAngle(AngleMath.NormalizeDegrees360(referenceAngleDegrees + result.Item1), AllowsFullRotation: true, "auto-pca-contour-polar", result.Item2, result.Item3, Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0), result.Item4, $"{profileDetail}; contourSignal={currentSignal:F3}; templateSignal={model.Signal:F3}");
	}

	private static ResolvedAngle MatchAutoFeatureRotation(Mat preparedImage, PartDetection detection, AutoFeatureAngleModel model, double referenceAngleDegrees, VisionParameters parameters, bool buildDiagnostics)
	{
		double coarseStep = NormalizeAngleStep(parameters.TemplateAngleCoarseStepDegrees, 5.0);
		double fineStep = NormalizeAngleStep(parameters.TemplateAngleFineStepDegrees, 0.5);
		double searchRange = Math.Clamp(parameters.TemplateAngleSearchRangeDegrees, 1.0, 360.0);
		using Mat currentGray = ToGray(preparedImage);
		using Mat currentNormalized = NormalizeFeatureMatchImage(currentGray);
		List<AutoFeatureVote> votes = new List<AutoFeatureVote>();
		foreach (AutoFeatureCandidate feature in model.Features.Take(4))
		{
			(double, double, double, IReadOnlyList<(double, double)>) coarse = SearchAutoFeatureAngle(currentNormalized, feature, detection.CenterXPixel, detection.CenterYPixel, 0.0 - searchRange, searchRange, coarseStep);
			(double, double, double, IReadOnlyList<(double, double)>) fine = SearchAutoFeatureAngle(currentNormalized, feature, detection.CenterXPixel, detection.CenterYPixel, coarse.Item1 - 6.0, coarse.Item1 + 6.0, fineStep);
			if (fine.Item2 > 0.0)
			{
				votes.Add(new AutoFeatureVote(feature.Index, fine.Item1, AngleMath.NormalizeDegrees360(referenceAngleDegrees + fine.Item1), fine.Item2, Math.Max(coarse.Item3, fine.Item3), feature.QualityScore, fine.Item4));
			}
		}
		if (votes.Count == 0)
		{
			return new ResolvedAngle(detection.AngleDegrees, AllowsFullRotation: true, "auto-feature-rotation-no-match", 0.0, 0.0, searchRange, Array.Empty<AngleCandidateDiagnostic>());
		}
		IReadOnlyList<AutoFeatureAngleCluster> clusters = BuildAutoFeatureAngleClusters(votes, referenceAngleDegrees);
		AutoFeatureAngleCluster bestCluster = SelectAutoFeatureAngleCluster(clusters, parameters.TemplateAngleMinimumScore);
		double selectedScore = CalculateAutoFeatureClusterConfidence(bestCluster);
		double alternativeScore = clusters.Where((AutoFeatureAngleCluster cluster) => Math.Abs(AngleMath.NormalizeDeltaDegrees360(cluster.AngleOffsetDegrees, bestCluster.AngleOffsetDegrees)) >= 12.0).Select(CalculateAutoFeatureClusterConfidence).DefaultIfEmpty(0.0)
			.Max();
		AngleCandidateDiagnostic[] candidates = (buildDiagnostics ? (from candidate in votes.SelectMany((AutoFeatureVote vote) => vote.Candidates.Select(((double AngleDegrees, double Score) candidate) => new
			{
				FeatureIndex = vote.FeatureIndex,
				AngleDegrees = candidate.AngleDegrees,
				ResolvedAngleDegrees = AngleMath.NormalizeDegrees360(referenceAngleDegrees + candidate.AngleDegrees),
				Score = candidate.Score
			}))
			orderby candidate.Score descending
			select candidate).Take(8).Select((vote, index) => new AngleCandidateDiagnostic(index + 1, vote.AngleDegrees, vote.ResolvedAngleDegrees, Math.Clamp(vote.Score, 0.0, 1.0), $"auto-feature-{vote.FeatureIndex}")).ToArray() : null);
		return new ResolvedAngle(bestCluster.ResolvedAngleDegrees, AllowsFullRotation: true, "auto-feature-rotation", selectedScore, alternativeScore, searchRange, candidates);
	}

	private static double CalculateAutoFeatureVoteRankScore(AutoFeatureVote vote)
	{
		return Math.Clamp(vote.Score * vote.QualityScore, 0.0, 1.0);
	}

	private static double CalculateAutoFeatureClusterConfidence(AutoFeatureAngleCluster cluster)
	{
		return Math.Clamp(cluster.RankScore, 0.0, 1.0);
	}

	private static IReadOnlyList<AutoFeatureAngleCluster> BuildAutoFeatureAngleClusters(IReadOnlyList<AutoFeatureVote> votes, double referenceAngleDegrees)
	{
		List<List<AutoFeatureVote>> clusters = new List<List<AutoFeatureVote>>();
		foreach (AutoFeatureVote vote in from autoFeatureVote in votes.SelectMany((AutoFeatureVote autoFeatureVote) => autoFeatureVote.Candidates.Select(((double AngleDegrees, double Score) candidate) => autoFeatureVote with
			{
				AngleOffsetDegrees = candidate.AngleDegrees,
				ResolvedAngleDegrees = AngleMath.NormalizeDegrees360(referenceAngleDegrees + candidate.AngleDegrees),
				Score = candidate.Score
			}))
			orderby autoFeatureVote.Score descending
			select autoFeatureVote)
		{
			List<AutoFeatureVote> cluster = clusters.FirstOrDefault((List<AutoFeatureVote> existing) => existing.Any((AutoFeatureVote existingVote) => Math.Abs(AngleMath.NormalizeDeltaDegrees360(vote.AngleOffsetDegrees, existingVote.AngleOffsetDegrees)) < 12.0));
			if (cluster == null)
			{
				int num = 1;
				List<AutoFeatureVote> list = new List<AutoFeatureVote>(num);
				CollectionsMarshal.SetCount(list, num);
				Span<AutoFeatureVote> span = CollectionsMarshal.AsSpan(list);
				int num2 = 0;
				span[num2] = vote;
				num2++;
				clusters.Add(list);
			}
			else
			{
				cluster.Add(vote);
			}
		}
		return (from autoFeatureAngleCluster in clusters.Select(delegate(List<AutoFeatureVote> source)
			{
				AutoFeatureVote autoFeatureVote = source.OrderByDescending((AutoFeatureVote autoFeatureVote2) => autoFeatureVote2.Score).First();
				int supportCount = source.Select((AutoFeatureVote autoFeatureVote2) => autoFeatureVote2.FeatureIndex).Distinct().Count();
				double rankScore = (from autoFeatureVote2 in source
					group autoFeatureVote2 by autoFeatureVote2.FeatureIndex into @group
					select ((IEnumerable<AutoFeatureVote>)@group).Max((Func<AutoFeatureVote, double>)CalculateAutoFeatureVoteRankScore)).Sum();
				return new AutoFeatureAngleCluster(autoFeatureVote.AngleOffsetDegrees, autoFeatureVote.ResolvedAngleDegrees, autoFeatureVote.Score, rankScore, autoFeatureVote.AlternativeScore, supportCount, autoFeatureVote.FeatureIndex);
			})
			orderby autoFeatureAngleCluster.RankScore descending, autoFeatureAngleCluster.Score descending
			select autoFeatureAngleCluster).ToArray();
	}

	private static AutoFeatureAngleCluster SelectAutoFeatureAngleCluster(IReadOnlyList<AutoFeatureAngleCluster> clusters, double minimumReliableScore)
	{
		AutoFeatureAngleCluster best = clusters[0];
		AutoFeatureAngleCluster rawScoreBest = (from cluster in clusters
			orderby cluster.Score descending, cluster.RankScore descending
			select cluster).First();
		if (Math.Abs(best.AngleOffsetDegrees) >= 30.0)
		{
			if (best.RankScore < minimumReliableScore && Math.Abs(rawScoreBest.AngleOffsetDegrees) < 30.0 && rawScoreBest.Score > best.Score)
			{
				return rawScoreBest;
			}
			return best;
		}
		return (from cluster in clusters
			where Math.Abs(cluster.AngleOffsetDegrees) >= 30.0
			where cluster.Score >= best.Score
			orderby cluster.SupportCount descending, cluster.RankScore descending, cluster.Score descending
			select cluster).FirstOrDefault() ?? best;
	}

	private static Mat ExtractSearchAnglePatch(Mat image, double centerX, double centerY, Size searchSize)
	{
		Rect sourceRect = BuildCenteredRect(centerX, centerY, Math.Max(searchSize.Width, searchSize.Height), image.Width, image.Height);
		using Mat sourcePatch = new Mat(image, sourceRect);
		using Mat gray = ToGray(sourcePatch);
		using Mat normalized = NormalizeTemplateAngleImage(gray);
		Mat resized = new Mat();
		Cv2.Resize(normalized, resized, searchSize, 0.0, 0.0, InterpolationFlags.Area);
		return resized;
	}

	private static IReadOnlyList<AutoFeatureCandidate> ExtractAutoFeatureCandidates(Mat preparedTemplate, PartTemplate template)
	{
		using Mat gray = ToGray(preparedTemplate);
		using Mat normalized = NormalizeFeatureMatchImage(gray);
		using Mat partMask = BuildFilledPartMask(gray, template);
		using Mat edges = new Mat();
		Cv2.Canny(normalized, edges, 40.0, 120.0);
		double radius = Math.Max(Math.Min(template.ReferenceWidthPixels, template.ReferenceHeightPixels) / 2.0, 64.0);
		int patchSize = CalculateAutoFeaturePatchSize(template.ReferenceWidthPixels, template.ReferenceHeightPixels);
		int step = Math.Max(patchSize / 2, (int)Math.Round(radius * 2.0 / 9.0));
		int startX = (int)Math.Round(template.ReferenceCenterXPixel - radius * 0.72);
		int endX = (int)Math.Round(template.ReferenceCenterXPixel + radius * 0.72);
		int num = (int)Math.Round(template.ReferenceCenterYPixel - radius * 0.72);
		int endY = (int)Math.Round(template.ReferenceCenterYPixel + radius * 0.72);
		List<(Rect, double, double, double, double)> scored = new List<(Rect, double, double, double, double)>();
		for (int y = num; y <= endY; y += step)
		{
			for (int x = startX; x <= endX; x += step)
			{
				int centerX = x;
				int centerY = y;
				double num2 = (double)centerX - template.ReferenceCenterXPixel;
				double dy = (double)centerY - template.ReferenceCenterYPixel;
				double distance = Math.Sqrt(num2 * num2 + dy * dy);
				if (distance < radius * 0.18 || distance > radius * 0.72 || !TryBuildCenteredRectInsideImage(centerX, centerY, patchSize, preparedTemplate.Width, preparedTemplate.Height, out var rect))
				{
					continue;
				}
				using Mat patch = new Mat(normalized, rect);
				using Mat maskPatch = new Mat(partMask, rect);
				if ((double)Cv2.CountNonZero(maskPatch) / Math.Max(rect.Width * rect.Height, 1.0) < 0.92)
				{
					continue;
				}
				using Mat edgePatch = new Mat(edges, rect);
				double mean = Cv2.Mean(patch).Val0;
				using Mat meanMat = new Mat(patch.Size(), patch.Type(), Scalar.All(mean));
				using Mat diff = new Mat();
				Cv2.Absdiff(patch, meanMat, diff);
				double val = Cv2.Mean(diff).Val0;
				double edgeDensity = (double)Cv2.CountNonZero(edgePatch) / Math.Max(rect.Width * rect.Height, 1.0);
				double overexposed = CountThreshold(patch, 245) / Math.Max(rect.Width * rect.Height, 1.0);
				double underexposed = CountUnderThreshold(patch, 10) / Math.Max(rect.Width * rect.Height, 1.0);
				double score = val * (1.0 + edgeDensity * 8.0) * (1.0 - Math.Min(overexposed + underexposed, 0.85));
				if (score >= 4.0)
				{
					scored.Add((rect, centerX, centerY, distance, score));
				}
			}
		}
		List<AutoFeatureCandidate> features = new List<AutoFeatureCandidate>();
		foreach (var item in scored.OrderByDescending<(Rect, double, double, double, double), double>(((Rect Rect, double CenterX, double CenterY, double Radius, double Score) tuple) => tuple.Score))
		{
			if (!features.Any((AutoFeatureCandidate feature) => DistancePixels(feature.CenterXPixel, feature.CenterYPixel, item.Item2, item.Item3) < (double)patchSize))
			{
				Mat patch2 = new Mat(normalized, item.Item1).Clone();
				double dx = item.Item2 - template.ReferenceCenterXPixel;
				double dy2 = item.Item3 - template.ReferenceCenterYPixel;
				features.Add(new AutoFeatureCandidate(features.Count + 1, patch2, item.Item2, item.Item3, dx, dy2, item.Item4, Math.Atan2(dy2, dx) * 180.0 / Math.PI, Math.Clamp(item.Item5 / 32.0, 0.1, 1.0)));
				if (features.Count >= 4)
				{
					break;
				}
			}
		}
		return features;
	}

	private static (double AngleDegrees, double Score, double SecondBestScore, IReadOnlyList<(double AngleDegrees, double Score)> TopCandidates) SearchAutoFeatureAngle(Mat currentNormalized, AutoFeatureCandidate feature, double currentCenterX, double currentCenterY, double startDegrees, double endDegrees, double stepDegrees)
	{
		double bestAngle = startDegrees;
		double bestScore = double.NegativeInfinity;
		List<(double AngleDegrees, double Score)> candidates = new List<(double AngleDegrees, double Score)>();
		for (double angle = startDegrees; angle <= endDegrees + 1E-09; angle += stepDegrees)
		{
			double num = (0.0 - angle) * Math.PI / 180.0;
			double cos = Math.Cos(num);
			double sin = Math.Sin(num);
			double centerX = currentCenterX + cos * feature.OffsetXPixel - sin * feature.OffsetYPixel;
			double predictedY = currentCenterY + sin * feature.OffsetXPixel + cos * feature.OffsetYPixel;
			int searchPadding = Math.Max(24, feature.Patch.Width / 2);
			int searchSize = feature.Patch.Width + searchPadding * 2;
			if (!TryBuildCenteredRectInsideImage(centerX, predictedY, searchSize, currentNormalized.Width, currentNormalized.Height, out var searchRect))
			{
				continue;
			}
			using Mat rotated = RotateTemplatePatch(feature.Patch, angle);
			using Mat search = new Mat(currentNormalized, searchRect);
			double score = MatchTemplateScore(search, rotated);
			AddAngleCandidate(candidates, angle, score);
			if (score > bestScore)
			{
				bestScore = score;
				bestAngle = angle;
			}
		}
		if (candidates.Count == 0)
		{
			return (AngleDegrees: bestAngle, Score: 0.0, SecondBestScore: 0.0, TopCandidates: Array.Empty<(double AngleDegrees, double Score)>());
		}
		(double AngleDegrees, double Score)[] topCandidates = (from candidate in candidates.OrderByDescending(((double AngleDegrees, double Score) candidate) => candidate.Score).Take(5)
			select (AngleDegrees: candidate.AngleDegrees, Score: Math.Clamp(candidate.Score, 0.0, 1.0))).ToArray();
		(double AngleDegrees, double Score) bestCandidate = topCandidates[0];
		double secondBestScore = (from candidate in topCandidates.Skip(1)
			where Math.Abs(AngleMath.NormalizeDeltaDegrees360(candidate.AngleDegrees, bestCandidate.AngleDegrees)) >= 12.0
			select candidate.Score).DefaultIfEmpty(0.0).Max();
		return (AngleDegrees: bestCandidate.AngleDegrees, Score: Math.Clamp(bestCandidate.Score, 0.0, 1.0), SecondBestScore: Math.Clamp(secondBestScore, 0.0, 1.0), TopCandidates: topCandidates);
	}

	private static double MatchTemplateScore(Mat search, Mat templatePatch)
	{
		if (search.Width < templatePatch.Width || search.Height < templatePatch.Height)
		{
			return 0.0;
		}
		using Mat result = new Mat();
		Cv2.MatchTemplate(search, templatePatch, result, TemplateMatchModes.CCoeffNormed);
		Cv2.MinMaxLoc((InputArray)result, out double _, out double maxVal);
		return Math.Clamp(maxVal, 0.0, 1.0);
	}

	private static (double AngleOffsetDegrees, double Score, double SecondBestScore, IReadOnlyList<AngleCandidateDiagnostic> Candidates) SearchPolarRingAngle(float[] currentSignature, float[] templateSignature, double referenceAngleDegrees, double configuredSearchRangeDegrees, bool buildDiagnostics = true)
	{
		int sampleCount = Math.Min(currentSignature.Length, templateSignature.Length);
		if (sampleCount == 0)
		{
			return (AngleOffsetDegrees: 0.0, Score: 0.0, SecondBestScore: 0.0, Candidates: Array.Empty<AngleCandidateDiagnostic>());
		}
		double searchRange = Math.Clamp(configuredSearchRangeDegrees, 1.0, 360.0);
		double effectiveSearchRange = Math.Min(searchRange, 180.0);
		int maxShift = (int)Math.Ceiling((double)sampleCount * effectiveSearchRange / 360.0);
		int bestShift = 0;
		double bestScore = double.NegativeInfinity;
		List<(double AngleDegrees, double Score)> candidates = new List<(double AngleDegrees, double Score)>();
		for (int shift = -maxShift; shift <= maxShift; shift++)
		{
			double angle = 0.0 - ShiftToDegrees(shift, sampleCount);
			if (!(Math.Abs(angle) > searchRange + 1E-09))
			{
				double score = CalculateCircularCorrelation(currentSignature, templateSignature, shift);
				AddAngleCandidate(candidates, angle, score);
				if (score > bestScore)
				{
					bestScore = score;
					bestShift = shift;
				}
			}
		}
		double bestAngle = 0.0 - ShiftToDegrees(bestShift, sampleCount);
		double secondBestScore = (from candidate in candidates
			where Math.Abs(AngleMath.NormalizeDeltaDegrees360(candidate.AngleDegrees, bestAngle)) >= 18.0
			select candidate.Score).DefaultIfEmpty(0.0).Max();
		AngleCandidateDiagnostic[] diagnostics = (buildDiagnostics ? candidates.OrderByDescending(((double AngleDegrees, double Score) candidate) => candidate.Score).Take(8).Select(((double AngleDegrees, double Score) candidate, int index) => new AngleCandidateDiagnostic(index + 1, candidate.AngleDegrees, AngleMath.NormalizeDegrees360(referenceAngleDegrees + candidate.AngleDegrees), Math.Clamp(candidate.Score, 0.0, 1.0), "polar-ring"))
			.ToArray() : Array.Empty<AngleCandidateDiagnostic>());
		return (AngleOffsetDegrees: bestAngle, Score: Math.Clamp(bestScore, 0.0, 1.0), SecondBestScore: Math.Clamp(secondBestScore, 0.0, 1.0), Candidates: diagnostics);
	}

	private static (double AngleDegrees, double Score, double SecondBestScore, IReadOnlyList<(double AngleDegrees, double Score)> TopCandidates) SearchTemplateAngle(Mat searchPatch, Mat templatePatch, double startDegrees, double endDegrees, double stepDegrees)
	{
		using Mat invertedSearch = new Mat();
		Cv2.BitwiseNot(searchPatch, invertedSearch);
		using Mat distance = new Mat();
		Cv2.DistanceTransform(invertedSearch, distance, DistanceTypes.L2, DistanceTransformMasks.Mask3);
		double bestAngle = startDegrees;
		double bestScore = double.NegativeInfinity;
		double secondBestScore = 0.0;
		List<(double AngleDegrees, double Score)> candidates = new List<(double AngleDegrees, double Score)>();
		for (double angle = startDegrees; angle <= endDegrees + 1E-09; angle += stepDegrees)
		{
			using Mat rotated = RotateTemplatePatch(templatePatch, angle);
			double score = CalculateChamferScore(distance, rotated);
			AddAngleCandidate(candidates, angle, score);
			if (score > bestScore)
			{
				secondBestScore = bestScore;
				bestScore = score;
				bestAngle = angle;
			}
			else if (Math.Abs(AngleMath.NormalizeDeltaDegrees360(angle, bestAngle)) >= 12.0 && score > secondBestScore)
			{
				secondBestScore = score;
			}
		}
		return (AngleDegrees: bestAngle, Score: Math.Clamp(bestScore, 0.0, 1.0), SecondBestScore: Math.Clamp(secondBestScore, 0.0, 1.0), TopCandidates: (from candidate in candidates.OrderByDescending(((double AngleDegrees, double Score) candidate) => candidate.Score).Take(5)
			select (AngleDegrees: candidate.AngleDegrees, Score: Math.Clamp(candidate.Score, 0.0, 1.0))).ToArray());
	}

	private static void AddAngleCandidate(List<(double AngleDegrees, double Score)> candidates, double angleDegrees, double score)
	{
		for (int i = 0; i < candidates.Count; i++)
		{
			if (Math.Abs(AngleMath.NormalizeDeltaDegrees360(angleDegrees, candidates[i].AngleDegrees)) < 12.0)
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

	private static IReadOnlyList<AngleCandidateDiagnostic> BuildAngleCandidates(double referenceAngleDegrees, IReadOnlyList<(double AngleDegrees, double Score)> fineCandidates, IReadOnlyList<(double AngleDegrees, double Score)> coarseCandidates)
	{
		return (from candidate in fineCandidates.Select(((double AngleDegrees, double Score) candidate) => new
			{
				AngleDegrees = candidate.AngleDegrees,
				Score = candidate.Score,
				Stage = "fine"
			}).Concat(coarseCandidates.Select(((double AngleDegrees, double Score) candidate) => new
			{
				AngleDegrees = candidate.AngleDegrees,
				Score = candidate.Score,
				Stage = "coarse"
			}))
			group candidate by Math.Round(candidate.AngleDegrees / 12.0) into @group
			select @group.OrderByDescending(candidate => candidate.Score).First() into candidate
			orderby candidate.Score descending
			select candidate).Take(5).Select((candidate, index) => new AngleCandidateDiagnostic(index + 1, candidate.AngleDegrees, AngleMath.NormalizeDegrees360(referenceAngleDegrees + candidate.AngleDegrees), Math.Clamp(candidate.Score, 0.0, 1.0), candidate.Stage)).ToArray();
	}

	private static double CalculateChamferScore(Mat distanceToSearchEdge, Mat rotatedTemplatePatch)
	{
		using Mat binaryTemplate = new Mat();
		Cv2.Threshold(rotatedTemplatePatch, binaryTemplate, 32.0, 255.0, ThresholdTypes.Binary);
		if (Cv2.CountNonZero(binaryTemplate) < 8)
		{
			return 0.0;
		}
		return Math.Exp((0.0 - Cv2.Mean(distanceToSearchEdge, binaryTemplate).Val0) / 6.0);
	}

	private static Mat RotateTemplatePatch(Mat templatePatch, double angleDegrees)
	{
		using Mat rotation = Cv2.GetRotationMatrix2D(new Point2f((float)templatePatch.Width / 2f, (float)templatePatch.Height / 2f), angleDegrees, 1.0);
		Mat rotated = new Mat();
		Cv2.WarpAffine(templatePatch, rotated, rotation, templatePatch.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0.0));
		return rotated;
	}

	private static Mat NormalizeTemplateAngleImage(Mat gray)
	{
		using Mat blurred = new Mat();
		Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0.0);
		using Mat equalized = new Mat();
		Cv2.EqualizeHist(blurred, equalized);
		Mat edges = new Mat();
		Cv2.Canny(equalized, edges, 40.0, 120.0);
		return edges;
	}

	private static Mat NormalizeFeatureMatchImage(Mat gray)
	{
		using Mat blurred = new Mat();
		Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0.0);
		using Mat equalized = new Mat();
		Cv2.EqualizeHist(blurred, equalized);
		Mat normalized = new Mat();
		Cv2.Normalize(equalized, normalized, 0.0, 255.0, NormTypes.MinMax);
		return normalized;
	}

	private static float[] BuildPolarRingSignature(Mat gray, double centerX, double centerY, double radiusPixels)
	{
		using Mat normalized = NormalizeFeatureMatchImage(gray);
		using Mat gradient = BuildGradientMagnitude(normalized);
		float[] signature = new float[720];
		double innerRadius = Math.Max(2.0, radiusPixels * 0.25);
		double radialStep = (Math.Max(innerRadius + 2.0, radiusPixels * 0.82) - innerRadius) / (double)Math.Max(63, 1);
		for (int angleIndex = 0; angleIndex < signature.Length; angleIndex++)
		{
			double num = (double)angleIndex * 2.0 * Math.PI / (double)signature.Length;
			double cos = Math.Cos(num);
			double sin = Math.Sin(num);
			double sum = 0.0;
			double weightSum = 0.0;
			for (int radiusIndex = 0; radiusIndex < 64; radiusIndex++)
			{
				double radius = innerRadius + (double)radiusIndex * radialStep;
				double x = centerX + cos * radius;
				double y = centerY + sin * radius;
				if (TrySampleByte(normalized, x, y, out var intensity) && TrySampleFloat(gradient, x, y, out var edge))
				{
					double radialPosition = (double)radiusIndex / Math.Max(63.0, 1.0);
					double radialWeight = 0.65 + radialPosition * 0.7;
					sum += radialWeight * (intensity / 255.0 + edge / 255.0 * 1.4);
					weightSum += radialWeight;
				}
			}
			signature[angleIndex] = ((weightSum > 0.0001) ? ((float)(sum / weightSum)) : 0f);
		}
		NormalizeSignatureInPlace(signature);
		return signature;
	}

	private static float[] BuildContourPolarSignature(PartDetection detection)
	{
		float[] signature = BuildContourRadiusSignature(detection, smooth: false);
		NormalizeSignatureInPlace(signature);
		return signature;
	}

	private static float[] BuildContourRadiusSignature(PartDetection detection, bool smooth)
	{
		float[] signature = new float[720];
		if (detection.Contour.Length < 3)
		{
			return signature;
		}
		double centerX = detection.CenterXPixel - (double)detection.Offset.X;
		double centerY = detection.CenterYPixel - (double)detection.Offset.Y;
		for (int i = 0; i < detection.Contour.Length; i++)
		{
			Point start = detection.Contour[i];
			Point point = detection.Contour[(i + 1) % detection.Contour.Length];
			int dx = point.X - start.X;
			int dy = point.Y - start.Y;
			int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy)));
			for (int step = 0; step <= steps; step++)
			{
				double t = (double)step / (double)steps;
				double x = (double)start.X + (double)dx * t;
				double num = (double)start.Y + (double)dy * t;
				double radiusX = x - centerX;
				double radiusY = num - centerY;
				double radius = Math.Sqrt(radiusX * radiusX + radiusY * radiusY);
				if (!(radius <= 0.0001))
				{
					double angle = Math.Atan2(radiusY, radiusX);
					if (angle < 0.0)
					{
						angle += Math.PI * 2.0;
					}
					int index = Math.Clamp((int)Math.Round(angle * (double)signature.Length / (Math.PI * 2.0)) % signature.Length, 0, signature.Length - 1);
					signature[index] = Math.Max(signature[index], (float)radius);
				}
			}
		}
		FillMissingCircularSignatureBins(signature);
		if (smooth)
		{
			SmoothCircularAverageSignatureInPlace(signature, 2);
		}
		return signature;
	}

	private static float[] BuildContourSampleMirrorSignature(PartDetection detection)
	{
		return BuildContourSampleMirrorSignature(BuildContourPolarSignature(detection));
	}

	private static float[] BuildContourSampleMirrorSignature(float[] normalizedRadiusSignature)
	{
		float[] signature = new float[normalizedRadiusSignature.Length];
		if (normalizedRadiusSignature.Length == 0)
		{
			return signature;
		}
		for (int i = 0; i < normalizedRadiusSignature.Length; i++)
		{
			float left = normalizedRadiusSignature[PositiveModulo(i - 24, normalizedRadiusSignature.Length)];
			float num = normalizedRadiusSignature[i];
			float right = normalizedRadiusSignature[PositiveModulo(i + 24, normalizedRadiusSignature.Length)];
			double prominence = (double)num - (double)(left + right) / 2.0;
			float slope = right - left;
			signature[i] = (float)(prominence * 0.75 + (double)slope * 0.25);
		}
		NormalizeSignatureInPlace(signature);
		return signature;
	}

	private static float[] MirrorCircularSignature(float[] signature)
	{
		float[] mirrored = new float[signature.Length];
		if (signature.Length == 0)
		{
			return mirrored;
		}
		mirrored[0] = signature[0];
		for (int i = 1; i < signature.Length; i++)
		{
			mirrored[i] = signature[^i];
		}
		return mirrored;
	}

	private static void FillMissingCircularSignatureBins(float[] signature)
	{
		if (signature.Length == 0 || signature.All((float value) => value <= 0f))
		{
			return;
		}
		for (int i = 0; i < signature.Length; i++)
		{
			if (!(signature[i] > 0f))
			{
				int previousIndex = FindNearestCircularSignatureValue(signature, i, -1);
				int nextIndex = FindNearestCircularSignatureValue(signature, i, 1);
				signature[i] = ((previousIndex >= 0 && nextIndex >= 0) ? ((signature[previousIndex] + signature[nextIndex]) / 2f) : ((previousIndex >= 0) ? signature[previousIndex] : signature[nextIndex]));
			}
		}
	}

	private static int FindNearestCircularSignatureValue(float[] signature, int startIndex, int direction)
	{
		for (int offset = 1; offset < signature.Length; offset++)
		{
			int index = PositiveModulo(startIndex + offset * direction, signature.Length);
			if (signature[index] > 0f)
			{
				return index;
			}
		}
		return -1;
	}

	private static Mat BuildGradientMagnitude(Mat gray)
	{
		using Mat sobelX = new Mat();
		using Mat sobelY = new Mat();
		Cv2.Sobel(gray, sobelX, 5, 1, 0);
		Cv2.Sobel(gray, sobelY, 5, 0, 1);
		using Mat magnitude = new Mat();
		Cv2.Magnitude(sobelX, sobelY, magnitude);
		Mat normalized = new Mat();
		Cv2.Normalize(magnitude, normalized, 0.0, 255.0, NormTypes.MinMax);
		return normalized;
	}

	private static void NormalizeSignatureInPlace(float[] signature)
	{
		if (signature.Length == 0)
		{
			return;
		}
		double mean = ((IEnumerable<float>)signature).Average((Func<float, double>)((float value) => value));
		double stdDev = Math.Sqrt(Math.Max(signature.Select((float value) => ((double)value - mean) * ((double)value - mean)).DefaultIfEmpty(0.0).Average(), 0.0));
		if (stdDev < 1E-06)
		{
			Array.Fill(signature, 0f);
			return;
		}
		for (int i = 0; i < signature.Length; i++)
		{
			signature[i] = (float)(((double)signature[i] - mean) / stdDev);
		}
		SmoothCircularSignatureInPlace(signature);
	}

	private static void SmoothCircularSignatureInPlace(float[] signature)
	{
		if (signature.Length >= 3)
		{
			float[] copy = signature.ToArray();
			for (int i = 0; i < signature.Length; i++)
			{
				float previous = copy[(i - 1 + copy.Length) % copy.Length];
				float current = copy[i];
				float next = copy[(i + 1) % copy.Length];
				signature[i] = (float)(((double)previous + (double)current * 2.0 + (double)next) / 4.0);
			}
		}
	}

	private static void SmoothCircularAverageSignatureInPlace(float[] signature, int radius)
	{
		if (signature.Length < 3 || radius <= 0)
		{
			return;
		}
		float[] copy = signature.ToArray();
		int windowSize = radius * 2 + 1;
		for (int i = 0; i < signature.Length; i++)
		{
			double sum = 0.0;
			for (int offset = -radius; offset <= radius; offset++)
			{
				sum += copy[PositiveModulo(i + offset, copy.Length)];
			}
			signature[i] = (float)(sum / (double)windowSize);
		}
	}

	private static double CalculateSignatureSignal(float[] signature)
	{
		if (signature.Length == 0)
		{
			return 0.0;
		}
		return Math.Sqrt(signature.Select((float value) => value * value).Average());
	}

	private static double CalculateRadiusSignalPixels(float[] signature)
	{
		if (signature.Length == 0)
		{
			return 0.0;
		}
		double mean = ((IEnumerable<float>)signature).Average((Func<float, double>)((float value) => value));
		double variance = signature.Select((float value) => ((double)value - mean) * ((double)value - mean)).DefaultIfEmpty(0.0).Average();
		return Math.Sqrt(Math.Max(variance, 0.0));
	}

	private static double CalculateCircularCorrelation(float[] currentSignature, float[] templateSignature, int shift)
	{
		int sampleCount = Math.Min(currentSignature.Length, templateSignature.Length);
		if (sampleCount == 0)
		{
			return 0.0;
		}
		double dot = 0.0;
		double currentNorm = 0.0;
		double templateNorm = 0.0;
		for (int i = 0; i < sampleCount; i++)
		{
			int templateIndex = PositiveModulo(i - shift, sampleCount);
			float current = currentSignature[i];
			float template = templateSignature[templateIndex];
			dot += (double)(current * template);
			currentNorm += (double)(current * current);
			templateNorm += (double)(template * template);
		}
		if (currentNorm < 1E-06 || templateNorm < 1E-06)
		{
			return 0.0;
		}
		return Math.Clamp((dot / Math.Sqrt(currentNorm * templateNorm) + 1.0) / 2.0, 0.0, 1.0);
	}

	private static (int Shift, double Score) SearchCircularCorrelationAroundShift(float[] currentSignature, float[] templateSignature, int centerShift, int maxShiftDelta)
	{
		if (Math.Min(currentSignature.Length, templateSignature.Length) == 0)
		{
			return (Shift: 0, Score: 0.0);
		}
		int bestShift = centerShift;
		double bestScore = double.NegativeInfinity;
		for (int delta = -maxShiftDelta; delta <= maxShiftDelta; delta++)
		{
			int shift = centerShift + delta;
			double score = CalculateCircularCorrelation(currentSignature, templateSignature, shift);
			if (score > bestScore)
			{
				bestScore = score;
				bestShift = shift;
			}
		}
		return (Shift: bestShift, Score: Math.Clamp(bestScore, 0.0, 1.0));
	}

	private static (int Shift, double ErrorPixels, double ErrorNormalized) SearchCircularRadiusError(float[] currentSignature, float[] templateSignature)
	{
		int sampleCount = Math.Min(currentSignature.Length, templateSignature.Length);
		if (sampleCount == 0)
		{
			return (Shift: 0, ErrorPixels: double.PositiveInfinity, ErrorNormalized: double.PositiveInfinity);
		}
		int bestShift = 0;
		double bestErrorPixels = double.PositiveInfinity;
		double bestErrorNormalized = double.PositiveInfinity;
		for (int shift = 0; shift < sampleCount; shift++)
		{
			double sumPixels = 0.0;
			double sumNormalized = 0.0;
			bool abandoned = false;
			for (int i = 0; i < sampleCount; i++)
			{
				float current = currentSignature[i];
				float template = templateSignature[PositiveModulo(i - shift, sampleCount)];
				double diff = Math.Abs(current - template);
				sumPixels += diff;
				double denominator = Math.Max((Math.Abs(current) + Math.Abs(template)) / 2.0, 1.0);
				sumNormalized += diff / denominator;
				if (sumPixels >= bestErrorPixels * sampleCount)
				{
					abandoned = true;
					break;
				}
			}
			if (abandoned)
			{
				continue;
			}
			double errorPixels = sumPixels / (double)sampleCount;
			if (errorPixels < bestErrorPixels)
			{
				bestErrorPixels = errorPixels;
				bestErrorNormalized = sumNormalized / (double)sampleCount;
				bestShift = shift;
			}
		}
		return (Shift: bestShift, ErrorPixels: bestErrorPixels, ErrorNormalized: bestErrorNormalized);
	}

	private static int DegreesToShift(double angleDegrees, int sampleCount)
	{
		if (sampleCount <= 0)
		{
			return 0;
		}
		return (int)Math.Round(angleDegrees * (double)sampleCount / 360.0);
	}

	private static double ShiftToDegrees(int shift, int sampleCount)
	{
		return AngleMath.NormalizeDegrees360((double)shift * 360.0 / (double)Math.Max(sampleCount, 1));
	}

	private static double ShiftToSignedDegrees(int shift, int sampleCount)
	{
		return AngleMath.NormalizeDegrees360((double)shift * 360.0 / (double)Math.Max(sampleCount, 1));
	}

	private static int PositiveModulo(int value, int modulo)
	{
		int result = value % modulo;
		if (result >= 0)
		{
			return result;
		}
		return result + modulo;
	}

	private static double CalculatePolarRingRadius(double referenceWidthPixels, double referenceHeightPixels)
	{
		return Math.Clamp(Math.Min((referenceWidthPixels > 0.0001) ? referenceWidthPixels : 420.0, (referenceHeightPixels > 0.0001) ? referenceHeightPixels : 420.0) / 2.0, 70.0, 210.0);
	}

	private static bool TrySampleByte(Mat image, double x, double y, out double value)
	{
		value = 0.0;
		if (x < 0.0 || y < 0.0 || x >= (double)(image.Width - 1) || y >= (double)(image.Height - 1))
		{
			return false;
		}
		int left = (int)Math.Floor(x);
		int top = (int)Math.Floor(y);
		double fx = x - (double)left;
		double fy = y - (double)top;
		byte topLeft = image.At<byte>(top, left);
		byte topRight = image.At<byte>(top, left + 1);
		byte bottomLeft = image.At<byte>(top + 1, left);
		byte bottomRight = image.At<byte>(top + 1, left + 1);
		value = Bilinear((int)topLeft, (int)topRight, (int)bottomLeft, (int)bottomRight, fx, fy);
		return true;
	}

	private static bool TrySampleFloat(Mat image, double x, double y, out double value)
	{
		value = 0.0;
		if (x < 0.0 || y < 0.0 || x >= (double)(image.Width - 1) || y >= (double)(image.Height - 1))
		{
			return false;
		}
		int left = (int)Math.Floor(x);
		int top = (int)Math.Floor(y);
		double fx = x - (double)left;
		double fy = y - (double)top;
		float topLeft = image.At<float>(top, left);
		float topRight = image.At<float>(top, left + 1);
		float bottomLeft = image.At<float>(top + 1, left);
		float bottomRight = image.At<float>(top + 1, left + 1);
		value = Bilinear(topLeft, topRight, bottomLeft, bottomRight, fx, fy);
		return true;
	}

	private static double Bilinear(double topLeft, double topRight, double bottomLeft, double bottomRight, double fx, double fy)
	{
		double top = topLeft + (topRight - topLeft) * fx;
		double bottom = bottomLeft + (bottomRight - bottomLeft) * fx;
		return top + (bottom - top) * fy;
	}

	private static Mat BuildFilledPartMask(Mat gray, PartTemplate template)
	{
		using Mat blurred = Blur(gray, 5);
		using Mat binary = Threshold(blurred, 0);
		using Mat inverted = new Mat();
		Cv2.BitwiseNot(binary, inverted);
		Mat binaryMask = SelectMaskContainingTemplateCenter(binary, template);
		Mat invertedMask = SelectMaskContainingTemplateCenter(inverted, template);
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
		Cv2.FindContours(binary, out Point[][] contours, out HierarchyIndex[] _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
		Mat mask = new Mat(binary.Size(), MatType.CV_8UC1, Scalar.Black);
		if (contours.Length == 0)
		{
			return mask;
		}
		Point2f center = new Point2f((float)template.ReferenceCenterXPixel, (float)template.ReferenceCenterYPixel);
		var selected = (from contour in contours
			select new
			{
				Contour = contour,
				Area = Cv2.ContourArea(contour),
				ContainsCenter = (Cv2.PointPolygonTest(contour, center, measureDist: false) >= 0.0)
			} into candidate
			where candidate.Area > 0.0
			orderby candidate.ContainsCenter descending, candidate.Area descending
			select candidate).FirstOrDefault();
		if (selected == null)
		{
			return mask;
		}
		Cv2.DrawContours(mask, new[] { selected.Contour }, -1, Scalar.White, -1);
		return mask;
	}

	private static int CalculateTemplateAnglePatchSize(double referenceWidthPixels, double referenceHeightPixels)
	{
		return Math.Clamp((int)Math.Round(Math.Min((referenceWidthPixels > 0.0001) ? referenceWidthPixels : 420.0, (referenceHeightPixels > 0.0001) ? referenceHeightPixels : 420.0) * 0.75), 140, 420);
	}

	private static int CalculateAutoFeaturePatchSize(double referenceWidthPixels, double referenceHeightPixels)
	{
		return Math.Clamp((int)Math.Round(Math.Min((referenceWidthPixels > 0.0001) ? referenceWidthPixels : 192.0, (referenceHeightPixels > 0.0001) ? referenceHeightPixels : 192.0) * 0.32), 64, 192);
	}

	private static Rect BuildCenteredRect(double centerX, double centerY, int requestedSize, int imageWidth, int imageHeight)
	{
		int size = Math.Clamp(requestedSize, 1, Math.Min(imageWidth, imageHeight));
		int value = (int)Math.Round(centerX - (double)size / 2.0);
		int y = (int)Math.Round(centerY - (double)size / 2.0);
		int x = Math.Clamp(value, 0, Math.Max(0, imageWidth - size));
		y = Math.Clamp(y, 0, Math.Max(0, imageHeight - size));
		return new Rect(x, y, size, size);
	}

	private static bool TryBuildCenteredRectInsideImage(double centerX, double centerY, int requestedSize, int imageWidth, int imageHeight, out Rect rect)
	{
		int size = Math.Clamp(requestedSize, 1, Math.Min(imageWidth, imageHeight));
		int x = (int)Math.Round(centerX - (double)size / 2.0);
		int y = (int)Math.Round(centerY - (double)size / 2.0);
		rect = new Rect(Math.Clamp(x, 0, Math.Max(0, imageWidth - size)), Math.Clamp(y, 0, Math.Max(0, imageHeight - size)), size, size);
		if (x >= 0 && y >= 0 && x + size <= imageWidth)
		{
			return y + size <= imageHeight;
		}
		return false;
	}

	private static bool IsRectInsideImage(Rect rect, int imageWidth, int imageHeight)
	{
		if (rect.X >= 0 && rect.Y >= 0 && rect.Right <= imageWidth)
		{
			return rect.Bottom <= imageHeight;
		}
		return false;
	}

	private static double CountThreshold(Mat image, byte threshold)
	{
		using Mat mask = new Mat();
		Cv2.Threshold(image, mask, (int)threshold, 255.0, ThresholdTypes.Binary);
		return Cv2.CountNonZero(mask);
	}

	private static double CountUnderThreshold(Mat image, byte threshold)
	{
		using Mat mask = new Mat();
		Cv2.Threshold(image, mask, (int)threshold, 255.0, ThresholdTypes.BinaryInv);
		return Cv2.CountNonZero(mask);
	}

	private static double DistancePixels(double ax, double ay, double bx, double by)
	{
		double num = ax - bx;
		double dy = ay - by;
		return Math.Sqrt(num * num + dy * dy);
	}

	private static double NormalizeAngleStep(double configuredStepDegrees, double fallbackStepDegrees)
	{
		if (!(configuredStepDegrees > 0.0001))
		{
			return fallbackStepDegrees;
		}
		return configuredStepDegrees;
	}

	private static bool HasTemplatePixelShape(PartTemplate template)
	{
		if (template.ReferenceWidthPixels > 0.0001)
		{
			return template.ReferenceHeightPixels > 0.0001;
		}
		return false;
	}

	private static double CalculateFillRatio(double areaPixels, double widthPixels, double heightPixels)
	{
		double boxArea = Math.Max(widthPixels * heightPixels, 0.0001);
		return Math.Clamp(areaPixels / boxArea, 0.0, 1.0);
	}

	private static double DistanceToTemplateCenterPixels(PartDetection detection, PartTemplate template)
	{
		double num = detection.CenterXPixel - template.ReferenceCenterXPixel;
		double dy = detection.CenterYPixel - template.ReferenceCenterYPixel;
		return Math.Sqrt(num * num + dy * dy);
	}

	private static double ApplyOutputDirection(double action, bool invertDirection)
	{
		if (!invertDirection)
		{
			return action;
		}
		return 0.0 - action;
	}

	public static ProductionSetupDecision ValidateProductionSetup(PartTemplate template, VisionParameters parameters)
	{
		ProductionSetupDecision machineSetup = ValidateMachineCalibration(parameters);
		if (!machineSetup.IsReady)
		{
			return machineSetup;
		}
		return ValidateTemplateCalibration(template, parameters);
	}

	public static ProductionSetupDecision ValidateMachineCalibration(VisionParameters parameters)
	{
		if (!parameters.CameraCalibration.Enabled)
		{
			return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.CameraCalibrationMissing);
		}
		string currentDistortionId = GetCurrentDistortionCalibrationId(parameters);
		if (!string.Equals(parameters.CameraCalibration.SourceDistortionCalibrationId, currentDistortionId, StringComparison.Ordinal))
		{
			return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.CameraCalibrationDistortionMismatch);
		}
		if (!parameters.RAxisCenterCalibration.Enabled)
		{
			return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.RAxisCenterMissing);
		}
		if (!string.Equals(parameters.RAxisCenterCalibration.SourceCameraCalibrationId, parameters.CameraCalibration.CalibrationId, StringComparison.Ordinal))
		{
			return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.RAxisCenterCameraMismatch);
		}
		return ProductionSetupDecision.Ready;
	}

	private static ProductionSetupDecision ValidateTemplateCalibration(PartTemplate template, VisionParameters parameters)
	{
		if (string.IsNullOrWhiteSpace(template.ImagePath) || !File.Exists(template.ImagePath))
		{
			return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.TemplateImageMissing);
		}
		if (string.IsNullOrWhiteSpace(template.SourceCameraCalibrationId))
		{
			return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.TemplateCameraCalibrationMissing);
		}
		if (!string.Equals(template.SourceCameraCalibrationId, parameters.CameraCalibration.CalibrationId, StringComparison.Ordinal))
		{
			return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.TemplateCameraCalibrationMismatch);
		}
		if (!string.Equals(template.SourceDistortionCalibrationId, GetCurrentDistortionCalibrationId(parameters), StringComparison.Ordinal))
		{
			return ProductionSetupDecision.Blocked(ProductionSetupBlockReason.TemplateDistortionCalibrationMismatch);
		}
		return ProductionSetupDecision.Ready;
	}

	private static void EnsureProductionCalibrationReady(Mat image, VisionParameters parameters)
	{
		if (parameters.LensDistortionCalibration.Enabled && !parameters.LensDistortionCalibration.CanApplyTo(image.Width, image.Height))
		{
			throw new InvalidOperationException($"Lens distortion calibration applies to {parameters.LensDistortionCalibration.ImageWidth}x{parameters.LensDistortionCalibration.ImageHeight}, but current image is {image.Width}x{image.Height}.");
		}
		ProductionSetupDecision setup = ValidateMachineCalibration(parameters);
		if (!setup.IsReady)
		{
			throw new InvalidOperationException(GetProductionSetupBlockMessage(setup.Reason));
		}
	}

	private static string GetProductionSetupBlockMessage(ProductionSetupBlockReason reason)
	{
		return reason switch
		{
			ProductionSetupBlockReason.CameraCalibrationMissing => "Camera calibration is missing.", 
			ProductionSetupBlockReason.CameraCalibrationDistortionMismatch => "Camera calibration does not match the current distortion calibration.", 
			ProductionSetupBlockReason.RAxisCenterMissing => "R-axis center calibration is missing.", 
			ProductionSetupBlockReason.RAxisCenterCameraMismatch => "R-axis center calibration does not match the current camera calibration.", 
			ProductionSetupBlockReason.TemplateImageMissing => "Template image is missing.", 
			ProductionSetupBlockReason.TemplateCameraCalibrationMissing => "Template camera calibration source is missing.", 
			ProductionSetupBlockReason.TemplateCameraCalibrationMismatch => "Template camera calibration source does not match the current camera calibration.", 
			ProductionSetupBlockReason.TemplateDistortionCalibrationMismatch => "Template distortion calibration source does not match the current distortion calibration.", 
			_ => "Production setup is not ready.", 
		};
	}

	private static MachinePoint GetReferenceCenterMachine(PartTemplate template, VisionParameters parameters)
	{
		ProductionSetupDecision setup = ValidateTemplateCalibration(template, parameters);
		if (!setup.IsReady)
		{
			throw new InvalidOperationException(GetProductionSetupBlockMessage(setup.Reason));
		}
		return new MachinePoint(template.ReferenceCenterXMm, template.ReferenceCenterYMm);
	}

	public static string? GetProductionSetupError(PartTemplate template, VisionParameters parameters)
	{
		ProductionSetupDecision setup = ValidateProductionSetup(template, parameters);
		if (!setup.IsReady)
		{
			return GetProductionSetupBlockMessage(setup.Reason);
		}
		return null;
	}

	public static string? GetMachineCalibrationError(VisionParameters parameters)
	{
		ProductionSetupDecision setup = ValidateMachineCalibration(parameters);
		if (!setup.IsReady)
		{
			return GetProductionSetupBlockMessage(setup.Reason);
		}
		return null;
	}

	private static void EnsureProductionSetup(Mat image, PartTemplate template, VisionParameters parameters)
	{
		EnsureProductionCalibrationReady(image, parameters);
		ProductionSetupDecision setup = ValidateTemplateCalibration(template, parameters);
		if (!setup.IsReady)
		{
			throw new InvalidOperationException(GetProductionSetupBlockMessage(setup.Reason));
		}
	}

	private static string GetCurrentDistortionCalibrationId(VisionParameters parameters)
	{
		if (!parameters.LensDistortionCalibration.Enabled)
		{
			return string.Empty;
		}
		return parameters.LensDistortionCalibration.CalibrationId;
	}

	private static Point[] OffsetContour(IEnumerable<Point> contour, Point offset)
	{
		return contour.Select((Point point) => new Point(point.X + offset.X, point.Y + offset.Y)).ToArray();
	}

	private static void DrawCandidateContours(Mat diagnostic, IReadOnlyList<PartDetection> candidates, PartDetection selected)
	{
		for (int index = 0; index < candidates.Count; index++)
		{
			PartDetection candidate = candidates[index];
			if ((object)candidate != selected)
			{
				Point[] contour = OffsetContour(candidate.Contour, candidate.Offset);
				Scalar color = new Scalar(0.0, 180.0, 255.0);
				DrawOutlinedContour(diagnostic, contour, color, 3);
				Cv2.PutText(diagnostic, $"C{index + 1}", new Point((int)Math.Round(candidate.CenterXPixel) + 8, (int)Math.Round(candidate.CenterYPixel) - 8), HersheyFonts.HersheySimplex, 0.55, color);
			}
		}
	}

	private static void DrawCenterMarkers(Mat diagnostic, PartDetection detection, PartTemplate template)
	{
		DrawOutlinedMarker(diagnostic, new Point((int)Math.Round(template.ReferenceCenterXPixel), (int)Math.Round(template.ReferenceCenterYPixel)), new Scalar(255.0, 191.0, 0.0), MarkerTypes.Cross, 36, 4);
		DrawOutlinedMarker(diagnostic, new Point((int)Math.Round(detection.CenterXPixel), (int)Math.Round(detection.CenterYPixel)), Scalar.Yellow, MarkerTypes.TiltedCross, 36, 4);
	}

	private static void DrawOutlinedContour(Mat image, Point[] contour, Scalar color, int thickness)
	{
		Cv2.DrawContours(image, new Point[1][] { contour }, -1, Scalar.Black, thickness + 4, LineTypes.AntiAlias);
		Cv2.DrawContours(image, new Point[1][] { contour }, -1, color, thickness, LineTypes.AntiAlias);
	}

	private static void DrawOutlinedMarker(Mat image, Point center, Scalar color, MarkerTypes markerType, int markerSize, int thickness)
	{
		Cv2.DrawMarker(image, center, Scalar.Black, markerType, markerSize, thickness + 4, LineTypes.AntiAlias);
		Cv2.DrawMarker(image, center, color, markerType, markerSize, thickness, LineTypes.AntiAlias);
	}

	private static void DrawOverlay(Mat diagnostic, InspectionDecision decision, string message, InspectionMeasurement measurement, AngleResolutionDiagnostic angleDiagnostic, TemplateSimilarityResult? similarity)
	{
		Scalar color = decision switch
		{
			InspectionDecision.Ok => Scalar.LimeGreen, 
			InspectionDecision.Ng => Scalar.Red, 
			_ => Scalar.OrangeRed, 
		};
		if (decision == InspectionDecision.Ng)
		{
			DrawLargeNgMarker(diagnostic);
		}
		Cv2.PutText(diagnostic, decision.ToString(), new Point(24, 44), HersheyFonts.HersheySimplex, 1.1, color, 2);
		Cv2.PutText(diagnostic, message, new Point(24, 84), HersheyFonts.HersheySimplex, 0.7, color, 2);
		Cv2.PutText(diagnostic, $"XY offset=({measurement.XOffsetMm:F3},{measurement.YOffsetMm:F3})mm comp=({measurement.XCompensationMm:F3},{measurement.YCompensationMm:F3})mm", new Point(24, 124), HersheyFonts.HersheySimplex, 0.65, Scalar.White, 2);
		Cv2.PutText(diagnostic, $"R offset={measurement.AngleOffsetDegrees:F3}deg comp={measurement.RotationCompensationDegrees:F3}deg", new Point(24, 160), HersheyFonts.HersheySimplex, 0.65, Scalar.White, 2);
		Cv2.PutText(diagnostic, $"W={measurement.WidthMm:F3}mm H={measurement.HeightMm:F3}mm score={measurement.MatchScore:F3}", new Point(24, 196), HersheyFonts.HersheySimplex, 0.65, Scalar.White, 2);
		Cv2.PutText(diagnostic, $"Angle={angleDiagnostic.Source} score={angleDiagnostic.Score:F3} margin={angleDiagnostic.ScoreMargin:F3}", new Point(24, 232), HersheyFonts.HersheySimplex, 0.65, Scalar.White, 2);
		if ((object)similarity != null)
		{
			Cv2.PutText(diagnostic, $"Shape=size {similarity.SizeScore:F3} contour {similarity.ShapeScore:F3} iou {similarity.MaskIoU:F3} edge {similarity.EdgeDistanceScore:F3}", new Point(24, 268), HersheyFonts.HersheySimplex, 0.65, Scalar.White, 2);
		}
	}

	private static void DrawLargeNgMarker(Mat diagnostic)
	{
		double fontScale = Math.Clamp((double)diagnostic.Width / 900.0, 3.6, 7.0);
		int thickness = Math.Max(8, (int)Math.Round(fontScale * 2.0));
		int padding = Math.Max(24, (int)Math.Round(fontScale * 8.0));
		int baseline = 0;
		Size textSize = Cv2.GetTextSize("NG", HersheyFonts.HersheySimplex, fontScale, thickness, out baseline);
		Point origin = new Point(padding, padding + textSize.Height);
		Point boxEnd = new Point(origin.X + textSize.Width + padding, origin.Y + baseline + padding);
		Cv2.Rectangle(diagnostic, new Point(0, 0), boxEnd, Scalar.Black, -1);
		Cv2.Rectangle(diagnostic, new Point(0, 0), boxEnd, Scalar.Red, Math.Max(4, thickness / 2));
		Cv2.PutText(diagnostic, "NG", origin, HersheyFonts.HersheySimplex, fontScale, Scalar.White, thickness + 8);
		Cv2.PutText(diagnostic, "NG", origin, HersheyFonts.HersheySimplex, fontScale, Scalar.Red, thickness);
	}
}
