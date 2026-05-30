using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JuliMvs.Camera.Hik;
using JuliMvs.Core;
using JuliMvs.Core.Batch;
using JuliMvs.Core.Camera;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Persistence;
using JuliMvs.Core.Vision;
using JuliMvs.App.Services;
using JuliMvs.Persistence;
using JuliMvs.Plc;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App;

public partial class MainWindow
{
    private static readonly bool VisionJudgmentDisabled = true;

    private async Task HandlePlcCaptureRequestAsync()
    {
        Log("收到PLC定位触发D1000=1");
        var requestStopwatch = Stopwatch.StartNew();
        var captureElapsedMilliseconds = 0L;
        var triggerClearedAfterCapture = false;

        try
        {
            var validation = _plcCaptureRequestValidator.Validate(new PlcCaptureRequestState(
                _productionEnabled,
                _changeoverTemplateRequested,
                _cameraConnected,
                _template is not null,
                _batchSession.CanInspect,
                VisionJudgmentDisabled));
            if (validation.Action != PlcCaptureRequestAction.Proceed)
            {
                await HandlePlcCaptureRequestValidationFailureAsync(validation);
                return;
            }

            Log("开始相机拍照检测");
            await ClearPlcResultCodeBeforeCaptureAsync();

            var captureStopwatch = Stopwatch.StartNew();
            var rawImagePath = await CaptureCameraImageAsync(saveImage: false);
            captureStopwatch.Stop();
            captureElapsedMilliseconds = captureStopwatch.ElapsedMilliseconds;
            await ClearPlcTriggerAfterCaptureAsync();
            triggerClearedAfterCapture = true;
            await InspectAndPersistAsync(
                _lastCameraImage!,
                rawImagePath,
                "PLC触发检测",
                writeToPlc: true,
                captureElapsedMilliseconds,
                requestStopwatch);
        }
        catch (Exception ex)
        {
            requestStopwatch.Stop();
            AddRuntimeLogLines(
                $"结果: Error  原因: {ex.Message}",
                $"耗时: 总{requestStopwatch.ElapsedMilliseconds}ms 拍照{captureElapsedMilliseconds}ms",
                "XYR: -");
            try
            {
                await WritePlcErrorResultAsync($"PLC触发检测异常: {ex.Message}", NgReason.AlgorithmError);
            }
            finally
            {
                if (!triggerClearedAfterCapture)
                {
                    await ClearPlcTriggerAfterFailedRequestAsync();
                }
            }
        }
        finally
        {
            UpdateBatchUi();
        }
    }

    private async Task ClearPlcResultCodeBeforeCaptureAsync()
    {
        var client = _plcClient;
        if (client is null || !client.IsConnected)
        {
            return;
        }

        try
        {
            await client.ClearResultCodeAsync();
            Log("PLC新触发已受理: 拍照前先清D1010=0，避免沿用上一轮OK/NG结果。");
        }
        catch (Exception ex)
        {
            Log($"PLC拍照前清D1010失败，继续本次检测: {ex.Message}");
        }
    }

    private async Task ClearPlcTriggerAfterCaptureAsync()
    {
        var client = _plcClient;
        if (client is null || !client.IsConnected)
        {
            Log("拍照已完成，但PLC未连接，无法清D1000=0。");
            return;
        }

        try
        {
            await client.ClearTriggerAsync();
            Log("拍照已完成，上位机已清D1000=0，随后继续检测并写D1010结果。");
        }
        catch (Exception ex)
        {
            Log($"拍照已完成，但上位机清D1000失败: {ex.Message}");
            throw;
        }
    }

    private async Task ClearPlcTriggerAfterFailedRequestAsync()
    {
        var client = _plcClient;
        if (client is null || !client.IsConnected)
        {
            Log("PLC触发处理失败，但PLC未连接，无法清D1000=0。");
            return;
        }

        try
        {
            await client.ClearTriggerAsync();
            Log("PLC触发处理失败，D1010错误结果已写入，上位机已清D1000=0。");
        }
        catch (Exception ex)
        {
            Log($"PLC触发处理失败，但上位机清D1000失败: {ex.Message}");
            throw;
        }
    }

    private async Task HandlePlcCaptureRequestValidationFailureAsync(PlcCaptureRequestDecision validation)
    {
        var messages = PlcCaptureRequestMessageFormatter.Format(validation.Reason);
        if (messages.LogMessage is not null)
        {
            Log(messages.LogMessage);
        }

        if (messages.UserMessage is not null)
        {
            MessageText.Text = messages.UserMessage;
        }

        if (validation.Action == PlcCaptureRequestAction.WritePlcError)
        {
            try
            {
                await WritePlcErrorResultAsync(
                    messages.PlcErrorMessage ?? "\u0050\u004c\u0043\u89e6\u53d1\u68c0\u6d4b\u5931\u8d25\u3002",
                    validation.NgReason ?? NgReason.PlcError);
            }
            finally
            {
                await ClearPlcTriggerAfterFailedRequestAsync();
            }
        }
        else if (validation.Action == PlcCaptureRequestAction.Ignore)
        {
            await ClearPlcTriggerAfterIgnoredRequestAsync();
        }
    }

