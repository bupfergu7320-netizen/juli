using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JuliMvs.App.Services;
using JuliMvs.Core.Camera;
using JuliMvs.Core.Vision;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App;

public partial class MainWindow
{
    private void OpenCalibrationDialog()
    {
        if (!RequireTechnician("9点XY标定"))
        {
            return;
        }

        var dialog = CreateToolDialog("9点XY标定", 1380, 800);
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var existingPoints = ReadRawCameraCalibrationFromUi().Points;
        var points = existingPoints.Count == RequiredCalibrationPointCount
            ? existingPoints
                .Select((point, index) => new EditableCalibrationPoint(
                    CalibrationEditorPointFactory.GetCalibrationPointName(index, RequiredCalibrationPointCount),
                    point.PixelX,
                    point.PixelY,
                    point.MachineXMm,
                    point.MachineYMm,
                    true))
                .ToList()
            : CalibrationEditorPointFactory.CreateDefaultNinePointCalibrationPoints(DefaultCalibrationStepMm);
        var selectedPoint = points.First();
        var computedCalibration = ReadEffectiveCameraCalibrationFromUi().Enabled
            ? ReadEffectiveCameraCalibrationFromUi()
            : null;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(CreateTurntableHeaderText($"当前型号: {TurntableStatusMessageFormatter.GetCurrentProductName(_batchSession.ProductName, _currentProductName)}", 0));
        var statusHeader = CreateTurntableHeaderText(
            TurntableStatusMessageFormatter.FormatCalibrationText(
                ReadEffectiveCameraCalibrationFromUi(),
                ReadEffectiveRAxisCenterCalibrationFromUi()),
            1);
        statusHeader.Foreground = IsMachineCalibrationReady(out _) ? Brushes.ForestGreen : Brushes.DarkOrange;
        header.Children.Add(statusHeader);
        root.Children.Add(header);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(620) });

        var previewImage = new System.Windows.Controls.Image
        {
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        var previewBorder = new Border
        {
            Background = Brushes.Black,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = previewImage
        };
        content.Children.Add(previewBorder);

        var sidePanel = new Grid { Margin = new Thickness(22, 0, 0, 0) };
        sidePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(310) });
        sidePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(sidePanel, 1);
        content.Children.Add(sidePanel);

        var table = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            FontSize = 16,
            ItemsSource = points,
            SelectedItem = selectedPoint
        };
        table.Columns.Add(new DataGridTextColumn { Header = "点位", Binding = new System.Windows.Data.Binding(nameof(EditableCalibrationPoint.Name)), IsReadOnly = true });
        table.Columns.Add(new DataGridTextColumn { Header = "建议机械X", Binding = new System.Windows.Data.Binding(nameof(EditableCalibrationPoint.SuggestedMachineXText)), IsReadOnly = true });
        table.Columns.Add(new DataGridTextColumn { Header = "建议机械Y", Binding = new System.Windows.Data.Binding(nameof(EditableCalibrationPoint.SuggestedMachineYText)), IsReadOnly = true });
        table.Columns.Add(new DataGridTextColumn { Header = "机械X(mm)", Binding = new System.Windows.Data.Binding(nameof(EditableCalibrationPoint.MachineXMm)) });
        table.Columns.Add(new DataGridTextColumn { Header = "机械Y(mm)", Binding = new System.Windows.Data.Binding(nameof(EditableCalibrationPoint.MachineYMm)) });
        table.Columns.Add(new DataGridTextColumn { Header = "像素X", Binding = new System.Windows.Data.Binding(nameof(EditableCalibrationPoint.PixelX)) });
        table.Columns.Add(new DataGridTextColumn { Header = "像素Y", Binding = new System.Windows.Data.Binding(nameof(EditableCalibrationPoint.PixelY)) });
        table.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new System.Windows.Data.Binding(nameof(EditableCalibrationPoint.CaptureStatus)), IsReadOnly = true });
        sidePanel.Children.Add(table);

        var pointEditor = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        pointEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        pointEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pointEditor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        pointEditor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        pointEditor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        pointEditor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        var currentPointText = CreateCalibrationLabel($"当前点: {selectedPoint.Name}");
        Grid.SetColumnSpan(currentPointText, 2);
        pointEditor.Children.Add(currentPointText);
        var suggestionText = new TextBlock
        {
            Text = CalibrationResultMessageFormatter.FormatCalibrationPointSuggestion(
                selectedPoint.SuggestedMachineXMm,
                selectedPoint.SuggestedMachineYMm),
            FontSize = 16,
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(suggestionText, 1);
        Grid.SetColumnSpan(suggestionText, 2);
        pointEditor.Children.Add(suggestionText);
        var machineXBox = AddCalibrationEditorRow(pointEditor, 2, "机械X(mm)", selectedPoint.MachineXMm);
        var machineYBox = AddCalibrationEditorRow(pointEditor, 3, "机械Y(mm)", selectedPoint.MachineYMm);
        Grid.SetRow(pointEditor, 1);
        sidePanel.Children.Add(pointEditor);

        var message = new TextBlock
        {
            Text = "默认使用7x7标定板中心圆点做9点XY，软件取第4行第4列圆点。R轴固定不动，只移动X/Y；机械X/Y填HMI/PLC实际到位值。",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0)
        };
        Grid.SetRow(message, 2);
        sidePanel.Children.Add(message);

        var resultText = new TextBlock
        {
            Text = computedCalibration is null
                ? "标定结果: 未计算"
                : $"标定结果: RMS={computedCalibration.RmsErrorMm:F4}mm",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 18, 0, 0)
        };
        Grid.SetRow(resultText, 3);
        sidePanel.Children.Add(resultText);

        Grid.SetRow(content, 1);
        root.Children.Add(content);

        table.SelectionChanged += (_, _) =>
        {
            CommitCalibrationEditor(table, selectedPoint, machineXBox, machineYBox);
            if (table.SelectedItem is EditableCalibrationPoint point)
            {
                selectedPoint = point;
                currentPointText.Text = $"当前点: {selectedPoint.Name}";
                suggestionText.Text = CalibrationResultMessageFormatter.FormatCalibrationPointSuggestion(
                    selectedPoint.SuggestedMachineXMm,
                    selectedPoint.SuggestedMachineYMm);
                machineXBox.Text = selectedPoint.MachineXMm.ToString(CultureInfo.InvariantCulture);
                machineYBox.Text = selectedPoint.MachineYMm.ToString(CultureInfo.InvariantCulture);
            }
        };

        var buttons = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        for (var index = 0; index < 6; index++)
        {
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        buttons.Children.Add(CreateCalibrationGridButton("上一点", (_, _) =>
        {
            CommitCalibrationEditor(table, selectedPoint, machineXBox, machineYBox);
            var index = Math.Max(0, points.IndexOf(selectedPoint) - 1);
            table.SelectedItem = points[index];
            table.ScrollIntoView(points[index]);
        }, 0));
        buttons.Children.Add(CreateCalibrationGridButton("下一点", (_, _) =>
        {
            CommitCalibrationEditor(table, selectedPoint, machineXBox, machineYBox);
            var index = Math.Min(points.Count - 1, points.IndexOf(selectedPoint) + 1);
            table.SelectedItem = points[index];
            table.ScrollIntoView(points[index]);
        }, 1));
        buttons.Children.Add(CreateCalibrationGridButton("拍照采集像素", async (_, _) =>
        {
            CalibrationImageSaveTarget? saveTarget = null;
            try
            {
                CommitCalibrationEditor(table, selectedPoint, machineXBox, machineYBox);
                saveTarget = CalibrationFileStore.CreateNinePointImageSaveTarget(
                    CalibrationEditorPointFactory.GetPointNumber(points, selectedPoint));
                if (!_cameraConnected)
                {
                    throw new InvalidOperationException("相机未连接，无法采集标定点。");
                }

                if (_productionEnabled)
                {
                    throw new InvalidOperationException("请先点击左侧“停止”，再进行9点XY标定。标定拍照由上位机按钮触发。");
                }

                message.Text = $"正在采集 {selectedPoint.Name}...";
                var rawImagePath = await CaptureCameraImageAsync(saveImage: true, saveTarget);
                var parameters = ReadVisionParameters() with { CameraCalibration = CameraCalibration.Disabled };
                using var calibrationImage = _visionService.PrepareImage(_lastCameraImage!, parameters);
                using var boardResult = _calibrationBoardVisionService.DetectCircleGrid(
                    calibrationImage,
                    CalibrationBoardRows,
                    CalibrationBoardColumns,
                    CalibrationBoardSpacingMm);
                var centerIndex = (CalibrationBoardRows / 2) * CalibrationBoardColumns + (CalibrationBoardColumns / 2);
                if (boardResult.Points.Count <= centerIndex)
                {
                    throw new InvalidOperationException("未识别到7x7标定板中心圆点，请确认49个圆点都完整进入画面。");
                }

                var centerPoint = boardResult.Points[centerIndex];
                selectedPoint.PixelX = centerPoint.X;
                selectedPoint.PixelY = centerPoint.Y;
                selectedPoint.IsCaptured = true;
                using var preview = DrawCalibrationBoardCenterPreview(calibrationImage, boardResult, centerPoint, selectedPoint.Name);
                var diagnosticPath = _calibrationFileStore.SaveCalibrationBoardDiagnosticImage(preview, success: true, saveTarget);
                previewImage.Source = CreateBitmapImageFromMat(preview);
                table.Items.Refresh();
                message.Text =
                    $"{selectedPoint.Name} 已采集: PixelX={selectedPoint.PixelX:F1}, PixelY={selectedPoint.PixelY:F1}\n" +
                    $"目标: 7x7标定板中心圆点(第4行第4列), 板角度={boardResult.BoardAngleDegrees:F2}deg, 板RMS={boardResult.RmsErrorPixels:F3}px\n" +
                    $"图像: {InspectionDiagnosticMessageFormatter.FormatSavedImagePath(rawImagePath)}\n" +
                    $"诊断图: {diagnosticPath}";
            }
            catch (CalibrationBoardDetectionException ex)
            {
                using (ex)
                {
                    previewImage.Source = CreateBitmapImageFromMat(ex.DiagnosticImage);
                    var diagnosticPath = _calibrationFileStore.SaveCalibrationBoardDiagnosticImage(ex.DiagnosticImage, success: false, saveTarget);
                    message.Text =
                        $"未识别到7x7标定板中心圆点。请确认49个圆点完整入镜、光源稳定、没有反光遮挡。\n" +
                        $"诊断图: {diagnosticPath}\n" +
                        $"详细信息: {ex.Message}";
                    Log($"9点XY标定板采集失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                message.Text = ex.Message;
                Log($"标定点采集失败: {ex.Message}");
            }
        }, 2));
        buttons.Children.Add(CreateCalibrationGridButton("计算标定", (_, _) =>
        {
            try
            {
                CommitCalibrationEditor(table, selectedPoint, machineXBox, machineYBox);
                computedCalibration = CalibrationEditorPointFactory.CalculateCameraCalibration(
                    _calibrationEditorSolver,
                    points,
                    RequiredCalibrationPointCount) with
                {
                    SourceDistortionCalibrationId = GetCurrentDistortionCalibrationId()
                };
                resultText.Text = CalibrationResultMessageFormatter.FormatCameraCalibrationResult(computedCalibration);
                var quality = _calibrationQualityEvaluator.EvaluateCamera(computedCalibration);
                resultText.Text += Environment.NewLine + quality.Summary;
                resultText.Foreground = quality.IsAccepted ? Brushes.ForestGreen : Brushes.DarkOrange;
                message.Text = quality.IsAccepted
                    ? "标定已计算且质量合格。确认后点击“保存标定”。"
                    : "标定已计算但质量不合格，请按提示重拍或检查机械坐标。";
            }
            catch (Exception ex)
            {
                message.Text = ex.Message;
                Log($"标定失败: {ex.Message}");
            }
        }, 3));
        buttons.Children.Add(CreateCalibrationGridButton("保存标定", (_, _) =>
        {
            try
            {
                CommitCalibrationEditor(table, selectedPoint, machineXBox, machineYBox);
                computedCalibration ??= CalibrationEditorPointFactory.CalculateCameraCalibration(
                    _calibrationEditorSolver,
                    points,
                    RequiredCalibrationPointCount) with
                {
                    SourceDistortionCalibrationId = GetCurrentDistortionCalibrationId()
                };
                var quality = _calibrationQualityEvaluator.EnsureCamera(computedCalibration);
                ApplyCalibrationToUi(computedCalibration);
                SaveMachineCalibration(computedCalibration);
                var reportPath = _reportSaveService.SaveCalibrationReport(
                    CreateCalibrationReportSaveContext("nine-point-xy", computedCalibration, quality),
                    Log);
                statusHeader.Text = TurntableStatusMessageFormatter.FormatCalibrationText(
                    ReadEffectiveCameraCalibrationFromUi(),
                    ReadEffectiveRAxisCenterCalibrationFromUi());
                statusHeader.Foreground = IsMachineCalibrationReady(out _) ? Brushes.ForestGreen : Brushes.DarkOrange;
                message.Text = $"标定已保存为机器全局9点XY标定，切换型号不会覆盖。R轴中心标定已清空，请继续完成R轴中心标定。\n标定报告: {reportPath}";
                MessageText.Text = message.Text;
                Log(message.Text);
            }
            catch (Exception ex)
            {
                message.Text = ex.Message;
                Log($"标定保存失败: {ex.Message}");
                MessageBox.Show(ex.Message, "9点XY标定", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }, 4));
        buttons.Children.Add(CreateCalibrationGridButton("关闭", (_, _) => dialog.Close(), 5));
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.ShowDialog();
    }
}
