namespace TaskManager.Core.Entities;

public class TaskItem
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Pending;
    public int Attempts { get; set; }
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