    private async Task ClearPlcTriggerAfterIgnoredRequestAsync()
    {
        var client = _plcClient;
        if (client is null || !client.IsConnected)
        {
            Log("PLC触发已忽略，但PLC未连接，无法清D1000=0。");
            return;
        }

        try
        {
            await client.ClearTriggerAsync();
            _plcTriggerGate.MarkTriggerCleared();
            SetPlcStatus("PLC通讯正常", isNormal: true);
            Log("PLC触发已忽略，上位机已清D1000=0。");
        }
        catch (Exception ex)
        {
            SetPlcStatus("PLC通讯异常", isNormal: false);
            Log($"PLC触发已忽略，但上位机清D1000失败: {ex.Message}");
        }
    }

    private async Task WritePlcErrorResultAsync(string message, NgReason reason)
    {
        MessageText.Text = message;
        Log(message);

        var result = InspectionResult.Error(
            string.IsNullOrWhiteSpace(_batchSession.BatchNo) ? "UNKNOWN" : _batchSession.BatchNo,
            DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff"),
            reason,
            message);
        _lastInspectionResult = result;

        await WritePlcResultIfConnectedAsync(result);
    }

    private void RenderResult(InspectionResult result)
    {
        MessageText.Text = result.Message;
    }

    private void ClearCurrentInspection()
    {
        _template = null;
        _templateImagePath = null;
        _lastInspectionResult = null;
        _lastRawImagePath = null;
        ClearPendingProductionNgDiagnosticOverlay();
        ResultImage.Source = null;
        RuntimeTemplateText.Text = "模板: -";
        RuntimeCurrentPartText.Text = "当前: -";
        RuntimeCurrentPartText.Foreground = Brushes.Black;
    }

    private void RenderTemplateSummary(PartTemplate template)
    {
        Log($"当前型号标准位: X={template.ReferenceCenterXMm:F3}mm, Y={template.ReferenceCenterYMm:F3}mm, R={template.ReferenceAngleDegrees:F3}deg; 像素中心=({template.ReferenceCenterXPixel:F1}px,{template.ReferenceCenterYPixel:F1}px)");
        UpdateRuntimeTemplatePanel(template);
    }

    private void UpdateBatchUi()
    {
        UpdateRunStopUi();
        UpdateProductionSummaryUi();
    }

    private void ResetProductionCounters()
    {
        _productionTotalCount = 0;
        _productionOkCount = 0;
        _productionNgCount = 0;
        UpdateProductionSummaryUi();
    }

    private void CountProductionResult(InspectionResult result)
    {
        if (result.Decision is not (InspectionDecision.Ok or InspectionDecision.Ng or InspectionDecision.Error))
        {
            return;
        }

        _productionTotalCount++;
        if (result.Decision == InspectionDecision.Ok)
        {
            _productionOkCount++;
        }
        else
        {
            _productionNgCount++;
        }

        UpdateProductionSummaryUi();
    }

    private void UpdateProductionSummaryUi()
    {
        var productName = !string.IsNullOrWhiteSpace(_batchSession.ProductName)
            ? _batchSession.ProductName
            : _currentProductName;
        var batchNo = !string.IsNullOrWhiteSpace(_batchSession.BatchNo)
            ? _batchSession.BatchNo
            : _currentBatchNo;

        ProductionSummaryText.Text = $"型号: {FormatSummaryValue(productName)}";
        ProductionBatchText.Text = $"批次: {FormatSummaryValue(batchNo)}";
        ProductionCountText.Text = $"总量: {_productionTotalCount}";
    }

