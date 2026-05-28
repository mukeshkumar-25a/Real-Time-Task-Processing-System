using TaskManager.Core.Entities;

namespace TaskManager.Core.Interfaces;

public interface ITaskRepository
{
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TaskItem?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<TaskItem?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task<TaskItem> CreateAsync(TaskItem task, string? idempotencyKey, CancellationToken cancellationToken = default);
    Task<bool> TryTransitionToProcessingAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task UpdateAsync(TaskItem task, CancellationToken cancellationToken = default);
}
