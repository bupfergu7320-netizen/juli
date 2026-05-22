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
    private void OpenRAxisCenterCalibrationDialog()
    {
        if (!RequireTechnician("R轴中心标定"))
        {
            return;
        }

        var dialog = CreateToolDialog("R轴中心标定", 1320, 800);
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var existingCalibration = ReadEffectiveRAxisCenterCalibrationFromUi();
        var points = existingCalibration.Points.Count >= 3
            ? existingCalibration.Points
                .OrderBy(point => point.AngleDegrees)
                .Select(point => new EditableRAxisCenterCalibrationPoint(
                    CalibrationEditorPointFactory.BuildRAxisCenterPointName(point.AngleDegrees),
                    point.AngleDegrees,
                    point.PixelX,
                    point.PixelY,
                    point.ObservedCenterXMm,
                    point.ObservedCenterYMm,
                    true))
                .ToList()
            : CalibrationEditorPointFactory.CreateDefaultRAxisCenterCalibrationPoints();
        var selectedPoint = points.First();
        var computedCalibration = existingCalibration.Enabled ? existingCalibration : null;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(CreateTurntableHeaderText($"当前型号: {TurntableStatusMessageFormatter.GetCurrentProductName(_batchSession.ProductName, _currentProductName)}", 0));
        var statusHeader = CreateTurntableHeaderText(
            CalibrationResultMessageFormatter.FormatRAxisCenterStatus(ReadEffectiveRAxisCenterCalibrationFromUi()),
            1);
        statusHeader.Foreground = ReadEffectiveRAxisCenterCalibrationFromUi().Enabled ? Brushes.ForestGreen : Brushes.DarkOrange;
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
        table.Columns.Add(new DataGridTextColumn { Header = "点位", Binding = new System.Windows.Data.Binding(nameof(EditableRAxisCenterCalibrationPoint.Name)), IsReadOnly = true });
        table.Columns.Add(new DataGridTextColumn { Header = "R角度(deg)", Binding = new System.Windows.Data.Binding(nameof(EditableRAxisCenterCalibrationPoint.AngleDegrees)) });
        table.Columns.Add(new DataGridTextColumn { Header = "中心X(mm)", Binding = new System.Windows.Data.Binding(nameof(EditableRAxisCenterCalibrationPoint.MachineXMm)), IsReadOnly = true });
        table.Columns.Add(new DataGridTextColumn { Header = "中心Y(mm)", Binding = new System.Windows.Data.Binding(nameof(EditableRAxisCenterCalibrationPoint.MachineYMm)), IsReadOnly = true });
        table.Columns.Add(new DataGridTextColumn { Header = "像素X", Binding = new System.Windows.Data.Binding(nameof(EditableRAxisCenterCalibrationPoint.PixelX)), IsReadOnly = true });
        table.Columns.Add(new DataGridTextColumn { Header = "像素Y", Binding = new System.Windows.Data.Binding(nameof(EditableRAxisCenterCalibrationPoint.PixelY)), IsReadOnly = true });
        table.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new System.Windows.Data.Binding(nameof(EditableRAxisCenterCalibrationPoint.CaptureStatus)), IsReadOnly = true });
        sidePanel.Children.Add(table);

        var pointEditor = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        pointEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        pointEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pointEditor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        pointEditor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        pointEditor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        var currentPointText = CreateCalibrationLabel($"当前点: {selectedPoint.Name}");
        Grid.SetColumnSpan(currentPointText, 2);
        pointEditor.Children.Add(currentPointText);
        var suggestionText = new TextBlock
        {
            Text = CalibrationResultMessageFormatter.FormatRAxisCenterPointSuggestion(selectedPoint.AngleDegrees),
            FontSize = 16,
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(suggestionText, 1);
        Grid.SetColumnSpan(suggestionText, 2);
        pointEditor.Children.Add(suggestionText);
        var angleBox = AddCalibrationEditorRow(pointEditor, 2, "R角度(deg)", selectedPoint.AngleDegrees);
        Grid.SetRow(pointEditor, 1);
        sidePanel.Children.Add(pointEditor);

        var message = new TextBlock
        {
            Text = "R轴中心标定固定使用7x7标定板中心圆点，软件取第4行第4列圆点。保持X/Y不动，只转R轴；R角度请填写设备HMI/PLC实际到位值。采集前必须已有有效9点XY标定。",
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
                : CalibrationResultMessageFormatter.FormatRAxisCenterResult(computedCalibration),
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
            CommitRAxisCenterCalibrationEditor(table, selectedPoint, angleBox);
            if (table.SelectedItem is EditableRAxisCenterCalibrationPoint point)
            {
                selectedPoint = point;
                currentPointText.Text = $"当前点: {selectedPoint.Name}";
                suggestionText.Text = CalibrationResultMessageFormatter.FormatRAxisCenterPointSuggestion(
                    selectedPoint.AngleDegrees);
                angleBox.Text = selectedPoint.AngleDegrees.ToString(CultureInfo.InvariantCulture);
            }
        };

        var buttons = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        for (var index = 0; index < 6; index++)
        {
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        buttons.Children.Add(CreateCalibrationGridButton("上一角度", (_, _) =>
        {
            CommitRAxisCenterCalibrationEditor(table, selectedPoint, angleBox);
            var index = Math.Max(0, points.IndexOf(selectedPoint) - 1);
            table.SelectedItem = points[index];
            table.ScrollIntoView(points[index]);
        }, 0));
        buttons.Children.Add(CreateCalibrationGridButton("下一角度", (_, _) =>
        {
            CommitRAxisCenterCalibrationEditor(table, selectedPoint, angleBox);
            var index = Math.Min(points.Count - 1, points.IndexOf(selectedPoint) + 1);
            table.SelectedItem = points[index];
            table.ScrollIntoView(points[index]);
        }, 1));
        buttons.Children.Add(CreateCalibrationGridButton("拍照采集中心", async (_, _) =>
        {
            CalibrationImageSaveTarget? saveTarget = null;
            try
            {
                CommitRAxisCenterCalibrationEditor(table, selectedPoint, angleBox);
                saveTarget = CalibrationFileStore.CreateRAxisCenterImageSaveTarget(selectedPoint.AngleDegrees);
                if (!_cameraConnected)
                {
                    throw new InvalidOperationException("相机未连接，无法采集R轴中心标定点。");
                }

                if (_productionEnabled)
                {
                    throw new InvalidOperationException("请先点击左侧“停止”，再进行R轴中心标定。标定拍照由上位机按钮触发。");
                }

                var cameraCalibration = ReadEffectiveCameraCalibrationFromUi();
                if (!cameraCalibration.Enabled)
                {
                    throw new InvalidOperationException("R轴中心标定前必须先完成有效9点XY标定。");
                }

                message.Text = $"正在采集 {selectedPoint.Name}...";
                var rawImagePath = await CaptureCameraImageAsync(saveImage: true, saveTarget);
                var parameters = ReadVisionParameters() with
                {
                    CameraCalibration = CameraCalibration.Disabled,
                    RAxisCenterCalibration = RAxisCenterCalibration.Disabled
                };
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
                var machineCenter = cameraCalibration.PixelToMachine(centerPoint.X, centerPoint.Y);
                selectedPoint.PixelX = centerPoint.X;
                selectedPoint.PixelY = centerPoint.Y;
                selectedPoint.MachineXMm = machineCenter.XMm;
                selectedPoint.MachineYMm = machineCenter.YMm;
                selectedPoint.IsCaptured = true;
                using var preview = DrawCalibrationBoardCenterPreview(calibrationImage, boardResult, centerPoint, selectedPoint.Name);
                var diagnosticPath = _calibrationFileStore.SaveCalibrationBoardDiagnosticImage(preview, success: true, saveTarget);
                previewImage.Source = CreateBitmapImageFromMat(preview);
                table.Items.Refresh();
                message.Text =
                    $"{selectedPoint.Name} 已采集: R={selectedPoint.AngleDegrees:F3}deg, " +
                    $"X={selectedPoint.MachineXMm:F4}mm, Y={selectedPoint.MachineYMm:F4}mm\n" +
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
                    Log($"R轴中心标定板采集失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                message.Text = ex.Message;
                Log($"R轴中心标定点采集失败: {ex.Message}");
            }
        }, 2));
        buttons.Children.Add(CreateCalibrationGridButton("计算中心", (_, _) =>
        {
            try
            {
                CommitRAxisCenterCalibrationEditor(table, selectedPoint, angleBox);
                computedCalibration = CalibrationEditorPointFactory.CalculateRAxisCenterCalibration(
                    _calibrationEditorSolver,
                    points,
                    ReadEffectiveCameraCalibrationFromUi(),
                    RAxisCenterCaptureTargetBoard);
                resultText.Text = CalibrationResultMessageFormatter.FormatRAxisCenterResult(computedCalibration);
                var quality = _calibrationQualityEvaluator.EvaluateRAxisCenter(computedCalibration);
                resultText.Text += Environment.NewLine + quality.Summary;
                resultText.Foreground = quality.IsAccepted ? Brushes.ForestGreen : Brushes.DarkOrange;
                message.Text = quality.IsAccepted
                    ? "R轴中心已计算且质量合格。确认每个R角度都是设备实际到位值后，再点击“保存标定”。"
                    : "R轴中心已计算但质量不合格，请按提示扩大角度覆盖或重拍。";
            }
            catch (Exception ex)
            {
                message.Text = ex.Message;
                Log($"R轴中心标定计算失败: {ex.Message}");
            }
        }, 3));
        buttons.Children.Add(CreateCalibrationGridButton("保存标定", (_, _) =>
        {
            try
            {
                CommitRAxisCenterCalibrationEditor(table, selectedPoint, angleBox);
                computedCalibration ??= CalibrationEditorPointFactory.CalculateRAxisCenterCalibration(
                    _calibrationEditorSolver,
                    points,
                    ReadEffectiveCameraCalibrationFromUi(),
                    RAxisCenterCaptureTargetBoard);
                var quality = _calibrationQualityEvaluator.EnsureRAxisCenter(computedCalibration);
                SaveMachineRAxisCenterCalibration(computedCalibration);
                var reportPath = _reportSaveService.SaveCalibrationReport(
                    CreateCalibrationReportSaveContext("r-axis-center", computedCalibration, quality),
                    Log);
                statusHeader.Text = CalibrationResultMessageFormatter.FormatRAxisCenterStatus(
                    ReadEffectiveRAxisCenterCalibrationFromUi());
                statusHeader.Foreground = ReadEffectiveRAxisCenterCalibrationFromUi().Enabled ? Brushes.ForestGreen : Brushes.DarkOrange;
                message.Text = $"R轴中心标定已保存为机器全局标定，切换型号不会覆盖。\n标定报告: {reportPath}";
                MessageText.Text = message.Text;
                Log(message.Text);
            }
            catch (Exception ex)
            {
                message.Text = ex.Message;
                Log($"R轴中心标定保存失败: {ex.Message}");
                MessageBox.Show(ex.Message, "R轴中心标定", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }, 4));
        buttons.Children.Add(CreateCalibrationGridButton("关闭", (_, _) => dialog.Close(), 5));
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.ShowDialog();
    }
}
