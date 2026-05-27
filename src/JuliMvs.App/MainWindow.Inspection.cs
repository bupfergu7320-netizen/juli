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
                $"耗时: 总{requestStopwatch.ElapsedMilliseconds}ms  拍照{captureElapsedMilliseconds}ms",
                "XYR: 无有效测量值");
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
        ResultImage.Source = null;
    }

    private void RenderTemplateSummary(PartTemplate template)
    {
        Log($"当前型号标准位: X={template.ReferenceCenterXMm:F3}mm, Y={template.ReferenceCenterYMm:F3}mm, R={template.ReferenceAngleDegrees:F3}deg; 像素中心=({template.ReferenceCenterXPixel:F1}px,{template.ReferenceCenterYPixel:F1}px)");
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
        var contourJudgment = BuildContourJudgment(image);
        var contourJudgmentText = contourJudgment.LogLine;
        var result = CreateBypassInspectionResult(rawImagePath, contourJudgment, out var productionOutputLog);
        await _repository.SaveResultAsync(result);
        inspectionStopwatch.Stop();

        _lastInspectionResult = result;
        _lastRawImagePath = rawImagePath;

        var renderStopwatch = Stopwatch.StartNew();
        ResultImage.Source = CreatePreviewBitmapImageFromMat(image);
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
                VisionMs: 0,
                SaveResultMs: inspectionStopwatch.ElapsedMilliseconds,
                SaveDiagnosticImageMs: 0,
                SaveReportMs: 0,
                VisionStageTimings.Empty),
            contourJudgmentText);
    }

    private InspectionResult CreateBypassInspectionResult(
        string? rawImagePath,
        ContourJudgmentAnalysis contourJudgment,
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

        var measurement = BuildProductionMeasurementOrZero(contourJudgment, out var measurementLog, out var okMessage);
        productionOutputLog = measurementLog;
        return ProductionInspectionResultFactory.CreateOk(
            _batchSession.BatchNo,
            measurement,
            okMessage,
            rawImagePath);
    }

    private InspectionMeasurement BuildProductionMeasurementOrZero(
        ContourJudgmentAnalysis contourJudgment,
        out string productionOutputLog,
        out string resultMessage)
    {
        if (TryBuildProductionMeasurement(contourJudgment, out var measurement, out var reason))
        {
            productionOutputLog = "生产正反面检测: 未发现反面NG，已输出XYR纠偏。";
            resultMessage = ProductionInspectionResultFactory.OkMessage;
            return measurement;
        }

        productionOutputLog = $"生产正反面检测: 未发现反面NG，但XYR不可用，PLC输出OK零补偿。原因={reason}";
        resultMessage = $"{ProductionInspectionResultFactory.ZeroCorrectionOkMessage}原因: {reason}";
        return CreateZeroProductionMeasurement();
    }

    private bool TryBuildProductionMeasurement(
        ContourJudgmentAnalysis contourJudgment,
        out InspectionMeasurement measurement,
        out string reason)
    {
        measurement = CreateZeroProductionMeasurement();
        reason = string.Empty;

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

        var resolvedAngleDegrees = template.ReferenceAngleDegrees;
        var matchScore = 0.0;
        var allowFullRotation = false;
        if (currentFeature.Strategy.AllowsRCorrection && templateFeature.Strategy.AllowsRCorrection)
        {
            var radiusMatch = contourJudgment.FrontBackMatch is { } match &&
                match.Decision != ContourFrontBackDecision.Unavailable
                    ? new ContourRadiusMatchWithAlternative(
                        Shift: 0,
                        AngleDegrees: match.FrontAngleDegrees,
                        ErrorPixels: match.FrontErrorPixels,
                        ErrorNormalized: 0,
                        AlternativeShift: 0,
                        AlternativeAngleDegrees: 0,
                        AlternativeErrorPixels: match.FrontAlternativeErrorPixels,
                        AlternativeErrorNormalized: 0)
                    : ContourFeatureExtractor.MatchRadiusSignatureWithAlternatives(
                        currentFeature.RadiusSignature,
                        templateFeature.RadiusSignature);

            resolvedAngleDegrees = AngleMath.NormalizeDegrees360(template.ReferenceAngleDegrees + radiusMatch.AngleDegrees);
            matchScore = BuildContourMatchScore(radiusMatch.ErrorPixels);
            allowFullRotation = true;
        }
        else
        {
            matchScore = currentFeature.Strategy.AllowsRCorrection || templateFeature.Strategy.AllowsRCorrection
                ? 0.0
                : 1.0;
        }

        var currentCenter = activeParameters.CameraCalibration.PixelToMachine(
            currentFeature.CenterXPixel,
            currentFeature.CenterYPixel);
        var referenceCenter = new MachinePoint(
            template.ReferenceCenterXMm,
            template.ReferenceCenterYMm);
        var alignmentSnapshot = XyrAlignmentSolver.Solve(
            new PartPose2D(currentCenter.XMm, currentCenter.YMm, resolvedAngleDegrees, matchScore),
            new PartPose2D(referenceCenter.XMm, referenceCenter.YMm, template.ReferenceAngleDegrees, template.MatchScoreBaseline),
            activeParameters.RAxisCenterCalibration,
            activeParameters.InvertRotationCompensation ? -1 : 1,
            allowFullRotation);
        measurement = new InspectionMeasurement(
            currentFeature.CenterXPixel,
            currentFeature.CenterYPixel,
            alignmentSnapshot.XOffsetMm,
            alignmentSnapshot.YOffsetMm,
            alignmentSnapshot.HomeXActionMm,
            alignmentSnapshot.HomeYActionMm,
            resolvedAngleDegrees,
            alignmentSnapshot.AngleOffsetDegrees,
            alignmentSnapshot.HomeRActionDegrees,
            template.WidthMm,
            template.HeightMm,
            currentFeature.AreaPixels,
            matchScore);
        return true;
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

    private static double BuildContourMatchScore(double errorPixels)
    {
        if (double.IsNaN(errorPixels) || double.IsInfinity(errorPixels))
        {
            return 0.0;
        }

        return Math.Clamp(1.0 - errorPixels / 30.0, 0.0, 1.0);
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

        var frontBackMatch = contourJudgment.FrontBackMatch;
        if (frontBackMatch is null ||
            frontBackMatch.Decision != ContourFrontBackDecision.Back ||
            !frontBackMatch.IsReliable)
        {
            return false;
        }

        message =
            "反面NG: 外轮廓半径更接近镜像模板，" +
            $"正面误差={frontBackMatch.FrontErrorPixels:F2}px，" +
            $"镜像误差={frontBackMatch.BackErrorPixels:F2}px，" +
            $"分离={frontBackMatch.SeparationPixels:F2}px。";
        return true;
    }

    private ContourJudgmentAnalysis BuildContourJudgment(Mat image)
    {
        try
        {
            var feature = _contourFeatureExtractor.Extract(image, ReadVisionParameters());
            var frontBack = AnalyzeContourFrontBack(feature);
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
            return new ContourJudgmentAnalysis(logLine, feature, frontBack.Match);
        }
        catch (Exception ex)
        {
            return new ContourJudgmentAnalysis($"判断: 外轮廓分析失败，原因={ex.Message}", null, null);
        }
    }

    private ContourFrontBackAnalysis AnalyzeContourFrontBack(ContourFeatureExtraction currentFeature)
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

        var match = _contourFrontBackMatcher.Match(currentFeature, _bypassLogTemplateFeature);
        if (match.Decision == ContourFrontBackDecision.Unavailable)
        {
            return new ContourFrontBackAnalysis($"，正反=不可用，原因={SimplifyFrontBackMessage(match.Message)}", match);
        }

        return new ContourFrontBackAnalysis(
            $"，正反={FormatContourFrontBackDecision(match.Decision)}({FormatContourFrontBackModeText()})" +
            $"，正面误差={FormatPixels(match.FrontErrorPixels)}px" +
            $"，镜像误差={FormatPixels(match.BackErrorPixels)}px" +
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
        ContourFrontBackMatch? FrontBackMatch);

    private sealed record ContourFrontBackAnalysis(
        string LogSegment,
        ContourFrontBackMatch? Match);

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
            $"结果: {result.Decision}  原因: {FormatNgReason(result)}",
            $"耗时: 总{totalElapsedMilliseconds}ms  拍照{captureElapsedMilliseconds}ms  检测/存储{inspectionElapsedMilliseconds}ms  显示{renderElapsedMilliseconds}ms  PLC{plcElapsedMilliseconds}ms",
            $"\u660e\u7ec6: \u89c6\u89c9{timings.VisionMs}ms  \u8bb0\u5f55{timings.SaveResultMs}ms  \u56fe{timings.SaveDiagnosticImageMs}ms  \u62a5\u544a{timings.SaveReportMs}ms",
            FormatVisionStageTimingLine(timings.StageTimings),
            FormatRuntimeXyrLine(result)
        };
        if (!string.IsNullOrWhiteSpace(judgmentLine))
        {
            lines.Add(judgmentLine);
        }

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

    private static string FormatNgReason(InspectionResult result)
    {
        if (result.Decision == InspectionDecision.Ok)
        {
            return "OK";
        }

        return result.NgReason switch
        {
            NgReason.MatchFailed => "未找到工件",
            NgReason.SizeOutOfTolerance => "尺寸超差",
            NgReason.ShapeOutOfTolerance => "形状不符",
            NgReason.HoleOutOfTolerance => "孔位超差",
            NgReason.CameraError => "相机异常",
            NgReason.PlcError => "PLC异常",
            NgReason.AlgorithmError => "算法异常",
            NgReason.BackSideDetected => "反面",
            NgReason.FrontBumpMissing => "正面特征缺失",
            _ => result.NgReason.ToString()
        };
    }

    private static bool ShouldShowOnRuntimePanel(string line)
    {
        return IsRuntimeNgLine(line) || IsRuntimeXyrLine(line) || IsRuntimeJudgmentLine(line);
    }

    private static string FormatRuntimeXyrLine(InspectionResult result)
    {
        var measurement = result.Measurement;
        if (measurement is null)
        {
            return "XYR: 无有效测量值";
        }

        return
            "XYR: " +
            $"偏差 X={FormatRuntimeNumber(measurement.XOffsetMm)}mm " +
            $"Y={FormatRuntimeNumber(measurement.YOffsetMm)}mm " +
            $"R={FormatRuntimeNumber(measurement.AngleOffsetDegrees)}deg  " +
            $"补偿 X={FormatRuntimeNumber(measurement.XCompensationMm)}mm " +
            $"Y={FormatRuntimeNumber(measurement.YCompensationMm)}mm " +
            $"R={FormatRuntimeNumber(measurement.RotationCompensationDegrees)}deg";
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
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index];
            _fileLogger.Write(line);
            if (ShouldShowOnRuntimePanel(line))
            {
                LogList.Items.Insert(0, CreateRuntimeLogLine(line));
            }
        }

        while (LogList.Items.Count > 80)
        {
            LogList.Items.RemoveAt(LogList.Items.Count - 1);
        }
    }

    private static RuntimeLogLine CreateRuntimeLogLine(string line)
    {
        var displayText = $"{DateTime.Now:HH:mm:ss} {FormatRuntimePanelLine(line)}";
        if (IsRuntimeNgLine(line))
        {
            return new RuntimeLogLine(displayText, Brushes.Red, FontWeights.Bold);
        }

        if (IsRuntimeOkLine(line))
        {
            return new RuntimeLogLine(displayText, Brushes.ForestGreen, FontWeights.Bold);
        }

        return new RuntimeLogLine(displayText, Brushes.Black, FontWeights.Normal);
    }

    private static string FormatRuntimePanelLine(string line)
    {
        if (!line.StartsWith("结果:", StringComparison.Ordinal))
        {
            return line;
        }

        var reasonIndex = line.IndexOf("原因:", StringComparison.Ordinal);
        var reason = reasonIndex >= 0
            ? line[(reasonIndex + "原因:".Length)..].Trim()
            : line;

        return $"NG  NG原因: {reason}";
    }

    private static bool IsRuntimeOkLine(string line)
    {
        return line.StartsWith("结果:", StringComparison.Ordinal)
            && line.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsRuntimeNgLine(string line)
    {
        return line.StartsWith("结果:", StringComparison.Ordinal)
            && (line.IndexOf("NG", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsRuntimeXyrLine(string line)
    {
        return line.StartsWith("XYR:", StringComparison.Ordinal);
    }

    private static bool IsRuntimeJudgmentLine(string line)
    {
        return line.StartsWith("判断:", StringComparison.Ordinal);
    }

    private PlcOutputCommand CalculatePlcOutputCommand(InspectionMeasurement measurement)
    {
        return PlcOutputDiagnosticFormatter.CalculatePlcOutputCommand(measurement, GetEffectivePlcOutputTransform());
    }

    private void Log(string message)
    {
        _fileLogger.Write(message);
    }

    private sealed record RuntimeLogLine(string DisplayText, Brush Foreground, FontWeight FontWeight);
}
