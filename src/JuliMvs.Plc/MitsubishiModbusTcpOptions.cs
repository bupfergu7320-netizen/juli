namespace JuliMvs.Plc;

public sealed record MitsubishiModbusTcpOptions(
    string Host = "192.168.3.40",
    int Port = 502,
    byte UnitId = 1,
    int TriggerAddress = 1000,
    int XCompensationAddress = 1002,
    int YCompensationAddress = 1004,
    int RCompensationAddress = 1006,
    int ResultAddress = 1010,
    int TargetProductionAddress = 1020,
    int CurrentProductionAddress = 1022,
    int ProductModelAddress = 1030,
    int ProductModelRegisterCount = 10,
    bool SwapFloatWords = true,
    PlcOutputTransform? OutputTransform = null,
    int ConnectTimeoutMilliseconds = 3000,
    int ReadWriteTimeoutMilliseconds = 3000)
{
    public static MitsubishiModbusTcpOptions Default { get; } = new();
}
