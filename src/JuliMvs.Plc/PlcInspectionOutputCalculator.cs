using JuliMvs.Core.Inspection;

namespace JuliMvs.Plc;

public static class PlcInspectionOutputCalculator
{
    public static PlcOutputCommand CalculateFinalCorrection(
        InspectionMeasurement measurement,
        PlcOutputTransform outputTransform)
    {
        return outputTransform.Apply(
            measurement.XCompensationMm,
            measurement.YCompensationMm,
            measurement.RotationCompensationDegrees);
    }

    public static PlcOutputCommand CalculatePreRotationCorrection(
        InspectionMeasurement measurement,
        PlcOutputTransform outputTransform)
    {
        return outputTransform.Apply(
            -measurement.XOffsetMm,
            -measurement.YOffsetMm,
            measurement.RotationCompensationDegrees);
    }
}
