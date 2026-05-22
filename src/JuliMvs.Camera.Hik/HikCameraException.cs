namespace JuliMvs.Camera.Hik;

public sealed class HikCameraException : Exception
{
    public HikCameraException(string message, int errorCode)
        : base($"{message}. MVS error=0x{errorCode:X8}")
    {
        ErrorCode = errorCode;
    }

    public int ErrorCode { get; }
}
