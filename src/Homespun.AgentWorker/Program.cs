using Homespun.AgentWorker.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Register services
builder.Services.AddSingleton<WorkerSessionService>();

// Add controllers
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Add Swagger for development
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health check endpoint
app.MapHealthChecks("/api/health");

// Map controllers
app.MapControllers();

// Log startup
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Homespun Agent Worker starting on {Urls}", app.Urls);

app.Run();
