using System.Globalization;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;

namespace JuliMvs.App.Services;

internal sealed class PlcOutputDiagnosticFormatter
{
    public string BuildOkMessage(
        InspectionMeasurement measurement,
        PlcOutputTransform outputTransform,
        bool writeToPlc)
    {
        var plcOutput = CalculatePlcOutputCommand(measurement, outputTransform);
        var preRotationOutput = CalculatePreRotationCorrectionCommand(measurement, outputTransform);
        var deviationText =
            $"X={FormatPlcValueText(measurement.XOffsetMm)}mm, " +
            $"Y={FormatPlcValueText(measurement.YOffsetMm)}mm, " +
            $"R={FormatPlcValueText(measurement.AngleOffsetDegrees)}deg";
        var preRotationCorrectionText =
            $"X={FormatPlcValueText(-measurement.XOffsetMm)}mm, " +
            $"Y={FormatPlcValueText(-measurement.YOffsetMm)}mm, " +
            $"R={FormatPlcValueText(-measurement.AngleOffsetDegrees)}deg";
        var finalCorrectionText =
            $"X={FormatPlcValueText(measurement.XCompensationMm)}mm, " +
            $"Y={FormatPlcValueText(measurement.YCompensationMm)}mm, " +
            $"R={FormatPlcValueText(measurement.RotationCompensationDegrees)}deg";
        var plcOutputText =
            $"D1002={FormatPlcValueText(plcOutput.XDeviation)}, " +
            $"D1004={FormatPlcValueText(plcOutput.YDeviation)}, " +
            $"D1006={FormatPlcValueText(plcOutput.RDeviation)}";
        var preRotationOutputText =
            $"D1002={FormatPlcValueText(preRotationOutput.XDeviation)}, " +
            $"D1004={FormatPlcValueText(preRotationOutput.YDeviation)}, " +
            $"D1006={FormatPlcValueText(preRotationOutput.RDeviation)}";

        return writeToPlc
            ? $"检测OK，PLC最终纠偏量已写入: {plcOutputText}。最终纠偏量(R轴中心后): {finalCorrectionText}。旋转前纠偏量(仅参考): {preRotationCorrectionText}，对应旧输出 {preRotationOutputText}。视觉偏差(当前-模板): {deviationText}。本轮不复检。"
            : $"检测OK，PLC最终纠偏量预览: {plcOutputText}。最终纠偏量(R轴中心后): {finalCorrectionText}。旋转前纠偏量(仅参考): {preRotationCorrectionText}，对应旧输出 {preRotationOutputText}。视觉偏差(当前-模板): {deviationText}。调试拍照未写PLC。";
    }

    public IEnumerable<string> BuildPreviewLogs(
        InspectionResult result,
        PlcOutputTransform outputTransform,
        XyrAlignmentSnapshot? alignmentSnapshot = null)
    {
        var measurement = result.Measurement;
        if (measurement is null)
        {
            yield return $"PLC输出预览: Decision={result.Decision}, NgReason={result.NgReason}";
            yield break;
        }

        var plcOutput = CalculatePlcOutputCommand(measurement, outputTransform);
        var preRotationOutput = CalculatePreRotationCorrectionCommand(measurement, outputTransform);
        yield return
            "PLC输出预览: " +
            $"Decision={result.Decision}, " +
            $"视觉偏差(当前-模板) X={FormatPlcValueText(measurement.XOffsetMm)}mm, " +
            $"Y={FormatPlcValueText(measurement.YOffsetMm)}mm, " +
            $"R={FormatPlcValueText(measurement.AngleOffsetDegrees)}deg, " +
            $"旋转前纠偏量(模板-当前) X={FormatPlcValueText(-measurement.XOffsetMm)}mm, " +
            $"Y={FormatPlcValueText(-measurement.YOffsetMm)}mm, " +
            $"R={FormatPlcValueText(-measurement.AngleOffsetDegrees)}deg, " +
            $"R后最终纠偏参考 X={FormatPlcValueText(measurement.XCompensationMm)}mm, " +
            $"Y={FormatPlcValueText(measurement.YCompensationMm)}mm, " +
            $"R={FormatPlcValueText(measurement.RotationCompensationDegrees)}deg, " +
            $"PLC最终纠偏输出 D1002={FormatPlcValueText(plcOutput.XDeviation)}, " +
            $"D1004={FormatPlcValueText(plcOutput.YDeviation)}, " +
            $"D1006={FormatPlcValueText(plcOutput.RDeviation)}, " +
            $"旋转前旧输出仅参考 D1002={FormatPlcValueText(preRotationOutput.XDeviation)}, " +
            $"D1004={FormatPlcValueText(preRotationOutput.YDeviation)}, " +
            $"D1006={FormatPlcValueText(preRotationOutput.RDeviation)}, " +
            $"NgReason={result.NgReason}";

        if (result.Decision != InspectionDecision.Ok)
        {
            yield return "PLC输出预览: 当前结果不是OK，正式流程只会写D1010=2，不会使用X/Y/R偏差输出。";
        }

        if (alignmentSnapshot is not null)
        {
            var rCommandDirection = alignmentSnapshot.RCommandDirection < 0
                ? "取反并同步重算XY"
                : "不取反";
            yield return
                "XYR几何快照: " +
                $"当前位姿 X={FormatPlcValueText(alignmentSnapshot.CurrentPose.XMm)}mm, " +
                $"Y={FormatPlcValueText(alignmentSnapshot.CurrentPose.YMm)}mm, " +
                $"R={FormatPlcValueText(alignmentSnapshot.CurrentPose.AngleDegrees)}deg, " +
                $"标准位姿 X={FormatPlcValueText(alignmentSnapshot.TemplatePose.XMm)}mm, " +
                $"Y={FormatPlcValueText(alignmentSnapshot.TemplatePose.YMm)}mm, " +
                $"R={FormatPlcValueText(alignmentSnapshot.TemplatePose.AngleDegrees)}deg, " +
                $"R轴中心 X={FormatPlcValueText(alignmentSnapshot.RAxisCenter.XMm)}mm, " +
                $"Y={FormatPlcValueText(alignmentSnapshot.RAxisCenter.YMm)}mm, " +
                $"R+方向={(alignmentSnapshot.RAxisMachineAngleDirection < 0 ? "顺时针" : "逆时针")}, " +
                $"PLC R方向={rCommandDirection}, " +
                $"视觉R纠偏={FormatPlcValueText(alignmentSnapshot.VisionHomeRActionDegrees)}deg, " +
                $"PLC实际R输出={FormatPlcValueText(alignmentSnapshot.HomeRActionDegrees)}deg, " +
                $"参与XY计算的实际旋转={FormatPlcValueText(alignmentSnapshot.PhysicalRotationDegrees)}deg, " +
                $"R后中心 X={FormatPlcValueText(alignmentSnapshot.CenterAfterRotation.XMm)}mm, " +
                $"Y={FormatPlcValueText(alignmentSnapshot.CenterAfterRotation.YMm)}mm, " +
                $"Home2D动作量 X={FormatPlcValueText(alignmentSnapshot.HomeXActionMm)}mm, " +
                $"Y={FormatPlcValueText(alignmentSnapshot.HomeYActionMm)}mm, " +
                $"R={FormatPlcValueText(alignmentSnapshot.HomeRActionDegrees)}deg";
        }

        yield return BuildRAxisCenterUsageJudgment(measurement, outputTransform, alignmentSnapshot);
    }

