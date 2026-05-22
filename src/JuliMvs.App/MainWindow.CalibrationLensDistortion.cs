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
    private void OpenLensDistortionCalibrationDialog()
    {
        if (!RequireTechnician("镜头畸变标定"))
        {
            return;
        }

        var dialog = CreateToolDialog("镜头畸变标定", 1280, 800);
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(500) });

        var previewImage = new System.Windows.Controls.Image
        {
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        root.Children.Add(new Border
        {
            Background = Brushes.Black,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = previewImage
        });

        var sidePanel = new Grid { Margin = new Thickness(22, 0, 0, 0) };
        sidePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        sidePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sidePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        sidePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        sidePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        sidePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(sidePanel, 1);
        root.Children.Add(sidePanel);

        var rowsBox = AddCalibrationBoardRow(sidePanel, 0, "行数", "7");
        var columnsBox = AddCalibrationBoardRow(sidePanel, 1, "列数", "7");
        var spacingBox = AddCalibrationBoardRow(sidePanel, 2, "点距(mm)", "10");

        var inputs = new List<LensDistortionCalibrationInput>();
        LensDistortionCalibration? computedCalibration = null;
        var resultText = new TextBlock
        {
            Text = CalibrationResultMessageFormatter.FormatLensDistortionInitialGuidance(
                CalibrationStatusMessageFormatter.FormatLensDistortionStatus(_lensDistortionCalibration),
                LensDistortionCalibrationService.MinimumImageCount),
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 10, 0)
        };
        var resultScroll = new ScrollViewer
        {
            Content = resultText,
            Margin = new Thickness(0, 18, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(resultScroll, 3);
        Grid.SetColumnSpan(resultScroll, 2);
        sidePanel.Children.Add(resultScroll);

        void AddImage(Mat image, string? sourcePath, CalibrationImageSaveTarget saveTarget)
        {
            var rows = InputValueParser.ReadRequiredInt(rowsBox.Text, "行数", 2, 30);
            var columns = InputValueParser.ReadRequiredInt(columnsBox.Text, "列数", 2, 30);
            var spacingMm = InputValueParser.ReadRequiredDouble(spacingBox.Text, "点距", 0.0001, 1_000_000);
            using var result = _calibrationBoardVisionService.DetectCircleGrid(image, rows, columns, spacingMm);
            previewImage.Source = CreateBitmapImageFromMat(result.DiagnosticImage);
            var diagnosticPath = _calibrationFileStore.SaveCalibrationBoardDiagnosticImage(result.DiagnosticImage, success: true, saveTarget);
            var input = _lensDistortionCalibrationService.CreateInput(
                result,
                image.Size(),
                InspectionDiagnosticMessageFormatter.FormatSavedImagePath(sourcePath));
            inputs.Add(input);
            computedCalibration = null;
            resultText.Text =
                $"已加入图片: {inputs.Count}\n" +
                $"本张识别点数: {result.DetectedPointCount}/{result.ExpectedPointCount}\n" +
                $"识别方式: {result.DetectionMode}\n" +
                $"当前图片尺寸: {image.Width}x{image.Height}\n" +
                $"原图: {InspectionDiagnosticMessageFormatter.FormatSavedImagePath(sourcePath)}\n" +
                $"诊断图: {diagnosticPath}\n\n" +
                $"达到{LensDistortionCalibrationService.MinimumImageCount}张后点击“计算畸变”。\n\n" +
                "建议采图表仍按下方顺序执行，已拍过的点位可以继续下一张。\n\n" +
                CalibrationResultMessageFormatter.FormatLensDistortionCapturePlan();
            MessageText.Text = $"畸变标定图片已加入: {inputs.Count}";
            Log(MessageText.Text);
        }

        var buttons = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        for (var index = 0; index < 5; index++)
        {
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var captureButton = CreateDialogButton("拍照加入", null, 0);
        captureButton.Width = double.NaN;
        captureButton.Margin = new Thickness(6, 0, 6, 0);
        captureButton.Click += async (_, _) =>
        {
            var saveTarget = CalibrationFileStore.CreateLensDistortionImageSaveTarget(inputs.Count + 1);
            try
            {
                captureButton.IsEnabled = false;
                if (!_cameraConnected)
                {
                    throw new InvalidOperationException("相机未连接，无法进行畸变标定。");
                }

                if (_productionEnabled)
                {
                    throw new InvalidOperationException("请先点击左侧“停止”，再进行畸变标定。");
                }

                resultText.Text = "正在拍照并识别标定板...";
                var rawImagePath = await CaptureCameraImageAsync(saveImage: true, saveTarget);
                AddImage(_lastCameraImage!, rawImagePath, saveTarget);
            }
            catch (CalibrationBoardDetectionException ex)
            {
                using (ex)
                {
                    previewImage.Source = CreateBitmapImageFromMat(ex.DiagnosticImage);
                    var diagnosticPath = _calibrationFileStore.SaveCalibrationBoardDiagnosticImage(ex.DiagnosticImage, success: false, saveTarget);
                    resultText.Text = $"标定板识别失败: {ex.Message}\n诊断图: {diagnosticPath}";
                    MessageText.Text = resultText.Text;
                    Log(MessageText.Text);
                }
            }
            catch (Exception ex)
            {
                resultText.Text = $"畸变标定图片加入失败: {ex.Message}";
                MessageText.Text = resultText.Text;
                Log(MessageText.Text);
            }
            finally
            {
                captureButton.IsEnabled = true;
            }
        };
        buttons.Children.Add(captureButton);

        var clearButton = CreateDialogButton("清空", (_, _) =>
        {
            inputs.Clear();
            computedCalibration = null;
            resultText.Text =
                "已清空。\n\n" +
                CalibrationResultMessageFormatter.FormatLensDistortionInitialGuidance(
                    CalibrationStatusMessageFormatter.FormatLensDistortionStatus(_lensDistortionCalibration),
                    LensDistortionCalibrationService.MinimumImageCount);
        }, 0);
        clearButton.Width = double.NaN;
        clearButton.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(clearButton, 1);
        buttons.Children.Add(clearButton);

        var computeButton = CreateDialogButton("计算畸变", (_, _) =>
        {
            try
            {
                computedCalibration = _lensDistortionCalibrationService.Calibrate(inputs);
                resultText.Text =
                    "畸变标定已计算\n" +
                    $"有效图片: {computedCalibration.CapturedImageCount}\n" +
                    $"图像尺寸: {computedCalibration.ImageWidth}x{computedCalibration.ImageHeight}\n" +
                    $"RMS误差: {computedCalibration.RmsReprojectionErrorPixels:F4} px\n" +
                    $"质量建议: {CalibrationResultMessageFormatter.FormatLensDistortionQuality(computedCalibration.RmsReprojectionErrorPixels)}\n" +
                    _calibrationQualityEvaluator.EvaluateLensDistortion(computedCalibration).Summary + "\n\n" +
                    "确认误差可接受后点击“保存畸变”。";
                MessageText.Text = $"畸变标定已计算: RMS={computedCalibration.RmsReprojectionErrorPixels:F4}px";
                Log(MessageText.Text);
            }
            catch (Exception ex)
            {
                computedCalibration = null;
                resultText.Text = $"畸变标定计算失败: {ex.Message}";
                MessageText.Text = resultText.Text;
                Log(MessageText.Text);
            }
        }, 0);
        computeButton.Width = double.NaN;
        computeButton.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(computeButton, 2);
        buttons.Children.Add(computeButton);

        var saveButton = CreateDialogButton("保存畸变", (_, _) =>
        {
            try
            {
                computedCalibration ??= _lensDistortionCalibrationService.Calibrate(inputs);
                var quality = _calibrationQualityEvaluator.EnsureLensDistortion(computedCalibration);
                SaveMachineLensDistortionCalibration(computedCalibration);
                var reportPath = _reportSaveService.SaveCalibrationReport(
                    CreateCalibrationReportSaveContext("lens-distortion", computedCalibration, quality),
                    Log);
                resultText.Text =
                    "畸变标定已保存。\n" +
                    "由于像素坐标体系已变化，旧9点XY标定和旧标准位/模板会失效。请重新完成9点XY标定，并重新建立当前型号标准位/模板。\n\n" +
                    CalibrationStatusMessageFormatter.FormatLensDistortionStatus(_lensDistortionCalibration) +
                    $"\n标定报告: {reportPath}";
                MessageText.Text = "畸变标定已保存，旧9点XY标定已禁用。";
                Log(MessageText.Text);
                Log($"标定报告已保存: {reportPath}");
            }
            catch (Exception ex)
            {
                resultText.Text = $"畸变标定保存失败: {ex.Message}";
                MessageText.Text = resultText.Text;
                Log(MessageText.Text);
                MessageBox.Show(ex.Message, "镜头畸变标定", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }, 0);
        saveButton.Width = double.NaN;
        saveButton.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(saveButton, 3);
        buttons.Children.Add(saveButton);

        var closeButton = CreateDialogButton("关闭", (_, _) => dialog.Close(), 0);
        closeButton.Width = double.NaN;
        closeButton.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(closeButton, 4);
        buttons.Children.Add(closeButton);

        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.ShowDialog();
    }
}
