using Google.Protobuf;
using LogMonitoring.OtlpProto;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Logs.V1;
using LogMonitoring.Consumers.Logs.Models;
using LogRecord = LogMonitoring.Consumers.Logs.Models.LogRecord;

namespace LogMonitoring.Consumers.Logs.Services;

/// <summary>
/// Interface for parsing logs from various formats.
/// </summary>
public interface ILogsParser
{
    /// <summary>
    /// Parses logs from an OTLP JSON payload or a legacy Protobuf payload.
    /// </summary>
    List<LogRecord> Parse(byte[] data);
}

/// <summary>
/// Parses OTLP JSON-encoded logs into LogRecord objects.
/// </summary>
public class OtlpLogsParser : ILogsParser
{
    public OtlpLogsParser(ILogger<OtlpLogsParser> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
    }

    public List<LogRecord> Parse(byte[] data)
    {
        List<LogRecord> result = new List<LogRecord>();
        ExportLogsServiceRequest request =
            OtlpPayloadParser.ParseJsonOrProtobuf(ExportLogsServiceRequest.Parser, data);

        foreach (OpenTelemetry.Proto.Logs.V1.ResourceLogs resourceLogs in request.ResourceLogs)
        {
            // Extract resource attributes
            ResourceAttributes resourceAttrs = ExtractResourceAttributes(resourceLogs);

            foreach (ScopeLogs scopeLogs in resourceLogs.ScopeLogs)
            {
                InstrumentationScope scope = new InstrumentationScope
                {
                    Name = scopeLogs.Scope?.Name,
                    Version = scopeLogs.Scope?.Version
                };

                foreach (OpenTelemetry.Proto.Logs.V1.LogRecord logRecord in scopeLogs.LogRecords)
                {
                    LogRecord record = ConvertLogRecord(logRecord, resourceAttrs, scope);
                    result.Add(record);
                }
            }
        }

        return result;
    }

    private static ResourceAttributes ExtractResourceAttributes(
        OpenTelemetry.Proto.Logs.V1.ResourceLogs resourceLogs)
    {
        var attrs = new ResourceAttributes();
        var resourceDict = new Dictionary<string, object>();

        foreach (var attr in resourceLogs.Resource.Attributes)
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

    private static LogRecord ConvertLogRecord(
        OpenTelemetry.Proto.Logs.V1.LogRecord logRecord,
        ResourceAttributes resourceAttrs,
        InstrumentationScope scope)
    {
        var record = new LogRecord
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)(logRecord.TimeUnixNano / 1_000_000)),
            ObservedTimestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)(logRecord.ObservedTimeUnixNano / 1_000_000)),
            Message = GetLogBody(logRecord.Body),
            Level = MapSeverityToLevel(logRecord.SeverityNumber),
            SeverityNumber = (int)logRecord.SeverityNumber,
            ServiceName = resourceAttrs.ServiceName,
            ServiceVersion = resourceAttrs.ServiceVersion,
            HostName = resourceAttrs.HostName,
            Environment = resourceAttrs.Environment,
            TraceId = logRecord.TraceId.IsEmpty ? null : ConvertBytesToHex(logRecord.TraceId),
            SpanId = logRecord.SpanId.IsEmpty ? null : ConvertBytesToHex(logRecord.SpanId),
            InstrumentationScope = scope,
            Resource = resourceAttrs.ResourceDict
        };

        // Extract attributes
        foreach (var attr in logRecord.Attributes)
        {
            var value = GetAttributeValue(attr.Value);
            if (value != null)
            {
                record.Attributes[attr.Key] = value;

                // Extract known attributes
                switch (attr.Key)
                {
                    case "correlation.id":
                    case "CorrelationId":
                        record.CorrelationId = value.ToString();
                        break;
                    case "user.id":
                    case "UserId":
                        record.UserId = value.ToString();
                        break;
                    case "session.id":
                    case "SessionId":
                        record.SessionId = value.ToString();
                        break;
                    case "request.id":
                    case "RequestId":
                        record.RequestId = value.ToString();
                        break;
                    case "logger.name":
                    case "LoggerName":
                        record.LoggerName = value.ToString();
                        break;
                    case "thread.name":
                    case "ThreadName":
                        record.ThreadName = value.ToString();
                        break;
                    case "exception.type":
                        record.Exception ??= new ExceptionInfo();
                        record.Exception.Type = value.ToString();
                        break;
                    case "exception.message":
                        record.Exception ??= new ExceptionInfo();
                        record.Exception.Message = value.ToString();
                        break;
                    case "exception.stacktrace":
                        record.Exception ??= new ExceptionInfo();
                        record.Exception.Stacktrace = value.ToString();
                        break;
                }
            }
        }

        return record;
    }

    private static string? GetLogBody(OpenTelemetry.Proto.Common.V1.AnyValue body)
    {
        return body.ValueCase switch
        {
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.StringValue => body.StringValue,
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.IntValue => body.IntValue.ToString(),
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.DoubleValue => body.DoubleValue.ToString(),
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.BoolValue => body.BoolValue.ToString(),
            _ => null
        };
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

    private static string MapSeverityToLevel(SeverityNumber severity)
    {
        return severity switch
        {
            SeverityNumber.Trace => "TRACE",
            SeverityNumber.Trace2 => "TRACE",
            SeverityNumber.Trace3 => "TRACE",
            SeverityNumber.Trace4 => "TRACE",
            SeverityNumber.Debug => "DEBUG",
            SeverityNumber.Debug2 => "DEBUG",
            SeverityNumber.Debug3 => "DEBUG",
            SeverityNumber.Debug4 => "DEBUG",
            SeverityNumber.Info => "INFO",
            SeverityNumber.Info2 => "INFO",
            SeverityNumber.Info3 => "INFO",
            SeverityNumber.Info4 => "INFO",
            SeverityNumber.Warn => "WARN",
            SeverityNumber.Warn2 => "WARN",
            SeverityNumber.Warn3 => "WARN",
            SeverityNumber.Warn4 => "WARN",
            SeverityNumber.Error => "ERROR",
            SeverityNumber.Error2 => "ERROR",
            SeverityNumber.Error3 => "ERROR",
            SeverityNumber.Error4 => "ERROR",
            SeverityNumber.Fatal => "FATAL",
            SeverityNumber.Fatal2 => "FATAL",
            SeverityNumber.Fatal3 => "FATAL",
            SeverityNumber.Fatal4 => "FATAL",
            _ => "UNKNOWN"
        };
    }

    private static string ConvertBytesToHex(ByteString bytes)
    {
        return Convert.ToHexString(bytes.ToByteArray()).ToLowerInvariant();
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
