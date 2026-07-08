using LogMonitoring.Instrumentation;

var builder = WebApplication.CreateBuilder(args);

// Add OpenTelemetry instrumentation
builder.Services.AddLogMonitoringTelemetry("SampleApp", "1.0.0");

// Configure logging with OpenTelemetry
builder.Logging.AddLogMonitoringLogging(new OtelOptions
{
    ServiceName = "SampleApp",
    ServiceVersion = "1.0.0",
    OtlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317",
    EnableOtlpExporter = true
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Add correlation middleware
app.UseCorrelation();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Get custom metrics instance
var metrics = app.Services.GetRequiredService<CustomMetrics>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// Health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("Health")
    .WithOpenApi();

// Sample endpoint that generates telemetry
app.MapGet("/api/sample", () =>
{
    using var scope = metrics.MeasureRequestDuration("/api/sample", "GET");
    
    logger.LogInformation("Processing sample request");
    
    // Simulate some work
    Thread.Sleep(Random.Shared.Next(10, 100));
    
    scope.SetStatusCode(200);
    return Results.Ok(new { message = "Hello from Sample App!", timestamp = DateTime.UtcNow });
})
.WithName("Sample")
.WithOpenApi();

// Sample endpoint that might fail
app.MapGet("/api/random", () =>
{
    using var scope = metrics.MeasureRequestDuration("/api/random", "GET");
    
    var value = Random.Shared.Next(1, 100);
    
    if (value < 20) // 20% error rate
    {
        logger.LogError("Random endpoint failed with value {Value}", value);
        scope.SetStatusCode(500);
        return Results.Problem("Random failure occurred", statusCode: 500);
    }
    
    logger.LogInformation("Random endpoint succeeded with value {Value}", value);
    scope.SetStatusCode(200);
    return Results.Ok(new { value, timestamp = DateTime.UtcNow });
})
.WithName("Random")
.WithOpenApi();

// Business operation endpoint
app.MapPost("/api/orders", (HttpContext context) =>
{
    using var scope = metrics.MeasureBusinessOperation("CreateOrder", context.GetUserId());
    
    logger.LogInformation("Creating new order");
    
    // Simulate database query
    Thread.Sleep(Random.Shared.Next(50, 200));
    metrics.RecordDbQuery("INSERT", "orders", true, Random.Shared.Next(20, 100));
    
    var orderId = Guid.NewGuid();
    logger.LogInformation("Order {OrderId} created successfully", orderId);
    
    return Results.Created($"/api/orders/{orderId}", new { orderId, timestamp = DateTime.UtcNow });
})
.WithName("CreateOrder")
.WithOpenApi();

// Cache simulation endpoint
app.MapGet("/api/cached/{key}", (string key) =>
{
    var hit = Random.Shared.Next(1, 100) > 30; // 70% cache hit rate
    metrics.RecordCacheAccess("sample-cache", hit);
    
    if (hit)
    {
        logger.LogDebug("Cache hit for key {Key}", key);
    }
    else
    {
        logger.LogDebug("Cache miss for key {Key}", key);
        Thread.Sleep(Random.Shared.Next(100, 300)); // Simulate fetching from source
    }
    
    return Results.Ok(new { key, cached = hit, value = $"value-{key}", timestamp = DateTime.UtcNow });
})
.WithName("CachedValue")
.WithOpenApi();

app.Run();
