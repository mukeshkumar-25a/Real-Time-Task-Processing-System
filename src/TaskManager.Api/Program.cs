using TaskManager.Api.Hubs;
using TaskManager.Core.Interfaces;
using TaskManager.Infrastructure;
using TaskManager.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Task Manager API",
        Version = "v1",
        Description = "Task submission, tracking, retries, and real-time updates"
    });
});
builder.Services.AddSignalR();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<ITaskNotificationService, SignalRTaskNotificationService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

await InitializeDatabaseWithRetryAsync(app.Services);

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Docker"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Task Manager API v1");
        options.RoutePrefix = "swagger";
    });
}

if (!app.Environment.IsEnvironment("Docker"))
{
    app.UseHttpsRedirection();
}
app.MapControllers();
app.MapHub<TaskHub>("/hubs/tasks");

app.Run();

static async Task InitializeDatabaseWithRetryAsync(IServiceProvider services)
{
    const int maxAttempts = 15;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            var initializer = services.GetRequiredService<IDatabaseInitializer>();
            await initializer.InitializeAsync();
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
            logger.LogWarning(ex, "Database initialization attempt {Attempt}/{MaxAttempts} failed, retrying...", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}
