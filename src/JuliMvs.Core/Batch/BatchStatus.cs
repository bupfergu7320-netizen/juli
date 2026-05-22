namespace JuliMvs.Core.Batch;

public enum BatchStatus
{
    NotStarted = 0,
    WaitingFirstArticle = 1,
    TemplateCreated = 2,
    Running = 3,
    Ended = 4
}
