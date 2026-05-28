using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using TaskManager.Core.Entities;
using TaskManager.Core.Interfaces;
using TaskManager.Infrastructure.Data;

namespace TaskManager.Infrastructure.Repositories;

public class TaskRepository : ITaskRepository
{
    private const string TaskColumns = """
        Id, Type, Status, Attempts, Result, ErrorMessage, CreatedAt, UpdatedAt, RowVersion
        """;

    private readonly ISqlConnectionFactory _connectionFactory;

    public TaskRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        return await connection.QueryFirstOrDefaultAsync<TaskItem>(
            new CommandDefinition(
                $"SELECT {TaskColumns} FROM Tasks WHERE Id = @Id",
                new { Id = id },
                cancellationToken: cancellationToken));
    }

    public Task<TaskItem?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var tasks = await connection.QueryAsync<TaskItem>(
            new CommandDefinition(
                $"SELECT {TaskColumns} FROM Tasks ORDER BY CreatedAt DESC",
                cancellationToken: cancellationToken));

        return tasks.AsList();
    }

    public async Task<TaskItem?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        return await connection.QueryFirstOrDefaultAsync<TaskItem>(
            new CommandDefinition(
                """
                SELECT t.Id, t.Type, t.Status, t.Attempts, t.Result, t.ErrorMessage,
                       t.CreatedAt, t.UpdatedAt, t.RowVersion
                FROM IdempotencyRecords r
                INNER JOIN Tasks t ON t.Id = r.TaskId
                WHERE r.IdempotencyKey = @IdempotencyKey
                """,
                new { IdempotencyKey = idempotencyKey },
                cancellationToken: cancellationToken));
    }

    public async Task<TaskItem> CreateAsync(TaskItem task, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO Tasks (Id, Type, Status, Attempts, Result, ErrorMessage, CreatedAt, UpdatedAt)
                    VALUES (@Id, @Type, @Status, @Attempts, @Result, @ErrorMessage, @CreatedAt, @UpdatedAt)
                    """,
                    task,
                    transaction,
                    cancellationToken: cancellationToken));

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO IdempotencyRecords (Id, IdempotencyKey, TaskId, CreatedAt)
                        VALUES (@Id, @IdempotencyKey, @TaskId, @CreatedAt)
                        """,
                        new
                        {
                            Id = Guid.NewGuid(),
                            IdempotencyKey = idempotencyKey,
                            TaskId = task.Id,
                            CreatedAt = DateTime.UtcNow
                        },
                        transaction,
                        cancellationToken: cancellationToken));
            }

            transaction.Commit();
            return await GetByIdAsync(task.Id, cancellationToken) ?? task;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            transaction.Rollback();

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var existing = await GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }
            }

            throw;
        }
    }

    public async Task<bool> TryTransitionToProcessingAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var rowsAffected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE Tasks
                SET Status = @ProcessingStatus, UpdatedAt = @UpdatedAt
                WHERE Id = @TaskId AND Status = @PendingStatus
                """,
                new
                {
                    TaskId = taskId,
                    PendingStatus = (int)TaskItemStatus.Pending,
                    ProcessingStatus = (int)TaskItemStatus.Processing,
                    UpdatedAt = DateTime.UtcNow
                },
                cancellationToken: cancellationToken));

        return rowsAffected > 0;
    }

    public async Task UpdateAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        task.UpdatedAt = DateTime.UtcNow;

        using var connection = _connectionFactory.CreateConnection();

        var newRowVersion = await connection.QuerySingleOrDefaultAsync<byte[]>(
            new CommandDefinition(
                """
                UPDATE Tasks
                SET Type = @Type,
                    Status = @Status,
                    Attempts = @Attempts,
                    Result = @Result,
                    ErrorMessage = @ErrorMessage,
                    UpdatedAt = @UpdatedAt
                OUTPUT INSERTED.RowVersion
                WHERE Id = @Id AND RowVersion = @RowVersion
                """,
                task,
                cancellationToken: cancellationToken));

        if (newRowVersion is null)
        {
            throw new InvalidOperationException($"Concurrency conflict updating task {task.Id}.");
        }

        task.RowVersion = newRowVersion;
    }
}
