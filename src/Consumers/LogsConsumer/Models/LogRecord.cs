using System.Text.Json.Serialization;

namespace LogMonitoring.Consumers.Logs.Models;

/// <summary>
/// Represents a log record for storage in Elasticsearch.
/// </summary>
public class LogRecord
{
    /// <summary>
    /// Timestamp when the log was emitted.
    /// </summary>
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Observed timestamp when the log was collected.
    /// </summary>
    [JsonPropertyName("observed_timestamp")]
    public DateTimeOffset ObservedTimestamp { get; set; }

    /// <summary>
    /// The log message body.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Severity level (e.g., INFO, WARN, ERROR).
    /// </summary>
    [JsonPropertyName("level")]
    public string? Level { get; set; }

    /// <summary>
    /// Numeric severity level.
    /// </summary>
    [JsonPropertyName("severity_number")]
    public int SeverityNumber { get; set; }

    /// <summary>
    /// Service that emitted the log.
    /// </summary>
    [JsonPropertyName("service_name")]
    public string? ServiceName { get; set; }

    /// <summary>
    /// Version of the service.
    /// </summary>
    [JsonPropertyName("service_version")]
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Host name where the log was emitted.
    /// </summary>
    [JsonPropertyName("host_name")]
    public string? HostName { get; set; }

    /// <summary>
    /// Deployment environment.
    /// </summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    /// <summary>
    /// Trace ID for distributed tracing correlation.
    /// </summary>
    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }

    /// <summary>
    /// Span ID for distributed tracing correlation.
    /// </summary>
    [JsonPropertyName("span_id")]
    public string? SpanId { get; set; }

    /// <summary>
    /// Correlation ID for request tracking.
    /// </summary>
    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// User ID if available.
    /// </summary>
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    /// <summary>
    /// Session ID if available.
    /// </summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    /// <summary>
    /// Request ID for request tracking.
    /// </summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    /// <summary>
    /// Logger name/class that emitted the log.
    /// </summary>
    [JsonPropertyName("logger_name")]
    public string? LoggerName { get; set; }

    /// <summary>
    /// Thread name or ID.
    /// </summary>
    [JsonPropertyName("thread_name")]
    public string? ThreadName { get; set; }

    /// <summary>
    /// Exception information if present.
    /// </summary>
    [JsonPropertyName("exception")]
    public ExceptionInfo? Exception { get; set; }

    /// <summary>
    /// Additional attributes.
    /// </summary>
    [JsonPropertyName("attributes")]
    public Dictionary<string, object> Attributes { get; set; } = new();

    /// <summary>
    /// Resource attributes.
    /// </summary>
    [JsonPropertyName("resource")]
    public Dictionary<string, object> Resource { get; set; } = new();

    /// <summary>
    /// Instrumentation scope information.
    /// </summary>
    [JsonPropertyName("instrumentation_scope")]
    public InstrumentationScope? InstrumentationScope { get; set; }
}

/// <summary>
/// Exception information in a log record.
/// </summary>
public class ExceptionInfo
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("stacktrace")]
    public string? Stacktrace { get; set; }
}

/// <summary>
/// Instrumentation scope information.
/// </summary>
public class InstrumentationScope
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

/// <summary>
/// Configuration for Kafka consumer.
/// </summary>
public class KafkaConfig
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroupId { get; set; } = "logs-consumer-group";
    public string LogsTopic { get; set; } = "telemetry-logs";

    /// <summary>
    /// Gets or sets the dead-letter topic. Null or empty disables DLQ publishing.
    /// </summary>
    public string? DlqTopic { get; set; } = "dlq";
}

/// <summary>
/// Configuration for Elasticsearch connection.
/// </summary>
public class ElasticsearchConfig
{
    public string Url { get; set; } = "http://localhost:9200";
    public string IndexAlias { get; set; } = "logs";
    public string? Username { get; set; }
    public string? Password { get; set; }
}
