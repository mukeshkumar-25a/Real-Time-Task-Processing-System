using Microsoft.Extensions.Logging;
using TaskManager.Core.DTOs;
using TaskManager.Core.Entities;
using TaskManager.Core.Interfaces;

namespace TaskManager.Core.Services;

public class TaskService : ITaskService
{
    private readonly ITaskRepository _repository;
    private readonly ITaskCacheService _cache;
    private readonly ITaskQueue _queue;
    private readonly ILogger<TaskService> _logger;

    public TaskService(
        ITaskRepository repository,
        ITaskCacheService cache,
        ITaskQueue queue,
        ILogger<TaskService> logger)
    {
        _repository = repository;
        _cache = cache;
        _queue = queue;
        _logger = logger;
    }

    public async Task<CreateTaskResult> CreateTaskAsync(
        CreateTaskRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _repository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing task {TaskId} for idempotency key {Key}", existing.Id, idempotencyKey);
                return new CreateTaskResult
                {
                    Task = TaskResponse.FromEntity(existing),
                    IsReplay = true
                };
            }
        }

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Type = request.Type,
            Status = TaskItemStatus.Pending,
            Attempts = 0,
            CreatedAt = DateTime.UtcNow
        };

        task = await _repository.CreateAsync(task, idempotencyKey, cancellationToken);
        await _queue.EnqueueAsync(task.Id, cancellationToken);

        var response = TaskResponse.FromEntity(task);
        await _cache.SetAsync(response, cancellationToken);

        _logger.LogInformation("Created task {TaskId} of type {Type}", task.Id, task.Type);
        return new CreateTaskResult
        {
            Task = response,
            IsReplay = false
        };
    }

    public async Task<TaskResponse?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetAsync(id, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var task = await _repository.GetByIdAsync(id, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var response = TaskResponse.FromEntity(task);
        await _cache.SetAsync(response, cancellationToken);
        return response;
    }

    public async Task<IReadOnlyList<TaskResponse>> ListTasksAsync(CancellationToken cancellationToken = default)
    {
        var tasks = await _repository.GetAllAsync(cancellationToken);
        return tasks.Select(TaskResponse.FromEntity).ToList();
    }
}
