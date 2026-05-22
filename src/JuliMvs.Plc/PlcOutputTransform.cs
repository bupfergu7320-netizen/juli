namespace JuliMvs.Plc;

public sealed record PlcOutputCommand(
    double XDeviation,
    double YDeviation,
    double RDeviation);

public sealed record PlcOutputTransform(
    double Xx = 1.0,
    double Xy = 0.0,
    double Yx = 0.0,
    double Yy = 1.0,
    double XBias = 0.0,
    double YBias = 0.0,
    double RScale = 1.0,
    double RBias = 0.0)
{
    public static PlcOutputTransform Identity { get; } = new();

    public PlcOutputTransform ApplyOutputSigns(
        bool invertX,
        bool invertY,
        bool invertRotation)
    {
        var xSign = invertX ? -1.0 : 1.0;
        var ySign = invertY ? -1.0 : 1.0;
        var rSign = invertRotation ? -1.0 : 1.0;
        return this with
        {
            Xx = Xx * xSign,
            Xy = Xy * xSign,
            XBias = XBias * xSign,
            Yx = Yx * ySign,
            Yy = Yy * ySign,
            YBias = YBias * ySign,
            RScale = RScale * rSign,
            RBias = RBias * rSign
        };
    }

    public PlcOutputCommand Apply(
        double xDeviation,
        double yDeviation,
        double rDeviation)
    {
        return new PlcOutputCommand(
            (Xx * xDeviation) + (Xy * yDeviation) + XBias,
            (Yx * xDeviation) + (Yy * yDeviation) + YBias,
            (RScale * rDeviation) + RBias);
    }
}
