using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JuliMvs.App.Services;
using JuliMvs.Core.Batch;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;

namespace JuliMvs.App;

public partial class MainWindow
{
    private async Task LoadExistingTemplateFromChangeoverAsync()
    {
        try
        {
            var productName = (_changeoverModelBox?.Text ?? _currentProductName).Trim();
            if (string.IsNullOrWhiteSpace(productName))
            {
                throw new InvalidOperationException("型号不能为空，无法加载模板。");
            }

            if (_productionEnabled)
            {
                throw new InvalidOperationException("请先点击左侧“停止”，再加载已有标准位/模板。");
            }

            _currentProductName = productName;
            var batchNo = BatchNumberGenerator.GenerateDefaultBatchNo();
            var templateLoaded = await StartBatchWithLatestTemplateAsync(batchNo, productName);

            if (templateLoaded)
            {
                _productionEnabled = true;
                UpdateRunStopUi();
                SaveLocalSettings();
                var templateBaselineSummary = _template is null
                    ? string.Empty
                    : $"{InspectionDiagnosticMessageFormatter.FormatTemplateBaselineSummary(_template)}\n";
                _changeoverStartButton?.SetCurrentValue(IsEnabledProperty, true);
                _changeoverCaptureTemplateButton?.SetCurrentValue(IsEnabledProperty, false);
                _changeoverCancelButton?.SetCurrentValue(IsEnabledProperty, false);
                MessageText.Text = $"已加载型号 {productName} 的当前标准位/模板，等待PLC触发拍照检测。";
                Log(MessageText.Text);
                Log($"最简生产模式已就绪: 型号 {productName}, 批次 {batchNo}, 等待PLC触发D1000=1。");
                UpdateChangeoverFlow(
                    activeStep: 4,
                    completedSteps: 5,
                    status: "已加载标准位/模板",
                    hint: "当前型号标准位/模板与当前标定匹配，已自动进入运行，等待PLC触发生产检测。",
                    summary:
                        $"型号: {productName}\n" +
                        $"批次: {batchNo}\n" +
                        "状态: 已加载当前型号标准位/模板\n" +
                        templateBaselineSummary +
                        "机器方向: " +
                        TurntableStatusMessageFormatter.FormatDirectionText(
                            _visionParameters,
                            _plcOutputTransform));
                return;
            }

            MessageText.Text = $"型号 {productName} 没有可用标准位/模板，请重新建立标准位/模板。";
            Log(MessageText.Text);
            UpdateChangeoverFlow(
                activeStep: 0,
                completedSteps: 0,
                status: "缺少标准位/模板",
                hint: "当前型号没有可用标准位/模板。请放入标准件后点击“重建标准位/模板”。",
                summary: $"型号: {productName}\n状态: 缺少当前标定链路下的可用标准位/模板",
                failed: true);
        }
        catch (Exception ex)
        {
            MessageText.Text = ex.Message;
            Log($"加载已有标准位/模板失败: {ex.Message}");
            UpdateChangeoverFlow(
                activeStep: 0,
                completedSteps: 0,
                status: "加载模板失败",
                hint: ex.Message,
                summary: ex.Message,
                failed: true);
            MessageBox.Show(ex.Message, "换型流程", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            UpdateBatchUi();
        }
    }

    private async Task StartChangeoverFromDialogAsync()
    {
        try
        {
            var batchNo = BatchNumberGenerator.GenerateDefaultBatchNo();
            var productName = (_changeoverModelBox?.Text ?? _currentProductName).Trim();
            if (string.IsNullOrWhiteSpace(productName))
            {
                throw new InvalidOperationException("型号不能为空。请先输入或选择型号。");
            }

            if (_productionEnabled)
            {
                throw new InvalidOperationException("请先点击左侧“停止”，再重新建立标准位/模板。换型调试由上位机按钮拍照，不使用PLC触发。");
            }

            RequireMachineCalibrationReady();

            _currentProductName = productName;
            if (_batchSession.CanEnd)
            {
                var endedBatch = _batchSession.BatchNo;
                _batchSession.End();
                Log($"换型前结束当前批次: {endedBatch}");
            }

            _currentBatchNo = batchNo;
            _batchSession = BatchSession.Empty();
            _batchSession.Start(batchNo, productName);
            ClearCurrentInspection();
            await LoadRecipeAsync(productName, showMessageWhenMissing: false);

            _changeoverTemplateRequested = true;
            _changeoverStartButton?.SetCurrentValue(IsEnabledProperty, false);
            _changeoverCaptureTemplateButton?.SetCurrentValue(IsEnabledProperty, true);
            _changeoverCancelButton?.SetCurrentValue(IsEnabledProperty, true);
            MessageText.Text = "换型模式已开启。请放入标准件到OK位置，点击“拍照设标准位/模板”；当前中心和角度会保存为当前型号标准位。";
            Log($"进入重建标准位/模板流程: 批次 {batchNo}, 型号 {productName}");
            UpdateChangeoverFlow(
                activeStep: 2,
                completedSteps: 2,
                status: "等待上位机拍照",
                hint: "请确认标准件已放到检测位OK位置并吸附稳定，然后点击“拍照设标准位/模板”。软件会把当前X/Y/R保存为当前型号标准位。",
                summary: $"型号: {productName}\n批次: {batchNo}\n状态: 等待上位机拍照建立标准位/模板\n当前型号标准位: 等待保存X/Y/R");
        }
        catch (Exception ex)
        {
            MessageText.Text = ex.Message;
            Log($"重建标准位/模板流程启动失败: {ex.Message}");
            UpdateChangeoverFlow(0, 0, "重建标准位/模板失败", ex.Message, summary: ex.Message, failed: true);
            MessageBox.Show(ex.Message, "换型流程", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            UpdateBatchUi();
        }
    }

    private async Task CaptureChangeoverTemplateFromAppAsync()
    {
        if (!_changeoverTemplateRequested)
        {
            MessageBox.Show("请先点击“重建标准位/模板”，再拍照建立标准位/模板。", "换型流程", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_productionEnabled)
        {
            MessageBox.Show("请先点击左侧“停止”，再进行换型拍照。换型调试不使用PLC触发。", "换型流程", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_cameraConnected)
        {
            UpdateChangeoverFlow(
                activeStep: 2,
                completedSteps: 2,
                status: "相机未连接",
                hint: "相机未连接，无法由上位机拍照建立标准位/模板。请先恢复相机通讯。",
                summary: "相机未连接，未拍照，未建立标准位/模板。",
                failed: true);
            return;
        }

        if (!IsMachineCalibrationReady(out var calibrationMessage))
        {
            UpdateChangeoverFlow(
                activeStep: 3,
                completedSteps: 2,
                status: "标定未完成",
                hint: calibrationMessage,
                summary: calibrationMessage,
                failed: true);
            MessageText.Text = calibrationMessage;
            Log(calibrationMessage);
            MessageBox.Show(calibrationMessage, "换型流程", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _changeoverCaptureTemplateButton?.SetCurrentValue(IsEnabledProperty, false);
            Log("换型模式由上位机按钮拍照，开始建立标准位/模板");
            UpdateChangeoverFlow(
                activeStep: 2,
                completedSteps: 2,
                status: "正在拍照",
                hint: "上位机正在拍照，请保持标准件稳定。",
                summary: "正在拍摄标准件图像...");
            var rawImagePath = await CaptureCameraImageAsync(saveImage: true);
            UpdateChangeoverFlow(
                activeStep: 3,
                completedSteps: 3,
                status: "正在建立标准位/模板",
                hint: "标准件图像已获取，正在提取当前型号标准位X/Y/R、尺寸和形状。",
                summary: $"标准件图像已保存:\n{InspectionDiagnosticMessageFormatter.FormatSavedImagePath(rawImagePath)}\n\n正在建立标准位/模板...");
            await CreateChangeoverTemplateFromCameraAsync(rawImagePath ?? throw new InvalidOperationException("标准件图像未保存，无法建立标准位/模板。"));
        }
        catch (Exception ex)
        {
            _changeoverCaptureTemplateButton?.SetCurrentValue(IsEnabledProperty, true);
            UpdateChangeoverFlow(
                activeStep: 3,
                completedSteps: 2,
                status: "标准位/模板建立失败",
                hint: "请检查标准件是否在视野内、光源/曝光是否合适。修正后再次点击“拍照设标准位/模板”。",
                summary: $"失败原因:\n{ex.Message}\n\n处理方式:\n1. 检查标准件位置和吸附\n2. 检查相机曝光和光源\n3. 再次点击“拍照设标准位/模板”",
                failed: true);
            MessageText.Text = $"换型标准位/模板建立失败: {ex.Message}";
            Log(MessageText.Text);
        }
        finally
        {
            UpdateBatchUi();
        }
    }

    private async Task CreateChangeoverTemplateFromCameraAsync(string rawImagePath)
    {
        if (!_batchSession.CanBuildTemplate)
        {
            throw new InvalidOperationException("当前批次状态不允许建立标准位/模板，请重新点击换型。");
        }

        var parameters = ReadVisionParameters();
        _templateImagePath = rawImagePath;
        _template = _visionService.CreateTemplate(
            _lastCameraImage!,
            _batchSession.BatchNo,
            _batchSession.ProductName,
            parameters,
            rawImagePath);
        var selfCheck = new ChangeoverTemplateSelfCheck(_visionService).Validate(
            _lastCameraImage!,
            _template,
            parameters,
            rawImagePath);
        if (!selfCheck.Passed)
        {
            throw new InvalidOperationException(selfCheck.Message);
        }

        var selfCheckEvidence = _changeoverTemplateReportWriter.SaveSelfCheckEvidence(
            new ChangeoverTemplateSelfCheckReportContext(
                _template,
                parameters,
                selfCheck,
                rawImagePath));

        await _repository.SaveTemplateAsync(_template);
        await SaveRecipeAsync(_batchSession.ProductName);

        if (_batchSession.CanBuildTemplate)
        {
            _batchSession.MarkTemplateCreated();
        }

        if (_batchSession.CanConfirmFirstArticle)
        {
            _batchSession.ConfirmFirstArticle();
        }

        _changeoverTemplateRequested = false;
        _productionEnabled = true;
        UpdateRunStopUi();
        SaveLocalSettings();
        _changeoverStartButton?.SetCurrentValue(IsEnabledProperty, true);
        _changeoverCaptureTemplateButton?.SetCurrentValue(IsEnabledProperty, false);
        _changeoverCancelButton?.SetCurrentValue(IsEnabledProperty, false);

        SetImage(ResultImage, selfCheckEvidence.DiagnosticImagePath);
        RenderTemplateSummary(_template);
        MessageText.Text = "当前型号标准位/模板已重新建立并保存，当前型号进入生产检测状态。";
        Log($"换型标准位/模板建立完成: 型号 {_template.ProductName}, 批次 {_template.BatchNo}, 图像 {rawImagePath}, 自检报告 {selfCheckEvidence.ReportPath}");
        Log($"最简生产模式已就绪: 型号 {_template.ProductName}, 批次 {_template.BatchNo}, 等待PLC触发D1000=1。");
        UpdateChangeoverFlow(
            activeStep: 5,
            completedSteps: 6,
            status: "完成换型",
            hint: "当前型号标准位X/Y/R已保存，当前型号已进入生产检测状态。后续PLC触发将执行检测，不会再建立标准位/模板。",
            summary:
                $"标准位/模板建立完成\n\n" +
                $"型号: {_template.ProductName}\n" +
                $"批次: {_template.BatchNo}\n" +
                $"{InspectionDiagnosticMessageFormatter.FormatTemplateBaselineSummary(_template)}\n" +
                $"宽度: {_template.WidthMm:F3}mm\n" +
                $"高度: {_template.HeightMm:F3}mm\n" +
                $"面积: {_template.AreaPixels:F0}px\n" +
                $"图像: {rawImagePath}\n" +
                $"自检诊断图: {selfCheckEvidence.DiagnosticImagePath}\n" +
                $"自检报告: {selfCheckEvidence.ReportPath}");

        var templateResult = InspectionResult.FromMeasurement(
            _template.BatchNo,
            DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff"),
            InspectionDecision.Ok,
            NgReason.None,
            "当前型号标准位/模板已重新建立。",
            new InspectionMeasurement(
                _template.ReferenceCenterXPixel,
                _template.ReferenceCenterYPixel,
                0,
                0,
                0,
                0,
                _template.ReferenceAngleDegrees,
                0,
                0,
                _template.WidthMm,
                _template.HeightMm,
                _template.AreaPixels,
                _template.MatchScoreBaseline),
            rawImagePath,
            selfCheckEvidence.DiagnosticImagePath);
        _lastInspectionResult = templateResult;
    }
}
