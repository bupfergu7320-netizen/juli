using JuliMvs.Core;

namespace JuliMvs.Core.Vision;

public static class XyrAlignmentSolver
{
    public static XyrAlignmentSnapshot Solve(
        PartPose2D currentPose,
        PartPose2D templatePose,
        RAxisCenterCalibration rAxisCenterCalibration,
        int rCommandDirection = 1,
        bool allowFullRotation = false)
    {
        ArgumentNullException.ThrowIfNull(currentPose);
        ArgumentNullException.ThrowIfNull(templatePose);
        ArgumentNullException.ThrowIfNull(rAxisCenterCalibration);

        var xOffsetMm = currentPose.XMm - templatePose.XMm;
        var yOffsetMm = currentPose.YMm - templatePose.YMm;
        var angleOffsetDegrees = allowFullRotation
            ? AngleMath.NormalizeDeltaDegrees360(currentPose.AngleDegrees, templatePose.AngleDegrees)
            : AngleMath.NormalizeDeltaDegrees(currentPose.AngleDegrees, templatePose.AngleDegrees);
        rCommandDirection = rCommandDirection < 0 ? -1 : 1;
        var visionHomeRActionDegrees = -angleOffsetDegrees;
        var homeRActionDegrees = rCommandDirection * visionHomeRActionDegrees;
        var rAxisCenterEnabled = rAxisCenterCalibration.Enabled;
        var rAxisMachineAngleDirection = rAxisCenterEnabled
            ? rAxisCenterCalibration.GetMachineAngleDirection()
            : 1;
        var rAxisCenter = rAxisCenterEnabled
            ? rAxisCenterCalibration.Center
            : currentPose.Center;
        var physicalRotationDegrees = rAxisMachineAngleDirection * homeRActionDegrees;
        var rotationTransform = rAxisCenterEnabled
            ? Transform2D.RotateAround(rAxisCenter, physicalRotationDegrees)
            : Transform2D.Identity;
        var centerAfterRotation = rotationTransform.TransformPoint(currentPose.Center);
        var homeXActionMm = templatePose.XMm - centerAfterRotation.XMm;
        var homeYActionMm = templatePose.YMm - centerAfterRotation.YMm;
        var homeActionTransform = rotationTransform.Then(
            Transform2D.Translate(homeXActionMm, homeYActionMm));

        return new XyrAlignmentSnapshot(
            currentPose,
            templatePose,
            xOffsetMm,
            yOffsetMm,
            angleOffsetDegrees,
            rAxisCenterEnabled,
            rAxisMachineAngleDirection,
            rAxisCenter,
            centerAfterRotation,
            homeXActionMm,
            homeYActionMm,
            homeRActionDegrees,
            rotationTransform,
            homeActionTransform,
            visionHomeRActionDegrees,
            rCommandDirection,
            physicalRotationDegrees);
    }
}
