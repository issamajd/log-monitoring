using Google.Protobuf;
using System.Text;
using LogMonitoring.Consumers.Logs.Services;
using LogMonitoring.Consumers.Metrics.Services;
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
            ("Parse OTLP JSON traces", ParseOtlpJsonTraces)
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
        AssertEqual("SELECT", records[0].DbOperation, "Span database operation");
        AssertEqual("session-42", records[0].SessionId, "Span session ID");
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
