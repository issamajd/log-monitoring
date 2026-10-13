using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf;
using LogMonitoring.OtlpProto;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Trace.V1;
using LogMonitoring.Consumers.Traces.Models;

namespace LogMonitoring.Consumers.Traces.Services;

/// <summary>
/// Interface for parsing traces from various formats.
/// </summary>
public interface ITracesParser
{
    /// <summary>
    /// Parses traces from an OTLP JSON payload.
    /// </summary>
    List<SpanRecord> Parse(byte[] data);
}

/// <summary>
/// Parses OTLP JSON-encoded traces into SpanRecord objects.
/// </summary>
public class OtlpTracesParser : ITracesParser
{
    public OtlpTracesParser(ILogger<OtlpTracesParser> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
    }

    public List<SpanRecord> Parse(byte[] data)
    {
        List<SpanRecord> result = new List<SpanRecord>();
        ExportTraceServiceRequest request =
            ExportTraceServiceRequest.Parser.ParseJson(Encoding.UTF8.GetString(data));

        foreach (OpenTelemetry.Proto.Trace.V1.ResourceSpans resourceSpans in request.ResourceSpans)
        {
            // Extract resource attributes
            ResourceAttributes resourceAttrs = ExtractResourceAttributes(resourceSpans);

            foreach (ScopeSpans scopeSpans in resourceSpans.ScopeSpans)
            {
                foreach (Span span in scopeSpans.Spans)
                {
                    SpanRecord record = ConvertSpanRecord(span, resourceAttrs);
                    result.Add(record);
                }
            }
        }

        return result;
    }

    private static ResourceAttributes ExtractResourceAttributes(
        OpenTelemetry.Proto.Trace.V1.ResourceSpans resourceSpans)
    {
        var attrs = new ResourceAttributes();
        var resourceDict = new Dictionary<string, object>();

        // resource is optional in OTLP; without this guard one resource-less batch
        // throws and sends every span in it to the DLQ.
        if (resourceSpans.Resource == null)
        {
            return attrs;
        }

        foreach (var attr in resourceSpans.Resource.Attributes)
        {
            var value = GetAttributeValue(attr.Value);
            if (value != null)
            {
                resourceDict[attr.Key] = value;
            }

            switch (attr.Key)
            {
                case "service.name":
                    attrs.ServiceName = attr.Value.StringValue;
                    break;
                case "service.version":
                    attrs.ServiceVersion = attr.Value.StringValue;
                    break;
                case "host.name":
                    attrs.HostName = attr.Value.StringValue;
                    break;
                case "deployment.environment":
                    attrs.Environment = attr.Value.StringValue;
                    break;
            }
        }

        attrs.ResourceDict = resourceDict;
        return attrs;
    }

