using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JuliMvs.App.Services;

namespace JuliMvs.App;

public partial class MainWindow
{
    private void PlcSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireTechnician("PLC通信"))
        {
            return;
        }

        OpenPlcSettingsDialog();
    }

    private void OpenPlcSettingsDialog()
    {
        var dialog = CreateToolDialog("PLC通信", 860, 600);
        dialog.ResizeMode = ResizeMode.NoResize;

        var grid = CreateFormGrid(7);
        grid.Margin = new Thickness(78, 42, 78, 42);
        for (var rowIndex = 0; rowIndex < grid.RowDefinitions.Count - 1; rowIndex++)
        {
            grid.RowDefinitions[rowIndex].Height = new GridLength(58);
        }

        var hostBox = AddFormRow(grid, 0, "PLC IP", _plcIpAddress);
        var portBox = AddFormRow(grid, 1, "PLC端口", _plcPort.ToString(CultureInfo.InvariantCulture));

        var registerText = new TextBlock
        {
            Text = "D1000触发；D1002/D1004/D1006三轴输出；D1010结果；D1020/D1022产量；D1030型号。",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var registerLabel = new TextBlock
        {
            Text = "寄存器",
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 28, 0)
        };
        Grid.SetRow(registerLabel, 2);
        Grid.SetColumn(registerLabel, 0);
        grid.Children.Add(registerLabel);
        Grid.SetRow(registerText, 2);
        Grid.SetColumn(registerText, 1);
        grid.Children.Add(registerText);

        var statusText = new TextBlock
        {
            Text = FormatPlcConnectionState(),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = _plcClient?.IsConnected == true ? Brushes.ForestGreen : Brushes.DarkOrange,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusLabel = new TextBlock
        {
            Text = "当前状态",
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 28, 0)
        };
        Grid.SetRow(statusLabel, 3);
        Grid.SetColumn(statusLabel, 0);
        grid.Children.Add(statusLabel);
        Grid.SetRow(statusText, 3);
        Grid.SetColumn(statusText, 1);
        grid.Children.Add(statusText);

        var hintText = new TextBlock
        {
            Text = "保存并连接会重建PLC连接并重新开始D1000轮询。断开连接会停止轮询并释放当前PLC连接。",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(hintText, 4);
        Grid.SetColumnSpan(hintText, 2);
        grid.Children.Add(hintText);

        var outputButton = CreateDialogButton("PLC输出设置/R取反", (_, _) =>
        {
            OpenCompensationDirectionDialog(() =>
            {
                statusText.Text = FormatPlcConnectionState();
                statusText.Foreground = _plcClient?.IsConnected == true ? Brushes.ForestGreen : Brushes.DarkOrange;
            });
        }, 0);
        outputButton.Width = double.NaN;
        outputButton.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(outputButton, 5);
        Grid.SetColumnSpan(outputButton, 2);
        grid.Children.Add(outputButton);

        AddDialogButtonRow(
            grid,
            6,
            ("保存并连接", async (_, _) =>
            {
                try
                {
                    _plcIpAddress = ReadRequiredHost(hostBox.Text);
                    _plcPort = InputValueParser.ReadRequiredInt(portBox.Text, "PLC端口", 1, 65535);
                    SaveLocalSettings();
                    await ConnectPlcAsync();
                    statusText.Text = FormatPlcConnectionState();
                    statusText.Foreground = Brushes.ForestGreen;
                    MessageText.Text = $"PLC通信参数已保存并连接: {_plcIpAddress}:{_plcPort}";
                    Log(MessageText.Text);
                }
                catch (Exception ex)
                {
                    statusText.Text = $"连接失败: {ex.Message}";
                    statusText.Foreground = Brushes.Red;
                    MessageText.Text = statusText.Text;
                    Log(MessageText.Text);
                    MessageBox.Show(ex.Message, "PLC通信", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }),
            ("断开连接", async (_, _) =>
            {
                try
                {
                    await StopPlcPollingAsync();
                    if (_plcClient is not null)
                    {
                        await _plcClient.DisposeAsync();
                        _plcClient = null;
                    }

                    SetPlcStatus("PLC通讯未连接", isNormal: false);
                    statusText.Text = FormatPlcConnectionState();
                    statusText.Foreground = Brushes.DarkOrange;
                    MessageText.Text = "PLC连接已断开。";
                    Log(MessageText.Text);
                }
                catch (Exception ex)
                {
                    MessageText.Text = $"PLC断开失败: {ex.Message}";
                    Log(MessageText.Text);
                    MessageBox.Show(ex.Message, "PLC通信", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }),
            ("关闭", (_, _) => dialog.Close()));

        dialog.Content = grid;
        dialog.ShowDialog();
    }

    private string FormatPlcConnectionState()
    {
        var state = _plcClient?.IsConnected == true ? "已连接" : "未连接";
        return $"{state}: {_plcIpAddress}:{_plcPort}";
    }

    private static string ReadRequiredHost(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("PLC IP不能为空。");
        }

        return trimmed;
    }
}
