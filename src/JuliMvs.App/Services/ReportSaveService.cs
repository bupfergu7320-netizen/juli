using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;
using JuliMvs.Vision;

namespace JuliMvs.App.Services;

internal sealed class ReportSaveService
{
    private readonly InspectionReportWriter _inspectionReportWriter;
    private readonly CalibrationFileStore _calibrationFileStore;
    private readonly CalibrationReportThresholds _calibrationThresholds;

    public ReportSaveService(
        InspectionReportWriter inspectionReportWriter,
        CalibrationFileStore calibrationFileStore,
        CalibrationReportThresholds calibrationThresholds)
    {
        _inspectionReportWriter = inspectionReportWriter;
        _calibrationFileStore = calibrationFileStore;
        _calibrationThresholds = calibrationThresholds;
    }

    public string SaveCalibrationReport(
        CalibrationReportSaveContext context,
        Action<string> log)
    {
        var path = _calibrationFileStore.SaveCalibrationReport(new CalibrationReportContext(
            context.CalibrationType,
            context.Calibration,
            context.Quality,
            _calibrationThresholds,
            context.CurrentMachine));
        log($"\u6807\u5b9a\u62a5\u544a\u5df2\u4fdd\u5b58: {path}");
        return path;
    }

    public string? TrySaveTurntableInspectionReport(
        TurntableInspectionReportSaveContext context,
        Action<string> log)
    {
        try
        {
            return _inspectionReportWriter.SaveInspectionReport(new InspectionReportContext(
                context.Result,
                context.Template,
                context.Parameters,
                context.Output,
                context.TriggerSource,
                context.WriteToPlc,
                context.PlcConnected,
                context.PlcHost,
                context.PlcPort,
                context.PlcOutputTransform));
        }
        catch (Exception ex)
        {
            log($"\u68c0\u6d4b\u8bca\u65ad\u62a5\u544a\u4fdd\u5b58\u5931\u8d25: {ex.Message}");
            return null;
        }
    }

    public string? TrySavePassivePlcVerificationReport(
        PassivePlcVerificationReportSaveContext context,
        Action<string> log)
    {
        try
        {
            return _inspectionReportWriter.SavePassivePlcVerificationReport(new PassivePlcVerificationReportContext(
                context.Result,
                context.TriggerSource,
                context.PlcWriteOutcome,
                context.PlcHost,
                context.PlcPort,
                context.PlcOutputTransform));
        }
        catch (Exception ex)
        {
            log($"被动PLC验证报告保存失败: {ex.Message}");
            return null;
        }
    }
}

internal sealed record CalibrationReportSaveContext(
    string CalibrationType,
    object Calibration,
    object Quality,
    CurrentMachineCalibrationSnapshot CurrentMachine);

internal sealed record TurntableInspectionReportSaveContext(
    InspectionResult Result,
    PartTemplate Template,
    VisionParameters Parameters,
    OpenCvInspectionOutput Output,
    string TriggerSource,
    bool WriteToPlc,
    bool PlcConnected,
    string PlcHost,
    int PlcPort,
    PlcOutputTransform PlcOutputTransform);

internal sealed record PassivePlcVerificationReportSaveContext(
    InspectionResult Result,
    string TriggerSource,
    PlcInspectionResultWriteOutcome PlcWriteOutcome,
    string PlcHost,
    int PlcPort,
    PlcOutputTransform PlcOutputTransform);