    public static PlcOutputCommand CalculatePlcOutputCommand(
        InspectionMeasurement measurement,
        PlcOutputTransform outputTransform)
    {
        return PlcInspectionOutputCalculator.CalculateFinalCorrection(measurement, outputTransform);
    }

    public static PlcOutputCommand CalculateRAxisCenterReferenceCommand(
        InspectionMeasurement measurement,
        PlcOutputTransform outputTransform)
    {
        return PlcInspectionOutputCalculator.CalculateFinalCorrection(measurement, outputTransform);
    }

    public static PlcOutputCommand CalculatePreRotationCorrectionCommand(
        InspectionMeasurement measurement,
        PlcOutputTransform outputTransform)
    {
        return PlcInspectionOutputCalculator.CalculatePreRotationCorrection(measurement, outputTransform);
    }

    public static string BuildRAxisCenterUsageJudgment(
        InspectionMeasurement measurement,
        PlcOutputTransform outputTransform,
        XyrAlignmentSnapshot? alignmentSnapshot = null)
    {
        var plcOutput = CalculatePlcOutputCommand(measurement, outputTransform);
        var preRotationOutput = CalculatePreRotationCorrectionCommand(measurement, outputTransform);
        var rAxisState = alignmentSnapshot?.RAxisCenterEnabled == true
            ? "已启用，已参与R后Home2D纠偏参考计算"
            : "未启用或本次无有效R轴中心快照";
        var rAxisDirection = alignmentSnapshot?.RAxisCenterEnabled == true
            ? alignmentSnapshot.RAxisMachineAngleDirection < 0 ? "R+顺时针" : "R+逆时针"
            : "无R方向";
        var rCommandDirection = alignmentSnapshot is not null
            ? alignmentSnapshot.RCommandDirection < 0 ? "PLC R取反并同步重算XY" : "PLC R不取反"
            : "无PLC R方向快照";

        return
            "R轴中心使用判断: " +
            $"内部计算={rAxisState}; R轴方向={rAxisDirection}; " +
            $"R命令方向={rCommandDirection}; " +
            "当前PLC写值=R轴中心后的最终纠偏量，D1002/D1004已采用R轴中心旋转后的XY补偿; " +
            $"当前PLC D1002={FormatPlcValueText(plcOutput.XDeviation)}, " +
            $"D1004={FormatPlcValueText(plcOutput.YDeviation)}, " +
            $"D1006={FormatPlcValueText(plcOutput.RDeviation)}; " +
            $"旋转前旧输出仅参考 D1002={FormatPlcValueText(preRotationOutput.XDeviation)}, " +
            $"D1004={FormatPlcValueText(preRotationOutput.YDeviation)}, " +
            $"D1006={FormatPlcValueText(preRotationOutput.RDeviation)}";
    }

    public static string FormatPlcValueText(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return Math.Abs(rounded) < 0.005
            ? "0"
            : rounded.ToString("0.00", CultureInfo.InvariantCulture);
    }

}
