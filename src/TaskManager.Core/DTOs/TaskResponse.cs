using TaskManager.Core.Entities;

namespace TaskManager.Core.DTOs;

public class TaskResponse
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }

    public static TaskResponse FromEntity(TaskItem task) => new()
    {
        Id = task.Id,
        Type = task.Type,
        Status = task.Status.ToString().ToLowerInvariant(),
        Attempts = task.Attempts,
        Result = task.Result,
        ErrorMessage = task.ErrorMessage,
        CreatedAt = task.CreatedAt
    };
}
