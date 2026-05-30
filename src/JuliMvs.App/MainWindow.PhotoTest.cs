using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JuliMvs.App.Services;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;
using JuliMvs.Vision;

namespace JuliMvs.App;

public partial class MainWindow
{
    private void PhotoTest_Click(object sender, RoutedEventArgs e)
    {
        OpenPhotoTestDialog();
    }

    private void OpenPhotoTestDialog()
    {
        var dialog = CreateToolDialog("拍照测试 - 生产流程预览", 1480, 900);
        var root = new Grid { Margin = new Thickness(18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.08, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.92, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = "拍照测试按生产流程计算和显示，不写 PLC。",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 0, 2, 14)
        };
        Grid.SetColumnSpan(header, 2);
        root.Children.Add(header);

        var previewBorder = new Border
        {
            Background = Brushes.Black,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 14, 0)
        };
        Grid.SetRow(previewBorder, 1);
        var previewImage = new Image { Stretch = Stretch.Uniform };
        previewBorder.Child = previewImage;
        root.Children.Add(previewBorder);

        var detailsBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 18,
            Padding = new Thickness(12),
            Text = BuildPhotoTestInitialText()
        };
        Grid.SetRow(detailsBox, 1);
        Grid.SetColumn(detailsBox, 1);
        root.Children.Add(detailsBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0)
        };
        Grid.SetRow(buttons, 2);
        Grid.SetColumnSpan(buttons, 2);
        var captureButton = CreateDialogButton("拍照测试", null, 180);
        var closeButton = CreateDialogButton("关闭", (_, _) => dialog.Close(), 160);
        buttons.Children.Add(captureButton);
        buttons.Children.Add(closeButton);
        root.Children.Add(buttons);

        captureButton.Click += async (_, _) =>
        {
            captureButton.IsEnabled = false;
            detailsBox.Text = "正在拍照测试...";
            try
            {
                var test = await CaptureAndRunPhotoTestAsync();
                if (test.Run is { } run)
                {
                    if (run.Result.ResultImagePath is not null)
                    {
                        SetImage(previewImage, run.Result.ResultImagePath);
                        SetImage(ResultImage, run.Result.ResultImagePath);
                    }
                    else if (run.Result.RawImagePath is not null)
                    {
                        SetImage(previewImage, run.Result.RawImagePath);
                        SetImage(ResultImage, run.Result.RawImagePath);
                    }
                    else
                    {
                        var preview = CreatePreviewBitmapImageFromMat(_lastCameraImage!);
                        previewImage.Source = preview;
                        ResultImage.Source = preview;
                    }

                    detailsBox.Text = BuildPhotoTestDetails(run);
                    MessageText.Text = $"拍照测试完成: {FormatDecisionText(run.Result)}，原因: {FormatNgReason(run.Result)}，不写PLC。";
                    Log(MessageText.Text);
                    LogPhotoTestSummary(run);
                }
                else
                {
                    if (test.RawImagePath is not null)
                    {
                        SetImage(previewImage, test.RawImagePath);
                        SetImage(ResultImage, test.RawImagePath);
                    }
                    else if (_lastCameraImage is not null)
                    {
                        var preview = CreateBitmapImageFromMat(_lastCameraImage);
                        previewImage.Source = preview;
                        ResultImage.Source = preview;
                    }

                    detailsBox.Text = BuildPhotoOnlyTestDetails(test);
                    MessageText.Text = "\u62cd\u7167\u6d4b\u8bd5\u5df2\u5b8c\u6210\uff1a\u4ec5\u62cd\u7167\u9884\u89c8\uff0c\u4e0d\u505a\u68c0\u6d4b\u548cPLC\u9884\u89c8\u3002";
                    Log($"{MessageText.Text} {test.SkipReason}");
                }
            }
            catch (Exception ex)
            {
                detailsBox.Text = $"拍照测试失败: {ex.Message}";
                MessageText.Text = detailsBox.Text;
                Log(detailsBox.Text);
                MessageBox.Show(ex.Message, "拍照测试", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                captureButton.IsEnabled = true;
            }
        };

        dialog.Content = root;
        dialog.Show();
    }

    private sealed record PhotoTestCaptureResult(
        string? RawImagePath,
        string? SkipReason,
        PhotoTestProductionRun? Run);

    private sealed record PhotoTestProductionRun(
        InspectionResult Result,
        string ProductionOutputLog,
        string JudgmentLine,
        BypassInspectionTimings Timings,
        long CaptureElapsedMilliseconds,
        long TotalElapsedMilliseconds);

    private async Task<PhotoTestCaptureResult> CaptureAndRunPhotoTestAsync()
    {
        if (!_cameraConnected)
        {
            throw new InvalidOperationException("相机未连接，无法拍照测试。");
        }

        var skipReasons = new List<string>();
        if (_template is null)
        {
            skipReasons.Add("未加载标准位/模板");
        }

        if (!IsMachineCalibrationReady(out var calibrationMessage))
        {
            skipReasons.Add($"机器标定未完成: {calibrationMessage}");
        }

        var shouldSaveImage = skipReasons.Count == 0;
        var totalStopwatch = Stopwatch.StartNew();
        var captureStopwatch = Stopwatch.StartNew();
        var rawImagePath = await CaptureCameraImageAsync(
            saveImage: shouldSaveImage,
            applyCaptureDelay: shouldSaveImage);
        captureStopwatch.Stop();
        if (shouldSaveImage && string.IsNullOrWhiteSpace(rawImagePath))
        {
            throw new InvalidOperationException("拍照测试保存图片失败，未生成原图路径。");
        }

        _lastRawImagePath = rawImagePath;
        if (skipReasons.Count > 0)
        {
            return new PhotoTestCaptureResult(
                rawImagePath,
                "仅拍照预览，跳过检测原因: " + string.Join("; ", skipReasons),
                null);
        }

        var timings = new BypassInspectionTimings();
        var contourJudgment = BuildContourJudgment(_lastCameraImage!, timings);
        var result = CreateBypassInspectionResult(rawImagePath, contourJudgment, timings, out var productionOutputLog);
        result = SaveProductionNgImagesIfNeeded(_lastCameraImage!, result);
        await _repository.SaveResultAsync(result);

        _lastInspectionResult = result;
        _lastRawImagePath = result.RawImagePath ?? rawImagePath;
        totalStopwatch.Stop();
        return new PhotoTestCaptureResult(
            rawImagePath,
            null,
            new PhotoTestProductionRun(
                result,
                productionOutputLog,
                contourJudgment.LogLine,
                timings,
                captureStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds));
    }

    private string BuildPhotoTestInitialText()
    {
        var templateText = _template is null
            ? "未加载模板"
            : $"{_template.ProductName} / {_template.BatchNo}";
        return
            "拍照测试\n" +
            "==============================\n" +
            "流程: 拍照 -> 外轮廓 -> 正反 -> XYR定位 -> 缺料/崩边 -> PLC预览\n" +
            "动作: 只计算和显示，不写 PLC。\n\n" +
            $"相机: {(_cameraConnected ? "已连接" : "未连接")}\n" +
            $"PLC: {(_plcClient?.IsConnected == true ? "已连接" : "未连接")}\n" +
            $"模板: {templateText}\n\n" +
            "点击下方“拍照测试”开始。";
    }

    private string BuildPhotoOnlyTestDetails(PhotoTestCaptureResult test)
    {
        var templateText = _template is null
            ? "未加载"
            : $"{_template.ProductName} / {_template.BatchNo}";
        var calibrationText = IsMachineCalibrationReady(out var calibrationMessage)
            ? "已完成"
            : $"未完成: {calibrationMessage}";
        var text = new List<string>
        {
            "拍照测试（不写PLC）",
            "==============================",
            "结果: 跳过    原因: 准备不足",
            "XYR: -",
            $"原因: {test.SkipReason}",
            string.Empty,
            "流程状态",
            "------------------------------",
            $"相机: {(_cameraConnected ? "已连接" : "未连接")}",
            $"PLC: {(_plcClient?.IsConnected == true ? "已连接" : "未连接")}",
            $"模板: {templateText}",
            $"机器标定: {calibrationText}",
            $"原图: {FormatPhotoOnlyRawImagePath(test.RawImagePath)}"
        };
        return string.Join(Environment.NewLine, text);
    }

    private static string FormatPhotoOnlyRawImagePath(string? rawImagePath)
    {
        return string.IsNullOrWhiteSpace(rawImagePath)
            ? "未保存（快速预览模式）"
            : FormatPathForDisplay(rawImagePath);
    }

    private string BuildPhotoTestDetails(PhotoTestProductionRun run)
    {
        var result = run.Result;
        var measurement = result.Measurement;
        var plcOutput = result.Decision == InspectionDecision.Ok && measurement is not null
            ? CalculatePlcOutputCommand(measurement)
            : new PlcOutputCommand(0, 0, 0);
        var expectedD1010 = result.Decision == InspectionDecision.Ok ? 1 : 2;
        var templateShape = _bypassLogTemplateFeature is null
            ? "-"
            : FormatAutoPartShapeClass(_bypassLogTemplateFeature.Strategy.ShapeClass);
        var backSideText = _visionParameters.BackSideNgEnabled ? "开" : "关";
        var fourWayText = _visionParameters.FourWaySymmetricEnabled ? "开" : "关";

        var text = new List<string>
        {
            "拍照测试（不写PLC）",
            "==============================",
            $"结果: {FormatDecisionText(result)}    原因: {FormatNgReason(result)}",
            FormatRuntimeXyrLine(result),
            $"耗时: 总{run.TotalElapsedMilliseconds}ms  拍照{run.CaptureElapsedMilliseconds}ms  视觉{run.Timings.TotalVisionMilliseconds}ms",
            FormatVisionStageTimingLine(run.Timings.ToVisionStageTimings()),
            string.Empty,
            "模板",
            "------------------------------",
            _template is null
                ? "型号: -"
                : $"型号: {FormatSummaryValue(_template.ProductName)}",
            _template is null
                ? "标准: -"
                : $"标准: X={FormatRuntimeNumber(_template.ReferenceCenterXMm)}  Y={FormatRuntimeNumber(_template.ReferenceCenterYMm)}  R={FormatRuntimeNumber(_template.ReferenceAngleDegrees)}",
            $"形态: {templateShape}    反面NG: {backSideText}    四边对称: {fourWayText}",
            string.Empty,
            "当前来料",
            "------------------------------",
            BuildPhotoCurrentPartLine(result),
            run.JudgmentLine,
            run.ProductionOutputLog,
            string.Empty,
            "PLC预览",
            "------------------------------",
            "拍照测试不写PLC；正式生产会写下面的值。",
            $"D1002 = {PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.XDeviation)} mm",
            $"D1004 = {PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.YDeviation)} mm",
            $"D1006 = {PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.RDeviation)} deg",
            $"D1010 = {expectedD1010}",
            string.Empty,
            "图片",
            "------------------------------",
            $"原图: {FormatPathForDisplay(result.RawImagePath)}",
            $"诊断图: {FormatPathForDisplay(result.ResultImagePath)}"
        };

        return string.Join(Environment.NewLine, text);
    }

    private static string BuildPhotoCurrentPartLine(InspectionResult result)
    {
        var measurement = result.Measurement;
        if (measurement is null || result.Decision != InspectionDecision.Ok)
        {
            return "来料: 无有效XYR输出";
        }

        return
            $"来料: 中心=({measurement.CenterXPixel:F1},{measurement.CenterYPixel:F1})px  " +
            $"R={FormatRuntimeNumber(measurement.AngleDegrees)}  " +
            $"分数={measurement.MatchScore:F3}";
    }

    private void LogPhotoTestSummary(PhotoTestProductionRun run)
    {
        Log($"拍照测试: {FormatDecisionText(run.Result)}: {run.Result.Message}");
        Log(run.JudgmentLine);
        Log(run.ProductionOutputLog);
        LogPlcOutputPreview(run.Result);
    }

    private static string FormatPathForDisplay(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "-" : path;
    }

}
