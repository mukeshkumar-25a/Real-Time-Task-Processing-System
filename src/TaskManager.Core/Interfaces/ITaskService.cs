using TaskManager.Core.DTOs;

namespace TaskManager.Core.Interfaces;

public interface ITaskService
{
    Task<CreateTaskResult> CreateTaskAsync(CreateTaskRequest request, string? idempotencyKey, CancellationToken cancellationToken = default);
    Task<TaskResponse?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskResponse>> ListTasksAsync(CancellationToken cancellationToken = default);
}
