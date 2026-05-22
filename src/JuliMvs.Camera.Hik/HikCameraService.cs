using System.Runtime.InteropServices;
using System.Text;
using JuliMvs.Core.Camera;
using MvCamCtrl.NET;

namespace JuliMvs.Camera.Hik;

public sealed class HikCameraService : ICameraService
{
    private static int s_initialized;

    private readonly object _syncRoot = new();
    private MyCamera.MV_CC_DEVICE_INFO_LIST _deviceList = new();
    private MyCamera? _camera;
    private bool _isGrabbing;
    private IReadOnlyList<string> _configurationWarnings = [];

    public bool IsOpen => _camera is not null;

    public IReadOnlyList<CameraDeviceInfo> EnumerateDevices()
    {
        EnsureInitialized();
        _deviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST();

        var ret = MyCamera.MV_CC_EnumDevices_NET(
            MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE,
            ref _deviceList);
        ThrowIfFailed(ret, "Enumerate Hikvision devices failed");

        var devices = new List<CameraDeviceInfo>();
        for (var index = 0; index < _deviceList.nDeviceNum; index++)
        {
            var device = ReadDeviceInfo(index);
            devices.Add(ToCameraDeviceInfo(index, device));
        }

        return devices;
    }

    public Task OpenAsync(string serialNumberOrIndex, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloseCurrentCamera();

        var devices = EnumerateDevices();
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No Hikvision camera was found.");
        }

        var selected = ResolveDevice(devices, serialNumberOrIndex);
        var deviceInfo = ReadDeviceInfo(selected.Index);
        if (!IsDeviceAccessible(ref deviceInfo))
        {
            throw new HikCameraException("Open Hikvision camera failed: device is not accessible", unchecked((int)0x80000203));
        }

        var camera = new MyCamera();

        var ret = camera.MV_CC_CreateDevice_NET(ref deviceInfo);
        ThrowIfFailed(ret, "Create Hikvision camera handle failed");

        try
        {
            ret = camera.MV_CC_OpenDevice_NET(MyCamera.MV_ACCESS_Control, 0);
            ThrowIfFailed(ret, "Open Hikvision camera failed");

            ConfigureCamera(camera, deviceInfo);
            _configurationWarnings = ConfigureLowLatencyGrabbing(camera);
            StartGrabbing(camera);
            _camera = camera;
        }
        catch
        {
            camera.MV_CC_DestroyDevice_NET();
            throw;
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<string> ApplySettings(CameraAcquisitionSettings settings)
    {
        if (_camera is null)
        {
            throw new InvalidOperationException("Camera is not open.");
        }

        var warnings = new List<string>(_configurationWarnings);
        warnings.AddRange(ApplySettings(_camera, settings));
        return warnings;
    }

    private static bool IsDeviceAccessible(ref MyCamera.MV_CC_DEVICE_INFO deviceInfo)
    {
        try
        {
            return MyCamera.MV_CC_IsDeviceAccessible_NET(ref deviceInfo, MyCamera.MV_ACCESS_Control);
        }
        catch (EntryPointNotFoundException)
        {
            return true;
        }
    }

    public Task<CameraFrame> CaptureAsync(int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_camera is null)
            {
                throw new InvalidOperationException("Camera is not open.");
            }

            if (!_isGrabbing)
            {
                StartGrabbing(_camera);
            }

            TryClearImageBuffer(_camera);
            var frame = new MyCamera.MV_FRAME_OUT();
            try
            {
                var ret = _camera.MV_CC_GetImageBuffer_NET(ref frame, timeoutMilliseconds);
                ThrowIfFailed(ret, "Get Hikvision image buffer failed");

                var length = checked((int)frame.stFrameInfo.nFrameLen);
                var buffer = new byte[length];
                Marshal.Copy(frame.pBufAddr, buffer, 0, length);

                return Task.FromResult(new CameraFrame(
                    checked((int)frame.stFrameInfo.nWidth),
                    checked((int)frame.stFrameInfo.nHeight),
                    frame.stFrameInfo.nFrameNum,
                    frame.stFrameInfo.enPixelType.ToString(),
                    buffer,
                    DateTimeOffset.Now));
            }
            finally
            {
                if (frame.pBufAddr != IntPtr.Zero)
                {
                    _camera.MV_CC_FreeImageBuffer_NET(ref frame);
                }
            }
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloseCurrentCamera();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        CloseCurrentCamera();
        return ValueTask.CompletedTask;
    }

