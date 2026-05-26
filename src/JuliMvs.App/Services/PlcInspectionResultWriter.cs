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
            log("PLC结果已交接：D1010=1已写入；D1000已在拍照完成后清零。");
            return PlcInspectionResultWriteOutcome.WriteCompleted(readbackAfterWrite, readbackAfterWrite);
        }

        log("PLC写入完成：D1010=2。");
        var ngReadbackAfterWrite = await LogPlcOutputReadbackAsync(client, log);
        log("PLC结果已交接：D1010=2已写入；D1000已在拍照完成后清零。");
        return PlcInspectionResultWriteOutcome.WriteCompleted(ngReadbackAfterWrite, ngReadbackAfterWrite);
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
    bool ShouldSetPlcStatusNormal,
    PlcOutputReadback? ReadbackAfterWrite,
    PlcOutputReadback? LastReadbackBeforeReturn)
{
    public static PlcInspectionResultWriteOutcome NotConnected { get; } =
        new(
            IsConnected: false,
            ShouldSetPlcStatusNormal: false,
            ReadbackAfterWrite: null,
            LastReadbackBeforeReturn: null);

    public static PlcInspectionResultWriteOutcome WriteCompleted(
        PlcOutputReadback? readbackAfterWrite,
        PlcOutputReadback? lastReadbackBeforeReturn)
    {
        return new(
            IsConnected: true,
            ShouldSetPlcStatusNormal: true,
            readbackAfterWrite,
            lastReadbackBeforeReturn);
    }
}
