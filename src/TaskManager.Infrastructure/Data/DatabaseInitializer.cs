using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TaskManager.Infrastructure.Data;

public class DatabaseInitializer : IDatabaseInitializer
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        ISqlConnectionFactory connectionFactory,
        IConfiguration configuration,
        ILogger<DatabaseInitializer> logger)
    {
        _connectionFactory = connectionFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured.");

        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("Initial Catalog (database name) is required in DefaultConnection.");
        }

        builder.InitialCatalog = "master";
        await using (var masterConnection = new SqlConnection(builder.ConnectionString))
        {
            await masterConnection.OpenAsync(cancellationToken);
            await masterConnection.ExecuteAsync(new CommandDefinition(
                $"""
                 IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @DatabaseName)
                 BEGIN
                     CREATE DATABASE [{databaseName.Replace("]", "]]")}];
                 END
                 """,
                new { DatabaseName = databaseName },
                cancellationToken: cancellationToken));
        }

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        const string tasksSql = """
            IF OBJECT_ID(N'dbo.Tasks', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Tasks
                (
                    Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tasks PRIMARY KEY,
                    Type         NVARCHAR(100)    NOT NULL,
                    Status       INT              NOT NULL,
                    Attempts     INT              NOT NULL CONSTRAINT DF_Tasks_Attempts DEFAULT (0),
                    Result       NVARCHAR(2000)   NULL,
                    ErrorMessage NVARCHAR(2000)   NULL,
                    CreatedAt    DATETIME2        NOT NULL,
                    UpdatedAt    DATETIME2        NULL,
                    RowVersion   ROWVERSION       NOT NULL
                );

                CREATE INDEX IX_Tasks_Status ON dbo.Tasks (Status);
                CREATE INDEX IX_Tasks_CreatedAt ON dbo.Tasks (CreatedAt DESC);
            END
            """;

        const string idempotencySql = """
            IF OBJECT_ID(N'dbo.IdempotencyRecords', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.IdempotencyRecords
                (
                    Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_IdempotencyRecords PRIMARY KEY,
                    IdempotencyKey NVARCHAR(256)    NOT NULL,
                    TaskId         UNIQUEIDENTIFIER NOT NULL,
                    CreatedAt      DATETIME2        NOT NULL,
                    CONSTRAINT FK_IdempotencyRecords_Tasks FOREIGN KEY (TaskId)
                        REFERENCES dbo.Tasks (Id) ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IX_IdempotencyRecords_IdempotencyKey
                    ON dbo.IdempotencyRecords (IdempotencyKey);
            END
            """;

        await connection.ExecuteAsync(new CommandDefinition(tasksSql, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(idempotencySql, cancellationToken: cancellationToken));

        _logger.LogInformation("Database '{DatabaseName}' initialized", databaseName);
    }
}
