namespace JuliMvs.App.Services;

internal static class PlcCaptureRequestMessageFormatter
{
    public static PlcCaptureRequestMessages Format(PlcCaptureRequestBlockReason reason)
    {
        return reason switch
        {
            PlcCaptureRequestBlockReason.Stopped => new PlcCaptureRequestMessages(
                LogMessage: "\u5f53\u524d\u4e3a\u505c\u6b62\u72b6\u6001\uff0c\u5df2\u5ffd\u7565PLC\u5b9a\u4f4d\u89e6\u53d1D1000=1\uff0c\u672a\u62cd\u7167\u3001\u672a\u5199\u7ed3\u679c\uff1b\u4e0a\u4f4d\u673a\u5c06\u6e05D1000=0\u3002",
                UserMessage: "\u5f53\u524d\u4e3a\u505c\u6b62\u72b6\u6001\uff0cPLC\u89e6\u53d1\u5df2\u5ffd\u7565\u3002\u70b9\u51fb\u8fd0\u884c\u540e\u624d\u4f1a\u81ea\u52a8\u68c0\u6d4b\u3002",
                PlcErrorMessage: null),
            PlcCaptureRequestBlockReason.ChangeoverTemplateRequested => new PlcCaptureRequestMessages(
                LogMessage: "\u5f53\u524d\u5904\u4e8e\u6362\u578b\u8c03\u8bd5\u6a21\u5f0f\uff0c\u5df2\u5ffd\u7565PLC\u89e6\u53d1D1000=1\u3002\u6807\u51c6\u4f4d/\u6a21\u677f\u9700\u5728\u4e0a\u4f4d\u673a\u70b9\u51fb\u201c\u62cd\u7167\u8bbe\u6807\u51c6\u4f4d/\u6a21\u677f\u201d\u3002",
                UserMessage: "\u6362\u578b\u8c03\u8bd5\u6a21\u5f0f\u4e0d\u4f7f\u7528PLC\u89e6\u53d1\u62cd\u7167\uff0c\u8bf7\u70b9\u51fb\u6362\u578b\u7a97\u53e3\u91cc\u7684\u201c\u62cd\u7167\u8bbe\u6807\u51c6\u4f4d/\u6a21\u677f\u201d\u3002",
                PlcErrorMessage: "PLC\u89e6\u53d1\u68c0\u6d4b\uff0c\u4f46\u5f53\u524d\u5904\u4e8e\u6362\u578b\u8c03\u8bd5\u6a21\u5f0f\u3002"),
            PlcCaptureRequestBlockReason.CameraDisconnected => new PlcCaptureRequestMessages(
                LogMessage: null,
                UserMessage: null,
                PlcErrorMessage: "PLC\u89e6\u53d1\u68c0\u6d4b\uff0c\u4f46\u76f8\u673a\u672a\u8fde\u63a5\u3002"),
            PlcCaptureRequestBlockReason.TemplateMissing => new PlcCaptureRequestMessages(
                LogMessage: null,
                UserMessage: null,
                PlcErrorMessage: "PLC\u89e6\u53d1\u68c0\u6d4b\uff0c\u4f46\u5f53\u524d\u578b\u53f7\u672a\u52a0\u8f7d\u6807\u51c6\u4f4d/\u6a21\u677f\u3002\u8bf7\u5148\u70b9\u201c\u6362\u578b\u201d\uff0c\u7531\u4e0a\u4f4d\u673a\u62cd\u7167\u91cd\u65b0\u5efa\u7acb\u6807\u51c6\u4f4d/\u6a21\u677f\u3002"),
            PlcCaptureRequestBlockReason.BatchNotReady => new PlcCaptureRequestMessages(
                LogMessage: null,
                UserMessage: null,
                PlcErrorMessage: "PLC\u89e6\u53d1\u68c0\u6d4b\uff0c\u4f46\u6279\u6b21\u672a\u8fdb\u5165\u751f\u4ea7\u72b6\u6001\u3002\u8bf7\u5148\u70b9\u51fb\u201c\u6362\u578b\u201d\u52a0\u8f7d\u5df2\u6709\u6807\u51c6\u4f4d/\u6a21\u677f\u751f\u4ea7\uff0c\u6216\u91cd\u65b0\u5efa\u7acb\u6807\u51c6\u4f4d/\u6a21\u677f\u3002"),
            _ => new PlcCaptureRequestMessages(
                LogMessage: null,
                UserMessage: null,
                PlcErrorMessage: "PLC\u89e6\u53d1\u68c0\u6d4b\u5931\u8d25\u3002")
        };
    }
}

internal sealed record PlcCaptureRequestMessages(
    string? LogMessage,
    string? UserMessage,
    string? PlcErrorMessage);
