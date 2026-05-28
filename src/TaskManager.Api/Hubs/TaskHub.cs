using Microsoft.AspNetCore.SignalR;
using TaskManager.Core.DTOs;
using TaskManager.Core.Interfaces;

namespace TaskManager.Api.Hubs;

public class TaskHub : Hub
{
    public async Task SubscribeToTask(Guid taskId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(taskId));
    }

    public async Task UnsubscribeFromTask(Guid taskId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(taskId));
    }

    public static string GetGroupName(Guid taskId) => $"task-{taskId}";
}

public class SignalRTaskNotificationService : ITaskNotificationService
{
    private readonly IHubContext<TaskHub> _hubContext;

    public SignalRTaskNotificationService(IHubContext<TaskHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyTaskUpdatedAsync(TaskResponse task, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients
            .Group(TaskHub.GetGroupName(task.Id))
            .SendAsync("TaskUpdated", task, cancellationToken);
    }
}
