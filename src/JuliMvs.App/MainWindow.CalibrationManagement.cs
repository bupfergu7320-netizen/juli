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
    private void OpenCalibrationManagementDialog()
    {
        if (!RequireTechnician("标定管理"))
        {
            return;
        }

        string FormatMachineCalibrationStatus()
        {
            return CalibrationStatusMessageFormatter.FormatMachineStatus(
                _lensDistortionCalibration,
                ReadEffectiveCameraCalibrationFromUi(),
                ReadEffectiveRAxisCenterCalibrationFromUi());
        }

        void RefreshMachineCalibrationStatus(TextBlock target)
        {
            target.Text = FormatMachineCalibrationStatus();
            target.Foreground = IsMachineCalibrationReady(out _) ? Brushes.ForestGreen : Brushes.DarkOrange;
        }

        var dialog = CreateToolDialog("标定管理", 1120, 500);
        dialog.ResizeMode = ResizeMode.NoResize;

        var root = new Grid { Margin = new Thickness(44, 34, 44, 34) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var statusText = new TextBlock
        {
            Text = FormatMachineCalibrationStatus(),
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = IsMachineCalibrationReady(out _) ? Brushes.ForestGreen : Brushes.DarkOrange
        };
        root.Children.Add(statusText);

        var hintText = new TextBlock
        {
            Text = "推荐流程: 联合标定一次完成镜头畸变和9点XY；正式生产还必须完成R轴中心标定，最后换型建立当前型号标准位/模板。单独标定入口保留用于调试。",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 24, 0, 0)
        };
        Grid.SetRow(hintText, 1);
        root.Children.Add(hintText);

        var buttons = new Grid { Margin = new Thickness(0, 34, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        var combinedButton = CreateDialogButton("联合标定", (_, _) =>
        {
            OpenCombinedCalibrationDialog();
            RefreshMachineCalibrationStatus(statusText);
        }, 0);
        combinedButton.Width = double.NaN;
        combinedButton.Margin = new Thickness(8, 0, 8, 0);
        buttons.Children.Add(combinedButton);

        var distortionButton = CreateDialogButton("镜头畸变标定", (_, _) =>
        {
            OpenLensDistortionCalibrationDialog();
            RefreshMachineCalibrationStatus(statusText);
        }, 0);
        distortionButton.Width = double.NaN;
        distortionButton.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(distortionButton, 1);
        buttons.Children.Add(distortionButton);

        var mechanicalButton = CreateDialogButton("9点XY标定", (_, _) =>
        {
            OpenCalibrationDialog();
            RefreshMachineCalibrationStatus(statusText);
        }, 0);
        mechanicalButton.Width = double.NaN;
        mechanicalButton.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(mechanicalButton, 2);
        buttons.Children.Add(mechanicalButton);

        var rAxisCenterButton = CreateDialogButton("R轴中心标定", (_, _) =>
        {
            OpenRAxisCenterCalibrationDialog();
            RefreshMachineCalibrationStatus(statusText);
        }, 0);
        rAxisCenterButton.Width = double.NaN;
        rAxisCenterButton.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(rAxisCenterButton, 3);
        buttons.Children.Add(rAxisCenterButton);

        var closeButton = CreateDialogButton("关闭", (_, _) => dialog.Close(), 0);
        closeButton.Width = double.NaN;
        closeButton.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(closeButton, 4);
        buttons.Children.Add(closeButton);

        var settingsButtons = new Grid { Margin = new Thickness(0, 30, 0, 0) };
        settingsButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        settingsButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        settingsButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(settingsButtons, 3);
        root.Children.Add(settingsButtons);

        var exportButton = CreateDialogButton("导出标定配置", (_, _) =>
        {
            ExportMachineSettings();
            RefreshMachineCalibrationStatus(statusText);
        }, 0);
        exportButton.Width = double.NaN;
        exportButton.Margin = new Thickness(8, 0, 8, 0);
        settingsButtons.Children.Add(exportButton);

        var importButton = CreateDialogButton("导入标定配置", (_, _) =>
        {
            ImportMachineSettings();
            RefreshMachineCalibrationStatus(statusText);
        }, 0);
        importButton.Width = double.NaN;
        importButton.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(importButton, 1);
        settingsButtons.Children.Add(importButton);

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private CalibrationReportSaveContext CreateCalibrationReportSaveContext(
        string calibrationType,
        object calibration,
        CalibrationQualityResult quality)
    {
        return new CalibrationReportSaveContext(
            calibrationType,
            calibration,
            quality,
            new CurrentMachineCalibrationSnapshot(
                _lensDistortionCalibration.CalibrationId,
                ReadRawCameraCalibrationFromUi().CalibrationId,
                ReadRawRAxisCenterCalibrationFromUi().CalibrationId,
                _plcOutputTransform));
    }
}
