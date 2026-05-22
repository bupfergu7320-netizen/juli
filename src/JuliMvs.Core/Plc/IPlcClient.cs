using JuliMvs.Core.Inspection;

namespace JuliMvs.Core.Plc;

public interface IPlcClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<PlcSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default);

    Task WriteInspectionResultAsync(InspectionResult result, CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}
