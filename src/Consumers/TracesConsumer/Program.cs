using LogMonitoring.Consumers.Traces;
using LogMonitoring.Consumers.Traces.Models;
using LogMonitoring.Consumers.Traces.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(
    builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);

// Bind configuration
builder.Services.Configure<KafkaConfig>(builder.Configuration.GetSection("Kafka"));
builder.Services.Configure<ElasticsearchConfig>(builder.Configuration.GetSection("Elasticsearch"));

// Register services
builder.Services.AddSingleton<ITracesRepository, ElasticsearchTracesRepository>();
builder.Services.AddSingleton<ITracesParser, OtlpTracesParser>();

// Register the worker
builder.Services.AddHostedService<TracesConsumerWorker>();

var host = builder.Build();
host.Run();
