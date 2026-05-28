namespace TaskManager.Core.Interfaces;

public interface ITaskQueue
{
    Task EnqueueAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task<Guid?> DequeueAsync(CancellationToken cancellationToken = default);
}
