namespace LogMonitoring.SystemTraceParser;

public sealed class SystemTraceEvent
{
    public DateTimeOffset Timestamp { get; init; }

    public string LogType { get; init; } = string.Empty;

    public int Severity { get; init; }

    public string Module { get; init; } = string.Empty;

    public string Group { get; init; } = string.Empty;

    public string SourceNamespace { get; init; } = string.Empty;

    public string SourceSenderType { get; init; } = string.Empty;

    public string SourceMethodName { get; init; } = string.Empty;

    public string? UserName { get; set; }

    public string? SessionId { get; set; }

    public string? ClientRequestTranId { get; set; }

    public string? OperationId { get; set; }

    public int? ThreadId { get; set; }

    public double? TotalElapsedSeconds { get; set; }

    public double? LastLogElapsedSeconds { get; set; }

    public string? Message { get; set; }

    public string? Data { get; set; }

    public Dictionary<string, string> TraceInfo { get; } = new(StringComparer.OrdinalIgnoreCase);
}