    private static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref s_initialized, 1) == 0)
        {
            try
            {
                var ret = MyCamera.MV_CC_Initialize_NET();
                ThrowIfFailed(ret, "Initialize Hikvision MVS SDK failed");
            }
            catch (EntryPointNotFoundException)
            {
                // Older MVS runtimes do not export MV_CC_Initialize. The rest of the
                // camera API can still enumerate/open devices on those installations.
            }
        }
    }

    private static void ConfigureCamera(MyCamera camera, MyCamera.MV_CC_DEVICE_INFO deviceInfo)
    {
        if (deviceInfo.nTLayerType == MyCamera.MV_GIGE_DEVICE)
        {
            var packetSize = camera.MV_CC_GetOptimalPacketSize_NET();
            if (packetSize > 0)
            {
                camera.MV_CC_SetIntValueEx_NET("GevSCPSPacketSize", packetSize);
            }
        }

        camera.MV_CC_SetEnumValue_NET(
            "AcquisitionMode",
            (uint)MyCamera.MV_CAM_ACQUISITION_MODE.MV_ACQ_MODE_CONTINUOUS);
        camera.MV_CC_SetEnumValue_NET(
            "TriggerMode",
            (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF);
    }

    private static IReadOnlyList<string> ConfigureLowLatencyGrabbing(MyCamera camera)
    {
        var warnings = new List<string>();
        var ret = camera.MV_CC_SetImageNodeNum_NET(1);
        if (ret != MyCamera.MV_OK)
        {
            warnings.Add($"海康相机缓存节点数设置为1失败: MVS error=0x{ret:X8}");
        }

        ret = camera.MV_CC_SetGrabStrategy_NET(MyCamera.MV_GRAB_STRATEGY.MV_GrabStrategy_LatestImagesOnly);
        if (ret != MyCamera.MV_OK)
        {
            warnings.Add($"海康相机最新帧抓取策略设置失败: MVS error=0x{ret:X8}");
        }

        return warnings;
    }

    private static void TryClearImageBuffer(MyCamera camera)
    {
        camera.MV_CC_ClearImageBuffer_NET();
    }

    private void StartGrabbing(MyCamera camera)
    {
        var ret = camera.MV_CC_StartGrabbing_NET();
        ThrowIfFailed(ret, "Start Hikvision grabbing failed");
        _isGrabbing = true;
    }

    private static IReadOnlyList<string> ApplySettings(MyCamera camera, CameraAcquisitionSettings settings)
    {
        var warnings = new List<string>();

        SetEnumValue(camera, "AcquisitionMode", "Continuous", (uint)MyCamera.MV_CAM_ACQUISITION_MODE.MV_ACQ_MODE_CONTINUOUS);
        SetEnumValue(camera, "TriggerMode", "Off", (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF);
        warnings.AddRange(ConfigureLowLatencyGrabbing(camera));

        SetEnumValue(camera, "GainAuto", "Off", 0);
        SetFloatValue(camera, "Gain", settings.Gain);

        if (settings.AutoExposureEnabled)
        {
            if (!TrySetExposureTarget(camera, settings.AutoExposureTarget))
            {
                warnings.Add("曝光阈值未下发：当前相机SDK未接受AutoExposureTarget/AutoTargetValue/Brightness参数。");
            }

            SetEnumValue(camera, "ExposureAuto", "Continuous", 2);
        }
        else
        {
            SetEnumValue(camera, "ExposureAuto", "Off", 0);
            SetFloatValue(camera, "ExposureTime", settings.ExposureTimeMicroseconds);
        }

        return warnings;
    }

    private static void SetEnumValue(MyCamera camera, string key, string stringValue, uint numericValue)
    {
        var ret = camera.MV_CC_SetEnumValueByString_NET(key, stringValue);
        if (ret == MyCamera.MV_OK)
        {
            return;
        }

        ret = camera.MV_CC_SetEnumValue_NET(key, numericValue);
        ThrowIfFailed(ret, $"Set Hikvision camera enum '{key}' failed");
    }

    private static void SetFloatValue(MyCamera camera, string key, double value)
    {
        var ret = camera.MV_CC_SetFloatValue_NET(key, (float)value);
        ThrowIfFailed(ret, $"Set Hikvision camera float '{key}' failed");
    }

    private static bool TrySetExposureTarget(MyCamera camera, int target)
    {
        var clamped = Math.Clamp(target, 0, 255);
        if (camera.MV_CC_SetIntValueEx_NET("AutoExposureTarget", clamped) == MyCamera.MV_OK)
        {
            return true;
        }

        if (camera.MV_CC_SetIntValueEx_NET("AutoTargetValue", clamped) == MyCamera.MV_OK)
        {
            return true;
        }

        return camera.MV_CC_SetBrightness_NET((uint)clamped) == MyCamera.MV_OK;
    }

    private CameraDeviceInfo ResolveDevice(IReadOnlyList<CameraDeviceInfo> devices, string serialNumberOrIndex)
    {
        if (int.TryParse(serialNumberOrIndex, out var index))
        {
            var byIndex = devices.FirstOrDefault(x => x.Index == index);
            if (byIndex is not null)
            {
                return byIndex;
            }
        }

        var bySerial = devices.FirstOrDefault(x =>
            string.Equals(x.SerialNumber, serialNumberOrIndex, StringComparison.OrdinalIgnoreCase));
        if (bySerial is not null)
        {
            return bySerial;
        }

        var byIpAddress = devices.FirstOrDefault(x =>
            string.Equals(x.IpAddress, serialNumberOrIndex, StringComparison.OrdinalIgnoreCase));
        if (byIpAddress is not null)
        {
            return byIpAddress;
        }

        throw new InvalidOperationException($"Camera '{serialNumberOrIndex}' was not found.");
    }

    private MyCamera.MV_CC_DEVICE_INFO ReadDeviceInfo(int index)
    {
        return (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(
            _deviceList.pDeviceInfo[index],
            typeof(MyCamera.MV_CC_DEVICE_INFO))!;
    }

    private static CameraDeviceInfo ToCameraDeviceInfo(int index, MyCamera.MV_CC_DEVICE_INFO device)
    {
        if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
        {
            var gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO_EX)MyCamera.ByteToStruct(
                device.SpecialInfo.stGigEInfo,
                typeof(MyCamera.MV_GIGE_DEVICE_INFO_EX));
            var ip = FormatIp(gigeInfo.nCurrentIp);
            var model = Clean(Convert.ToString(gigeInfo.chModelName));
            var serial = Clean(Convert.ToString(gigeInfo.chSerialNumber));
            var name = DecodeName(gigeInfo.chUserDefinedName);
            var displayName = string.IsNullOrWhiteSpace(name)
                ? $"GEV: {model} ({serial}) {ip}"
                : $"GEV: {name} ({serial}) {ip}";

            return new CameraDeviceInfo(index, displayName, serial, model, "GEV", ip);
        }

        if (device.nTLayerType == MyCamera.MV_USB_DEVICE)
        {
            var usbInfo = (MyCamera.MV_USB3_DEVICE_INFO_EX)MyCamera.ByteToStruct(
                device.SpecialInfo.stUsb3VInfo,
                typeof(MyCamera.MV_USB3_DEVICE_INFO_EX));
            var model = Clean(Convert.ToString(usbInfo.chModelName));
            var serial = Clean(Convert.ToString(usbInfo.chSerialNumber));
            var name = DecodeName(usbInfo.chUserDefinedName);
            var displayName = string.IsNullOrWhiteSpace(name)
                ? $"U3V: {model} ({serial})"
                : $"U3V: {name} ({serial})";

            return new CameraDeviceInfo(index, displayName, serial, model, "U3V", null);
        }

        return new CameraDeviceInfo(index, $"Unknown camera {index}", string.Empty, string.Empty, "Unknown", null);
    }

    private void StopGrabbing()
    {
        if (_camera is not null && _isGrabbing)
        {
            _camera.MV_CC_StopGrabbing_NET();
            _isGrabbing = false;
        }
    }

    private void CloseCurrentCamera()
    {
        if (_camera is null)
        {
            return;
        }

        StopGrabbing();
        _camera.MV_CC_CloseDevice_NET();
        _camera.MV_CC_DestroyDevice_NET();
        _camera = null;
    }

    private static string FormatIp(uint rawIp)
    {
        var n1 = (rawIp & 0xff000000) >> 24;
        var n2 = (rawIp & 0x00ff0000) >> 16;
        var n3 = (rawIp & 0x0000ff00) >> 8;
        var n4 = rawIp & 0x000000ff;
        return $"{n1}.{n2}.{n3}.{n4}";
    }

    private static string DecodeName(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes[0] == 0)
        {
            return string.Empty;
        }

        var encoding = MyCamera.IsTextUTF8(bytes) ? Encoding.UTF8 : Encoding.Default;
        return encoding.GetString(bytes).TrimEnd('\0').Trim();
    }

    private static string Clean(string? value)
    {
        return (value ?? string.Empty).TrimEnd('\0').Trim();
    }

    private static void ThrowIfFailed(int errorCode, string message)
    {
        if (errorCode != MyCamera.MV_OK)
        {
            throw new HikCameraException(message, errorCode);
        }
    }
}
