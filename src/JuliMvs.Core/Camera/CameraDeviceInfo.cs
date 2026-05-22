namespace JuliMvs.Core.Camera;

public sealed record CameraDeviceInfo(
    int Index,
    string DisplayName,
    string SerialNumber,
    string ModelName,
    string TransportLayer,
    string? IpAddress);
