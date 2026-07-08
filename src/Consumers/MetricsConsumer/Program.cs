using LogMonitoring.Consumers.Metrics;
using LogMonitoring.Consumers.Metrics.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(
    builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);

// Bind configuration
builder.Services.Configure<KafkaConfig>(builder.Configuration.GetSection("Kafka"));
builder.Services.Configure<TimescaleDbConfig>(builder.Configuration.GetSection("TimescaleDb"));

// Register services
builder.Services.AddSingleton<IMetricsRepository, TimescaleDbMetricsRepository>();
builder.Services.AddSingleton<IMetricsParser, OtlpMetricsParser>();

// Register the worker
builder.Services.AddHostedService<MetricsConsumerWorker>();

var host = builder.Build();
host.Run();
