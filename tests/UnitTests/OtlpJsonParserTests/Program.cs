using Google.Protobuf;
using System.Text;
using LogMonitoring.Consumers.Logs.Services;
using LogMonitoring.Consumers.Metrics.Services;
using LogMonitoring.Consumers.Traces.Models;
using LogMonitoring.Consumers.Traces.Services;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using OtlpLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace LogMonitoring.Tests.OtlpJsonParser;

internal static class Program
{
    private const ulong TimestampNanoseconds = 1_721_568_000_123_000_000;

    private static int Main()
    {
        var tests = new List<(string Name, Action Test)>
        {
            ("Parse OTLP JSON logs", ParseOtlpJsonLogs),
            ("Parse OTLP JSON metrics", ParseOtlpJsonMetrics),
            ("Parse OTLP JSON traces", ParseOtlpJsonTraces),
            ("Collector hex trace/span IDs survive parsing", ParseCollectorHexIds),
            ("Failure details are promoted for search", ParseFailureDetails),
            ("SQL executions group by statement fingerprint", FingerprintSqlStatements)
        };

        foreach (var (name, test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL {name}");
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        return 0;
    }

    private static void ParseOtlpJsonLogs()
    {
        var request = new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = CreateResource(),
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            Scope = new InstrumentationScope { Name = "json-test", Version = "1.0" },
                            LogRecords =
                            {
                                new OtlpLogRecord
                                {
                                    TimeUnixNano = TimestampNanoseconds,
                                    ObservedTimeUnixNano = TimestampNanoseconds,
                                    SeverityNumber = SeverityNumber.Info,
                                    Body = StringValue("Hello from OTLP JSON"),
                                    TraceId = ByteString.CopyFrom(Enumerable.Range(1, 16).Select(value => (byte)value).ToArray()),
                                    SpanId = ByteString.CopyFrom(Enumerable.Range(17, 8).Select(value => (byte)value).ToArray()),
                                    Attributes = { Attribute("session.id", "session-42") }
                                }
                            }
                        }
                    }
                }
            }
        };

        var parser = new OtlpLogsParser(NullLogger<OtlpLogsParser>.Instance);
        var records = parser.Parse(ToJsonBytes(request));

        AssertEqual(1, records.Count, "Log count");
        AssertEqual("Hello from OTLP JSON", records[0].Message, "Log message");
        AssertEqual("INFO", records[0].Level, "Log level");
        AssertEqual("rndfwcore-server", records[0].ServiceName, "Log service name");
        AssertEqual("session-42", records[0].SessionId, "Log session ID");
        AssertEqual("0102030405060708090a0b0c0d0e0f10", records[0].TraceId, "Log trace ID");
    }

    private static void ParseOtlpJsonMetrics()
    {
        var request = new ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                {
                    Resource = CreateResource(),
                    ScopeMetrics =
                    {
                        new ScopeMetrics
                        {
                            Metrics =
                            {
                                new Metric
                                {
                                    Name = "process.test.value",
                                    Unit = "1",
                                    Gauge = new Gauge
                                    {
                                        DataPoints =
                                        {
                                            new NumberDataPoint
                                            {
                                                TimeUnixNano = TimestampNanoseconds,
                                                AsDouble = 42.5,
                                                Attributes = { Attribute("state", "ready") }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var parser = new OtlpMetricsParser(NullLogger<OtlpMetricsParser>.Instance);
        var records = parser.Parse(ToJsonBytes(request));

        AssertEqual(1, records.Count, "Metric count");
        AssertEqual("process.test.value", records[0].MetricName, "Metric name");
        AssertEqual(42.5, records[0].Value, "Metric value");
        AssertEqual("rndfwcore-server", records[0].ServiceName, "Metric service name");
        AssertEqual("ready", records[0].Labels["state"], "Metric label");
    }

    private static void ParseOtlpJsonTraces()
    {
        var request = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = CreateResource(),
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Spans =
                            {
                                new Span
                                {
                                    TraceId = ByteString.CopyFrom(Enumerable.Range(1, 16).Select(value => (byte)value).ToArray()),
                                    SpanId = ByteString.CopyFrom(Enumerable.Range(17, 8).Select(value => (byte)value).ToArray()),
                                    Name = "sql.query",
                                    Kind = Span.Types.SpanKind.Client,
                                    StartTimeUnixNano = TimestampNanoseconds,
                                    EndTimeUnixNano = TimestampNanoseconds + 2_500_000,
                                    Status = new Status { Code = Status.Types.StatusCode.Ok },
                                    Attributes =
                                    {
                                        Attribute("db.operation", "SELECT"),
                                        Attribute("session.id", "session-42")
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var parser = new OtlpTracesParser(NullLogger<OtlpTracesParser>.Instance);
        var records = parser.Parse(ToJsonBytes(request));

        AssertEqual(1, records.Count, "Span count");
        AssertEqual("sql.query", records[0].Name, "Span name");
        AssertEqual("CLIENT", records[0].Kind, "Span kind");
        AssertEqual("OK", records[0].StatusCode, "Span status");
        AssertEqual(2.5, records[0].DurationMs, "Span duration");
        AssertEqual((long)(TimestampNanoseconds / 1_000), records[0].StartTimeUs, "Span start time (us)");
        AssertEqual("SELECT", records[0].DbOperation, "Span database operation");
        AssertEqual("session-42", records[0].SessionId, "Span session ID");
        AssertEqual("development", records[0].Environment, "Span environment");
    }

    /// <summary>
    /// sql_id is new for every execution, so slow-SQL panels group by sql_fingerprint instead:
    /// the same statement must hash the same however it is spaced, and a different one must not.
    /// </summary>
    private static void FingerprintSqlStatements()
    {
        const string Statement = "SELECT S.NAME AS \"NAME\", S.VALUE AS \"VALUE\" FROM SYSTEMFLAGS S WHERE (S.NAME = @fp1e)";

        static Span SqlSpan(byte id, string? statement)
        {
            Span span = new Span
            {
                TraceId = ByteString.CopyFrom(Enumerable.Range(1, 16).Select(value => (byte)value).ToArray()),
                SpanId = ByteString.CopyFrom(Enumerable.Repeat(id, 8).ToArray()),
                Name = "sql.query",
                StartTimeUnixNano = TimestampNanoseconds,
                EndTimeUnixNano = TimestampNanoseconds + 1_000_000
            };
            if (statement != null)
                span.Attributes.Add(Attribute("db.statement", statement));
            return span;
        }

        ExportTraceServiceRequest request = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = CreateResource(),
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Spans =
                            {
                                SqlSpan(1, Statement),
                                SqlSpan(2, "  " + Statement.Replace(" FROM ", "\r\n  FROM ") + "\n"),
                                SqlSpan(3, Statement.Replace("SYSTEMFLAGS", "USERS")),
                                SqlSpan(4, null),
                                AppFingerprintedSpan()
                            }
                        }
                    }
                }
            }
        };

        // rndfwcore hashes the statement before its 4096-character cut; its value wins.
        Span AppFingerprintedSpan()
        {
            Span span = SqlSpan(5, Statement);
            span.Attributes.Add(Attribute("rndfw.sql.fingerprint", "0123456789abcdef"));
            return span;
        }

        OtlpTracesParser parser = new OtlpTracesParser(NullLogger<OtlpTracesParser>.Instance);
        List<SpanRecord> records = parser.Parse(ToJsonBytes(request));

        AssertEqual(16, records[0].SqlFingerprint?.Length ?? 0, "Fingerprint length");
        AssertEqual(records[0].SqlFingerprint, records[1].SqlFingerprint, "Fingerprint ignores whitespace");
        AssertEqual(false, records[0].SqlFingerprint == records[2].SqlFingerprint, "Different statements differ");
        AssertEqual("SELECT … FROM SYSTEMFLAGS S WHERE (S.NAME = @fp1e)", records[0].DbStatementPreview, "Statement preview");
        AssertEqual(null, records[3].SqlFingerprint, "No fingerprint without a statement");
        AssertEqual("0123456789abcdef", records[4].SqlFingerprint, "App fingerprint is kept");
    }

    /// <summary>
    /// The other tests build their payloads with <see cref="JsonFormatter"/>, which emits
    /// base64 IDs per the Protobuf JSON spec. The real collector emits OTLP/JSON, where
    /// trace and span IDs are hex strings, so only a hand-written payload exercises the
    /// path that actually runs in production.
    /// </summary>
    private static void ParseCollectorHexIds()
    {
        const string TraceId = "0123456789abcdef0123456789abcdef";
        const string SpanId = "0123456789abcdef";
        const string ParentSpanId = "fedcba9876543210";

        string tracesJson = $$"""
        {
          "resourceSpans": [{
            "resource": { "attributes": [
              { "key": "service.name", "value": { "stringValue": "rndfwcore-server" } }
            ]},
            "scopeSpans": [{ "spans": [{
              "traceId": "{{TraceId}}",
              "spanId": "{{SpanId}}",
              "parentSpanId": "{{ParentSpanId}}",
              "name": "hex-id-span",
              "kind": 2,
              "startTimeUnixNano": "{{TimestampNanoseconds}}",
              "endTimeUnixNano": "{{TimestampNanoseconds}}"
            }]}]
          }]
        }
        """;

        List<Consumers.Traces.Models.SpanRecord> spans =
            new OtlpTracesParser(NullLogger<OtlpTracesParser>.Instance)
                .Parse(Encoding.UTF8.GetBytes(tracesJson));

        AssertEqual(1, spans.Count, "Span count");
        AssertEqual(TraceId, spans[0].TraceId, "Span trace ID");
        AssertEqual(SpanId, spans[0].SpanId, "Span span ID");
        AssertEqual(ParentSpanId, spans[0].ParentSpanId, "Span parent span ID");

        // No host.name or deployment.environment on the resource: stored as "" so the dashboards'
        // `host_name:$host` / `environment:$env` filters (All = `*`) still match the span.
        AssertEqual("", spans[0].HostName, "Span host name without host.name");
        AssertEqual("", spans[0].Environment, "Span environment without deployment.environment");

        string logsJson = $$"""
        {
          "resourceLogs": [{
            "resource": { "attributes": [
              { "key": "service.name", "value": { "stringValue": "rndfwcore-server" } }
            ]},
            "scopeLogs": [{ "logRecords": [{
              "timeUnixNano": "{{TimestampNanoseconds}}",
              "observedTimeUnixNano": "{{TimestampNanoseconds}}",
              "severityNumber": 17,
              "body": { "stringValue": "correlated log" },
              "traceId": "{{TraceId}}",
              "spanId": "{{SpanId}}"
            }]}]
          }]
        }
        """;

        List<Consumers.Logs.Models.LogRecord> logs =
            new OtlpLogsParser(NullLogger<OtlpLogsParser>.Instance)
                .Parse(Encoding.UTF8.GetBytes(logsJson));

        AssertEqual(1, logs.Count, "Log count");
        AssertEqual(TraceId, logs[0].TraceId, "Log trace ID");
        AssertEqual(SpanId, logs[0].SpanId, "Log span ID");

        // A log with no correlation must stay null rather than becoming empty-string hex.
        string noIdsJson = """
        {
          "resourceLogs": [{
            "scopeLogs": [{ "logRecords": [{
              "timeUnixNano": "1721568000123000000",
              "severityNumber": 9,
              "body": { "stringValue": "uncorrelated log" }
            }]}]
          }]
        }
        """;

        List<Consumers.Logs.Models.LogRecord> uncorrelated =
            new OtlpLogsParser(NullLogger<OtlpLogsParser>.Instance)
                .Parse(Encoding.UTF8.GetBytes(noIdsJson));

        // No correlation must not become hex. The trace ID is stored as "" (not null) so the
        // dashboards' `trace_id:*$trace_id*` filter still matches the record; the span ID has no filter.
        AssertEqual(1, uncorrelated.Count, "Uncorrelated log count");
        AssertEqual("", uncorrelated[0].TraceId, "Uncorrelated trace ID");
        AssertEqual(null, uncorrelated[0].SpanId, "Uncorrelated span ID");
        AssertEqual("", uncorrelated[0].SessionId, "Uncorrelated session ID");
        AssertEqual("", uncorrelated[0].HostName, "Resource-less log host name");
        AssertEqual("", uncorrelated[0].Environment, "Resource-less log environment");
    }

    /// <summary>
    /// A failure must reach Elasticsearch searchable: the span's exception event and error.kind
    /// promoted to top-level fields, the attempted user name kept on a session-less failed login,
    /// and the log side mapping enduser.id/error.kind the same way.
    /// </summary>
    private static void ParseFailureDetails()
    {
        Span failedLogin = new Span
        {
            TraceId = ByteString.CopyFrom(Enumerable.Range(1, 16).Select(value => (byte)value).ToArray()),
            SpanId = ByteString.CopyFrom(Enumerable.Range(17, 8).Select(value => (byte)value).ToArray()),
            Name = "command Login",
            StartTimeUnixNano = TimestampNanoseconds,
            EndTimeUnixNano = TimestampNanoseconds + 1_000_000,
            Status = new Status { Code = Status.Types.StatusCode.Error, Message = "Invalid Credentials" },
            Attributes = { Attribute("enduser.id", "support.test"), Attribute("error.kind", "method") }
        };
        failedLogin.Events.Add(new Span.Types.Event
        {
            Name = "exception",
            TimeUnixNano = TimestampNanoseconds,
            Attributes =
            {
                Attribute("exception.type", "common.infrastructure.exceptions.MethodException"),
                Attribute("exception.message", "Invalid Credentials")
            }
        });

        ExportTraceServiceRequest traces = new ExportTraceServiceRequest
        {
            ResourceSpans = { new ResourceSpans { Resource = CreateResource(), ScopeSpans = { new ScopeSpans { Spans = { failedLogin } } } } }
        };

        List<Consumers.Traces.Models.SpanRecord> spans =
            new OtlpTracesParser(NullLogger<OtlpTracesParser>.Instance).Parse(ToJsonBytes(traces));

        AssertEqual(true, spans[0].Error, "Failed span error flag");
        AssertEqual("method", spans[0].ErrorKind, "Span error kind");
        AssertEqual("common.infrastructure.exceptions.MethodException", spans[0].Exception?.Type, "Span exception type");
        AssertEqual("Invalid Credentials", spans[0].Exception?.Message, "Span exception message");
        AssertEqual("support.test", spans[0].UserName, "Attempted user name");
        AssertEqual("", spans[0].SessionId, "Session-less span session ID");

        ExportLogsServiceRequest logs = new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = CreateResource(),
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            LogRecords =
                            {
                                new OtlpLogRecord
                                {
                                    TimeUnixNano = TimestampNanoseconds,
                                    SeverityNumber = SeverityNumber.Error,
                                    Body = StringValue("NullReferenceException: Object reference not set"),
                                    Attributes =
                                    {
                                        Attribute("enduser.id", "admin"),
                                        Attribute("operation.id", "op-7"),
                                        Attribute("error.kind", "system"),
                                        Attribute("exception.type", "System.NullReferenceException")
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        List<Consumers.Logs.Models.LogRecord> records =
            new OtlpLogsParser(NullLogger<OtlpLogsParser>.Instance).Parse(ToJsonBytes(logs));

        AssertEqual("admin", records[0].UserId, "Log user from enduser.id");
        AssertEqual("op-7", records[0].OperationId, "Log operation ID");
        AssertEqual("system", records[0].ErrorKind, "Log error kind");
        AssertEqual("System.NullReferenceException", records[0].Exception?.Type, "Log exception type");
    }

    private static Resource CreateResource()
    {
        return new Resource
        {
            Attributes =
            {
                Attribute("service.name", "rndfwcore-server"),
                Attribute("deployment.environment", "development")
            }
        };
    }

    private static KeyValue Attribute(string key, string value)
    {
        return new KeyValue { Key = key, Value = StringValue(value) };
    }

    private static AnyValue StringValue(string value)
    {
        return new AnyValue { StringValue = value };
    }

    private static byte[] ToJsonBytes(IMessage message)
    {
        return Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(message));
    }

    private static void AssertEqual<T>(T expected, T actual, string field)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{field}: expected '{expected}', got '{actual}'.");
        }
    }
}
