using StackExchange.Redis;
using TaskManager.Core.Interfaces;

namespace TaskManager.Infrastructure.Queue;

public class RedisTaskQueue : ITaskQueue
{
    private const string QueueKey = "taskmanager:queue";

    private readonly IConnectionMultiplexer _redis;

    public RedisTaskQueue(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task EnqueueAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        await db.ListLeftPushAsync(QueueKey, taskId.ToString());
    }

    public async Task<Guid?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var result = await db.ListRightPopAsync(QueueKey);

        if (result.IsNullOrEmpty)
        {
            return null;
        }

        return Guid.TryParse(result.ToString(), out var taskId) ? taskId : null;
    }
}
