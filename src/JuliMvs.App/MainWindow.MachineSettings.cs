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
    private void LoadLocalSettings()
    {
        try
        {
            var settings = _localAppSettingsStore.Load();
            _cameraIpAddress = string.IsNullOrWhiteSpace(settings?.CameraIpAddress)
                ? DefaultCameraIpAddress
                : settings.CameraIpAddress.Trim();
            _plcIpAddress = string.IsNullOrWhiteSpace(settings?.PlcIpAddress)
                ? DefaultPlcIpAddress
                : settings.PlcIpAddress.Trim();
            _plcPort = settings?.PlcPort is > 0 and <= 65535
                ? settings.PlcPort
                : DefaultPlcPort;
            _cameraSettings = settings?.CameraSettings ?? CameraAcquisitionSettings.Default;
            _currentProductName = string.IsNullOrWhiteSpace(settings?.CurrentProductName)
                ? DefaultProductName
                : settings.CurrentProductName.Trim();
        }
        catch (Exception ex)
        {
            _cameraIpAddress = DefaultCameraIpAddress;
            _plcIpAddress = DefaultPlcIpAddress;
            _plcPort = DefaultPlcPort;
            _currentProductName = DefaultProductName;
            _cameraSettings = CameraAcquisitionSettings.Default;
            Log($"本机配置读取失败，使用默认相机IP {DefaultCameraIpAddress}: {ex.Message}");
        }
    }

    private void LoadMachineCalibration()
    {
        var result = _machineSettingsStore.LoadOrDefault();
        ApplyMachineSettings(result.Settings);

        if (result.Error is not null)
        {
            Log($"机器全局参数读取失败，使用默认值: {result.Error.Message}");
            return;
        }

        if (!result.LoadedFromFile)
        {
            return;
        }

        var settings = result.Settings;
        var distortionCalibration = settings.LensDistortionCalibration ?? LensDistortionCalibration.Disabled;
        var rAxisCenterCalibration = NormalizeRAxisCenterCalibration(
            settings.RAxisCenterCalibration ?? RAxisCenterCalibration.Disabled);
        Log(
            "机器全局参数已加载: " +
            (distortionCalibration.Enabled
                ? $"畸变标定=已启用 RMS={distortionCalibration.RmsReprojectionErrorPixels:F4}px, "
                : "畸变标定=未启用, ") +
            (settings.CameraCalibration.Enabled
                ? $"9点XY={settings.CameraCalibration.Points.Count}点 RMS={settings.CameraCalibration.RmsErrorMm:F4}mm"
                : "9点XY=未启用") +
            (rAxisCenterCalibration.Enabled
                ? $", R轴中心=已启用 RMS={rAxisCenterCalibration.RmsErrorMm:F4}mm"
                : ", R轴中心=未启用"));
    }

    private void ExportMachineSettings()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出机器标定配置",
                FileName = MachineSettingsStore.FileName,
                Filter = "机器标定配置 (*.json)|*.json|所有文件 (*.*)|*.*",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _machineSettingsStore.ExportTo(dialog.FileName, BuildCurrentMachineSettings());
            MessageText.Text = $"机器标定配置已导出: {dialog.FileName}";
            Log(MessageText.Text);
        }
        catch (Exception ex)
        {
            MessageText.Text = $"机器标定配置导出失败: {ex.Message}";
            Log(MessageText.Text);
            MessageBox.Show(ex.Message, "导出标定配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportMachineSettings()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入机器标定配置",
                FileName = MachineSettingsStore.FileName,
                Filter = "机器标定配置 (*.json)|*.json|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var settings = _machineSettingsStore.ImportFrom(dialog.FileName);
            ApplyMachineSettings(settings);
            ClearCurrentInspection();
            var ready = IsMachineCalibrationReady(out var message);
            MessageText.Text = ready
                ? $"机器标定配置已导入: {dialog.FileName}。请确认当前型号标准位/模板已加载。"
                : $"机器标定配置已导入，但仍未满足生产条件: {message}";
            Log(MessageText.Text);
        }
        catch (Exception ex)
        {
            MessageText.Text = $"机器标定配置导入失败: {ex.Message}";
            Log(MessageText.Text);
            MessageBox.Show(ex.Message, "导入标定配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearMachineCalibration()
    {
        var settings = BuildCurrentMachineSettings().ClearCalibration();
        ApplyMachineSettings(settings);
        ClearCurrentInspection();
        _productionEnabled = false;
        UpdateRunStopUi();
        _machineSettingsStore.Save(settings);
        MessageText.Text = "\u6807\u5b9a\u5df2\u6e05\u9664\uff1a\u8054\u5408\u6807\u5b9a\u3001\u955c\u5934\u7578\u53d8\u30019\u70b9XY\u548cR\u8f74\u4e2d\u5fc3\u90fd\u5df2\u5931\u6548\uff0c\u8bf7\u91cd\u65b0\u6807\u5b9a\u540e\u518d\u8fdb\u5165\u751f\u4ea7\u3002";
        Log(MessageText.Text);
    }

    private void ApplyMachineSettings(MachineSettings settings)
    {
        _lensDistortionCalibration = settings.LensDistortionCalibration ?? LensDistortionCalibration.Disabled;
        _visionParameters = _visionParameters with
        {
            InvertXCompensation = settings.InvertXCompensation,
            InvertYCompensation = settings.InvertYCompensation,
            InvertRotationCompensation = settings.InvertRotationCompensation ||
                (settings.PlcOutputTransform?.RScale < 0.0)
        };
        _plcOutputTransform = NormalizePlcOutputTransform(settings.PlcOutputTransform ?? PlcOutputTransform.Identity);
        ApplyCalibrationToUi(settings.CameraCalibration);
        ApplyRAxisCenterCalibrationToUi(NormalizeRAxisCenterCalibration(
            settings.RAxisCenterCalibration ?? RAxisCenterCalibration.Disabled));
    }

    private void SaveMachineCalibration(CameraCalibration calibration)
    {
        _rAxisCenterCalibration = RAxisCenterCalibration.Disabled;
        SaveMachineSettings(
            calibration: calibration,
            rAxisCenterCalibration: RAxisCenterCalibration.Disabled);
        ApplyCalibrationToUi(calibration);
    }

    private void SaveMachineRAxisCenterCalibration(RAxisCenterCalibration calibration)
    {
        SaveMachineSettings(rAxisCenterCalibration: calibration);
        ApplyRAxisCenterCalibrationToUi(calibration);
    }

    private void SaveMachineCombinedCalibration(
        LensDistortionCalibration distortionCalibration,
        CameraCalibration calibration)
    {
        _lensDistortionCalibration = distortionCalibration;
        ApplyCalibrationToUi(calibration);
        ApplyRAxisCenterCalibrationToUi(RAxisCenterCalibration.Disabled);
        ClearCurrentInspection();
        SaveMachineSettings(
            distortionCalibration: distortionCalibration,
            calibration: calibration,
            rAxisCenterCalibration: RAxisCenterCalibration.Disabled);
    }

    private void SaveMachineLensDistortionCalibration(LensDistortionCalibration calibration)
    {
        _lensDistortionCalibration = calibration;
        ApplyCalibrationToUi(CameraCalibration.Disabled);
        ApplyRAxisCenterCalibrationToUi(RAxisCenterCalibration.Disabled);
        ClearCurrentInspection();
        SaveMachineSettings(
            distortionCalibration: calibration,
            calibration: CameraCalibration.Disabled,
            rAxisCenterCalibration: RAxisCenterCalibration.Disabled);
    }

    private async Task SaveMachineCompensationDirectionsAsync(
        bool invertX,
        bool invertY,
        bool invertRotation,
        PlcOutputTransform? plcOutputTransform = null)
    {
        _visionParameters = _visionParameters with
        {
            InvertXCompensation = invertX,
            InvertYCompensation = invertY,
            InvertRotationCompensation = invertRotation
        };
        if (plcOutputTransform is not null)
        {
            _plcOutputTransform = plcOutputTransform;
        }

        SaveMachineSettings();
        if (_plcClient is not null)
        {
            await StopPlcPollingAsync();
            await _plcClient.DisposeAsync();
            _plcClient = null;
            SetPlcStatus("PLC通讯未连接", isNormal: false);
        }
    }

    private void SaveMachineSettings(
        LensDistortionCalibration? distortionCalibration = null,
        CameraCalibration? calibration = null,
        RAxisCenterCalibration? rAxisCenterCalibration = null)
    {
        var settings = BuildCurrentMachineSettings(
            distortionCalibration,
            calibration,
            rAxisCenterCalibration);
        _machineSettingsStore.Save(settings);
    }

    private MachineSettings BuildCurrentMachineSettings(
        LensDistortionCalibration? distortionCalibration = null,
        CameraCalibration? calibration = null,
        RAxisCenterCalibration? rAxisCenterCalibration = null)
    {
        return new MachineSettings
        {
            LensDistortionCalibration = distortionCalibration ?? _lensDistortionCalibration,
            CameraCalibration = calibration ?? ReadRawCameraCalibrationFromUi(),
            RAxisCenterCalibration = NormalizeRAxisCenterCalibration(
                rAxisCenterCalibration ?? ReadRawRAxisCenterCalibrationFromUi()),
            InvertXCompensation = _visionParameters.InvertXCompensation,
            InvertYCompensation = _visionParameters.InvertYCompensation,
            InvertRotationCompensation = _visionParameters.InvertRotationCompensation,
            PlcOutputTransform = _plcOutputTransform
        };
    }

    private static RAxisCenterCalibration NormalizeRAxisCenterCalibration(RAxisCenterCalibration calibration)
    {
        return calibration.Enabled && calibration.MachineAngleDirection == 0
            ? calibration with { MachineAngleDirection = calibration.GetMachineAngleDirection() }
            : calibration;
    }

    private static PlcOutputTransform NormalizePlcOutputTransform(PlcOutputTransform transform)
    {
        return transform.RScale < 0.0
            ? transform with { RScale = Math.Abs(transform.RScale) }
            : transform;
    }

    private void SaveLocalSettings()
    {
        var settings = new LocalAppSettings(
            _cameraIpAddress.Trim(),
            _plcIpAddress.Trim(),
            _plcPort,
            _cameraSettings,
            _currentProductName.Trim());
        _localAppSettingsStore.Save(settings);
    }
}
