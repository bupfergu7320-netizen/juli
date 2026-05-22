using JuliMvs.Core.Camera;
using JuliMvs.Core.Vision;

namespace JuliMvs.Core.Persistence;

public sealed record ProductRecipe
{
    public VisionParameters VisionParameters { get; init; } = VisionParameters.Default;

    public CameraAcquisitionSettings CameraSettings { get; init; } = CameraAcquisitionSettings.Default;
}
