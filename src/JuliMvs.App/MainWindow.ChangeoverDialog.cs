using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JuliMvs.App.Services;
using JuliMvs.Core.Batch;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;

namespace JuliMvs.App;

public partial class MainWindow
{
    private void Changeover_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireTechnician("换型"))
        {
            return;
        }

        OpenChangeoverDialog();
    }

    private void OpenChangeoverDialog()
    {
        if (_changeoverDialog is { IsVisible: true })
        {
            _changeoverDialog.Activate();
            return;
        }

        _changeoverStepTexts.Clear();
        var dialog = CreateToolDialog("换型流程", 980, 720);
        dialog.ResizeMode = ResizeMode.NoResize;
        dialog.Closing += (_, _) =>
        {
            _changeoverDialog = null;
            _changeoverModelBox = null;
            _changeoverStatusText = null;
            _changeoverHintText = null;
            _changeoverSummaryText = null;
            _changeoverStartButton = null;
            _changeoverCaptureTemplateButton = null;
            _changeoverCancelButton = null;
            _changeoverStepTexts.Clear();
        };

        var root = new Grid { Margin = new Thickness(30, 26, 30, 26) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        header.Children.Add(new TextBlock
        {
            Text = "当前型号",
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 22, 0)
        });

        _changeoverModelBox = new TextBox
        {
            Text = _currentProductName,
            Height = 42,
            FontSize = 24,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(_changeoverModelBox, 1);
        header.Children.Add(_changeoverModelBox);

        _changeoverStatusText = new TextBlock
        {
            Text = "未开始",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 20, 0, 0)
        };
        Grid.SetRow(_changeoverStatusText, 1);
        Grid.SetColumnSpan(_changeoverStatusText, 2);
        header.Children.Add(_changeoverStatusText);

        _changeoverHintText = new TextBlock
        {
            Text = "确认型号后，可加载已有标准位/模板生产，或重新建立当前型号标准位/模板。动作方向在“转盘定位 -> 方向设置”中作为机器全局参数保存。",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 58, 0, 0)
        };
        Grid.SetRow(_changeoverHintText, 1);
        Grid.SetColumnSpan(_changeoverHintText, 2);
        header.Children.Add(_changeoverHintText);

        root.Children.Add(header);

        var body = new Grid { Margin = new Thickness(0, 28, 0, 0), MinHeight = 260 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var stepPanel = new StackPanel();
        for (var index = 0; index < ChangeoverStepLabels.Length; index++)
        {
            var stepText = new TextBlock
            {
                FontSize = 22,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            };
            _changeoverStepTexts.Add(stepText);
            stepPanel.Children.Add(stepText);
        }

        body.Children.Add(stepPanel);

        var summaryBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18),
            Margin = new Thickness(28, 0, 0, 0)
        };
        _changeoverSummaryText = new TextBlock
        {
            Text = "当前型号标准位/模板信息将在上位机拍照建立后显示。",
            FontSize = 20,
            TextWrapping = TextWrapping.Wrap
        };
        summaryBorder.Child = _changeoverSummaryText;
        Grid.SetColumn(summaryBorder, 1);
        body.Children.Add(summaryBorder);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var buttons = new Grid { Margin = new Thickness(0, 24, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var loadTemplateButton = CreateDialogButton("加载标准位/模板", async (_, _) => await LoadExistingTemplateFromChangeoverAsync(), 0);
        loadTemplateButton.Width = double.NaN;
        loadTemplateButton.FontSize = 18;
        loadTemplateButton.Margin = new Thickness(8, 0, 8, 0);
        buttons.Children.Add(loadTemplateButton);

        _changeoverStartButton = CreateDialogButton("重建标准位/模板", async (_, _) => await StartChangeoverFromDialogAsync(), 0);
        _changeoverStartButton.Width = double.NaN;
        _changeoverStartButton.FontSize = 18;
        _changeoverStartButton.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(_changeoverStartButton, 1);
        buttons.Children.Add(_changeoverStartButton);

        _changeoverCaptureTemplateButton = CreateDialogButton("拍照设标准位/模板", async (_, _) => await CaptureChangeoverTemplateFromAppAsync(), 0);
        _changeoverCaptureTemplateButton.Width = double.NaN;
        _changeoverCaptureTemplateButton.FontSize = 18;
        _changeoverCaptureTemplateButton.Margin = new Thickness(8, 0, 8, 0);
        _changeoverCaptureTemplateButton.IsEnabled = _changeoverTemplateRequested;
        Grid.SetColumn(_changeoverCaptureTemplateButton, 2);
        buttons.Children.Add(_changeoverCaptureTemplateButton);

        _changeoverCancelButton = CreateDialogButton("取消换型", (_, _) => CancelChangeover(), 0);
        _changeoverCancelButton.Width = double.NaN;
        _changeoverCancelButton.FontSize = 18;
        _changeoverCancelButton.Margin = new Thickness(8, 0, 8, 0);
        _changeoverCancelButton.IsEnabled = _changeoverTemplateRequested;
        Grid.SetColumn(_changeoverCancelButton, 3);
        buttons.Children.Add(_changeoverCancelButton);

        var closeButton = CreateDialogButton("关闭", (_, _) => dialog.Close(), 0);
        closeButton.Width = double.NaN;
        closeButton.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(closeButton, 4);
        buttons.Children.Add(closeButton);

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        _changeoverDialog = dialog;
        UpdateChangeoverFlow(
            activeStep: _changeoverTemplateRequested ? 2 : 0,
            completedSteps: _changeoverTemplateRequested ? 2 : 0,
            status: _changeoverTemplateRequested ? "等待上位机拍照" : "未开始",
            hint: _changeoverTemplateRequested
                ? "换型模式已开启。请确认标准件已放稳在OK位置，然后点击“拍照设标准位/模板”；当前中心和角度会保存为当前型号标准位X/Y/R。"
                : "确认型号后，可加载已有标准位/模板生产，或重新建立当前型号标准位/模板。动作方向是机器全局参数，请在转盘定位里设置。",
            summary: null);
        dialog.Show();
    }

    private void CancelChangeover()
    {
        _changeoverTemplateRequested = false;
        if (_batchSession.Status is BatchStatus.WaitingFirstArticle or BatchStatus.TemplateCreated)
        {
            try
            {
                _batchSession.End();
            }
            catch (Exception ex)
            {
                Log($"取消换型时结束批次失败: {ex.Message}");
            }
        }

        _batchSession = BatchSession.Empty();
        ClearCurrentInspection();
        _currentBatchNo = BatchNumberGenerator.GenerateDefaultBatchNo();
        MessageText.Text = "换型已取消。普通生产不会建立标准位/模板。";
        Log("换型已取消");
        _changeoverStartButton?.SetCurrentValue(IsEnabledProperty, true);
        _changeoverCaptureTemplateButton?.SetCurrentValue(IsEnabledProperty, false);
        _changeoverCancelButton?.SetCurrentValue(IsEnabledProperty, false);
        UpdateChangeoverFlow(0, 0, "换型已取消", "如需重新建立当前型号标准位/模板，请再次点击“重建标准位/模板”。", summary: "换型已取消。");
        UpdateBatchUi();
    }

    private void UpdateChangeoverFlow(
        int activeStep,
        int completedSteps,
        string status,
        string hint,
        string? summary = null,
        bool failed = false)
    {
        if (_changeoverStatusText is null || _changeoverHintText is null)
        {
            return;
        }

        _changeoverStatusText.Text = status;
        _changeoverStatusText.Foreground = failed ? Brushes.Red : Brushes.Black;
        _changeoverHintText.Text = hint;

        for (var index = 0; index < _changeoverStepTexts.Count; index++)
        {
            var stepText = _changeoverStepTexts[index];
            var isCompleted = index < completedSteps;
            var isActive = index == activeStep;
            var state = isCompleted ? "已完成" : isActive ? failed ? "失败" : "进行中" : "未开始";
            stepText.Text = $"[{state}] {index + 1}. {ChangeoverStepLabels[index]}";
            stepText.Foreground = isCompleted
                ? Brushes.ForestGreen
                : isActive
                    ? failed ? Brushes.Red : Brushes.DodgerBlue
                    : Brushes.DimGray;
            stepText.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
        }

        if (summary is not null && _changeoverSummaryText is not null)
        {
            _changeoverSummaryText.Text = summary;
        }
    }
}
