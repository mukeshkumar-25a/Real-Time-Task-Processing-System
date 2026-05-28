namespace TaskManager.Core.Options;

public class TaskProcessingOptions
{
    public const string SectionName = "TaskProcessing";

    public int MinProcessingDelaySeconds { get; set; } = 2;
    public int MaxProcessingDelaySeconds { get; set; } = 5;
    public int MaxRetryAttempts { get; set; } = 3;
    public double FailureProbability { get; set; } = 0.30;
    public int CacheTtlMinSeconds { get; set; } = 30;
    public int CacheTtlMaxSeconds { get; set; } = 60;
}
