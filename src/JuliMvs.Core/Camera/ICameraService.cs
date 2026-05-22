namespace JuliMvs.Core.Camera;

public interface ICameraService : IAsyncDisposable
{
    bool IsOpen { get; }

    IReadOnlyList<CameraDeviceInfo> EnumerateDevices();

    Task OpenAsync(string serialNumberOrIndex, CancellationToken cancellationToken = default);

    Task<CameraFrame> CaptureAsync(int timeoutMilliseconds, CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}
