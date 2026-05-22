using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JuliMvs.App.Services;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App;

public partial class MainWindow
{
    private static CheckBox CreateMachineDirectionCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            FontSize = 24,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18)
        };
    }

    private static TextBox CreateMachineTransformTextBox(double value)
    {
        return new TextBox
        {
            Text = InputValueParser.FormatMachineTransformNumber(value),
            Width = 92,
            Height = 36,
            FontSize = 18,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0)
        };
    }

    private static StackPanel CreateTransformRow(
        string prefix,
        TextBox first,
        string middle,
        TextBox? second,
        string suffix,
        TextBox bias)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        row.Children.Add(CreateTransformLabel(prefix));
        row.Children.Add(first);
        row.Children.Add(CreateTransformLabel(middle));
        if (second is not null)
        {
            row.Children.Add(second);
            row.Children.Add(CreateTransformLabel(suffix));
        }
        else
        {
            row.Children.Add(CreateTransformLabel(" + "));
        }

        row.Children.Add(bias);
        return row;
    }

    private static TextBlock CreateTransformLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static PlcOutputTransform ReadPlcOutputTransformFromTextBoxes(
        TextBox xxBox,
        TextBox xyBox,
        TextBox yxBox,
        TextBox yyBox,
        TextBox xBiasBox,
        TextBox yBiasBox,
        TextBox rScaleBox,
        TextBox rBiasBox)
    {
        return new PlcOutputTransform(
            InputValueParser.ReadMachineTransformNumber(xxBox.Text, "D1002的X系数"),
            InputValueParser.ReadMachineTransformNumber(xyBox.Text, "D1002的Y系数"),
            InputValueParser.ReadMachineTransformNumber(yxBox.Text, "D1004的X系数"),
            InputValueParser.ReadMachineTransformNumber(yyBox.Text, "D1004的Y系数"),
            InputValueParser.ReadMachineTransformNumber(xBiasBox.Text, "D1002偏置"),
            InputValueParser.ReadMachineTransformNumber(yBiasBox.Text, "D1004偏置"),
            InputValueParser.ReadMachineTransformNumber(rScaleBox.Text, "D1006比例"),
            InputValueParser.ReadMachineTransformNumber(rBiasBox.Text, "D1006偏置"));
    }

    private static void ApplyTransformToTextBoxes(
        PlcOutputTransform transform,
        TextBox xxBox,
        TextBox xyBox,
        TextBox yxBox,
        TextBox yyBox,
        TextBox xBiasBox,
        TextBox yBiasBox,
        TextBox rScaleBox,
        TextBox rBiasBox)
    {
        xxBox.Text = InputValueParser.FormatMachineTransformNumber(transform.Xx);
        xyBox.Text = InputValueParser.FormatMachineTransformNumber(transform.Xy);
        yxBox.Text = InputValueParser.FormatMachineTransformNumber(transform.Yx);
        yyBox.Text = InputValueParser.FormatMachineTransformNumber(transform.Yy);
        xBiasBox.Text = InputValueParser.FormatMachineTransformNumber(transform.XBias);
        yBiasBox.Text = InputValueParser.FormatMachineTransformNumber(transform.YBias);
        rScaleBox.Text = InputValueParser.FormatMachineTransformNumber(transform.RScale);
        rBiasBox.Text = InputValueParser.FormatMachineTransformNumber(transform.RBias);
    }

    private static void ApplyRScaleSignToTextBox(TextBox rScaleBox, bool invert)
    {
        var currentScale = InputValueParser.ReadMachineTransformNumber(rScaleBox.Text, "D1006比例");
        var magnitude = Math.Abs(currentScale);
        if (magnitude < 0.000001)
        {
            magnitude = 1.0;
        }

        rScaleBox.Text = InputValueParser.FormatMachineTransformNumber(invert ? -magnitude : magnitude);
    }
}
