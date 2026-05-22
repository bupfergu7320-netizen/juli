using System.Globalization;
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
    private async Task HandlePlcCaptureRequestAsync()
    {
        Log("收到PLC定位触发D1000=1");

        try
        {
            var validation = _plcCaptureRequestValidator.Validate(new PlcCaptureRequestState(
                _productionEnabled,
                _changeoverTemplateRequested,
                _cameraConnected,
                _template is not null,
                _batchSession.CanInspect));
            if (validation.Action != PlcCaptureRequestAction.Proceed)
            {
                await HandlePlcCaptureRequestValidationFailureAsync(validation);
                return;
            }

            Log("开始相机拍照检测");
            var rawImagePath = await CaptureCameraImageAsync(saveImage: false);
            await InspectAndPersistAsync(
                _lastCameraImage!,
                rawImagePath,
                "PLC触发检测",
                writeToPlc: true);
        }
        catch (Exception ex)
        {
            await WritePlcErrorResultAsync($"PLC触发检测异常: {ex.Message}", NgReason.AlgorithmError);
        }
        finally
        {
            UpdateBatchUi();
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
            await WritePlcErrorResultAsync(
                messages.PlcErrorMessage ?? "\u0050\u004c\u0043\u89e6\u53d1\u68c0\u6d4b\u5931\u8d25\u3002",
                validation.NgReason ?? NgReason.PlcError);
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
        bool writeToPlc = false)
    {
        if (_template is null)
        {
            throw new InvalidOperationException("请先建立当前型号标准位/模板。");
        }

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
            GetEffectivePlcOutputTransform()));

        var result = run.Result;
        var output = run.Output;
        foreach (var log in run.Logs)
        {
            Log(log);
        }

        _lastInspectionResult = result;
        _lastRawImagePath = rawImagePath;

        if (run.ResultImagePath is not null)
        {
            SetImage(ResultImage, run.ResultImagePath);
        }
        else
        {
            ResultImage.Source = CreateBitmapImageFromMat(output.DiagnosticImage);
        }

        RenderResult(result);
        if (result.Decision is InspectionDecision.Ok or InspectionDecision.Ng)
        {
        }

        Log($"{logPrefix}: {result.Decision}: {result.Message}");
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
        if (writeToPlc)
        {
            await WritePlcResultIfConnectedAsync(result);
        }

        if (result.Decision == InspectionDecision.Ok && result.Measurement is { } measurement)
        {
            MessageText.Text = _plcOutputDiagnosticFormatter.BuildOkMessage(
                measurement,
                _plcOutputTransform,
                writeToPlc);
        }
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
            if (outcome.TriggerCleared)
            {
                _plcTriggerGate.MarkTriggerCleared();
            }

            if (outcome.ShouldSetPlcStatusNormal)
            {
                SetPlcStatus("PLC通讯正常", isNormal: true);
            }

            if (outcome.ShouldSetPlcStatusWaitingReset)
            {
                SetPlcStatus("PLC等待复位", isNormal: false);
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

    private PlcOutputCommand CalculatePlcOutputCommand(InspectionMeasurement measurement)
    {
        return PlcOutputDiagnosticFormatter.CalculatePlcOutputCommand(measurement, GetEffectivePlcOutputTransform());
    }

    private void Log(string message)
    {
        _fileLogger.Write(message);
        LogList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");
        while (LogList.Items.Count > 200)
        {
            LogList.Items.RemoveAt(LogList.Items.Count - 1);
        }
    }
}
