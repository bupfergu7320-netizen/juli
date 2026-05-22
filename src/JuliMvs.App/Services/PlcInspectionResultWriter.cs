using System.Globalization;
using JuliMvs.Core.Inspection;
using JuliMvs.Plc;

namespace JuliMvs.App.Services;

internal sealed class PlcInspectionResultWriter
{
    private const int SelfClearTimeoutMilliseconds = 3000;
    private const int SelfClearPollMilliseconds = 250;

    public async Task<PlcInspectionResultWriteOutcome> WriteInspectionResultAsync(
        MitsubishiModbusTcpPlcClient? client,
        InspectionResult result,
        PlcOutputTransform outputTransform,
        string outputTransformText,
        Action<string> log)
    {
        if (client is null || !client.IsConnected)
        {
            log("PLC not connected; inspection result was not written.");
            return PlcInspectionResultWriteOutcome.NotConnected;
        }

        await client.WriteInspectionResultAsync(result);
        var measurement = result.Measurement;
        if (result.Decision == InspectionDecision.Ok && measurement is not null)
        {
            var plcOutput = PlcInspectionOutputCalculator.CalculateFinalCorrection(measurement, outputTransform);
            var preRotationOutput = PlcInspectionOutputCalculator.CalculatePreRotationCorrection(measurement, outputTransform);
            log(
                "PLC write completed: final R-axis-center correction output " +
                $"D1002={FormatPlcValueText(plcOutput.XDeviation)}, " +
                $"D1004={FormatPlcValueText(plcOutput.YDeviation)}, " +
                $"D1006={FormatPlcValueText(plcOutput.RDeviation)}, " +
                $"vision deviation current-template X={FormatPlcValueText(measurement.XOffsetMm)}, " +
                $"Y={FormatPlcValueText(measurement.YOffsetMm)}, " +
                $"R={FormatPlcValueText(measurement.AngleOffsetDegrees)}, " +
                $"pre-rotation correction template-current X={FormatPlcValueText(-measurement.XOffsetMm)}, " +
                $"Y={FormatPlcValueText(-measurement.YOffsetMm)}, " +
                $"R={FormatPlcValueText(-measurement.AngleOffsetDegrees)}, " +
                $"R-after reference correction X={FormatPlcValueText(measurement.XCompensationMm)}, " +
                $"Y={FormatPlcValueText(measurement.YCompensationMm)}, " +
                $"R={FormatPlcValueText(measurement.RotationCompensationDegrees)}, " +
                $"old pre-rotation output reference D1002={FormatPlcValueText(preRotationOutput.XDeviation)}, " +
                $"D1004={FormatPlcValueText(preRotationOutput.YDeviation)}, " +
                $"D1006={FormatPlcValueText(preRotationOutput.RDeviation)}, " +
                $"PLC deviation transform {outputTransformText}, " +
                "D1010=1, waiting for PLC to clear D1000.");
            var readbackAfterWrite = await LogPlcOutputReadbackAsync(client, log);
            log("PLC result handed off: waiting for PLC to clear D1000; PC will not actively clear D1000.");
            return BuildOutcome(
                await WaitForPlcTriggerClearAsync(client, log),
                readbackAfterWrite);
        }

        log("PLC write completed: D1010=2, waiting for PLC to clear D1000.");
        var ngReadbackAfterWrite = await LogPlcOutputReadbackAsync(client, log);
        log("PLC result handed off: waiting for PLC to clear D1000; PC will not actively clear D1000.");
        return BuildOutcome(
            await WaitForPlcTriggerClearAsync(client, log),
            ngReadbackAfterWrite);
    }

    private static PlcInspectionResultWriteOutcome BuildOutcome(
        PlcTriggerClearWaitOutcome clearOutcome,
        PlcOutputReadback? readbackAfterWrite)
    {
        return clearOutcome.Result switch
        {
            PlcTriggerClearResult.Cleared => PlcInspectionResultWriteOutcome.Cleared(readbackAfterWrite, clearOutcome.LastReadback),
            PlcTriggerClearResult.Timeout => PlcInspectionResultWriteOutcome.WaitingForReset(readbackAfterWrite, clearOutcome.LastReadback),
            _ => PlcInspectionResultWriteOutcome.WriteCompleted(readbackAfterWrite, clearOutcome.LastReadback)
        };
    }

    private static async Task<PlcTriggerClearWaitOutcome> WaitForPlcTriggerClearAsync(
        MitsubishiModbusTcpPlcClient client,
        Action<string> log)
    {
        log($"PLC self-clear wait: max {SelfClearTimeoutMilliseconds}ms; keeping D1000=1 for PLC result consumption.");

        PlcOutputReadback? lastReadback = null;
        var pollCount = Math.Max(1, SelfClearTimeoutMilliseconds / SelfClearPollMilliseconds);
        for (var i = 0; i < pollCount; i++)
        {
            await Task.Delay(SelfClearPollMilliseconds);
            try
            {
                lastReadback = await client.ReadOutputReadbackAsync();
            }
            catch (Exception ex)
            {
                log($"PLC self-clear readback failed: {ex.Message}");
                return new PlcTriggerClearWaitOutcome(PlcTriggerClearResult.ReadbackFailed, lastReadback);
            }

            if (lastReadback.Trigger == 0)
            {
                log("PLC cleared D1000=0; standard handshake completed and next trigger is allowed.");
                return new PlcTriggerClearWaitOutcome(PlcTriggerClearResult.Cleared, lastReadback);
            }
        }

        var trigger = lastReadback?.Trigger;
        log($"PLC self-clear timed out: D1000={trigger?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; PC will not clear D1000 and is waiting for PLC/manual reset.");
        return new PlcTriggerClearWaitOutcome(PlcTriggerClearResult.Timeout, lastReadback);
    }

    private static async Task<PlcOutputReadback?> LogPlcOutputReadbackAsync(
        MitsubishiModbusTcpPlcClient client,
        Action<string> log)
    {
        try
        {
            var readback = await client.ReadOutputReadbackAsync();
            log(
                "PLC readback check: " +
                $"D1000={readback.Trigger}, " +
                $"D1002={FormatPlcValueText(readback.XDeviation)}, " +
                $"D1004={FormatPlcValueText(readback.YDeviation)}, " +
                $"D1006={FormatPlcValueText(readback.RDeviation)}, " +
                $"D1010={readback.ResultCode}");
            return readback;
        }
        catch (Exception ex)
        {
            log($"PLC readback check failed: {ex.Message}");
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

internal sealed record PlcTriggerClearWaitOutcome(
    PlcTriggerClearResult Result,
    PlcOutputReadback? LastReadback);

internal enum PlcTriggerClearResult
{
    Cleared,
    ReadbackFailed,
    Timeout
}
