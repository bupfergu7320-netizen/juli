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
    private System.Windows.Window CreateToolDialog(string title, double width, double height)
    {
        return new System.Windows.Window
        {
            Owner = this,
            Title = title,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(238, 238, 238)),
            FontFamily = new FontFamily("SimSun"),
            ResizeMode = ResizeMode.CanResize
        };
    }

    private static Grid CreateFormGrid(int rowCount = 6)
    {
        var grid = new Grid { Margin = new Thickness(110, 70, 110, 70) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rowCount; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = i == rowCount - 1 ? GridLength.Auto : new GridLength(62) });
        }

        return grid;
    }

    private static TextBox AddFormRow(Grid grid, int row, string label, string value)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 28, 0)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var box = new TextBox
        {
            Text = value,
            FontSize = 26,
            Height = 40,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        return box;
    }

    private void AddDialogButtonRow(
        Grid grid,
        int row,
        params (string Text, RoutedEventHandler? Handler)[] buttons)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 36, 0, 0)
        };
        foreach (var (text, handler) in buttons)
        {
            panel.Children.Add(CreateDialogButton(text, handler, 200));
        }

        Grid.SetRow(panel, row);
        Grid.SetColumnSpan(panel, 2);
        grid.Children.Add(panel);
    }

    private Button CreateDialogButton(string text, RoutedEventHandler? handler, double width)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            Height = 54,
            FontSize = 24,
            FontFamily = new FontFamily("SimSun"),
            Margin = new Thickness(14, 0, 14, 0)
        };
        button.Click += (sender, e) =>
        {
            handler?.Invoke(sender, e);
            if (text == "取消" && System.Windows.Window.GetWindow(button) is { } owner)
            {
                owner.Close();
            }
        };
        return button;
    }
}
