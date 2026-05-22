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
    private async Task AutoStartConnectionsAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);

        try
        {
            if (!_cameraConnected)
            {
                await ConnectCameraAsync(showNoCameraDialog: false);
            }
        }
        catch (Exception ex)
        {
            var message = CameraErrorMessageFormatter.Format(ex);
            MessageText.Text = message;
            Log($"自动连接相机失败: {message}");
        }

        try
        {
            if (_plcClient?.IsConnected != true)
            {
                await ConnectPlcAsync();
            }
        }
        catch (Exception ex)
        {
            MessageText.Text = ex.Message;
            Log($"自动连接PLC失败: {ex.Message}");
        }

        UpdateBatchUi();
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            await StopPlcPollingAsync();
            if (_plcClient is not null)
            {
                await _plcClient.DisposeAsync();
                _plcClient = null;
            }

            await _cameraService.CloseAsync();
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }
}
