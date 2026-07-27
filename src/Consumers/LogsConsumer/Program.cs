using LogMonitoring.Consumers.Logs;
using LogMonitoring.Consumers.Logs.Models;
using LogMonitoring.Consumers.Logs.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(
    builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);

// Bind configuration
builder.Services.Configure<KafkaConfig>(builder.Configuration.GetSection("Kafka"));
builder.Services.Configure<ElasticsearchConfig>(builder.Configuration.GetSection("Elasticsearch"));

// Register services
builder.Services.AddSingleton<ILogsRepository, ElasticsearchLogsRepository>();
builder.Services.AddSingleton<ILogsParser, OtlpLogsParser>();
builder.Services.AddSingleton<KafkaDlqProducer>();

// Register the worker
builder.Services.AddHostedService<LogsConsumerWorker>();

var host = builder.Build();
host.Run();
