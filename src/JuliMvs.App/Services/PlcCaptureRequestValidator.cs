using JuliMvs.Core.Inspection;

namespace JuliMvs.App.Services;

internal sealed class PlcCaptureRequestValidator
{
    public PlcCaptureRequestDecision Validate(PlcCaptureRequestState state)
    {
        if (!state.ProductionEnabled)
        {
            return PlcCaptureRequestDecision.Ignore(PlcCaptureRequestBlockReason.Stopped);
        }

        if (state.ChangeoverTemplateRequested)
        {
            return PlcCaptureRequestDecision.WriteError(
                PlcCaptureRequestBlockReason.ChangeoverTemplateRequested,
                NgReason.PlcError);
        }

        if (!state.CameraConnected)
        {
            return PlcCaptureRequestDecision.WriteError(
                PlcCaptureRequestBlockReason.CameraDisconnected,
                NgReason.CameraError);
        }

        if (!state.TemplateLoaded)
        {
            return PlcCaptureRequestDecision.WriteError(
                PlcCaptureRequestBlockReason.TemplateMissing,
                NgReason.PlcError);
        }

        if (!state.BatchCanInspect)
        {
            return PlcCaptureRequestDecision.WriteError(
                PlcCaptureRequestBlockReason.BatchNotReady,
                NgReason.PlcError);
        }

        return PlcCaptureRequestDecision.Proceed;
    }
}

internal sealed record PlcCaptureRequestState(
    bool ProductionEnabled,
    bool ChangeoverTemplateRequested,
    bool CameraConnected,
    bool TemplateLoaded,
    bool BatchCanInspect);

internal sealed record PlcCaptureRequestDecision(
    PlcCaptureRequestAction Action,
    PlcCaptureRequestBlockReason Reason,
    NgReason? NgReason)
{
    public static PlcCaptureRequestDecision Proceed { get; } =
        new(PlcCaptureRequestAction.Proceed, PlcCaptureRequestBlockReason.None, null);

    public static PlcCaptureRequestDecision Ignore(PlcCaptureRequestBlockReason reason)
    {
        return new(PlcCaptureRequestAction.Ignore, reason, null);
    }

    public static PlcCaptureRequestDecision WriteError(PlcCaptureRequestBlockReason reason, NgReason ngReason)
    {
        return new(PlcCaptureRequestAction.WritePlcError, reason, ngReason);
    }
}

internal enum PlcCaptureRequestAction
{
    Proceed,
    Ignore,
    WritePlcError
}

internal enum PlcCaptureRequestBlockReason
{
    None,
    Stopped,
    ChangeoverTemplateRequested,
    CameraDisconnected,
    TemplateMissing,
    BatchNotReady
}
