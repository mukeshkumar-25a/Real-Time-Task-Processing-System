namespace TaskManager.Core.DTOs;

public class CreateTaskResult
{
    public required TaskResponse Task { get; init; }
    public bool IsReplay { get; init; }
}
