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
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AppDataDirectoryInitializer.EnsureDataDirectories(AppContext.BaseDirectory);
            LoadLocalSettings();
            LoadMachineCalibration();
            await _repository.InitializeAsync();
            _currentUserRole = UserRole.Operator;
            UpdateUserRoleUi();
            _productionEnabled = true;
            UpdateRunStopUi();
            SetCameraStatus("相机通讯未连接", isNormal: false);
            SetPlcStatus("PLC通讯未连接", isNormal: false);
            Log("数据库已初始化");
            Log("上位机启动后自动进入运行状态，开始自动连接相机和PLC");
            _ = AutoStartConnectionsAsync();
        }
        catch (Exception ex)
        {
            SystemStatusText.Text = "初始化失败";
            Log(ex.Message);
        }
        finally
        {
            UpdateBatchUi();
        }
    }

    private void RunStop_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_productionEnabled && !IsMachineCalibrationReady(out var message))
        {
            MessageText.Text = message;
            Log(message);
            MessageBox.Show(message, "标定未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _productionEnabled = !_productionEnabled;
        UpdateRunStopUi();
        Log(_productionEnabled ? "上位机当前为运行中，允许PLC触发自动检测" : "上位机当前为已停止，PLC触发将被忽略");
    }

    private void CurrentUser_Click(object sender, MouseButtonEventArgs e)
    {
        OpenUserSwitchDialog();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Keyboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "osk.exe",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageText.Text = ex.Message;
            Log($"小键盘打开失败: {ex.Message}");
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateRunStopUi()
    {
        SystemStatusText.Text = _productionEnabled ? "运行中" : "已停止";
        SystemStatusBadge.Background = new SolidColorBrush(_productionEnabled ? Colors.Lime : Colors.Red);
    }

    private void OpenUserSwitchDialog()
    {
        var dialog = CreateToolDialog("用户切换", 520, 330);
        dialog.ResizeMode = ResizeMode.NoResize;

        var grid = new Grid { Margin = new Thickness(46, 34, 46, 34) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var roleLabel = new TextBlock
        {
            Text = "用户",
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 24, 0)
        };
        grid.Children.Add(roleLabel);

        var roleBox = new ComboBox
        {
            FontSize = 24,
            Height = 42,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        roleBox.Items.Add("操作员");
        roleBox.Items.Add("技术员");
        roleBox.SelectedIndex = _currentUserRole == UserRole.Operator ? 0 : 1;
        Grid.SetColumn(roleBox, 1);
        grid.Children.Add(roleBox);

        var passwordLabel = new TextBlock
        {
            Text = "密码",
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 24, 0)
        };
        Grid.SetRow(passwordLabel, 1);
        grid.Children.Add(passwordLabel);

        var passwordBox = new PasswordBox
        {
            FontSize = 24,
            Height = 42,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(passwordBox, 1);
        Grid.SetColumn(passwordBox, 1);
        grid.Children.Add(passwordBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 34, 0, 0)
        };
        Grid.SetRow(buttons, 2);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);

        var confirmButton = CreateDialogButton("确认", (_, _) =>
        {
            var selectedRole = roleBox.SelectedIndex == 1 ? UserRole.Technician : UserRole.Operator;
            if (passwordBox.Password != "1")
            {
                MessageBox.Show("密码错误。", "用户切换", MessageBoxButton.OK, MessageBoxImage.Warning);
                passwordBox.SelectAll();
                passwordBox.Focus();
                return;
            }

            _currentUserRole = selectedRole;
            UpdateUserRoleUi();
            Log($"当前用户切换为: {GetUserRoleDisplayName(_currentUserRole)}");
            dialog.Close();
        }, 150);
        var cancelButton = CreateDialogButton("取消", (_, _) => dialog.Close(), 150);
        buttons.Children.Add(confirmButton);
        buttons.Children.Add(cancelButton);

        dialog.Content = grid;
        dialog.Loaded += (_, _) => passwordBox.Focus();
        dialog.ShowDialog();
    }

    private void UpdateUserRoleUi()
    {
        CurrentUserText.Text = $"当前用户:{GetUserRoleDisplayName(_currentUserRole)}";
        var isTechnician = _currentUserRole == UserRole.Technician;
        var technicianVisibility = isTechnician ? Visibility.Visible : Visibility.Collapsed;
        TurntablePositionNavButton.Visibility = technicianVisibility;
        PhotoTestNavButton.Visibility = technicianVisibility;
        NewBatchButton.Visibility = technicianVisibility;
    }

    private bool RequireTechnician(string featureName)
    {
        if (_currentUserRole == UserRole.Technician)
        {
            return true;
        }

        var message = $"当前用户为操作员，不能使用“{featureName}”。请切换到技术员后操作。";
        MessageText.Text = message;
        Log(message);
        MessageBox.Show(message, "权限不足", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private static string GetUserRoleDisplayName(UserRole role)
    {
        return role == UserRole.Technician ? "技术员" : "操作员";
    }

    private void SetCameraStatus(string text, bool isNormal)
    {
        CameraText.Text = text;
        CameraText.Background = new SolidColorBrush(isNormal ? Colors.Lime : Colors.Red);
    }

    private void SetPlcStatus(string text, bool isNormal)
    {
        PlcText.Text = text;
        PlcText.Background = new SolidColorBrush(isNormal ? Colors.Lime : Colors.Red);
    }
}
