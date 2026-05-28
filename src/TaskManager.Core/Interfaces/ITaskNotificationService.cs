using TaskManager.Core.DTOs;

namespace TaskManager.Core.Interfaces;

public interface ITaskNotificationService
{
    Task NotifyTaskUpdatedAsync(TaskResponse task, CancellationToken cancellationToken = default);
}
