using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using TaskManager.Core.DTOs;
using TaskManager.Core.Interfaces;
using TaskManager.Core.Options;

namespace TaskManager.Infrastructure.Caching;

public class TaskCacheService : ITaskCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDistributedCache _cache;
    private readonly TaskProcessingOptions _options;
    private readonly Random _random = new();

    public TaskCacheService(IDistributedCache cache, IOptions<TaskProcessingOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public async Task<TaskResponse?> GetAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetStringAsync(GetCacheKey(taskId), cancellationToken);
        return cached is null ? null : JsonSerializer.Deserialize<TaskResponse>(cached, JsonOptions);
    }

    public async Task SetAsync(TaskResponse task, CancellationToken cancellationToken = default)
    {
        var ttlSeconds = _random.Next(_options.CacheTtlMinSeconds, _options.CacheTtlMaxSeconds + 1);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttlSeconds)
        };

        var json = JsonSerializer.Serialize(task, JsonOptions);
        await _cache.SetStringAsync(GetCacheKey(task.Id), json, options, cancellationToken);
    }

    public Task InvalidateAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveAsync(GetCacheKey(taskId), cancellationToken);
    }

    private static string GetCacheKey(Guid taskId) => $"task:{taskId}";
}
