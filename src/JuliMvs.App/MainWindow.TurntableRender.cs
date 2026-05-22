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
    private static TextBlock CreateTurntableHeaderText(string text, int column)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 20, 0)
        };
        Grid.SetColumn(textBlock, column);
        return textBlock;
    }

    private static TextBlock CreateTurntableSectionText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 8)
        };
    }

    private static TextBlock CreateTurntableValueText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 20,
            Margin = new Thickness(0, 0, 0, 7)
        };
    }

    private void RenderTurntableResult(
        InspectionResult result,
        XyrAlignmentSnapshot? alignmentSnapshot,
        IReadOnlyList<ContourCandidateDiagnostic>? candidateDiagnostics,
        AngleResolutionDiagnostic? angleDiagnostic,
        TextBlock statusText,
        TextBlock xOffsetText,
        TextBlock yOffsetText,
        TextBlock rOffsetText,
        TextBlock xCompText,
        TextBlock yCompText,
        TextBlock rCompText,
        TextBlock scoreText,
        TextBlock messageText)
    {
        statusText.Text = TurntableStatusMessageFormatter.FormatInspectionDecision(result.Decision);
        statusText.Foreground = GetInspectionDecisionBrush(result.Decision);

        var measurement = result.Measurement;
        if (measurement is null)
        {
            xOffsetText.Text = "X偏差: -";
            yOffsetText.Text = "Y偏差: -";
            rOffsetText.Text = "R偏差: -";
            xCompText.Text = "X偏差输出: -";
            yCompText.Text = "Y偏差输出: -";
            rCompText.Text = "R偏差输出: -";
            scoreText.Text = "识别分数: -";
        }
        else
        {
            var plcOutput = CalculatePlcOutputCommand(measurement);
            xOffsetText.Text = $"X偏差: {measurement.XOffsetMm:F3} mm";
            yOffsetText.Text = $"Y偏差: {measurement.YOffsetMm:F3} mm";
            rOffsetText.Text = $"R偏差: {measurement.AngleOffsetDegrees:F3} deg";
            xCompText.Text = $"D1002修正偏差: {PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.XDeviation)} mm";
            yCompText.Text = $"D1004修正偏差: {PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.YDeviation)} mm";
            rCompText.Text = $"D1006修正偏差: {PlcOutputDiagnosticFormatter.FormatPlcValueText(plcOutput.RDeviation)} deg";
            scoreText.Text = $"识别分数: {measurement.MatchScore:F3}";
        }

        messageText.Text = _inspectionDiagnosticMessageFormatter.BuildTurntableResultDetails(
            result,
            _template,
            _plcOutputTransform,
            alignmentSnapshot,
            candidateDiagnostics,
            angleDiagnostic);
    }

    private static void RenderTurntableBasicPositionResult(
        PartDetection detection,
        TextBlock statusText,
        TextBlock xOffsetText,
        TextBlock yOffsetText,
        TextBlock rOffsetText,
        TextBlock xCompText,
        TextBlock yCompText,
        TextBlock rCompText,
        TextBlock scoreText,
        TextBlock messageText,
        string? rawImagePath)
    {
        statusText.Text = "基础定位";
        statusText.Foreground = Brushes.DodgerBlue;
        xOffsetText.Text = $"中心X: {detection.CenterXPixel:F1} px";
        yOffsetText.Text = $"中心Y: {detection.CenterYPixel:F1} px";
        rOffsetText.Text = $"角度R: {detection.AngleDegrees:F3} deg";
        xCompText.Text = "X偏差输出: 无模板";
        yCompText.Text = "Y偏差输出: 无模板";
        rCompText.Text = "R偏差输出: 无模板";
        scoreText.Text = $"面积: {detection.AreaPixels:F0} px";
        messageText.Text =
            "未加载标准位/模板，仅显示基础定位。\n" +
            "当前不判断OK/NG，不写PLC；X/Y/R偏差输出需加载模板后计算。\n" +
            $"图像: {InspectionDiagnosticMessageFormatter.FormatSavedImagePath(rawImagePath)}";
    }

    private static void RenderTurntableBasicPositionFailure(
        TextBlock statusText,
        TextBlock xOffsetText,
        TextBlock yOffsetText,
        TextBlock rOffsetText,
        TextBlock xCompText,
        TextBlock yCompText,
        TextBlock rCompText,
        TextBlock scoreText,
        TextBlock messageText,
        string? rawImagePath)
    {
        statusText.Text = "识别失败";
        statusText.Foreground = Brushes.Red;
        xOffsetText.Text = "中心X: -";
        yOffsetText.Text = "中心Y: -";
        rOffsetText.Text = "角度R: -";
        xCompText.Text = "X偏差输出: 无模板";
        yCompText.Text = "Y偏差输出: 无模板";
        rCompText.Text = "R偏差输出: 无模板";
        scoreText.Text = "面积: -";
        messageText.Text =
            "未加载标准位/模板，基础定位失败。\n" +
            "未找到工件轮廓，请检查光源、曝光、阈值、最小面积和工件位置。\n" +
            $"图像: {InspectionDiagnosticMessageFormatter.FormatSavedImagePath(rawImagePath)}";
    }

    private void ApplyTurntableCalibrationBrush(TextBlock textBlock)
    {
        textBlock.Foreground = IsMachineCalibrationReady(out _)
            ? Brushes.ForestGreen
            : Brushes.DarkOrange;
    }

    private void ApplyTurntableFlowBrush(TextBlock textBlock)
    {
        textBlock.Foreground = IsMachineCalibrationReady(out _)
            ? Brushes.Black
            : Brushes.DarkOrange;
    }

    private static Brush GetInspectionDecisionBrush(InspectionDecision decision)
    {
        return decision switch
        {
            InspectionDecision.Ok => Brushes.ForestGreen,
            InspectionDecision.Ng => Brushes.Red,
            InspectionDecision.Error => Brushes.Red,
            _ => Brushes.Black
        };
    }
}
