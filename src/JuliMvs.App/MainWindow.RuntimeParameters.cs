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
    private VisionParameters ReadVisionParameters()
    {
        return _machineCalibrationRuntime.BuildRuntimeParameters(
            _visionParameters,
            _lensDistortionCalibration,
            ReadRawCameraCalibrationFromUi(),
            ReadRawRAxisCenterCalibrationFromUi());
    }

    private void ApplyRecipeVisionParameters(VisionParameters recipeParameters)
    {
        var applied = ProductRecipeVisionParameters.ApplyToRuntime(
            ReadVisionParameters(),
            recipeParameters);
        _visionParameters = applied with
        {
            LensDistortionCalibration = LensDistortionCalibration.Disabled,
            CameraCalibration = CameraCalibration.Disabled,
            RAxisCenterCalibration = RAxisCenterCalibration.Disabled
        };
    }

    private CameraCalibration ReadRawCameraCalibrationFromUi()
    {
        return _cameraCalibration;
    }

    private CameraCalibration ReadEffectiveCameraCalibrationFromUi()
    {
        return _machineCalibrationRuntime.GetEffectiveCameraCalibration(
            _lensDistortionCalibration,
            ReadRawCameraCalibrationFromUi());
    }

    private void ApplyCalibrationToUi(CameraCalibration calibration)
    {
        _cameraCalibration = calibration;
    }

    private RAxisCenterCalibration ReadRawRAxisCenterCalibrationFromUi()
    {
        return _rAxisCenterCalibration;
    }

    private RAxisCenterCalibration ReadEffectiveRAxisCenterCalibrationFromUi()
    {
        return _machineCalibrationRuntime.GetEffectiveRAxisCenterCalibration(
            ReadEffectiveCameraCalibrationFromUi(),
            ReadRawRAxisCenterCalibrationFromUi());
    }

    private void ApplyRAxisCenterCalibrationToUi(RAxisCenterCalibration calibration)
    {
        _rAxisCenterCalibration = calibration;
    }

    private string GetCurrentDistortionCalibrationId()
    {
        return _lensDistortionCalibration.Enabled ? _lensDistortionCalibration.CalibrationId : string.Empty;
    }

    private PlcOutputTransform GetEffectivePlcOutputTransform()
    {
        return _plcOutputTransform;
    }

    private MitsubishiModbusTcpOptions ReadPlcOptionsFromUi()
    {
        var host = string.IsNullOrWhiteSpace(_plcIpAddress)
            ? DefaultPlcIpAddress
            : _plcIpAddress.Trim();
        if (_plcPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("PLC端口必须在1到65535之间。");
        }

        return MitsubishiModbusTcpOptions.Default with
        {
            Host = host,
            Port = _plcPort,
            OutputTransform = GetEffectivePlcOutputTransform()
        };
    }
}
