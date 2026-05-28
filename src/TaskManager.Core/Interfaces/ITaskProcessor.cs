namespace TaskManager.Core.Interfaces;

public interface ITaskProcessor
{
    Task ProcessAsync(Guid taskId, CancellationToken cancellationToken = default);
}
