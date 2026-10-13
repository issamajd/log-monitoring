using System.Text.Json.Serialization;

namespace LogMonitoring.Consumers.Traces.Models;

/// <summary>
/// Represents a span record for storage in Elasticsearch.
/// </summary>
public class SpanRecord
{
    /// <summary>
    /// Timestamp when the span started.
    /// </summary>
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Timestamp when the span ended.
    /// </summary>
    [JsonPropertyName("end_timestamp")]
    public DateTimeOffset EndTimestamp { get; set; }

    /// <summary>
    /// Start time in Unix microseconds. @timestamp keeps only milliseconds, so a parent and
    /// its children often tie; dashboards sort a trace's spans by this to list callers first.
    /// </summary>
    [JsonPropertyName("start_time_us")]
    public long StartTimeUs { get; set; }

    /// <summary>
    /// Span duration in milliseconds.
    /// </summary>
    [JsonPropertyName("duration_ms")]
    public double DurationMs { get; set; }

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
    /// Parent span ID, if this is not a root span.
    /// </summary>
    [JsonPropertyName("parent_span_id")]
    public string? ParentSpanId { get; set; }

    /// <summary>
    /// Name of the span.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Kind of span.
    /// </summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>
    /// Span status code.
    /// </summary>
    [JsonPropertyName("status_code")]
    public string? StatusCode { get; set; }

    /// <summary>
    /// Span status message.
    /// </summary>
    [JsonPropertyName("status_message")]
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Indicates whether the span completed with an error.
    /// </summary>
    [JsonPropertyName("error")]
    public bool Error { get; set; }

    /// <summary>
    /// Service that emitted the span.
    /// </summary>
    [JsonPropertyName("service_name")]
    public string? ServiceName { get; set; }

    /// <summary>
    /// Version of the service.
    /// </summary>
    [JsonPropertyName("service_version")]
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Host name where the span was emitted.
    /// </summary>
    [JsonPropertyName("host_name")]
    public string? HostName { get; set; }

    /// <summary>
    /// Deployment environment.
    /// </summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    /// <summary>
    /// Session ID, or empty when the span has none (see OtlpTracesParser for why it is never null).
    /// </summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    /// <summary>
    /// Operation ID, or empty when the span has none.
    /// </summary>
    [JsonPropertyName("operation_id")]
    public string? OperationId { get; set; }

    /// <summary>
    /// Client request transaction ID if available.
    /// </summary>
    [JsonPropertyName("client_request_tran_id")]
    public string? ClientRequestTranId { get; set; }

    /// <summary>
    /// End user name, or empty when the span has none. On a failed login it is the attempted name.
    /// </summary>
    [JsonPropertyName("user_name")]
    public string? UserName { get; set; }

    /// <summary>
    /// "method" for a business rejection the user was shown, "system" for an unexpected failure.
    /// Set only on failed spans.
    /// </summary>
    [JsonPropertyName("error_kind")]
    public string? ErrorKind { get; set; }

    /// <summary>
    /// The span's first "exception" event, promoted so dashboards can show and filter it.
    /// Same shape as the logs index's exception object.
    /// </summary>
    [JsonPropertyName("exception")]
    public SpanException? Exception { get; set; }

    /// <summary>
    /// Business process name if available.
    /// </summary>
    [JsonPropertyName("bp_name")]
    public string? BpName { get; set; }

    /// <summary>
    /// Business process method if available.
    /// </summary>
    [JsonPropertyName("bp_method")]
    public string? BpMethod { get; set; }

    /// <summary>
    /// Business process result if available.
    /// </summary>
    [JsonPropertyName("bp_result")]
    public string? BpResult { get; set; }

    /// <summary>
    /// Data query name if available.
    /// </summary>
    [JsonPropertyName("dq_name")]
    public string? DqName { get; set; }

    /// <summary>
    /// Data query method if available.
    /// </summary>
    [JsonPropertyName("dq_method")]
    public string? DqMethod { get; set; }

    /// <summary>
    /// Database operation if available.
    /// </summary>
    [JsonPropertyName("db_operation")]
    public string? DbOperation { get; set; }