    private static SpanRecord ConvertSpanRecord(
        Span span,
        ResourceAttributes resourceAttrs)
    {
        var statusCode = span.Status == null ? "UNSET" : MapStatusCode(span.Status.Code);
        var record = new SpanRecord
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)(span.StartTimeUnixNano / 1_000_000)),
            EndTimestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)(span.EndTimeUnixNano / 1_000_000)),
            StartTimeUs = (long)(span.StartTimeUnixNano / 1_000),
            DurationMs = (span.EndTimeUnixNano - span.StartTimeUnixNano) / 1_000_000.0,
            TraceId = OtlpJsonIds.ToHex(span.TraceId, OtlpJsonIds.TraceIdBytes),
            SpanId = OtlpJsonIds.ToHex(span.SpanId, OtlpJsonIds.SpanIdBytes),
            ParentSpanId = OtlpJsonIds.ToHex(span.ParentSpanId, OtlpJsonIds.SpanIdBytes),
            Name = span.Name,
            Kind = MapSpanKind(span.Kind),
            StatusCode = statusCode,
            StatusMessage = span.Status?.Message,
            Error = statusCode == "ERROR",
            ServiceName = resourceAttrs.ServiceName,
            ServiceVersion = resourceAttrs.ServiceVersion,
            HostName = resourceAttrs.HostName,
            Environment = resourceAttrs.Environment,
            Resource = resourceAttrs.ResourceDict
        };

        // Extract attributes
        foreach (var attr in span.Attributes)
        {
            var value = GetAttributeValue(attr.Value);
            if (value != null)
            {
                record.Attributes[attr.Key] = value;

                // Extract known attributes
                switch (attr.Key)
                {
                    case "session.id":
                        record.SessionId = value.ToString();
                        break;
                    case "operation.id":
                        record.OperationId = value.ToString();
                        break;
                    case "client_request_tran_id":
                        record.ClientRequestTranId = value.ToString();
                        break;
                    case "enduser.id":
                        record.UserName = value.ToString();
                        break;
                    case "error.kind":
                        record.ErrorKind = value.ToString();
                        break;
                    case "bp.name":
                        record.BpName = value.ToString();
                        break;
                    case "bp.method":
                        record.BpMethod = value.ToString();
                        break;
                    case "bp.result":
                        record.BpResult = value.ToString();
                        break;
                    case "dq.name":
                        record.DqName = value.ToString();
                        break;
                    case "dq.method":
                        record.DqMethod = value.ToString();
                        break;
                    case "db.operation":
                        record.DbOperation = value.ToString();
                        break;
                    case "db.statement":
                        record.DbStatement = value.ToString();
                        break;
                    case "db.rows_affected":
                        record.DbRowsAffected = ConvertToDouble(value);
                        break;
                    case "rndfw.sql.danger_flags":
                        record.SqlDangerFlags = value.ToString();
                        break;
                    case "rndfw.sql.id":
                        record.SqlId = value.ToString();
                        break;
                    case "rndfw.sql.transaction_id":
                        record.SqlTransactionId = value.ToString();
                        break;
                    case "db.system":
                        record.DbSystem = value.ToString();
                        break;
                    case "db.name":
                        record.DbName = value.ToString();
                        break;
                    case "job.name":
                        record.JobName = value.ToString();
                        break;
                    case "job.queue":
                        record.JobQueue = value.ToString();
                        break;
                    case "job.run_id":
                        record.JobRunId = value.ToString();
                        break;
                    case "job.status":
                        record.JobStatus = value.ToString();
                        break;
                    case "rndfw.transport":
                        record.Transport = value.ToString();
                        break;
                    case "client.address":
                        record.ClientAddress = value.ToString();
                        break;
                }
            }
        }

        foreach (var spanEvent in span.Events)
        {
            var eventRecord = new SpanEvent
            {
                Name = spanEvent.Name,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)(spanEvent.TimeUnixNano / 1_000_000))
            };

            foreach (var attr in spanEvent.Attributes)
            {
                var value = GetAttributeValue(attr.Value);
                if (value != null)
                {
                    eventRecord.Attributes[attr.Key] = value;
                }
            }

            record.Events.Add(eventRecord);

            if (record.Exception == null && spanEvent.Name == "exception")
            {
                record.Exception = new SpanException
                {
                    Type = eventRecord.Attributes.GetValueOrDefault("exception.type")?.ToString(),
                    Message = eventRecord.Attributes.GetValueOrDefault("exception.message")?.ToString(),
                    Stacktrace = eventRecord.Attributes.GetValueOrDefault("exception.stacktrace")?.ToString()
                };
            }
        }

        // Dashboards filter correlation IDs with `field:*$var*`, which matches an empty string but
        // not a missing field. Writing "" keeps spans without a session or user (a failed login, a
        // scheduler job) visible while the filter box is empty.
        record.SessionId ??= string.Empty;
        record.OperationId ??= string.Empty;
        record.UserName ??= string.Empty;

        if (!string.IsNullOrWhiteSpace(record.DbStatement))
        {
            string statement = WhitespaceRun.Replace(record.DbStatement.Trim(), " ");
            record.SqlFingerprint = FingerprintStatement(statement);
            record.DbStatementPreview = PreviewStatement(statement);
        }

        return record;
    }

    private static readonly Regex WhitespaceRun = new Regex(@"\s+", RegexOptions.Compiled);

    private const int StatementPreviewLength = 200;

    /// <summary>
    /// First 16 hex characters of the statement's SHA-256. rndfwcore masks literals before
    /// emitting db.statement, so every execution of one statement shape hashes the same.
    /// </summary>
    private static string FingerprintStatement(string normalizedStatement)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedStatement));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>
    /// A SELECT's column list says little about it, so the preview starts at FROM.
    /// </summary>
    private static string PreviewStatement(string normalizedStatement)
    {
        string preview = normalizedStatement;
        if (preview.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
        {
            int fromIndex = preview.IndexOf(" FROM ", StringComparison.OrdinalIgnoreCase);
            if (fromIndex > 0)
                preview = "SELECT … " + preview.Substring(fromIndex + 1);
        }

        return preview.Length <= StatementPreviewLength
            ? preview
            : preview.Substring(0, StatementPreviewLength) + "…";
    }

    private static object? GetAttributeValue(OpenTelemetry.Proto.Common.V1.AnyValue value)
    {
        return value.ValueCase switch
        {
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.StringValue => value.StringValue,
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.IntValue => value.IntValue,
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue,
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.BoolValue => value.BoolValue,
            _ => null
        };
    }

    private static double? ConvertToDouble(object value)
    {
        try
        {
            return Convert.ToDouble(value);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static string MapSpanKind(Span.Types.SpanKind kind)
    {
        return kind switch
        {
            Span.Types.SpanKind.Internal => "INTERNAL",
            Span.Types.SpanKind.Server => "SERVER",
            Span.Types.SpanKind.Client => "CLIENT",
            Span.Types.SpanKind.Producer => "PRODUCER",
            Span.Types.SpanKind.Consumer => "CONSUMER",
            _ => "UNSPECIFIED"
        };
    }

    private static string MapStatusCode(Status.Types.StatusCode statusCode)
    {
        return statusCode switch
        {
            Status.Types.StatusCode.Ok => "OK",
            Status.Types.StatusCode.Error => "ERROR",
            _ => "UNSET"
        };
    }

    private class ResourceAttributes
    {
        public string? ServiceName { get; set; }
        public string? ServiceVersion { get; set; }
        public string? HostName { get; set; }
        public string? Environment { get; set; }
        public Dictionary<string, object> ResourceDict { get; set; } = new();
    }
}
