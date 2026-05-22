namespace JuliMvs.App.Services;

internal static class PlcStatusMessageFormatter
{
    public static string FormatPollingStatus(PlcPollingStatus status)
    {
        return status switch
        {
            PlcPollingStatus.CaptureRequested => "PLC\u6536\u5230\u89e6\u53d1",
            _ => "PLC\u901a\u8baf\u6b63\u5e38"
        };
    }
}
