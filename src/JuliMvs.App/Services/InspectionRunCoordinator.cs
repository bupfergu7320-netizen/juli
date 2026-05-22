using JuliMvs.Core.Inspection;
using JuliMvs.Core.Persistence;
using JuliMvs.Core.Vision;
using JuliMvs.Plc;
using JuliMvs.Vision;
using OpenCvSharp;

namespace JuliMvs.App.Services;

internal sealed class InspectionRunCoordinator
{
    private readonly OpenCvVisionService _visionService;
    private readonly IInspectionRepository _repository;
    private readonly InspectionFileStore _fileStore;
    private readonly InspectionReportWriter _reportWriter;

    public InspectionRunCoordinator(
        OpenCvVisionService visionService,
        IInspectionRepository repository,
        InspectionFileStore fileStore,
        InspectionReportWriter reportWriter)
    {
        _visionService = visionService;
        _repository = repository;
        _fileStore = fileStore;
        _reportWriter = reportWriter;
    }

    public async Task<InspectionRunResult> RunAsync(InspectionRunRequest request)
    {
        var output = _visionService.Inspect(
            request.Image,
            request.Template,
            request.Parameters,
            partNo: DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff"),
            rawImagePath: request.RawImagePath);

        var logs = new List<string>();
        var shouldSaveProductionImages = request.WriteToPlc;
        var resultRawImagePath = request.RawImagePath;
        if (shouldSaveProductionImages)
        {
            resultRawImagePath = _fileStore.SaveInspectionRawImage(
                request.Image,
                request.Template.BatchNo,
                output.Result.PartNo);
            logs.Add(output.Result.Decision == InspectionDecision.Ok
                ? $"Production OK raw image saved for replay: {resultRawImagePath}"
                : $"Production non-OK raw image saved for diagnostics: {resultRawImagePath}");
        }

        var saveImages = !request.WriteToPlc || shouldSaveProductionImages;
        var resultImagePath = saveImages
            ? _fileStore.SaveDiagnosticImage(output.DiagnosticImage, request.Template.BatchNo, output.Result.PartNo)
            : null;
        var result = output.Result with
        {
            RawImagePath = resultRawImagePath,
            ResultImagePath = resultImagePath
        };
        await _repository.SaveResultAsync(result);

        if (resultImagePath is not null && shouldSaveProductionImages)
        {
            logs.Add(result.Decision == InspectionDecision.Ok
                ? $"Production OK result image saved for replay: {resultImagePath}"
                : $"Production non-OK result image saved for diagnostics: {resultImagePath}");
        }

        string? reportPath = null;
        string? reportError = null;
        try
        {
            reportPath = _reportWriter.SaveInspectionReport(new InspectionReportContext(
                result,
                request.Template,
                request.Parameters,
                output,
                request.TriggerSource,
                request.WriteToPlc,
                request.PlcConnected,
                request.PlcHost,
                request.PlcPort,
                request.PlcOutputTransform,
                request.FrontBackDebug));
        }
        catch (Exception ex)
        {
            reportError = ex.Message;
        }

        return new InspectionRunResult(
            result,
            output,
            request.Parameters,
            resultImagePath,
            reportPath,
            reportError,
            logs,
            request.FrontBackDebug);
    }
}

internal sealed record InspectionRunRequest(
    Mat Image,
    string? RawImagePath,
    PartTemplate Template,
    VisionParameters Parameters,
    string TriggerSource,
    bool WriteToPlc,
    bool PlcConnected,
    string PlcHost,
    int PlcPort,
    PlcOutputTransform PlcOutputTransform,
    FrontBackDebugResult? FrontBackDebug = null);

internal sealed record InspectionRunResult(
    InspectionResult Result,
    OpenCvInspectionOutput Output,
    VisionParameters Parameters,
    string? ResultImagePath,
    string? ReportPath,
    string? ReportError,
    IReadOnlyList<string> Logs,
    FrontBackDebugResult? FrontBackDebug = null);
