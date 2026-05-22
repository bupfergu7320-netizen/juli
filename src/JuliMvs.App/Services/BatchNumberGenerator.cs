namespace JuliMvs.App.Services;

internal static class BatchNumberGenerator
{
    public static string GenerateDefaultBatchNo()
    {
        return $"BATCH-{DateTime.Now:yyyyMMdd-HHmmss}";
    }
}
