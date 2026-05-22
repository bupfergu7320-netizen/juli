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
    private async Task ConnectCameraAsync(bool showNoCameraDialog)
    {
        SetCameraStatus("相机连接中", isNormal: false);
        MessageText.Text = $"正在连接相机 {_cameraIpAddress}";
        Log(MessageText.Text);

        try
        {
            var devices = await Task.Run(() => _cameraService.EnumerateDevices());
            if (devices.Count == 0)
            {
                if (showNoCameraDialog)
                {
                    MessageBox.Show("未发现海康相机。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                throw new InvalidOperationException("未发现海康相机。");
            }

            var targetIp = _cameraIpAddress;
            var selected = devices.Count == 1
                ? devices[0]
                : !string.IsNullOrWhiteSpace(targetIp)
                    ? devices.FirstOrDefault(device => string.Equals(device.IpAddress, targetIp, StringComparison.OrdinalIgnoreCase))
                    : null;
            if (selected is null)
            {
                var deviceList = string.Join("; ", devices.Select(device => device.DisplayName));
                throw new InvalidOperationException($"枚举到多台相机，但未找到目标相机IP {targetIp}。已枚举设备: {deviceList}");
            }

            if (devices.Count == 1 &&
                !string.IsNullOrWhiteSpace(targetIp) &&
                !string.Equals(selected.IpAddress, targetIp, StringComparison.OrdinalIgnoreCase))
            {
                Log($"现场仅枚举到1台相机，已忽略配置IP {targetIp}，直接连接: {selected.DisplayName}");
                if (!string.IsNullOrWhiteSpace(selected.IpAddress))
                {
                    _cameraIpAddress = selected.IpAddress;
                }
            }

            var selector = !string.IsNullOrWhiteSpace(selected.IpAddress)
                ? selected.IpAddress
                : !string.IsNullOrWhiteSpace(selected.SerialNumber)
                    ? selected.SerialNumber
                    : selected.Index.ToString(CultureInfo.InvariantCulture);
            await Task.Run(() => _cameraService.OpenAsync(selector));
            _cameraConnected = true;
            _connectedCameraInfo = selected;
            await ApplyCameraSettingsToConnectedCameraAsync();
            SetCameraStatus("相机通讯正常", isNormal: true);
            Log($"相机已连接: {selected.DisplayName}");
            SaveLocalSettings();
            MessageText.Text = $"相机连接成功: {selected.DisplayName}";
            Log(MessageText.Text);
        }
        catch
        {
            _cameraConnected = false;
            _connectedCameraInfo = null;
            try
            {
                await Task.Run(() => _cameraService.CloseAsync());
            }
            catch (Exception closeEx)
            {
                Log($"相机关闭失败: {closeEx.Message}");
            }

            SetCameraStatus("相机通讯异常", isNormal: false);
            throw;
        }
    }
}
