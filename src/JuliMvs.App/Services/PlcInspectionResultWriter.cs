using System.Globalization;
using JuliMvs.Core.Inspection;
using JuliMvs.Plc;

namespace JuliMvs.App.Services;

internal sealed class PlcInspectionResultWriter
{
    public async Task<PlcInspectionResultWriteOutcome> WriteInspectionResultAsync(
        MitsubishiModbusTcpPlcClient? client,
        InspectionResult result,
        PlcOutputTransform outputTransform,
        string outputTransformText,
        Action<string> log)
    {
        if (client is null || !client.IsConnected)
        {
            log("PLC未连接，检测结果未写入。");
            return PlcInspectionResultWriteOutcome.NotConnected;
        }

        await client.WriteInspectionResultAsync(result);
        var measurement = result.Measurement;
        if (result.Decision == InspectionDecision.Ok && measurement is not null)
        {
            var plcOutput = PlcInspectionOutputCalculator.CalculateFinalCorrection(measurement, outputTransform);
            var preRotationOutput = PlcInspectionOutputCalculator.CalculatePreRotationCorrection(measurement, outputTransform);
            log(
                "PLC写入完成：R轴中心后的最终纠偏输出 " +
                $"D1002={FormatPlcValueText(plcOutput.XDeviation)}, " +
                $"D1004={FormatPlcValueText(plcOutput.YDeviation)}, " +
                $"D1006={FormatPlcValueText(plcOutput.RDeviation)}, " +
                $"视觉偏差(当前-模板) X={FormatPlcValueText(measurement.XOffsetMm)}, " +
                $"Y={FormatPlcValueText(measurement.YOffsetMm)}, " +
                $"R={FormatPlcValueText(measurement.AngleOffsetDegrees)}, " +
                $"旋转前纠偏量(模板-当前) X={FormatPlcValueText(-measurement.XOffsetMm)}, " +
                $"Y={FormatPlcValueText(-measurement.YOffsetMm)}, " +
                $"R={FormatPlcValueText(-measurement.AngleOffsetDegrees)}, " +
                $"R轴中心后纠偏量 X={FormatPlcValueText(measurement.XCompensationMm)}, " +
                $"Y={FormatPlcValueText(measurement.YCompensationMm)}, " +
                $"R={FormatPlcValueText(measurement.RotationCompensationDegrees)}, " +
                $"旋转前旧输出仅参考 D1002={FormatPlcValueText(preRotationOutput.XDeviation)}, " +
                $"D1004={FormatPlcValueText(preRotationOutput.YDeviation)}, " +
                $"D1006={FormatPlcValueText(preRotationOutput.RDeviation)}, " +
                $"PLC偏差输出坐标系 {outputTransformText}, " +
                "D1010=1.");
            var readbackAfterWrite = await LogPlcOutputReadbackAsync(client, log);
            log("PLC结果已交接：上位机写入D1010后将清D1000=0。");
            return await ClearTriggerAndBuildOutcomeAsync(client, readbackAfterWrite, log);
        }

        log("PLC写入完成：D1010=2。");
        var ngReadbackAfterWrite = await LogPlcOutputReadbackAsync(client, log);
        log("PLC结果已交接：上位机写入D1010后将清D1000=0。");
        return await ClearTriggerAndBuildOutcomeAsync(client, ngReadbackAfterWrite, log);
    }

    private static async Task<PlcInspectionResultWriteOutcome> ClearTriggerAndBuildOutcomeAsync(
        MitsubishiModbusTcpPlcClient client,
        PlcOutputReadback? readbackAfterWrite,
        Action<string> log)
    {
        try
        {
            await client.ClearTriggerAsync();
            log("上位机已清PLC触发位：D1000=0。");
            var readbackAfterClear = await LogPlcOutputReadbackAsync(client, log);

            if (readbackAfterClear?.Trigger == 0)
            {
                log("PLC触发位清除确认：D1000=0；标准握手完成，允许下一次触发。");
                return PlcInspectionResultWriteOutcome.Cleared(readbackAfterWrite, readbackAfterClear);
            }

            log($"PLC触发位清除后读回异常：D1000={readbackAfterClear?.Trigger.ToString(CultureInfo.InvariantCulture) ?? "未知"}。");
            return PlcInspectionResultWriteOutcome.WriteCompleted(readbackAfterWrite, readbackAfterClear);
        }
        catch (Exception ex)
        {
            log($"上位机清D1000失败：{ex.Message}");
            throw;
        }
    }

    private static async Task<PlcOutputReadback?> LogPlcOutputReadbackAsync(
        MitsubishiModbusTcpPlcClient client,
        Action<string> log)
    {
        try
        {
            var readback = await client.ReadOutputReadbackAsync();
            log(
                "PLC读回检查：" +
                $"D1000={readback.Trigger}, " +
                $"D1002={FormatPlcValueText(readback.XDeviation)}, " +
                $"D1004={FormatPlcValueText(readback.YDeviation)}, " +
                $"D1006={FormatPlcValueText(readback.RDeviation)}, " +
                $"D1010={readback.ResultCode}");
            return readback;
        }
        catch (Exception ex)
        {
            log($"PLC读回检查失败：{ex.Message}");
            return null;
        }
    }

    private static string FormatPlcValueText(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return Math.Abs(rounded) < 0.005
            ? "0"
            : rounded.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

internal sealed record PlcInspectionResultWriteOutcome(
    bool IsConnected,
    bool TriggerCleared,
    bool ShouldSetPlcStatusNormal,
    bool ShouldSetPlcStatusWaitingReset,
    PlcOutputReadback? ReadbackAfterWrite,
    PlcOutputReadback? LastReadbackBeforeReturn)
{
    public static PlcInspectionResultWriteOutcome NotConnected { get; } =
        new(
            IsConnected: false,
            TriggerCleared: false,
            ShouldSetPlcStatusNormal: false,
            ShouldSetPlcStatusWaitingReset: false,
            ReadbackAfterWrite: null,
            LastReadbackBeforeReturn: null);

    public static PlcInspectionResultWriteOutcome WriteCompleted(
        PlcOutputReadback? readbackAfterWrite,
        PlcOutputReadback? lastReadbackBeforeReturn)
    {
        return new(
            IsConnected: true,
            TriggerCleared: false,
            ShouldSetPlcStatusNormal: false,
            ShouldSetPlcStatusWaitingReset: false,
            readbackAfterWrite,
            lastReadbackBeforeReturn);
    }

    public static PlcInspectionResultWriteOutcome Cleared(
        PlcOutputReadback? readbackAfterWrite,
        PlcOutputReadback? lastReadbackBeforeReturn)
    {
        return new(
            IsConnected: true,
            TriggerCleared: true,
            ShouldSetPlcStatusNormal: true,
            ShouldSetPlcStatusWaitingReset: false,
            readbackAfterWrite,
            lastReadbackBeforeReturn);
    }

    public static PlcInspectionResultWriteOutcome WaitingForReset(
        PlcOutputReadback? readbackAfterWrite,
        PlcOutputReadback? lastReadbackBeforeReturn)
    {
        return new(
            IsConnected: true,
            TriggerCleared: false,
            ShouldSetPlcStatusNormal: false,
            ShouldSetPlcStatusWaitingReset: true,
            readbackAfterWrite,
            lastReadbackBeforeReturn);
    }
}
