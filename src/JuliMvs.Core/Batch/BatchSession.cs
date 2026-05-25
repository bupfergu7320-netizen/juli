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
            throw new InvalidOperationException("开始新批次前必须先结束当前批次。");
        }

        BatchNo = batchNo.Trim();
        ProductName = productName.Trim();
        Status = BatchStatus.WaitingFirstArticle;
    }

    public void MarkTemplateCreated()
    {
        if (!CanBuildTemplate)
        {
            throw new InvalidOperationException($"当前批次状态为{FormatStatus(Status)}，不能建立模板。");
        }

        Status = BatchStatus.TemplateCreated;
    }

    public void ConfirmFirstArticle()
    {
        if (!CanConfirmFirstArticle)
        {
            throw new InvalidOperationException($"当前批次状态为{FormatStatus(Status)}，不能确认首件。");
        }

        Status = BatchStatus.Running;
    }

    public void End()
    {
        if (!CanEnd)
        {
            throw new InvalidOperationException($"当前批次状态为{FormatStatus(Status)}，不能结束批次。");
        }

        Status = BatchStatus.Ended;
    }

    private static string FormatStatus(BatchStatus status)
    {
        return status switch
        {
            BatchStatus.NotStarted => "未开始",
            BatchStatus.WaitingFirstArticle => "等待首件",
            BatchStatus.TemplateCreated => "模板已建立",
            BatchStatus.Running => "生产中",
            BatchStatus.Ended => "已结束",
            _ => status.ToString()
        };
    }
}
