namespace JuliMvs.Core.Vision;

public sealed record XyrAlignmentSnapshot(
    PartPose2D CurrentPose,
    PartPose2D TemplatePose,
    double XOffsetMm,
    double YOffsetMm,
    double AngleOffsetDegrees,
    bool RAxisCenterEnabled,
    int RAxisMachineAngleDirection,
    MachinePoint RAxisCenter,
    MachinePoint CenterAfterRotation,
    double HomeXActionMm,
    double HomeYActionMm,
    double HomeRActionDegrees,
    Transform2D RAxisRotationTransform,
    Transform2D HomeActionTransform,
    double VisionHomeRActionDegrees,
    int RCommandDirection,
    double PhysicalRotationDegrees);
