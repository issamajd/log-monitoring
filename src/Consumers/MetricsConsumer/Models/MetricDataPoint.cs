namespace LogMonitoring.Consumers.Metrics.Models;

/// <summary>
/// Represents a single metric data point for storage in TimescaleDB.
/// </summary>
public class MetricDataPoint
{
    /// <summary>
    /// Timestamp of the metric.
    /// </summary>
    public DateTimeOffset Time { get; set; }

    /// <summary>
    /// Name of the metric.
    /// </summary>
    public required string MetricName { get; set; }

    /// <summary>
    /// Value of the metric.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Service that emitted the metric.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Version of the service.
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Host name where the metric was collected.
    /// </summary>
    public string? HostName { get; set; }

    /// <summary>
    /// Deployment environment (development, staging, production).
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Type of metric (gauge, counter, histogram, summary).
    /// </summary>
    public string? MetricType { get; set; }

    /// <summary>
    /// Unit of the metric value.
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// Additional labels as JSON.
    /// </summary>
    public Dictionary<string, string> Labels { get; set; } = new();
}

/// <summary>
/// Configuration for Kafka consumer.
/// </summary>
public class KafkaConfig
{
    /// <summary>
    /// Kafka bootstrap servers.
    /// </summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Consumer group ID.
    /// </summary>
    public string ConsumerGroupId { get; set; } = "metrics-consumer-group";

    /// <summary>
    /// Topic for metrics.
    /// </summary>
    public string MetricsTopic { get; set; } = "telemetry-metrics";

    /// <summary>
    /// Topic for logs.
    /// </summary>
    public string LogsTopic { get; set; } = "telemetry-logs";
}

/// <summary>
/// Configuration for TimescaleDB connection.
/// </summary>
public class TimescaleDbConfig
{
    /// <summary>
    /// Connection string for TimescaleDB.
    /// </summary>
    public string ConnectionString { get; set; } = 
        "Host=localhost;Port=5432;Database=metrics;Username=monitoring;Password=monitoring123";
}
