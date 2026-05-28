using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskManager.Core.DTOs;
using TaskManager.Core.Entities;
using TaskManager.Core.Interfaces;
using TaskManager.Core.Options;

namespace TaskManager.Infrastructure.Processing;

public class TaskProcessor : ITaskProcessor
{
    private readonly ITaskRepository _repository;
    private readonly ITaskCacheService _cache;
    private readonly ITaskQueue _queue;
    private readonly ITaskNotificationService _notificationService;
    private readonly TaskProcessingOptions _options;
    private readonly ILogger<TaskProcessor> _logger;
    private readonly Random _random = new();

    public TaskProcessor(
        ITaskRepository repository,
        ITaskCacheService cache,
        ITaskQueue queue,
        ITaskNotificationService notificationService,
        IOptions<TaskProcessingOptions> options,
        ILogger<TaskProcessor> logger)
    {
        _repository = repository;
        _cache = cache;
        _queue = queue;
        _notificationService = notificationService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var transitioned = await _repository.TryTransitionToProcessingAsync(taskId, cancellationToken);
        if (!transitioned)
        {
            _logger.LogWarning("Task {TaskId} could not transition to processing (already handled or missing)", taskId);
            return;
        }

        var task = await _repository.GetByIdForUpdateAsync(taskId, cancellationToken);
        if (task is null)
        {
            _logger.LogWarning("Task {TaskId} not found after transition", taskId);
            return;
        }

        task.Attempts++;
        await _repository.UpdateAsync(task, cancellationToken);
        await NotifyAndCacheAsync(task, cancellationToken);

        _logger.LogInformation(
            "Processing task {TaskId}, attempt {Attempt}/{MaxAttempts}",
            taskId, task.Attempts, _options.MaxRetryAttempts);

        var delaySeconds = _random.Next(_options.MinProcessingDelaySeconds, _options.MaxProcessingDelaySeconds + 1);
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

        var shouldFail = _random.NextDouble() < _options.FailureProbability;

        if (shouldFail)
        {
            await HandleFailureAsync(task, cancellationToken);
            return;
        }

        task.Status = TaskItemStatus.Completed;
        task.Result = $"Task '{task.Type}' completed successfully on attempt {task.Attempts}";
        task.ErrorMessage = null;
        await _repository.UpdateAsync(task, cancellationToken);
        await NotifyAndCacheAsync(task, cancellationToken);

        _logger.LogInformation("Task {TaskId} completed successfully", taskId);
    }

    private async Task HandleFailureAsync(TaskItem task, CancellationToken cancellationToken)
    {
        var errorMessage = $"Simulated failure for task '{task.Type}' on attempt {task.Attempts}";

        if (task.Attempts < _options.MaxRetryAttempts)
        {
            task.Status = TaskItemStatus.Pending;
            task.ErrorMessage = errorMessage;
            await _repository.UpdateAsync(task, cancellationToken);
            await _queue.EnqueueAsync(task.Id, cancellationToken);
            await NotifyAndCacheAsync(task, cancellationToken);

            _logger.LogWarning(
                "Task {TaskId} failed attempt {Attempt}, re-queued for retry",
                task.Id, task.Attempts);
            return;
        }

        task.Status = TaskItemStatus.Failed;
        task.ErrorMessage = $"{errorMessage}. Max retries ({_options.MaxRetryAttempts}) exceeded.";
        await _repository.UpdateAsync(task, cancellationToken);
        await NotifyAndCacheAsync(task, cancellationToken);

        _logger.LogError("Task {TaskId} failed permanently after {Attempts} attempts", task.Id, task.Attempts);
    }

    private async Task NotifyAndCacheAsync(TaskItem task, CancellationToken cancellationToken)
    {
        var response = TaskResponse.FromEntity(task);
        await _cache.InvalidateAsync(task.Id, cancellationToken);
        await _cache.SetAsync(response, cancellationToken);
        await _notificationService.NotifyTaskUpdatedAsync(response, cancellationToken);
    }
}
