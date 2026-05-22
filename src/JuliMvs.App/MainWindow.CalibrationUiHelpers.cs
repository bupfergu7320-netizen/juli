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
    private static TextBox AddCalibrationBoardRow(Grid grid, int row, string label, string value)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 18, 0)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var box = new TextBox
        {
            Text = value,
            FontSize = 22,
            Height = 38,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        return box;
    }

    private static TextBlock CreateCalibrationLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static TextBox AddCalibrationEditorRow(Grid grid, int row, string label, double value)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 18, 0)
        };
        Grid.SetRow(labelBlock, row);
        grid.Children.Add(labelBlock);

        var box = new TextBox
        {
            Text = value.ToString(CultureInfo.InvariantCulture),
            FontSize = 22,
            Height = 38,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        return box;
    }

    private Button CreateCalibrationGridButton(string text, RoutedEventHandler handler, int column)
    {
        var button = CreateDialogButton(text, handler, 0);
        button.Width = double.NaN;
        button.Margin = new Thickness(6, 0, 6, 0);
        button.FontSize = 21;
        Grid.SetColumn(button, column);
        return button;
    }

    private static void CommitCalibrationEditor(
        DataGrid table,
        EditableCalibrationPoint point,
        TextBox machineXBox,
        TextBox machineYBox)
    {
        table.CommitEdit(DataGridEditingUnit.Cell, true);
        table.CommitEdit(DataGridEditingUnit.Row, true);
        point.MachineXMm = InputValueParser.ReadRequiredDouble(machineXBox.Text, "机械X", -1_000_000, 1_000_000);
        point.MachineYMm = InputValueParser.ReadRequiredDouble(machineYBox.Text, "机械Y", -1_000_000, 1_000_000);
    }

    private static void CommitRAxisCenterCalibrationEditor(
        DataGrid table,
        EditableRAxisCenterCalibrationPoint point,
        TextBox angleBox)
    {
        table.CommitEdit(DataGridEditingUnit.Cell, true);
        table.CommitEdit(DataGridEditingUnit.Row, true);
        point.AngleDegrees = InputValueParser.ReadRequiredDouble(angleBox.Text, "R角度", -360, 360);
        point.Name = CalibrationEditorPointFactory.BuildRAxisCenterPointName(point.AngleDegrees);
    }
}
