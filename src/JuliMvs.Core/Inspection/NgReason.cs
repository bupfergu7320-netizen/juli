namespace JuliMvs.Core.Inspection;

public enum NgReason
{
    None = 0,
    MatchFailed = 1,
    SizeOutOfTolerance = 2,
    ShapeOutOfTolerance = 3,
    HoleOutOfTolerance = 4,
    CameraError = 5,
    PlcError = 6,
    AlgorithmError = 7,
    BackSideDetected = 8
}