    /// <summary>
    /// Database statement if available.
    /// </summary>
    [JsonPropertyName("db_statement")]
    public string? DbStatement { get; set; }

    /// <summary>
    /// Number of database rows affected if available.
    /// </summary>
    [JsonPropertyName("db_rows_affected")]
    public double? DbRowsAffected { get; set; }

    /// <summary>
    /// SQL danger flags if available.
    /// </summary>
    [JsonPropertyName("sql_danger_flags")]
    public string? SqlDangerFlags { get; set; }

    /// <summary>
    /// Hash of the normalized statement, the same for every execution of one statement shape.
    /// Set only when db_statement is. Dashboards group slow SQL by it.
    /// </summary>
    [JsonPropertyName("sql_fingerprint")]
    public string? SqlFingerprint { get; set; }

    /// <summary>
    /// Short readable form of the statement, shown next to sql_fingerprint. db_statement itself
    /// cannot be aggregated past the 1024-character keyword limit.
    /// </summary>
    [JsonPropertyName("db_statement_preview")]
    public string? DbStatementPreview { get; set; }

    /// <summary>
    /// ID of this one SQL execution (a new GUID per run), if available.
    /// </summary>
    [JsonPropertyName("sql_id")]
    public string? SqlId { get; set; }

    /// <summary>
    /// SQL transaction ID if available.
    /// </summary>
    [JsonPropertyName("sql_transaction_id")]
    public string? SqlTransactionId { get; set; }

    /// <summary>
    /// Database system if available.
    /// </summary>
    [JsonPropertyName("db_system")]
    public string? DbSystem { get; set; }

    /// <summary>
    /// Database name if available.
    /// </summary>
    [JsonPropertyName("db_name")]
    public string? DbName { get; set; }

    /// <summary>
    /// Job name if available.
    /// </summary>
    [JsonPropertyName("job_name")]
    public string? JobName { get; set; }

    /// <summary>
    /// Job queue if available.
    /// </summary>
    [JsonPropertyName("job_queue")]
    public string? JobQueue { get; set; }

    /// <summary>
    /// Job run ID if available.
    /// </summary>
    [JsonPropertyName("job_run_id")]
    public string? JobRunId { get; set; }

    /// <summary>
    /// Job status if available.
    /// </summary>
    [JsonPropertyName("job_status")]
    public string? JobStatus { get; set; }

    /// <summary>
    /// Transport type if available.
    /// </summary>
    [JsonPropertyName("transport")]
    public string? Transport { get; set; }

    /// <summary>
    /// Client address if available.
    /// </summary>
    [JsonPropertyName("client_address")]
    public string? ClientAddress { get; set; }

    /// <summary>
    /// Span events.
    /// </summary>
    [JsonPropertyName("events")]
    public List<SpanEvent> Events { get; set; } = new();

    /// <summary>
    /// Additional span attributes.
    /// </summary>
    [JsonPropertyName("attributes")]
    public Dictionary<string, object> Attributes { get; set; } = new();

    /// <summary>
    /// Resource attributes.
    /// </summary>
    [JsonPropertyName("resource")]
    public Dictionary<string, object> Resource { get; set; } = new();
}

/// <summary>
/// Exception details promoted from a span's "exception" event.
/// </summary>
public class SpanException
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("stacktrace")]
    public string? Stacktrace { get; set; }
}

/// <summary>
/// Represents an event associated with a span.
/// </summary>
public class SpanEvent
{
    /// <summary>
    /// Name of the event.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Event attributes.
    /// </summary>
    [JsonPropertyName("attributes")]
    public Dictionary<string, object> Attributes { get; set; } = new();
}

/// <summary>
/// Configuration for Kafka consumer.
/// </summary>
public class KafkaConfig
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroupId { get; set; } = "traces-consumer-group";
    public string TracesTopic { get; set; } = "telemetry-traces";

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
    public string IndexAlias { get; set; } = "traces";
    public string? Username { get; set; }
    public string? Password { get; set; }
}
