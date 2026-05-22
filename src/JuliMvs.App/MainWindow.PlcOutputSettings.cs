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

        directionPanel.Children.Add(CreateTurntableSectionText("PLC\u6700\u7ec8\u7ea0\u504f\u8f93\u51fa\u77e9\u9635"));
        var xxBox = CreateMachineTransformTextBox(_plcOutputTransform.Xx);
        var xyBox = CreateMachineTransformTextBox(_plcOutputTransform.Xy);
        var xBiasBox = CreateMachineTransformTextBox(_plcOutputTransform.XBias);
        var yxBox = CreateMachineTransformTextBox(_plcOutputTransform.Yx);
        var yyBox = CreateMachineTransformTextBox(_plcOutputTransform.Yy);
        var yBiasBox = CreateMachineTransformTextBox(_plcOutputTransform.YBias);
        var rScaleBox = CreateMachineTransformTextBox(_plcOutputTransform.RScale);
        var rBiasBox = CreateMachineTransformTextBox(_plcOutputTransform.RBias);
        var invertRCheckBox = CreateMachineDirectionCheckBox(
            "R方向取反并同步重算XY（XY对、R方向反时勾选）",
            _visionParameters.InvertRotationCompensation);
        invertRCheckBox.FontSize = 20;
        invertRCheckBox.Margin = new Thickness(0, 0, 0, 12);
        var backSideNgCheckBox = CreateMachineDirectionCheckBox(
            "\u542f\u7528\u53cd\u9762NG\uff08ContourMirror\u5206\u5dee<0\uff09",
            _visionParameters.BackSideNgEnabled);
        backSideNgCheckBox.FontSize = 20;
        backSideNgCheckBox.Margin = new Thickness(0, 0, 0, 12);
        directionPanel.Children.Add(CreateTransformRow("D1002 =", xxBox, " * X\u7ea0\u504f + ", xyBox, " * Y\u7ea0\u504f + ", xBiasBox));
        directionPanel.Children.Add(CreateTransformRow("D1004 =", yxBox, " * X\u7ea0\u504f + ", yyBox, " * Y\u7ea0\u504f + ", yBiasBox));
        directionPanel.Children.Add(CreateTransformRow("D1006 =", rScaleBox, " * R\u7ea0\u504f", null, string.Empty, rBiasBox));
        directionPanel.Children.Add(invertRCheckBox);
        directionPanel.Children.Add(backSideNgCheckBox);
        directionPanel.Children.Add(new TextBlock
        {
            Text = "\u53cd\u9762NG\u89c4\u5219\uff1aFrontScore - BackScore < 0 \u5224\u53cd\u9762NG\uff1b>= 0 \u5224\u6b63\u9762\u3002",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Brushes.DarkRed
        });

        var resetButton = CreateDialogButton("\u6062\u590d\u5355\u4f4d\u77e9\u9635", (_, _) =>
        {
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
            invertRCheckBox.IsChecked = false;
            backSideNgCheckBox.IsChecked = false;
            rScaleBox.Text = InputValueParser.FormatMachineTransformNumber(1.0);
        }, 0);
        resetButton.Width = double.NaN;
        resetButton.Margin = new Thickness(8, 8, 8, 0);
        directionPanel.Children.Add(resetButton);

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
                var reconnectPlc = _plcClient?.IsConnected == true;
                await SaveMachineCompensationDirectionsAsync(
                    invertX: false,
                    invertY: false,
                    invertRotation: invertRCheckBox.IsChecked == true,
                    backSideNgEnabled: backSideNgCheckBox.IsChecked == true,
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
