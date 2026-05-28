using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskManager.Core.Interfaces;

namespace TaskManager.Infrastructure.Processing;

public class TaskBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITaskQueue _queue;
    private readonly ILogger<TaskBackgroundService> _logger;

    public TaskBackgroundService(
        IServiceScopeFactory scopeFactory,
        ITaskQueue queue,
        ILogger<TaskBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Task background worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var taskId = await _queue.DequeueAsync(stoppingToken);

                if (taskId is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                    continue;
                }

                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<ITaskProcessor>();
                await processor.ProcessAsync(taskId.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in task background worker");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        _logger.LogInformation("Task background worker stopped");
    }
}
