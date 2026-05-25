using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JuliMvs.App.Services;

namespace JuliMvs.App;

public partial class MainWindow
{
    private void OpenCompensationDirectionDialog(Action? onSaved = null)
    {
        if (!RequireTechnician("PLC方向取反设置"))
        {
            return;
        }

        var dialog = CreateToolDialog("PLC方向取反设置", 620, 430);
        dialog.ResizeMode = ResizeMode.NoResize;

        var root = new Grid { Margin = new Thickness(42, 34, 42, 30) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var currentText = new TextBlock
        {
            Text = TurntableStatusMessageFormatter.FormatDirectionText(
                _visionParameters,
                GetEffectivePlcOutputTransform()),
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 26),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(currentText);

        var initialInvertX = PlcOutputDirectionSettings.IsSimpleXInverted(_plcOutputTransform);
        var initialInvertY = PlcOutputDirectionSettings.IsSimpleYInverted(_plcOutputTransform);
        var invertXCheckBox = CreateDirectionCheckBox("X方向取反", initialInvertX);
        var invertYCheckBox = CreateDirectionCheckBox("Y方向取反", initialInvertY);
        var invertRCheckBox = CreateDirectionCheckBox("R方向取反", _visionParameters.InvertRotationCompensation);

        var directionPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        directionPanel.Children.Add(invertXCheckBox);
        directionPanel.Children.Add(invertYCheckBox);
        directionPanel.Children.Add(invertRCheckBox);
        Grid.SetRow(directionPanel, 1);
        root.Children.Add(directionPanel);

        var buttons = new Grid { Margin = new Thickness(0, 24, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var saveButton = CreateDialogButton("保存", async (_, _) =>
        {
            try
            {
                var invertX = invertXCheckBox.IsChecked == true;
                var invertY = invertYCheckBox.IsChecked == true;
                var transform = PlcOutputDirectionSettings.ApplySimpleXyDirection(
                    _plcOutputTransform,
                    invertX,
                    invertY);
                var reconnectPlc = _plcClient?.IsConnected == true;

                await SaveMachineCompensationDirectionsAsync(
                    invertX,
                    invertY,
                    invertRCheckBox.IsChecked == true,
                    transform);

                currentText.Text = TurntableStatusMessageFormatter.FormatDirectionText(
                    _visionParameters,
                    GetEffectivePlcOutputTransform());
                MessageText.Text = "PLC方向取反设置已保存。";
                Log(
                    "PLC方向取反设置已保存: " +
                    TurntableStatusMessageFormatter.FormatDirectionText(
                        _visionParameters,
                        GetEffectivePlcOutputTransform()));

                if (reconnectPlc)
                {
                    await ConnectPlcAsync();
                    Log("PLC已按新的方向取反设置重新连接。");
                }

                onSaved?.Invoke();
                dialog.Close();
            }
            catch (Exception ex)
            {
                MessageText.Text = $"PLC方向取反设置保存失败: {ex.Message}";
                Log(MessageText.Text);
                MessageBox.Show(ex.Message, "PLC方向取反设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }, 0);
        saveButton.Width = double.NaN;
        saveButton.Margin = new Thickness(8, 0, 8, 0);
        buttons.Children.Add(saveButton);

        var cancelButton = CreateDialogButton("取消", (_, _) => dialog.Close(), 0);
        cancelButton.Width = double.NaN;
        cancelButton.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(cancelButton, 1);
        buttons.Children.Add(cancelButton);

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private static CheckBox CreateDirectionCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 28)
        };
    }
}
