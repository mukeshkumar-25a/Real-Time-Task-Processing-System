using TaskManager.Core.DTOs;

namespace TaskManager.Core.Interfaces;

public interface ITaskCacheService
{
    Task<TaskResponse?> GetAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task SetAsync(TaskResponse task, CancellationToken cancellationToken = default);
    Task InvalidateAsync(Guid taskId, CancellationToken cancellationToken = default);
}
