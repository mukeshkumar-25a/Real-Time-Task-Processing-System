using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TaskManager.Core.Interfaces;
using TaskManager.Core.Options;
using TaskManager.Core.Services;
using TaskManager.Infrastructure.Caching;
using TaskManager.Infrastructure.Data;
using TaskManager.Infrastructure.Processing;
using TaskManager.Infrastructure.Queue;
using TaskManager.Infrastructure.Repositories;

namespace TaskManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TaskProcessingOptions>(configuration.GetSection(TaskProcessingOptions.SectionName));

        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

        var redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Redis connection string is not configured.");

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "TaskManager:";
        });

        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ITaskCacheService, TaskCacheService>();
        services.AddSingleton<ITaskQueue, RedisTaskQueue>();
        services.AddScoped<ITaskProcessor, TaskProcessor>();
        services.AddScoped<ITaskService, TaskService>();

        services.AddHostedService<TaskBackgroundService>();

        return services;
    }
}
