using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JuliMvs.App.Services;
using JuliMvs.Plc;

namespace JuliMvs.App;

public partial class MainWindow
{
    private void OpenCompensationDirectionDialog(Action? onSaved = null)
    {
        if (!RequireTechnician("PLC\u6700\u7ec8\u7ea0\u504f\u8f93\u51fa\u8bbe\u7f6e"))
        {
            return;
        }

        var dialog = CreateToolDialog("PLC\u6700\u7ec8\u7ea0\u504f\u8f93\u51fa\u8bbe\u7f6e", 900, 640);
        dialog.ResizeMode = ResizeMode.NoResize;

        var root = new Grid { Margin = new Thickness(34, 28, 34, 28) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var currentText = new TextBlock
        {
            Text = TurntableStatusMessageFormatter.FormatDirectionText(
                _visionParameters,
                GetEffectivePlcOutputTransform()),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 18),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(currentText);

        var directionPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        directionPanel.Children.Add(CreateTurntableSectionText("PLC\u7edd\u5bf9\u5b9a\u4f4d\u5408\u540c"));
        directionPanel.Children.Add(new TextBlock
        {
            Text = "PLC\u4f7f\u7528\u7edd\u5bf9\u5b9a\u4f4d\u6267\u884c\uff0c\u4e0a\u4f4d\u673a\u5199R\u8f74\u4e2d\u5fc3\u540e\u6700\u7ec8\u7ea0\u504f\u91cf\uff1aTargetX=BaseX+D1002\uff0cTargetY=BaseY+D1004\uff0cTargetR=BaseR+D1006\u3002",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Brushes.DarkRed
        });
        directionPanel.Children.Add(new TextBlock
        {
            Text = "D1002=HomeXAction\uff0cD1004=HomeYAction\uff0cD1006=HomeRAction\u3002D1002/D1004\u662f\u6309\u6700\u7ec8PLC R\u547d\u4ee4\u548cR\u8f74\u4e2d\u5fc3\u540c\u6b65\u91cd\u7b97\u540e\u7684XY\u6700\u7ec8\u8865\u507f\u3002\u9ed8\u8ba4\u5355\u4f4d\u77e9\u9635\u4e0d\u4ea4\u6362\u3001\u4e0d\u7f29\u653e\u3001\u4e0d\u504f\u7f6e\u3002",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var xxBox = CreateMachineTransformTextBox(_plcOutputTransform.Xx);
        var xyBox = CreateMachineTransformTextBox(_plcOutputTransform.Xy);
        var xBiasBox = CreateMachineTransformTextBox(_plcOutputTransform.XBias);
        var yxBox = CreateMachineTransformTextBox(_plcOutputTransform.Yx);
        var yyBox = CreateMachineTransformTextBox(_plcOutputTransform.Yy);
        var yBiasBox = CreateMachineTransformTextBox(_plcOutputTransform.YBias);
        var rScaleBox = CreateMachineTransformTextBox(_plcOutputTransform.RScale);
        var rBiasBox = CreateMachineTransformTextBox(_plcOutputTransform.RBias);
        var initialInvertX = PlcOutputDirectionSettings.IsSimpleXInverted(_plcOutputTransform);
        var initialInvertY = PlcOutputDirectionSettings.IsSimpleYInverted(_plcOutputTransform);
        var invertXCheckBox = CreateMachineDirectionCheckBox(
            "X\u65b9\u5411\u53d6\u53cd\uff08\u8865\u507f\u540eX\u8d8a\u8d70\u8d8a\u504f\u65f6\u52fe\u9009\uff09",
            initialInvertX);
        var invertYCheckBox = CreateMachineDirectionCheckBox(
            "Y\u65b9\u5411\u53d6\u53cd\uff08\u8865\u507f\u540eY\u8d8a\u8d70\u8d8a\u504f\u65f6\u52fe\u9009\uff09",
            initialInvertY);
        var invertRCheckBox = CreateMachineDirectionCheckBox(
            "R方向取反并同步重算XY（XY对、R方向反时勾选）",
            _visionParameters.InvertRotationCompensation);
        invertXCheckBox.FontSize = 20;
        invertYCheckBox.FontSize = 20;
        invertRCheckBox.FontSize = 20;
        invertXCheckBox.Margin = new Thickness(0, 0, 0, 10);
        invertYCheckBox.Margin = new Thickness(0, 0, 0, 10);
        invertRCheckBox.Margin = new Thickness(0, 0, 0, 12);
        directionPanel.Children.Add(invertXCheckBox);
        directionPanel.Children.Add(invertYCheckBox);
        directionPanel.Children.Add(new TextBlock
        {
            Text = "X/Y\u65b9\u5411\u53d6\u53cd\u662f\u673a\u5668\u5168\u5c40\u53c2\u6570\uff0c\u4e0d\u8ddf\u578b\u53f7/\u6a21\u677f\u8d70\u3002\u53ea\u6709\u5f53\u5355\u8f74\u8865\u507f\u540e\u8d8a\u8d70\u8d8a\u504f\u65f6\u624d\u52fe\u9009\u5bf9\u5e94\u8f74\u3002",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Brushes.DarkGreen
        });
        directionPanel.Children.Add(invertRCheckBox);
        directionPanel.Children.Add(new TextBlock
        {
            Text = "\u53cd\u9762NG\u8ddf\u968f\u578b\u53f7/\u6a21\u677f\u4fdd\u5b58\uff1a\u8bf7\u5728\u6362\u578b\u7a97\u53e3\u52fe\u9009\u201c\u6b64\u578b\u53f7\u68c0\u67e5\u53cd\u9762NG\u201d\u3002\u6b63\u53cd\u9762\u5bf9\u79f0\u4ef6\u4e0d\u8981\u52fe\u9009\u3002",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Brushes.DarkSlateBlue
        });

        var advancedPanel = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 12, 0, 0)
        };
        advancedPanel.Children.Add(CreateTurntableSectionText("高级输出矩阵"));
        advancedPanel.Children.Add(new TextBlock
        {
            Text = "高级输出矩阵会直接改变D1002/D1004/D1006，请只在工程人员指导下修改。",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Brushes.DarkRed
        });
        advancedPanel.Children.Add(CreateTransformRow("D1002 =", xxBox, " * X纠偏 + ", xyBox, " * Y纠偏 + ", xBiasBox));
        advancedPanel.Children.Add(CreateTransformRow("D1004 =", yxBox, " * X纠偏 + ", yyBox, " * Y纠偏 + ", yBiasBox));
        advancedPanel.Children.Add(CreateTransformRow("D1006 =", rScaleBox, " * R纠偏", null, string.Empty, rBiasBox));

        var resetButton = CreateDialogButton("恢复默认输出", (_, _) =>
        {
            var confirm = MessageBox.Show(
                dialog,
                "将恢复默认PLC输出：X不取反、Y不取反、R不取反，并清除X/Y交叉系数和偏置。\n确认继续？",
                "恢复默认输出",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            ApplyTransformToTextBoxes(
                PlcOutputTransform.Identity,
                xxBox,
                xyBox,
                yxBox,
                yyBox,
                xBiasBox,
                yBiasBox,
                rScaleBox,
                rBiasBox);
            invertXCheckBox.IsChecked = false;
            invertYCheckBox.IsChecked = false;
            invertRCheckBox.IsChecked = false;
            rScaleBox.Text = InputValueParser.FormatMachineTransformNumber(1.0);
        }, 0);
        resetButton.Width = double.NaN;
        resetButton.Margin = new Thickness(8, 8, 8, 0);
        advancedPanel.Children.Add(resetButton);

        var advancedToggleButton = CreateDialogButton("显示高级输出矩阵", null, 0);
        advancedToggleButton.Width = double.NaN;
        advancedToggleButton.Margin = new Thickness(8, 4, 8, 0);
        advancedToggleButton.Click += (_, _) =>
        {
            var showAdvanced = advancedPanel.Visibility != Visibility.Visible;
            advancedPanel.Visibility = showAdvanced ? Visibility.Visible : Visibility.Collapsed;
            advancedToggleButton.Content = showAdvanced ? "隐藏高级输出矩阵" : "显示高级输出矩阵";
        };
        directionPanel.Children.Add(advancedToggleButton);
        directionPanel.Children.Add(advancedPanel);

        var scroller = new ScrollViewer
        {
            Content = directionPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);

        var buttons = new Grid { Margin = new Thickness(0, 24, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var saveButton = CreateDialogButton("\u4fdd\u5b58", async (_, _) =>
        {
            try
            {
                ApplyRScaleSignToTextBox(rScaleBox, invert: false);
                var transform = ReadPlcOutputTransformFromTextBoxes(
                    xxBox,
                    xyBox,
                    yxBox,
                    yyBox,
                    xBiasBox,
                    yBiasBox,
                    rScaleBox,
                    rBiasBox);
                var invertX = invertXCheckBox.IsChecked == true;
                var invertY = invertYCheckBox.IsChecked == true;
                var xyDirectionChanged = invertX != initialInvertX || invertY != initialInvertY;
                if (xyDirectionChanged || PlcOutputDirectionSettings.IsSimpleXyTransform(transform))
                {
                    transform = PlcOutputDirectionSettings.ApplySimpleXyDirection(
                        transform,
                        invertX,
                        invertY);
                    ApplyTransformToTextBoxes(
                        transform,
                        xxBox,
                        xyBox,
                        yxBox,
                        yyBox,
                        xBiasBox,
                        yBiasBox,
                        rScaleBox,
                        rBiasBox);
                }
                var reconnectPlc = _plcClient?.IsConnected == true;
                await SaveMachineCompensationDirectionsAsync(
                    invertX: invertX,
                    invertY: invertY,
                    invertRotation: invertRCheckBox.IsChecked == true,
                    transform);
                currentText.Text = TurntableStatusMessageFormatter.FormatDirectionText(
                    _visionParameters,
                    GetEffectivePlcOutputTransform());
                MessageText.Text = "PLC\u6700\u7ec8\u7ea0\u504f\u8f93\u51fa\u77e9\u9635\u5df2\u4fdd\u5b58\u3002";
                Log(
                    "PLC\u6700\u7ec8\u7ea0\u504f\u8f93\u51fa\u77e9\u9635\u5df2\u4fdd\u5b58: " +
                    TurntableStatusMessageFormatter.FormatDirectionText(
                        _visionParameters,
                        GetEffectivePlcOutputTransform()));
                if (reconnectPlc)
                {
                    await ConnectPlcAsync();
                    Log("PLC\u5df2\u6309\u65b0\u7684\u6700\u7ec8\u7ea0\u504f\u8f93\u51fa\u77e9\u9635\u91cd\u65b0\u8fde\u63a5\u3002");
                }

                onSaved?.Invoke();
                dialog.Close();
            }
            catch (Exception ex)
            {
                MessageText.Text = $"PLC\u6700\u7ec8\u7ea0\u504f\u8f93\u51fa\u77e9\u9635\u4fdd\u5b58\u5931\u8d25: {ex.Message}";
                Log(MessageText.Text);
                MessageBox.Show(ex.Message, "PLC\u6700\u7ec8\u7ea0\u504f\u8f93\u51fa\u8bbe\u7f6e", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }, 0);
        saveButton.Width = double.NaN;
        saveButton.Margin = new Thickness(8, 0, 8, 0);
        buttons.Children.Add(saveButton);

        var cancelButton = CreateDialogButton("\u53d6\u6d88", (_, _) => dialog.Close(), 0);
        cancelButton.Width = double.NaN;
        cancelButton.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(cancelButton, 1);
        buttons.Children.Add(cancelButton);

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.ShowDialog();
    }
}
