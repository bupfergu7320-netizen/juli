using System.Globalization;
using System.IO;
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
        if (!RequireTechnician("拍照测试"))
        {
            return;
        }

        OpenPhotoTestDialog();
    }

    private void OpenPhotoTestDialog()
    {
        var dialog = CreateToolDialog("拍照测试 - PLC输出预览", 1480, 900);
        var root = new Grid { Margin = new Thickness(18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.08, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.92, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = "拍照测试只计算和显示结果，不写 PLC。",
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
                    if (run.ResultImagePath is not null)
                    {
                        SetImage(previewImage, run.ResultImagePath);
                        SetImage(ResultImage, run.ResultImagePath);
                    }
                    else
                    {
                        previewImage.Source = CreateBitmapImageFromMat(run.Output.DiagnosticImage);
                        ResultImage.Source = previewImage.Source;
                    }

                    detailsBox.Text = BuildPhotoTestDetails(run);
                    MessageText.Text = $"\u62cd\u7167\u6d4b\u8bd5\u5b8c\u6210: {run.Result.Decision}, PLC\u9884\u89c8\u4e0d\u5199\u5165\u3002";
                    Log(MessageText.Text);
                    LogPhotoTestSummary(run);
                }
                else
                {
                    SetImage(previewImage, test.RawImagePath);
                    SetImage(ResultImage, test.RawImagePath);
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
        string RawImagePath,
        string? SkipReason,
        InspectionRunResult? Run);

    private async Task<PhotoTestCaptureResult> CaptureAndRunPhotoTestAsync()
    {
        if (!_cameraConnected)
        {
            throw new InvalidOperationException("相机未连接，无法拍照测试。");
        }

        var rawImagePath = await CaptureCameraImageAsync(saveImage: true);
        if (string.IsNullOrWhiteSpace(rawImagePath))
        {
            throw new InvalidOperationException("拍照测试保存图片失败，未生成原图路径。");
        }

        _lastRawImagePath = rawImagePath;
        var skipReasons = new List<string>();
        if (_template is null)
        {
            skipReasons.Add("未加载标准位/模板");
        }

        if (!IsMachineCalibrationReady(out var calibrationMessage))
        {
            skipReasons.Add($"机器标定未完成: {calibrationMessage}");
        }

        if (skipReasons.Count > 0)
        {
            return new PhotoTestCaptureResult(
                rawImagePath,
                "仅拍照预览，跳过检测原因: " + string.Join("; ", skipReasons),
                null);
        }

        var activeParameters = ReadVisionParameters();
        var fixedOverlayPath = CreateFrontBackOverlayDiagnosticPath();
        var frontBackDebug = _visionService.AnalyzeFrontBackDebug(
            _lastCameraImage!,
            _template!,
            activeParameters,
            fixedOverlayPath);
        var run = await _inspectionRunCoordinator.RunAsync(new InspectionRunRequest(
            _lastCameraImage!,
            rawImagePath,
            _template!,
            activeParameters,
            "拍照测试",
            WriteToPlc: false,
            _plcClient?.IsConnected == true,
            _plcIpAddress,
            _plcPort,
            GetEffectivePlcOutputTransform(),
            frontBackDebug));

        foreach (var log in run.Logs)
        {
            Log(log);
        }

        _lastInspectionResult = run.Result;
        return new PhotoTestCaptureResult(rawImagePath, null, run);
    }

    private static string CreateFrontBackOverlayDiagnosticPath()
    {
        var now = DateTimeOffset.Now;
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "Diagnostics",
            "FrontBackOverlay",
            now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        return Path.Combine(
            directory,
            $"{now:HHmmssfff}-fixed-overlay.png");
    }

    private string BuildPhotoTestInitialText()
    {
        var templateText = _template is null
            ? "未加载模板"
            : $"{_template.ProductName} / {_template.BatchNo}";
        return
            "拍照测试\n" +
            "==============================\n" +
            "用途: 查看当前工件信息、模板对比偏差、准备给 PLC 的 X/Y/R。\n" +
            "动作: 只拍照、只计算、只显示，不写 PLC。\n\n" +
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
            "拍照测试结果",
            "==============================",
            "模式: 仅拍照预览",
            $"原因: {test.SkipReason}",
            $"原图: {FormatPathForDisplay(test.RawImagePath)}",
            string.Empty,
            "当前状态",
            "------------------------------",
            $"相机: {(_cameraConnected ? "已连接" : "未连接")}",
            $"PLC: {(_plcClient?.IsConnected == true ? "已连接" : "未连接")}",
            $"模板: {templateText}",
            $"机器标定: {calibrationText}",
            string.Empty,
            "说明",
            "------------------------------",
            "本次只保存并显示相机原图，不做工件检测，不生成PLC预览，也不写PLC。",
            "模板和机器标定都准备好后，再点拍照测试会自动恢复完整检测预览。"
        };
        return string.Join(Environment.NewLine, text);
    }

    private string BuildPhotoTestDetails(InspectionRunResult run)
    {
        var result = run.Result;
        var output = run.Output;
        var measurement = result.Measurement;
        var alignment = output.AlignmentSnapshot;
        var angle = output.AngleDiagnostic;
        var similarity = output.TemplateSimilarity;
        var frontBackDebug = run.FrontBackDebug;
        var plcOutput = measurement is null
            ? new PlcOutputCommand(0, 0, 0)
            : PlcOutputDiagnosticFormatter.CalculatePlcOutputCommand(measurement, GetEffectivePlcOutputTransform());
        var rAxisReferenceOutput = measurement is null
            ? new PlcOutputCommand(0, 0, 0)
            : PlcOutputDiagnosticFormatter.CalculateRAxisCenterReferenceCommand(measurement, GetEffectivePlcOutputTransform());
        var expectedD1010 = result.Decision == InspectionDecision.Ok ? 1 : 2;
        var currentCenter = measurement is null
            ? null
            : ReadVisionParameters().CameraCalibration.PixelToMachine(measurement.CenterXPixel, measurement.CenterYPixel);

        var text = new List<string>
        {
            "拍照测试结果",
            "==============================",
            $"结果: {result.Decision}",
            $"NG原因: {result.NgReason}",
            $"消息: {result.Message}",
            $"原图: {FormatPathForDisplay(result.RawImagePath)}",
            $"诊断图: {FormatPathForDisplay(result.ResultImagePath)}",
            $"报告: {FormatPathForDisplay(run.ReportPath)}",
            string.Empty,
            "当前工件",
            "------------------------------"
        };

        if (measurement is null)
        {
            text.Add("未识别到有效工件。");
        }
        else
        {
            text.Add($"中心像素: X={measurement.CenterXPixel:F1}px, Y={measurement.CenterYPixel:F1}px");
            if (currentCenter is not null)
            {
                text.Add($"当前机械坐标: X={FormatDisplayValue(currentCenter.XMm)}mm, Y={FormatDisplayValue(currentCenter.YMm)}mm");
            }

            text.Add($"当前角度R: {FormatDisplayValue(measurement.AngleDegrees)}deg");
            text.Add($"宽度: {measurement.WidthMm:F3}mm");
            text.Add($"高度: {measurement.HeightMm:F3}mm");
            text.Add($"面积: {measurement.AreaPixels:F0}px");
            text.Add($"识别分数: {measurement.MatchScore:F3}");
        }

        text.AddRange([
            string.Empty,
            "模板工件",
            "------------------------------"
        ]);
        if (_template is null)
        {
            text.Add("未加载模板。");
        }
        else
        {
            text.Add($"型号: {_template.ProductName}");
            text.Add($"批次: {_template.BatchNo}");
            text.Add($"模板中心: X={FormatDisplayValue(_template.ReferenceCenterXMm)}mm, Y={FormatDisplayValue(_template.ReferenceCenterYMm)}mm");
            text.Add($"模板角度R: {FormatDisplayValue(_template.ReferenceAngleDegrees)}deg");
            text.Add($"模板像素中心: X={_template.ReferenceCenterXPixel:F1}px, Y={_template.ReferenceCenterYPixel:F1}px");
            text.Add($"模板尺寸: W={_template.WidthMm:F3}mm, H={_template.HeightMm:F3}mm, Area={_template.AreaPixels:F0}px");
            text.Add($"模板图片: {FormatPathForDisplay(_template.ImagePath)}");
        }

        text.AddRange([
            string.Empty,
            "当前-模板偏差",
            "------------------------------"
        ]);
        if (measurement is null)
        {
            text.Add("无有效偏差。正式流程会判 NG。");
        }
        else
        {
            text.Add($"视觉偏差 当前-模板: X={FormatDisplayValue(measurement.XOffsetMm)}mm, Y={FormatDisplayValue(measurement.YOffsetMm)}mm, R={FormatDisplayValue(measurement.AngleOffsetDegrees)}deg");
            text.Add($"旋转前纠偏 模板-当前: X={FormatDisplayValue(-measurement.XOffsetMm)}mm, Y={FormatDisplayValue(-measurement.YOffsetMm)}mm, R={FormatDisplayValue(-measurement.AngleOffsetDegrees)}deg");
            text.Add($"R后最终纠偏参考: X={FormatDisplayValue(measurement.XCompensationMm)}mm, Y={FormatDisplayValue(measurement.YCompensationMm)}mm, R={FormatDisplayValue(measurement.RotationCompensationDegrees)}deg");
            if (alignment is not null)
            {
                text.Add($"R方向同步: {(alignment.RCommandDirection < 0 ? "取反并同步重算XY" : "不取反")}");
                text.Add($"视觉R纠偏: {FormatDisplayValue(alignment.VisionHomeRActionDegrees)}deg");
                text.Add($"PLC实际R输出: {FormatDisplayValue(alignment.HomeRActionDegrees)}deg");
                text.Add($"参与XY计算的实际旋转: {FormatDisplayValue(alignment.PhysicalRotationDegrees)}deg");
            }
        }

        text.AddRange([
            string.Empty,
            "PLC输出预览",
            "------------------------------",
            "拍照测试不写 PLC；下面是正式检测时将使用的值。"
        ]);
        if (result.Decision == InspectionDecision.Ok && measurement is not null)
        {
            text.Add($"D1002 = {PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.XDeviation)} mm");
            text.Add($"D1004 = {PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.YDeviation)} mm");
            text.Add($"D1006 = {PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.RDeviation)} deg");
            text.Add($"D1010 = {expectedD1010}");
        }
        else
        {
            text.Add("D1002 = 0 mm");
            text.Add("D1004 = 0 mm");
            text.Add("D1006 = 0 deg");
            text.Add($"D1010 = {expectedD1010}");
            text.Add("说明: NG/Error 时正式流程不使用 X/Y/R 偏差输出。");
        }

        text.AddRange([
            string.Empty,
            "R轴中心使用判断",
            "------------------------------"
        ]);
        if (measurement is null)
        {
            text.Add("无有效工件，无法判断本次XYR输出。");
        }
        else
        {
            text.Add(alignment?.RAxisCenterEnabled == true
                ? $"R轴中心标定: 已启用，已参与 R后最终纠偏参考 / Home2D动作量计算，R+方向={(alignment.RAxisMachineAngleDirection < 0 ? "顺时针" : "逆时针")}。"
                : "R轴中心标定: 未启用或本次没有有效R轴中心快照。");
            if (alignment is not null)
            {
                text.Add($"R命令方向: {(alignment.RCommandDirection < 0 ? "取反并同步重算XY" : "不取反")}");
                text.Add($"视觉R纠偏={FormatDisplayValue(alignment.VisionHomeRActionDegrees)} deg, PLC实际R输出={FormatDisplayValue(alignment.HomeRActionDegrees)} deg");
            }
            text.Add("当前PLC写值: 使用R轴中心后的最终纠偏量，D1002/D1004已包含R轴中心旋转后的XY补偿。");
            text.Add($"当前PLC值: D1002={PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.XDeviation)} mm, D1004={PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.YDeviation)} mm, D1006={PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.RDeviation)} deg");
            text.Add($"R轴中心修正后确认值: D1002={PlcOutputDiagnosticFormatter.FormatPlcValueText(rAxisReferenceOutput.XDeviation)} mm, D1004={PlcOutputDiagnosticFormatter.FormatPlcValueText(rAxisReferenceOutput.YDeviation)} mm, D1006={PlcOutputDiagnosticFormatter.FormatPlcValueText(rAxisReferenceOutput.RDeviation)} deg");
            text.Add(PlcOutputDiagnosticFormatter.BuildRAxisCenterUsageJudgment(
                measurement,
                GetEffectivePlcOutputTransform(),
                alignment));
        }

        text.AddRange([
            string.Empty,
            "角度诊断",
            "------------------------------"
        ]);
        if (angle is null)
        {
            text.Add("无角度诊断。");
        }
        else
        {
            text.Add($"模式: {angle.Mode}");
            text.Add($"来源: {angle.Source}");
            text.Add($"轮廓角: {angle.ContourAngleDegrees:F3}deg");
            text.Add($"解析角: {angle.ResolvedAngleDegrees:F3}deg");
            text.Add($"可靠: {(angle.IsReliable ? "是" : "否")}");
            text.Add($"分数: {angle.Score:F3}");
            text.Add($"第二候选: {angle.AlternativeScore:F3}");
            text.Add($"差值: {angle.ScoreMargin:F3}");
            text.Add($"说明: {angle.Message}");
            text.Add(_inspectionDiagnosticMessageFormatter.BuildAngleCandidatesText(angle));
        }

        text.AddRange([
            string.Empty,
            "形状/模板匹配",
            "------------------------------"
        ]);
        if (similarity is null)
        {
            text.Add("无模板形状相似度明细。");
        }
        else
        {
            text.Add($"是否可靠: {(similarity.IsReliable ? "是" : "否")}");
            text.Add($"是否同一工件: {(similarity.IsSamePart ? "是" : "否")}");
            text.Add($"最终分数: {similarity.FinalScore:F3}");
            text.Add($"尺寸分数: {similarity.SizeScore:F3}");
            text.Add($"形状分数: {similarity.ShapeScore:F3}");
            text.Add($"Mask IoU: {similarity.MaskIoU:F3}");
            text.Add($"边缘分数: {similarity.EdgeDistanceScore:F3}");
            text.Add($"说明: {similarity.Message}");
        }

        text.AddRange([
            string.Empty,
            "正反面调试",
            "------------------------------",
            "状态: 只显示，不参与NG，不写PLC"
        ]);
        if (frontBackDebug is null)
        {
            text.Add("无正反面调试结果。");
        }
        else
        {
            text.Add($"建议: {FormatFrontBackDebugDecision(frontBackDebug.SuggestedDecision)}");
            text.Add($"可靠: {(frontBackDebug.IsReliable ? "是" : "否")}");
            text.Add($"正面分数: {frontBackDebug.FrontScore:F3}");
            text.Add($"反面分数: {frontBackDebug.BackScore:F3}");
            text.Add($"分差(正面-反面): {frontBackDebug.ScoreDifference:F3}");
            text.Add($"正面对齐: {frontBackDebug.FrontAlignment}");
            text.Add($"反面对齐: {frontBackDebug.BackAlignment}");
            text.Add($"说明: {frontBackDebug.Message}");
            if (frontBackDebug.SameAngleOverlay is { } sameAngle)
            {
                text.Add(string.Empty);
                text.Add("同角度叠放调试");
                text.Add("状态: 只显示，不参与NG，不写PLC");
                text.Add($"建议: {FormatFrontBackDebugDecision(sameAngle.SuggestedDecision)}");
                text.Add($"可对齐分数: {sameAngle.Score:F3}");
                text.Add($"Mask IoU: {sameAngle.MaskIoU:F3}");
                text.Add($"形状分数: {sameAngle.ShapeScore:F3}");
                text.Add($"边缘距离分数: {sameAngle.EdgeDistanceScore:F3}");
                text.Add($"尺寸分数: {sameAngle.SizeScore:F3}");
                text.Add($"对齐方式: {sameAngle.Alignment}");
                text.Add($"说明: {sameAngle.Message}");
            }
            else
            {
                text.Add("同角度叠放调试: 不可用。");
            }
            if (frontBackDebug.ContourMirror is { } contourMirror)
            {
                text.Add(string.Empty);
                text.Add("Contour mirror front/back debug");
                text.Add(_visionParameters.BackSideNgEnabled
                ? "Status: backside NG enabled; diff(front-back)<0 is NG"
                : "Status: switch off; display only");
            text.Add($"Decision: {FormatFrontBackDebugDecision(contourMirror.SuggestedDecision)}");
                text.Add($"Reliable: {(contourMirror.IsReliable ? "Yes" : "No")}");
                text.Add($"Front contour score: {contourMirror.FrontScore:F3}");
                text.Add($"Back mirrored score: {contourMirror.BackScore:F3}");
                text.Add($"Diff(front-back): {contourMirror.ScoreDifference:F3}");
                text.Add($"Front angle offset: {contourMirror.FrontAngleOffsetDegrees:F3}deg");
                text.Add($"Back mirror angle offset: {contourMirror.BackAngleOffsetDegrees:F3}deg");
                text.Add($"Front alternative score: {contourMirror.FrontAlternativeScore:F3}");
                text.Add($"Back alternative score: {contourMirror.BackAlternativeScore:F3}");
                text.Add($"Current contour signal: {contourMirror.CurrentSignal:F3}");
                text.Add($"Template contour signal: {contourMirror.TemplateSignal:F3}");
                text.Add($"Search range: {contourMirror.SearchRangeDegrees:F1}deg");
                text.Add($"Message: {contourMirror.Message}");
            }
            else
            {
                text.Add("Contour mirror front/back debug: unavailable.");
            }
            if (frontBackDebug.FixedAngleOverlay is { } fixedOverlay)
            {
                text.Add(string.Empty);
                text.Add("Fixed overlay front/back debug");
                text.Add("Status: display only; not NG; not PLC");
                text.Add($"Diagnostic image: {FormatPathForDisplay(fixedOverlay.DiagnosticImagePath)}");
                AppendFixedOverlayVariantText(text, fixedOverlay.CenterOnly);
                AppendFixedOverlayVariantText(text, fixedOverlay.ResolvedAngle);
                if (fixedOverlay.MirrorAngle is { } mirrorAngle)
                {
                    AppendFixedOverlayVariantText(text, mirrorAngle);
                }

                text.Add($"Message: {fixedOverlay.Message}");
            }
            else
            {
                text.Add("Fixed overlay front/back debug: unavailable.");
            }
            if (frontBackDebug.EdgeRing is { } edgeRing)
            {
                text.Add(string.Empty);
                text.Add("边缘环带调试");
                text.Add("建议: 仅看边缘对比，不作为正反面依据");
                text.Add($"可靠: {(edgeRing.IsReliable ? "是" : "否")}");
                text.Add($"边缘正面分数: {edgeRing.FrontScore:F3}");
                text.Add($"边缘反面分数: {edgeRing.BackScore:F3}");
                text.Add($"边缘分差(正面-反面): {edgeRing.ScoreDifference:F3}");
                text.Add($"稳定边缘比例: {edgeRing.StableSampleRatio:F3}");
                text.Add($"梯度方向一致性: {edgeRing.GradientDirectionAgreement:F3}");
                text.Add($"模板边缘对比度: {edgeRing.TemplateEdgeContrast:F3}");
                text.Add($"当前边缘对比度: {edgeRing.CurrentEdgeContrast:F3}");
                text.Add($"采样点数: {edgeRing.SampleCount}");
                text.Add($"说明: {edgeRing.Message}");
            }
            else
            {
                text.Add("边缘环带调试: 不可用。");
            }
        }

        if (alignment is not null)
        {
            text.AddRange([
                string.Empty,
                "XYR几何快照",
                "------------------------------",
                $"当前位姿: X={FormatDisplayValue(alignment.CurrentPose.XMm)}mm, Y={FormatDisplayValue(alignment.CurrentPose.YMm)}mm, R={FormatDisplayValue(alignment.CurrentPose.AngleDegrees)}deg",
                $"标准位姿: X={FormatDisplayValue(alignment.TemplatePose.XMm)}mm, Y={FormatDisplayValue(alignment.TemplatePose.YMm)}mm, R={FormatDisplayValue(alignment.TemplatePose.AngleDegrees)}deg",
                alignment.RAxisCenterEnabled
                    ? $"R轴中心: X={FormatDisplayValue(alignment.RAxisCenter.XMm)}mm, Y={FormatDisplayValue(alignment.RAxisCenter.YMm)}mm, R+方向={(alignment.RAxisMachineAngleDirection < 0 ? "顺时针" : "逆时针")}"
                    : "R轴中心: 未启用",
                $"R命令方向: {(alignment.RCommandDirection < 0 ? "取反并同步重算XY" : "不取反")}",
                $"视觉R纠偏: {FormatDisplayValue(alignment.VisionHomeRActionDegrees)}deg",
                $"PLC实际R输出: {FormatDisplayValue(alignment.HomeRActionDegrees)}deg",
                $"参与XY计算的实际旋转: {FormatDisplayValue(alignment.PhysicalRotationDegrees)}deg",
                $"R后中心: X={FormatDisplayValue(alignment.CenterAfterRotation.XMm)}mm, Y={FormatDisplayValue(alignment.CenterAfterRotation.YMm)}mm",
                $"Home2D动作量: X={FormatDisplayValue(alignment.HomeXActionMm)}mm, Y={FormatDisplayValue(alignment.HomeYActionMm)}mm, R={FormatDisplayValue(alignment.HomeRActionDegrees)}deg"
            ]);
        }

        return string.Join(Environment.NewLine, text);
    }

    private static void AppendFixedOverlayVariantText(
        List<string> text,
        FixedAngleOverlayVariantDebugResult variant)
    {
        text.Add(
            $"{variant.Name}: Score={variant.Score:F3}, IoU={variant.MaskIoU:F3}, " +
            $"Mismatch={variant.MismatchRatio:F3}, TemplateOnly={variant.TemplateOnlyRatio:F3}, " +
            $"CurrentOnly={variant.CurrentOnlyRatio:F3}, CurrentAngle={variant.CurrentAngleDegrees:F3}deg, " +
            $"TemplateAngle={variant.TemplateAngleDegrees:F3}deg, Alignment={variant.Alignment}");
    }

    private void LogPhotoTestSummary(InspectionRunResult run)
    {
        Log($"拍照测试: {run.Result.Decision}: {run.Result.Message}");
        Log(_inspectionDiagnosticMessageFormatter.BuildCandidateDiagnosticsText(run.Output.CandidateDiagnostics));
        Log(_inspectionDiagnosticMessageFormatter.BuildAngleCandidatesText(run.Output.AngleDiagnostic));
        if (run.FrontBackDebug is not null)
        {
            Log(
                "正反面调试(只显示不NG): " +
                $"建议={FormatFrontBackDebugDecision(run.FrontBackDebug.SuggestedDecision)}, " +
                $"可靠={(run.FrontBackDebug.IsReliable ? "是" : "否")}, " +
                $"正面={run.FrontBackDebug.FrontScore:F3}, " +
                $"反面={run.FrontBackDebug.BackScore:F3}, " +
                $"分差={run.FrontBackDebug.ScoreDifference:F3}");
            if (run.FrontBackDebug.SameAngleOverlay is { } sameAngle)
            {
                Log(
                    "同角度叠放调试(只显示不NG): " +
                    $"建议={FormatFrontBackDebugDecision(sameAngle.SuggestedDecision)}, " +
                    $"Score={sameAngle.Score:F3}, " +
                    $"MaskIoU={sameAngle.MaskIoU:F3}, " +
                    $"Shape={sameAngle.ShapeScore:F3}, " +
                    $"Edge={sameAngle.EdgeDistanceScore:F3}");
            }
            if (run.FrontBackDebug.ContourMirror is { } contourMirror)
            {
                Log(
                    $"Contour mirror front/back debug({(_visionParameters.BackSideNgEnabled ? "backside NG enabled" : "display only")}): " +
                    $"Decision={FormatFrontBackDebugDecision(contourMirror.SuggestedDecision)}, " +
                    $"Reliable={(contourMirror.IsReliable ? "Yes" : "No")}, " +
                    $"Front={contourMirror.FrontScore:F3}, " +
                    $"Back={contourMirror.BackScore:F3}, " +
                    $"Diff={contourMirror.ScoreDifference:F3}({(contourMirror.ScoreDifference < 0.0 ? "NG backside" : "front")}), " +
                    $"FrontAngle={contourMirror.FrontAngleOffsetDegrees:F3}deg, " +
                    $"BackAngle={contourMirror.BackAngleOffsetDegrees:F3}deg");
            }
            if (run.FrontBackDebug.FixedAngleOverlay is { } fixedOverlay)
            {
                Log(
                    "Fixed overlay front/back debug(display only): " +
                    $"CenterMismatch={fixedOverlay.CenterOnly.MismatchRatio:F3}, " +
                    $"ResolvedMismatch={fixedOverlay.ResolvedAngle.MismatchRatio:F3}, " +
                    $"MirrorMismatch={(fixedOverlay.MirrorAngle is null ? "-" : fixedOverlay.MirrorAngle.MismatchRatio.ToString("F3", CultureInfo.InvariantCulture))}, " +
                    $"Image={fixedOverlay.DiagnosticImagePath ?? "-"}");
            }
            if (run.FrontBackDebug.EdgeRing is { } edgeRing)
            {
                Log(
                    "边缘正反面调试(只显示不NG): " +
                    "建议=仅看边缘对比, " +
                    $"可靠={(edgeRing.IsReliable ? "是" : "否")}, " +
                    $"正面={edgeRing.FrontScore:F3}, " +
                    $"反面={edgeRing.BackScore:F3}, " +
                    $"分差={edgeRing.ScoreDifference:F3}, " +
                    $"稳定边缘比例={edgeRing.StableSampleRatio:F3}, " +
                    $"模板对比={edgeRing.TemplateEdgeContrast:F3}, " +
                    $"当前对比={edgeRing.CurrentEdgeContrast:F3}");
            }
        }
        LogPlcOutputPreview(run.Result, run.Output.AlignmentSnapshot);
        if (run.ReportPath is not null)
        {
            Log($"拍照测试报告已保存: {run.ReportPath}");
        }
        else if (run.ReportError is not null)
        {
            Log($"拍照测试报告保存失败: {run.ReportError}");
        }
    }

    private static string FormatPathForDisplay(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "-" : path;
    }

    private static string FormatDisplayValue(double value)
    {
        return PlcOutputDiagnosticFormatter.FormatPlcValueText(value);
    }

    private static string FormatFrontBackDebugDecision(FrontBackDebugDecision decision)
    {
        return decision switch
        {
            FrontBackDebugDecision.Front => "正面",
            FrontBackDebugDecision.Back => "反面",
            FrontBackDebugDecision.Uncertain => "不确定",
            _ => "不可用"
        };
    }
}
