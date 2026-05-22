namespace JuliMvs.Core.Batch;

public sealed class BatchSession
{
    private BatchSession(string batchNo, string productName, BatchStatus status)
    {
        BatchNo = batchNo;
        ProductName = productName;
        Status = status;
    }

    public string BatchNo { get; private set; }

    public string ProductName { get; private set; }

    public BatchStatus Status { get; private set; }

    public bool CanBuildTemplate => Status is BatchStatus.WaitingFirstArticle or BatchStatus.TemplateCreated;

    public bool CanConfirmFirstArticle => Status == BatchStatus.TemplateCreated;

    public bool CanInspect => Status == BatchStatus.Running;

    public bool CanEnd => Status is BatchStatus.WaitingFirstArticle or BatchStatus.TemplateCreated or BatchStatus.Running;

    public static BatchSession Empty()
    {
        return new BatchSession(string.Empty, string.Empty, BatchStatus.NotStarted);
    }

    public void Start(string batchNo, string productName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);

        if (Status is BatchStatus.WaitingFirstArticle or BatchStatus.TemplateCreated or BatchStatus.Running)
        {
            throw new InvalidOperationException("Current batch must be ended before starting a new batch.");
        }

        BatchNo = batchNo.Trim();
        ProductName = productName.Trim();
        Status = BatchStatus.WaitingFirstArticle;
    }

    public void MarkTemplateCreated()
    {
        if (!CanBuildTemplate)
        {
            throw new InvalidOperationException($"Cannot create template while batch status is {Status}.");
        }

        Status = BatchStatus.TemplateCreated;
    }

    public void ConfirmFirstArticle()
    {
        if (!CanConfirmFirstArticle)
        {
            throw new InvalidOperationException($"Cannot confirm first article while batch status is {Status}.");
        }

        Status = BatchStatus.Running;
    }

    public void End()
    {
        if (!CanEnd)
        {
            throw new InvalidOperationException($"Cannot end batch while batch status is {Status}.");
        }

        Status = BatchStatus.Ended;
    }
}
