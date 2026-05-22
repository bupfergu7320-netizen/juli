namespace JuliMvs.Core.Vision;

public readonly record struct ImageRoi(int X, int Y, int Width, int Height)
{
    public static ImageRoi Empty { get; } = new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;
}
