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
    private async Task ConnectPlcAsync()
    {
        try
        {
            await StopPlcPollingAsync();
            if (_plcClient is not null)
            {
                await _plcClient.DisposeAsync();
            }

            var options = ReadPlcOptionsFromUi();
            _plcClient = new MitsubishiModbusTcpPlcClient(options);
            SetPlcStatus("PLC连接中", isNormal: false);
            await _plcClient.ConnectAsync();
            SetPlcStatus("PLC通讯正常", isNormal: true);
            SaveLocalSettings();
            Log($"PLC已连接: Mitsubishi Modbus TCP {options.Host}:{options.Port}");
            StartPlcPolling();
        }
        catch
        {
            SetPlcStatus("PLC通讯异常", isNormal: false);
            if (_plcClient is not null)
            {
                await _plcClient.DisposeAsync();
                _plcClient = null;
            }

            throw;
        }
    }

    private void StartPlcPolling()
    {
        _plcTriggerGate.Reset();
        _plcPollingCts = new CancellationTokenSource();
        var token = _plcPollingCts.Token;
        _ = Task.Run(() => PollPlcLoopAsync(token), token);
    }

    private async Task StopPlcPollingAsync()
    {
        var cts = _plcPollingCts;
        if (cts is null)
        {
            return;
        }

        _plcPollingCts = null;
        await cts.CancelAsync();
        cts.Dispose();
    }

    private async Task PollPlcLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = _plcClient;
                if (client is null || !client.IsConnected)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        SetPlcStatus("PLC通讯未连接", isNormal: false);
                        UpdateBatchUi();
                    });
                    return;
                }

                var snapshot = await client.ReadSnapshotAsync(cancellationToken);
                var pollingDecision = _plcPollingCoordinator.Evaluate(snapshot);
                await Dispatcher.InvokeAsync(() =>
                {
                    SetPlcStatus(PlcStatusMessageFormatter.FormatPollingStatus(pollingDecision.Status), pollingDecision.IsStatusNormal);
                });

                if (pollingDecision.ShouldDelayAndContinue)
                {
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                if (pollingDecision.LogTriggerCleared)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        Log("PLC已清D1000=0，本轮标准握手结束，允许下一次触发。");
                    });
                }
                else if (pollingDecision.StartInspection)
                {
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            await HandlePlcCaptureRequestAsync();
                        }
                        catch (Exception ex)
                        {
                            Log($"PLC触发检测处理失败: {ex.Message}");
                            SetPlcStatus("PLC处理失败", isNormal: false);
                        }
                        finally
                        {
                            _plcTriggerGate.EndOperation();
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    SetPlcStatus("PLC通讯异常", isNormal: false);
                    Log($"PLC通讯异常: {ex.Message}");
                    _plcClient = null;
                    UpdateBatchUi();
                });
                return;
            }

            await Task.Delay(250, cancellationToken);
        }
    }
}
