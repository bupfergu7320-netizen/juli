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
    private void CameraSettings_Click(object sender, RoutedEventArgs e)
    {
        string FormatMachineCalibrationStatus()
        {
            return CalibrationStatusMessageFormatter.FormatMachineStatus(
                _lensDistortionCalibration,
                ReadEffectiveCameraCalibrationFromUi(),
                ReadEffectiveRAxisCenterCalibrationFromUi());
        }

        var dialog = CreateToolDialog("相机设置", 980, 700);
        var grid = CreateFormGrid(7);
        grid.Margin = new Thickness(80, 44, 80, 44);
        for (var rowIndex = 0; rowIndex < grid.RowDefinitions.Count - 1; rowIndex++)
        {
            grid.RowDefinitions[rowIndex].Height = new GridLength(56);
        }

        var exposureBox = AddFormRow(grid, 0, "曝光时间(us)", _cameraSettings.ExposureTimeMicroseconds.ToString(CultureInfo.InvariantCulture));
        var gainBox = AddFormRow(grid, 1, "增益", _cameraSettings.Gain.ToString(CultureInfo.InvariantCulture));
        var captureDelayBox = AddFormRow(grid, 2, "曝光延迟(s)", _cameraSettings.CaptureDelaySeconds.ToString(CultureInfo.InvariantCulture));
        var exposureTargetBox = AddFormRow(grid, 3, "曝光阈值", _cameraSettings.AutoExposureTarget.ToString(CultureInfo.InvariantCulture));
        var autoExposure = new CheckBox
        {
            Content = "自动曝光",
            IsChecked = _cameraSettings.AutoExposureEnabled,
            FontSize = 18,
            Margin = new Thickness(0, 8, 0, 8)
        };
        Grid.SetRow(autoExposure, 4);
        Grid.SetColumn(autoExposure, 1);
        grid.Children.Add(autoExposure);

        var calibrationStatusText = new TextBlock
        {
            Text = FormatMachineCalibrationStatus(),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = IsMachineCalibrationReady(out _) ? Brushes.ForestGreen : Brushes.DarkOrange,
            TextWrapping = TextWrapping.Wrap
        };
        var calibrationStatusLabel = new TextBlock
        {
            Text = "标定状态",
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 28, 0)
        };
        Grid.SetRow(calibrationStatusLabel, 5);
        Grid.SetColumn(calibrationStatusLabel, 0);
        grid.Children.Add(calibrationStatusLabel);
        Grid.SetRow(calibrationStatusText, 5);
        Grid.SetColumn(calibrationStatusText, 1);
        grid.Children.Add(calibrationStatusText);

        RoutedEventHandler saveCameraSettings = async (sender, args) =>
        {
            try
            {
                _cameraSettings = ReadCameraSettingsFromDialog(
                    exposureBox,
                    gainBox,
                    captureDelayBox,
                    exposureTargetBox,
                    autoExposure);
                SaveLocalSettings();
                await ApplyCameraSettingsToConnectedCameraAsync();

                MessageText.Text = $"相机设置已保存: 曝光{_cameraSettings.ExposureTimeMicroseconds:F1}us, 增益{_cameraSettings.Gain:F1}, 曝光延迟{_cameraSettings.CaptureDelaySeconds:F3}s";
                Log(MessageText.Text);
            }
            catch (Exception ex)
            {
                MessageText.Text = ex.Message;
                Log($"相机设置保存失败: {ex.Message}");
                MessageBox.Show(ex.Message, "相机设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        AddDialogButtonRow(
            grid,
            6,
            ("保存", saveCameraSettings),
            ("标定管理", (_, _) =>
            {
                OpenCalibrationManagementDialog();
                calibrationStatusText.Text = FormatMachineCalibrationStatus();
                calibrationStatusText.Foreground = IsMachineCalibrationReady(out _) ? Brushes.ForestGreen : Brushes.DarkOrange;
            }),
            ("关闭", (_, _) => dialog.Close()));
        dialog.Content = grid;
        dialog.ShowDialog();
    }

    private static CameraAcquisitionSettings ReadCameraSettingsFromDialog(
        TextBox exposureBox,
        TextBox gainBox,
        TextBox captureDelayBox,
        TextBox exposureTargetBox,
        CheckBox autoExposure)
    {
        return CameraAcquisitionSettings.Default with
        {
            ExposureTimeMicroseconds = InputValueParser.ReadRequiredDouble(exposureBox.Text, "曝光时间", 1, 10_000_000),
            Gain = InputValueParser.ReadRequiredDouble(gainBox.Text, "增益", 0, 48),
            CaptureDelaySeconds = InputValueParser.ReadRequiredDouble(captureDelayBox.Text, "曝光延迟", 0, 10),
            AutoExposureTarget = InputValueParser.ReadRequiredInt(exposureTargetBox.Text, "曝光阈值", 0, 255),
            AutoExposureEnabled = autoExposure.IsChecked == true
        };
    }

    private async Task ApplyCameraSettingsToConnectedCameraAsync()
    {
        if (!_cameraConnected)
        {
            return;
        }

        var warnings = await Task.Run(() => _cameraService.ApplySettings(_cameraSettings));
        foreach (var warning in warnings)
        {
            Log(warning);
        }
    }
}