    private static string FormatSummaryValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }

    private static void SetImage(System.Windows.Controls.Image target, string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        target.Source = image;
    }

    private async Task InspectAndPersistAsync(
        Mat image,
        string? rawImagePath,
        string logPrefix,
        bool writeToPlc = false,
        long captureElapsedMilliseconds = 0,
        Stopwatch? totalStopwatch = null)
    {
        if (VisionJudgmentDisabled && writeToPlc)
        {
            await InspectAndPersistWithoutVisionJudgmentAsync(
                image,
                rawImagePath,
                logPrefix,
                captureElapsedMilliseconds,
                totalStopwatch);
            return;
        }

        if (_template is null)
        {
            throw new InvalidOperationException("请先建立当前型号标准位/模板。");
        }

        totalStopwatch ??= Stopwatch.StartNew();
        var inspectionStopwatch = Stopwatch.StartNew();
        var activeParameters = ReadVisionParameters();
        var run = await _inspectionRunCoordinator.RunAsync(new InspectionRunRequest(
            image,
            rawImagePath,
            _template,
            activeParameters,
            logPrefix,
            writeToPlc,
            _plcClient?.IsConnected == true,
            _plcIpAddress,
            _plcPort,
            GetEffectivePlcOutputTransform(),
            BuildDiagnosticImage: !writeToPlc));
        inspectionStopwatch.Stop();

        var result = run.Result;
        var output = run.Output;
        foreach (var log in run.Logs)
        {
            Log(log);
        }

        _lastInspectionResult = result;
        _lastRawImagePath = rawImagePath;

        var renderStopwatch = Stopwatch.StartNew();
        if (run.ResultImagePath is not null)
        {
            SetImage(ResultImage, run.ResultImagePath);
        }
        else if (writeToPlc && result.Decision == InspectionDecision.Ok)
        {
            ResultImage.Source = CreatePreviewBitmapImageFromMat(image);
        }
        else
        {
            ResultImage.Source = CreateBitmapImageFromMat(output.DiagnosticImage);
        }

        RenderResult(result);
        renderStopwatch.Stop();
        if (result.Decision is InspectionDecision.Ok or InspectionDecision.Ng)
        {
        }

        Log($"{logPrefix}: {result.Decision}: {result.Message}");
        var writeVerboseInspectionLog = !writeToPlc || result.Decision != InspectionDecision.Ok;
        if (writeVerboseInspectionLog)
        {
            if (run.ReportPath is not null)
            {
                Log($"检测诊断报告已保存: {run.ReportPath}");
            }
            else if (run.ReportError is not null)
            {
                Log($"检测诊断报告保存失败: {run.ReportError}");
            }

            Log(_inspectionDiagnosticMessageFormatter.BuildCandidateDiagnosticsText(output.CandidateDiagnostics));
            Log(_inspectionDiagnosticMessageFormatter.BuildAngleCandidatesText(output.AngleDiagnostic));
            LogPlcOutputPreview(result, output.AlignmentSnapshot);
        }
        var plcStopwatch = Stopwatch.StartNew();
        if (writeToPlc)
        {
            await WritePlcResultIfConnectedAsync(result);
        }
        plcStopwatch.Stop();

        if (result.Decision == InspectionDecision.Ok && result.Measurement is { } measurement)
        {
            MessageText.Text = _plcOutputDiagnosticFormatter.BuildOkMessage(
                measurement,
                _plcOutputTransform,
                writeToPlc);
        }

        totalStopwatch.Stop();
        if (writeToPlc)
        {
            CountProductionResult(result);
        }

        AddInspectionRuntimeSummary(
            result,
            captureElapsedMilliseconds,
            inspectionStopwatch.ElapsedMilliseconds,
            renderStopwatch.ElapsedMilliseconds,
            plcStopwatch.ElapsedMilliseconds,
            totalStopwatch.ElapsedMilliseconds,
            run.Timings);
    }

    private async Task InspectAndPersistWithoutVisionJudgmentAsync(
        Mat image,
        string? rawImagePath,
        string logPrefix,
        long captureElapsedMilliseconds,
        Stopwatch? totalStopwatch)
    {
        totalStopwatch ??= Stopwatch.StartNew();
        var inspectionStopwatch = Stopwatch.StartNew();
        var bypassTimings = new BypassInspectionTimings();
        var contourJudgment = BuildContourJudgment(image, bypassTimings);
        var contourJudgmentText = contourJudgment.LogLine;
        var result = CreateBypassInspectionResult(rawImagePath, contourJudgment, bypassTimings, out var productionOutputLog);
        var saveDiagnosticStopwatch = Stopwatch.StartNew();
        result = SaveProductionNgImagesIfNeeded(image, result);
        saveDiagnosticStopwatch.Stop();
        var saveResultStopwatch = Stopwatch.StartNew();
        await _repository.SaveResultAsync(result);
        saveResultStopwatch.Stop();
        inspectionStopwatch.Stop();

        _lastInspectionResult = result;
        _lastRawImagePath = result.RawImagePath ?? rawImagePath;

        var renderStopwatch = Stopwatch.StartNew();
        if (result.ResultImagePath is not null)
        {
            SetImage(ResultImage, result.ResultImagePath);
        }
        else
        {
            ResultImage.Source = CreatePreviewBitmapImageFromMat(image);
        }
        RenderResult(result);
        renderStopwatch.Stop();

        Log($"{logPrefix}: {result.Decision}: {result.Message}");
        Log(productionOutputLog);
        Log(contourJudgmentText);

        var plcStopwatch = Stopwatch.StartNew();
        await WritePlcResultIfConnectedAsync(result);
        plcStopwatch.Stop();

        if (result.Decision == InspectionDecision.Ok && result.Measurement is { } measurement)
        {
            MessageText.Text = _plcOutputDiagnosticFormatter.BuildOkMessage(
                measurement,
                _plcOutputTransform,
                writeToPlc: true);
        }

        totalStopwatch.Stop();
        CountProductionResult(result);

        AddInspectionRuntimeSummary(
            result,
            captureElapsedMilliseconds,
            inspectionStopwatch.ElapsedMilliseconds,
            renderStopwatch.ElapsedMilliseconds,
            plcStopwatch.ElapsedMilliseconds,
            totalStopwatch.ElapsedMilliseconds,
            new InspectionRunTimings(
                VisionMs: bypassTimings.TotalVisionMilliseconds,
                SaveResultMs: saveResultStopwatch.ElapsedMilliseconds,
                SaveDiagnosticImageMs: saveDiagnosticStopwatch.ElapsedMilliseconds,
                SaveReportMs: 0,
                bypassTimings.ToVisionStageTimings()),
            contourJudgmentText);
    }

    private InspectionResult CreateBypassInspectionResult(
        string? rawImagePath,
        ContourJudgmentAnalysis contourJudgment,
        BypassInspectionTimings timings,
        out string productionOutputLog)
    {
        if (ShouldApplyBypassBackSideNg(contourJudgment, out var ngMessage))
        {
            productionOutputLog = "生产正反面检测: 已判定反面NG，XYR仍为零补偿，PLC通信流程不变。";
            return ProductionInspectionResultFactory.CreateBackSideNg(
                _batchSession.BatchNo,
                ngMessage,
                rawImagePath);
        }

        if (TryBuildProductionMeasurement(contourJudgment, timings, out var measurement, out var reason, out var ngReason))
        {
            productionOutputLog = "生产检测: 未发现反面NG/缺料崩边NG，轮廓角度可靠，已输出XYR纠偏。";
            return ProductionInspectionResultFactory.CreateOk(
                _batchSession.BatchNo,
                measurement,
                ProductionInspectionResultFactory.OkMessage,
                rawImagePath);
        }

        var unsafeXyrMessage = $"轮廓检测NG: {reason}，XYR已清零。";
        productionOutputLog = $"生产检测: {unsafeXyrMessage}";
        return ProductionInspectionResultFactory.CreateUnsafeXyrNg(
            _batchSession.BatchNo,
            unsafeXyrMessage,
            rawImagePath,
            ngReason: ngReason);
    }

    private bool TryBuildProductionMeasurement(
        ContourJudgmentAnalysis contourJudgment,
        BypassInspectionTimings timings,
        out InspectionMeasurement measurement,
        out string reason,
        out NgReason ngReason)
    {
        measurement = CreateZeroProductionMeasurement();
        reason = string.Empty;
        ngReason = NgReason.MatchFailed;
        ClearPendingProductionNgDiagnosticOverlay();

        var template = _template;
        if (template is null)
        {
            reason = "未加载当前型号标准位/模板";
            return false;
        }

        var currentFeature = contourJudgment.Feature;
        if (currentFeature is null)
        {
            reason = "当前图片外轮廓提取失败";
            ngReason = NgReason.ShapeOutOfTolerance;
            return false;
        }

        var templateFeature = _bypassLogTemplateFeature;
        if (templateFeature is null)
        {
            reason = "未加载模板外轮廓特征";
            return false;
        }

        var activeParameters = ReadVisionParameters();
        var setup = OpenCvVisionService.ValidateProductionSetup(template, activeParameters);
        if (!setup.IsReady)
        {
            reason = ProductionSetupMessageFormatter.FormatBlockMessage(setup.Reason);
            return false;
        }

        var shapeStopwatch = Stopwatch.StartNew();
        var angleResult = _productionAutoAngleResolver.Resolve(
            currentFeature,
            templateFeature,
            template,
            contourJudgment.ShapeFrontBackMatch?.Front,
            activeParameters.FourWaySymmetricEnabled);
        shapeStopwatch.Stop();
        timings.ShapeMatchMilliseconds += shapeStopwatch.ElapsedMilliseconds;
        if (!angleResult.IsReliable)
        {
            reason = angleResult.Message;
            ngReason = NgReason.ShapeOutOfTolerance;
            return false;
        }

        var reliability = ProductionContourReliabilityGuard.Evaluate(
            currentFeature,
            templateFeature,
            template,
            angleResult.MatchScore);
        if (!reliability.IsReliable)
        {
            reason = reliability.Reason;
            ngReason = NgReason.ShapeOutOfTolerance;
            return false;
        }
        if (!string.IsNullOrWhiteSpace(reliability.Warning))
        {
            Log($"生产轮廓提示: {reliability.Warning}");
        }

        var currentCenter = activeParameters.CameraCalibration.PixelToMachine(
            angleResult.CenterXPixel,
            angleResult.CenterYPixel);
        var referenceCenter = new MachinePoint(
            template.ReferenceCenterXMm,
            template.ReferenceCenterYMm);
        var alignmentSnapshot = XyrAlignmentSolver.Solve(
            new PartPose2D(currentCenter.XMm, currentCenter.YMm, angleResult.ResolvedAngleDegrees, angleResult.MatchScore),
            new PartPose2D(referenceCenter.XMm, referenceCenter.YMm, template.ReferenceAngleDegrees, template.MatchScoreBaseline),
            activeParameters.RAxisCenterCalibration,
            activeParameters.InvertRotationCompensation ? -1 : 1,
            angleResult.AllowsFullRotation);
        measurement = new InspectionMeasurement(
            angleResult.CenterXPixel,
            angleResult.CenterYPixel,
            alignmentSnapshot.XOffsetMm,
            alignmentSnapshot.YOffsetMm,
            alignmentSnapshot.HomeXActionMm,
            alignmentSnapshot.HomeYActionMm,
            angleResult.ResolvedAngleDegrees,
            alignmentSnapshot.AngleOffsetDegrees,
            alignmentSnapshot.HomeRActionDegrees,
            template.WidthMm,
            template.HeightMm,
            currentFeature.AreaPixels,
            angleResult.MatchScore);
        Log($"生产Shape配准: {angleResult.Message}");
        if (angleResult.SkipMissingMaterialDetection)
        {
            Log("缺料精检跳过: Shape兜底时不做mask精对齐差分，只保留肉眼可见边缘缺损粗判，避免良品误NG。");
            return true;
        }

        var defectStopwatch = Stopwatch.StartNew();
        var defect = _productionMissingMaterialDetector.Evaluate(
            currentFeature,
            templateFeature,
            template,
            angleResult,
            buildDiagnosticOverlay: true);
        defectStopwatch.Stop();
        timings.DecisionMilliseconds += defectStopwatch.ElapsedMilliseconds;
        Log(defect.Message);
        if (!defect.IsPass)
        {
            _pendingProductionNgDiagnosticOverlay?.Dispose();
            _pendingProductionNgDiagnosticOverlay = defect.DiagnosticOverlay;
            measurement = CreateZeroProductionMeasurement();
            reason = defect.Message;
            ngReason = NgReason.MissingMaterial;
            return false;
        }

        return true;
    }

    private InspectionResult SaveProductionNgImagesIfNeeded(Mat image, InspectionResult result)
    {
        if (result.Decision == InspectionDecision.Ok)
        {
            ClearPendingProductionNgDiagnosticOverlay();
            return result;
        }

        var rawImagePath = result.RawImagePath;
        var resultImagePath = result.ResultImagePath;
        try
        {
            rawImagePath ??= _inspectionFileStore.SaveInspectionRawImage(
                image,
                result.BatchNo,
                result.PartNo);
            if (resultImagePath is null)
            {
                using var diagnostic = BuildProductionNgDiagnosticImage(image, result, _pendingProductionNgDiagnosticOverlay);
                resultImagePath = _inspectionFileStore.SaveDiagnosticImage(
                    diagnostic,
                    result.BatchNo,
                    result.PartNo);
            }
            Log($"生产NG原图已保存: {rawImagePath}");
            Log($"生产NG诊断图已保存: {resultImagePath}");
        }
        catch (Exception ex)
        {
            Log($"生产NG图片保存失败: {ex.Message}");
        }
        finally
        {
            ClearPendingProductionNgDiagnosticOverlay();
        }

        return result with
        {
            RawImagePath = rawImagePath,
            ResultImagePath = resultImagePath
        };
    }

    private static Mat BuildProductionNgDiagnosticImage(Mat image, InspectionResult result, Mat? diagnosticOverlay = null)
    {
        var diagnostic = diagnosticOverlay is not null && !diagnosticOverlay.Empty()
            ? diagnosticOverlay.Clone()
            : image.Channels() == 1
                ? image.CvtColor(ColorConversionCodes.GRAY2BGR)
                : image.Clone();
        var overlay = result.NgReason == NgReason.MissingMaterial
            ? "NG edge missing"
            : "NG 轮廓匹配不稳";
        Cv2.PutText(
            diagnostic,
            overlay,
            new OpenCvSharp.Point(60, 160),
            HersheyFonts.HersheySimplex,
            4.0,
            Scalar.Red,
            10,
            LineTypes.AntiAlias);
        return diagnostic;
    }

    private void ClearPendingProductionNgDiagnosticOverlay()
    {
        _pendingProductionNgDiagnosticOverlay?.Dispose();
        _pendingProductionNgDiagnosticOverlay = null;
    }

    private static InspectionMeasurement CreateZeroProductionMeasurement()
    {
        return new InspectionMeasurement(
            CenterXPixel: 0,
            CenterYPixel: 0,
            XOffsetMm: 0,
            YOffsetMm: 0,
            XCompensationMm: 0,
            YCompensationMm: 0,
            AngleDegrees: 0,
            AngleOffsetDegrees: 0,
            RotationCompensationDegrees: 0,
            WidthMm: 0,
            HeightMm: 0,
            AreaPixels: 0,
            MatchScore: 0);
    }

    private bool ShouldApplyBypassBackSideNg(
        ContourJudgmentAnalysis contourJudgment,
        out string message)
    {
        message = string.Empty;
        if (!_visionParameters.BackSideNgEnabled)
        {
            return false;
        }

        var shapeFrontBack = contourJudgment.ShapeFrontBackMatch;
        if (shapeFrontBack is null ||
            shapeFrontBack.Decision != ContourFrontBackDecision.Back ||
            !shapeFrontBack.IsReliable)
        {
            return false;
        }

        message =
            "反面NG: Shape匹配更接近镜像模板，" +
            $"正面误差={shapeFrontBack.Front.ErrorPixels:F2}px，" +
            $"镜像误差={shapeFrontBack.Back.ErrorPixels:F2}px，" +
            $"分离={shapeFrontBack.SeparationPixels:F2}px。";
        return true;
    }

    private ContourJudgmentAnalysis BuildContourJudgment(Mat image, BypassInspectionTimings timings)
    {
        try
        {
            var featureStopwatch = Stopwatch.StartNew();
            var feature = _contourFeatureExtractor.Extract(image, ReadVisionParameters());
            featureStopwatch.Stop();
            timings.DetectPartMilliseconds += featureStopwatch.ElapsedMilliseconds;
            var frontBack = AnalyzeContourFrontBack(feature, timings);
            var frontBackSegment = BuildContourFrontBackLogSegment(frontBack);
            var logLine =
                "判断: " +
                $"{FormatAutoPartShapeClass(feature.Strategy.ShapeClass)}，" +
                $"R={(feature.Strategy.AllowsRCorrection ? "可计算" : "锁定0")}，" +
                $"方法={FormatAutoAngleMethod(feature.Strategy.Method)}，" +
                $"中心=({feature.CenterXPixel:F1},{feature.CenterYPixel:F1})px，" +
                $"轴比={feature.AxisRatio:F3}，" +
                $"PCA={feature.PcaRatio:F3}，" +
                $"圆度={feature.Circularity:F3}，" +
                $"半径特征={feature.RadiusSignalPixels:F2}px" +
                $"{frontBackSegment}。";
            return new ContourJudgmentAnalysis(logLine, feature, frontBack.ShapeMatch);
        }
        catch (Exception ex)
        {
            return new ContourJudgmentAnalysis($"判断: 外轮廓分析失败，原因={ex.Message}", null, null);
        }
    }

    private ContourFrontBackAnalysis AnalyzeContourFrontBack(
        ContourFeatureExtraction currentFeature,
        BypassInspectionTimings timings)
    {
        if (_bypassLogTemplateFeature is null)
        {
            return new ContourFrontBackAnalysis("，正反=无参考模板", null);
        }

        if (!string.IsNullOrWhiteSpace(_bypassLogTemplateProductName) &&
            !string.Equals(_bypassLogTemplateProductName, _currentProductName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new ContourFrontBackAnalysis($"，正反=参考模板型号不一致，参考={_bypassLogTemplateProductName}", null);
        }

        if (!_visionParameters.BackSideNgEnabled)
        {
            return new ContourFrontBackAnalysis("，正反=未启用", null);
        }

        var frontBackStopwatch = Stopwatch.StartNew();
        var match = _productionAutoAngleResolver.MatchFrontBack(currentFeature, _bypassLogTemplateFeature);
        frontBackStopwatch.Stop();
        timings.FrontBackMilliseconds += frontBackStopwatch.ElapsedMilliseconds;
        if (match.Decision == ContourFrontBackDecision.Unavailable)
        {
            return new ContourFrontBackAnalysis($"，正反=不可用，原因={SimplifyFrontBackMessage(match.Message)}", match);
        }

        return new ContourFrontBackAnalysis(
            $"，正反={FormatContourFrontBackDecision(match.Decision)}({FormatContourFrontBackModeText()})" +
            $"，正面Shape误差={FormatPixels(match.Front.ErrorPixels)}px" +
            $"，镜像Shape误差={FormatPixels(match.Back.ErrorPixels)}px" +
            $"，分离={FormatPixels(match.SeparationPixels)}px",
            match);
    }

    private string BuildContourFrontBackLogSegment(ContourFrontBackAnalysis analysis)
    {
        return analysis.LogSegment;
    }

    private string FormatContourFrontBackModeText()
    {
        return _visionParameters.BackSideNgEnabled ? "启用反面NG" : "仅记录";
    }

    private static string FormatContourFrontBackDecision(ContourFrontBackDecision decision)
    {
        return decision switch
        {
            ContourFrontBackDecision.Front => "正面",
            ContourFrontBackDecision.Back => "反面",
            ContourFrontBackDecision.Uncertain => "不确定",
            ContourFrontBackDecision.Unavailable => "不可用",
            _ => decision.ToString()
        };
    }

    private static string FormatPixels(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? "-"
            : value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string SimplifyFrontBackMessage(string message)
    {
        const string unavailablePrefix = "正反面判断不可用: ";
        const string uncertainPrefix = "正反面判断不确定: ";
        if (message.StartsWith(unavailablePrefix, StringComparison.Ordinal))
        {
            return message[unavailablePrefix.Length..].TrimEnd('。');
        }

        if (message.StartsWith(uncertainPrefix, StringComparison.Ordinal))
        {
            return message[uncertainPrefix.Length..].TrimEnd('。');
        }

        return message.TrimEnd('。');
    }

    private sealed record ContourJudgmentAnalysis(
        string LogLine,
        ContourFeatureExtraction? Feature,
        ContourShapeFrontBackMatch? ShapeFrontBackMatch);

    private sealed record ContourFrontBackAnalysis(
        string LogSegment,
        ContourShapeFrontBackMatch? ShapeMatch);

    private sealed class BypassInspectionTimings
    {
        public long DetectPartMilliseconds { get; set; }

        public long ShapeMatchMilliseconds { get; set; }

        public long DecisionMilliseconds { get; set; }

        public long FrontBackMilliseconds { get; set; }

        public long TotalVisionMilliseconds =>
            DetectPartMilliseconds +
            ShapeMatchMilliseconds +
            DecisionMilliseconds +
            FrontBackMilliseconds;

        public VisionStageTimings ToVisionStageTimings()
        {
            return new VisionStageTimings(
                PrepareImageMs: 0,
                DetectPartMs: DetectPartMilliseconds,
                ResolveAngleMs: ShapeMatchMilliseconds,
                TemplateSimilarityMs: 0,
                AlignmentMs: 0,
                DecisionMs: DecisionMilliseconds,
                FrontBackMs: FrontBackMilliseconds,
                OverlayMs: 0);
        }
    }

    private static string FormatAutoPartShapeClass(AutoPartShapeClass shapeClass)
    {
        return shapeClass switch
        {
            AutoPartShapeClass.StrongEllipse => "明显椭圆",
            AutoPartShapeClass.IrregularRound => "不规则圆/带缺口",
            AutoPartShapeClass.WeakEllipse => "无强主方向微椭圆",
            AutoPartShapeClass.NearCircle => "完美近圆/方向弱",
            _ => shapeClass.ToString()
        };
    }

    private static string FormatAutoAngleMethod(AutoAngleMethod method)
    {
        return method switch
        {
            AutoAngleMethod.PcaAxis => "长轴/PCA",
            AutoAngleMethod.ContourPolar => "外轮廓极坐标",
            AutoAngleMethod.Disabled => "不算R",
            _ => method.ToString()
        };
    }

    private async Task WritePlcResultIfConnectedAsync(InspectionResult result)
    {
        try
        {
            var outcome = await _plcInspectionResultWriter.WriteInspectionResultAsync(
                _plcClient,
                result,
                GetEffectivePlcOutputTransform(),
                InspectionDiagnosticMessageFormatter.FormatPlcOutputTransform(GetEffectivePlcOutputTransform()),
                Log);
            SavePassivePlcVerificationReport(result, outcome);
            if (outcome.ShouldSetPlcStatusNormal)
            {
                SetPlcStatus("PLC通讯正常", isNormal: true);
            }
        }
        catch (Exception ex)
        {
            SetPlcStatus("PLC通讯异常", isNormal: false);
            Log($"PLC写入失败: {ex.Message}");
            throw;
        }
    }

    private void SavePassivePlcVerificationReport(
        InspectionResult result,
        PlcInspectionResultWriteOutcome outcome)
    {
        if (!outcome.IsConnected)
        {
            return;
        }

        var reportPath = _reportSaveService.TrySavePassivePlcVerificationReport(
            new PassivePlcVerificationReportSaveContext(
                result,
                "PLC自动触发被动验证",
                outcome,
                _plcIpAddress,
                _plcPort,
                GetEffectivePlcOutputTransform()),
            Log);
        if (reportPath is not null)
        {
            Log($"被动PLC验证报告已保存: {reportPath}");
        }
    }

    private void LogPlcOutputPreview(
        InspectionResult result,
        XyrAlignmentSnapshot? alignmentSnapshot = null)
    {
        foreach (var line in _plcOutputDiagnosticFormatter.BuildPreviewLogs(
            result,
            GetEffectivePlcOutputTransform(),
            alignmentSnapshot))
        {
            Log(line);
        }
    }

    private void AddInspectionRuntimeSummary(
        InspectionResult result,
        long captureElapsedMilliseconds,
        long inspectionElapsedMilliseconds,
        long renderElapsedMilliseconds,
        long plcElapsedMilliseconds,
        long totalElapsedMilliseconds,
        InspectionRunTimings timings,
        string? judgmentLine = null)
    {
        var lines = new List<string>
        {
            $"结果: {FormatDecisionText(result)}  原因: {FormatNgReason(result)}",
            $"耗时: 总{totalElapsedMilliseconds}ms 拍照{captureElapsedMilliseconds}ms 视觉{timings.VisionMs}ms PLC{plcElapsedMilliseconds}ms",
            FormatVisionStageTimingLine(timings.StageTimings),
            FormatRuntimeXyrLine(result)
        };
        if (!string.IsNullOrWhiteSpace(judgmentLine))
        {
            lines.Add(judgmentLine);
        }

        UpdateRuntimeCurrentPartPanel(result, captureElapsedMilliseconds, totalElapsedMilliseconds, judgmentLine);
        AddRuntimeLogLines(lines.ToArray());
    }

    private static string FormatVisionStageTimingLine(VisionStageTimings timings)
    {
        return
            "视觉明细: " +
            $"畸变/预处理{timings.PrepareImageMs}ms  " +
            $"找轮廓{timings.DetectPartMs}ms  " +
            $"角度{timings.ResolveAngleMs}ms  " +
            $"模板相似{timings.TemplateSimilarityMs}ms  " +
            $"XYR{timings.AlignmentMs}ms  " +
            $"判定{timings.DecisionMs}ms  " +
            $"正反面{timings.FrontBackMs}ms  " +
            $"叠加{timings.OverlayMs}ms";
    }

    private void UpdateRuntimeTemplatePanel(PartTemplate template)
    {
        var shapeText = _bypassLogTemplateFeature is null
            ? "-"
            : FormatAutoPartShapeClass(_bypassLogTemplateFeature.Strategy.ShapeClass);
        var backSideText = _visionParameters.BackSideNgEnabled ? "开" : "关";
        var fourWayText = _visionParameters.FourWaySymmetricEnabled ? "开" : "关";
        RuntimeTemplateText.Text =
            $"型号: {FormatSummaryValue(template.ProductName)}\n" +
            $"标准: X={FormatRuntimeNumber(template.ReferenceCenterXMm)}  " +
            $"Y={FormatRuntimeNumber(template.ReferenceCenterYMm)}  " +
            $"R={FormatRuntimeNumber(template.ReferenceAngleDegrees)}\n" +
            $"形态: {shapeText}    反面NG: {backSideText}    四边对称: {fourWayText}";
    }

    private void UpdateRuntimeCurrentPartPanel(
        InspectionResult result,
        long captureElapsedMilliseconds,
        long totalElapsedMilliseconds,
        string? judgmentLine)
    {
        var decisionText = FormatDecisionText(result);
        var reasonText = FormatNgReason(result);
        var xyrText = FormatRuntimeXyrLine(result);
        RuntimeCurrentPartText.Text =
            $"结果: {decisionText}    原因: {reasonText}\n" +
            $"{xyrText}\n" +
            $"耗时: 总{totalElapsedMilliseconds}ms  拍照{captureElapsedMilliseconds}ms";
        RuntimeCurrentPartText.Foreground = result.Decision == InspectionDecision.Ok
            ? Brushes.ForestGreen
            : result.Decision is InspectionDecision.Ng or InspectionDecision.Error
                ? Brushes.Red
                : Brushes.Black;
    }

    private static string FormatNgReason(InspectionResult result)
    {
        if (result.Decision == InspectionDecision.Ok)
        {
            return "OK";
        }

        return result.NgReason switch
        {
            NgReason.MatchFailed => "定位失败",
            NgReason.SizeOutOfTolerance => "尺寸超差",
            NgReason.ShapeOutOfTolerance => "轮廓匹配不稳",
            NgReason.HoleOutOfTolerance => "孔位超差",
            NgReason.CameraError => "相机异常",
            NgReason.PlcError => "PLC异常",
            NgReason.AlgorithmError => "算法异常",
            NgReason.BackSideDetected => "反面",
            NgReason.FrontBumpMissing => "正面特征缺失",
            NgReason.MissingMaterial => "边缘缺损/缺料",
            _ => result.NgReason.ToString()
        };
    }

    private static string FormatDecisionText(InspectionResult result)
    {
        return result.Decision == InspectionDecision.Ok
            ? "OK"
            : result.Decision == InspectionDecision.Ng
                ? "NG"
                : result.Decision.ToString();
    }

    private static string FormatRuntimeXyrLine(InspectionResult result)
    {
        var measurement = result.Measurement;
        if (measurement is null)
        {
            return "XYR: -";
        }

        return
            "XYR: " +
            $"X={FormatRuntimeNumber(measurement.XCompensationMm)}  " +
            $"Y={FormatRuntimeNumber(measurement.YCompensationMm)}  " +
            $"R={FormatRuntimeNumber(measurement.RotationCompensationDegrees)}";
    }

    private static string FormatRuntimeNumber(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return Math.Abs(rounded) < 0.005
            ? "0"
            : rounded.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void AddRuntimeLogLines(params string[] lines)
    {
        foreach (var line in lines)
        {
            _fileLogger.Write(line);
        }
    }

    private PlcOutputCommand CalculatePlcOutputCommand(InspectionMeasurement measurement)
    {
        return PlcOutputDiagnosticFormatter.CalculatePlcOutputCommand(measurement, GetEffectivePlcOutputTransform());
    }

    private void Log(string message)
    {
        _fileLogger.Write(message);
    }
}
