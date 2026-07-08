# Instrumentation Component Test

This test validates the OpenTelemetry instrumentation library without requiring external dependencies like an OTLP endpoint.

## What This Test Does

1. **Basic Logging** - Simple log messages at different levels
2. **Structured Logging** - Log messages with structured parameters
3. **Logging with Scopes** - Log messages with correlation context
4. **Trace Correlation** - Logs linked to distributed traces
5. **Exception Logging** - Exception details and stack traces
6. **All Log Levels** - Tests Trace, Debug, Info, Warning, Error, Critical

## Running the Test

```bash
cd tests/ComponentTests/InstrumentationTests
dotnet run
```

## Output

The test produces:
- **Console output** - Real-time log display
- **File output** - Logs saved to `output/otel-logs-{timestamp}.txt`

## Expected Output Format

```
[2026-01-14 10:30:00.123] [Information] [LogMonitoring.Tests.Instrumentation.Program] This is a basic information log message
[2026-01-14 10:30:00.124] [Information] [LogMonitoring.Tests.Instrumentation.Program] [TraceId:abc123] [SpanId:def456] Log with trace context
```

## Verifying Success

1. Console shows all log messages with OpenTelemetry format
2. Output file contains all log entries with:
   - Timestamps
   - Log levels
   - Category names
   - Trace/Span IDs when applicable
   - Structured attributes
   - Scope information
   - Exception details

## No External Dependencies

This test does NOT require:
- ❌ OTLP endpoint
- ❌ OpenTelemetry Collector
- ❌ Kafka
- ❌ Elasticsearch
- ❌ Any external services

It only tests the instrumentation library's ability to create properly formatted OpenTelemetry logs.
