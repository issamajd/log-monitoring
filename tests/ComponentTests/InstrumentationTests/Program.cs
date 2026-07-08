using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

namespace LogMonitoring.Tests.Instrumentation;

/// <summary>
/// Component test for OpenTelemetry instrumentation.
/// Tests log creation and export to file/console without OTLP endpoint.
/// </summary>
class Program
{
    private const string ServiceName = "InstrumentationTest";
    private const string ServiceVersion = "1.0.0";
    
    static async Task Main(string[] args)
    {
        Console.WriteLine("==============================================");
        Console.WriteLine("OpenTelemetry Instrumentation Component Test");
        Console.WriteLine("==============================================\n");

        // Setup output file
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
        Directory.CreateDirectory(outputDir);
        var logFilePath = Path.Combine(outputDir, $"otel-logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        
        Console.WriteLine($"Log output file: {logFilePath}\n");

        // Create a file writer for capturing logs
        var fileWriter = new StreamWriter(logFilePath, append: true);
        var fileExporter = new FileLogExporter(fileWriter);
        
        // Enable activity/trace listening
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { }
        });

        // Build service provider with OpenTelemetry
        var services = new ServiceCollection();
        
        // Configure OpenTelemetry Logging
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            //builder.AddConsole();
            
            builder.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                
                options.SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(ServiceName, serviceVersion: ServiceVersion)
                    .AddAttributes(new[]
                    {
                        new KeyValuePair<string, object>("deployment.environment", "test"),
                        new KeyValuePair<string, object>("host.name", Environment.MachineName)
                    }));

                options.AddConsoleExporter();
                // Add custom file exporter
                options.AddProcessor(new SimpleLogRecordExportProcessor(fileExporter));
            });
        });

      
        var serviceProvider = services.BuildServiceProvider();

        // Run tests
        Console.WriteLine("\n--- Test 1: Basic Logging ---\n");
        await TestBasicLogging(serviceProvider);

        Console.WriteLine("\n--- Test 2: Structured Logging ---\n");
        await TestStructuredLogging(serviceProvider);

        Console.WriteLine("\n--- Test 3: Logging with Scopes ---\n");
        await TestLoggingWithScopes(serviceProvider);

        Console.WriteLine("\n--- Test 4: Logging with Trace Correlation ---\n");
        await TestLoggingWithTraceCorrelation(serviceProvider);

        Console.WriteLine("\n--- Test 5: Exception Logging ---\n");
        await TestExceptionLogging(serviceProvider);

        Console.WriteLine("\n--- Test 6: All Log Levels ---\n");
        await TestAllLogLevels(serviceProvider);

        // Flush and cleanup
        await fileWriter.FlushAsync();
        await fileWriter.DisposeAsync();
        
        Console.WriteLine("\n==============================================");
        Console.WriteLine("Test Complete!");
        Console.WriteLine($"Logs written to: {logFilePath}");
        Console.WriteLine("==============================================");
        
        // Display file contents
        Console.WriteLine("\n--- File Contents ---\n");
        Console.WriteLine(await File.ReadAllTextAsync(logFilePath));
    }

    static Task TestBasicLogging(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        
        logger.LogInformation("This is a basic information log message");
        logger.LogWarning("This is a warning message");
        logger.LogError("This is an error message");
        
        return Task.CompletedTask;
    }

    static Task TestStructuredLogging(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        
        var userId = "user-12345";
        var orderId = Guid.NewGuid();
        var amount = 99.99m;
        
        logger.LogInformation(
            "Order {OrderId} created by user {UserId} for amount {Amount:C}",
            orderId, userId, amount);
        
        logger.LogInformation(
            "Processing request for customer {CustomerId} with items {ItemCount}",
            "CUST-001", 5);
        
        return Task.CompletedTask;
    }

    static Task TestLoggingWithScopes(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = Guid.NewGuid().ToString(),
            ["RequestId"] = "REQ-12345",
            ["UserId"] = "user-67890"
        }))
        {
            logger.LogInformation("Starting operation within scope");
            logger.LogInformation("Processing step 1");
            logger.LogInformation("Processing step 2");
            logger.LogInformation("Operation completed");
        }
        
        return Task.CompletedTask;
    }

    static Task TestLoggingWithTraceCorrelation(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        
        // Create an ActivitySource for tracing
        using var activitySource = new ActivitySource(ServiceName);
        
        // Start a trace/span
        using var activity = activitySource.StartActivity("TestOperation", ActivityKind.Internal);
        
        if (activity != null)
        {
            activity.SetTag("operation.type", "test");
            activity.SetTag("test.name", "TraceCorrelation");
            
            logger.LogInformation(
                "Log with trace context - TraceId: {TraceId}, SpanId: {SpanId}",
                activity.TraceId.ToString(),
                activity.SpanId.ToString());
            
            // Nested span
            using var childActivity = activitySource.StartActivity("ChildOperation");
            if (childActivity != null)
            {
                logger.LogInformation(
                    "Child operation log - TraceId: {TraceId}, SpanId: {SpanId}, ParentSpanId: {ParentSpanId}",
                    childActivity.TraceId.ToString(),
                    childActivity.SpanId.ToString(),
                    childActivity.ParentSpanId.ToString());
            }
        }
        
        return Task.CompletedTask;
    }

    static Task TestExceptionLogging(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        
        try
        {
            throw new InvalidOperationException("This is a test exception", 
                new ArgumentException("Inner exception details"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while processing the request");
        }
        
        try
        {
            SimulateNestedExceptionChain();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Critical error with nested exceptions");
        }
        
        return Task.CompletedTask;
    }

    static void SimulateNestedExceptionChain()
    {
        try
        {
            try
            {
                throw new ArgumentNullException("parameter", "Parameter cannot be null");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Operation failed due to invalid input", ex);
            }
        }
        catch (Exception ex)
        {
            throw new ApplicationException("Application error occurred", ex);
        }
    }

    static Task TestAllLogLevels(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        
        logger.LogTrace("TRACE: Detailed diagnostic information");
        logger.LogDebug("DEBUG: Debugging information for developers");
        logger.LogInformation("INFO: General operational information");
        logger.LogWarning("WARNING: Something unexpected but not critical");
        logger.LogError("ERROR: An error occurred but application continues");
        logger.LogCritical("CRITICAL: Application failure requiring immediate attention");
        
        return Task.CompletedTask;
    }
}

/// <summary>
/// Custom exporter that writes OpenTelemetry logs to a file.
/// </summary>
public class FileLogExporter : BaseExporter<LogRecord>
{
    private readonly StreamWriter _writer;
    
    public FileLogExporter(StreamWriter writer)
    {
        _writer = writer;
    }
    
    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        foreach (var logRecord in batch)
        {
            try
            {
                var line = FormatLogRecord(logRecord);
                _writer.WriteLine(line);
            }
            catch
            {
                return ExportResult.Failure;
            }
        }
        
        _writer.Flush();
        return ExportResult.Success;
    }
    
    private static string FormatLogRecord(LogRecord logRecord)
    {
        var sb = new System.Text.StringBuilder();
        
        // Timestamp
        sb.Append($"[{logRecord.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
        
        // Severity
        sb.Append($"[{logRecord.LogLevel,-11}] ");
        
        // Category
        if (!string.IsNullOrEmpty(logRecord.CategoryName))
        {
            sb.Append($"[{logRecord.CategoryName}] ");
        }
        
        // Trace context if available
        if (logRecord.TraceId != default)
        {
            sb.Append($"[TraceId:{logRecord.TraceId}] ");
        }
        if (logRecord.SpanId != default)
        {
            sb.Append($"[SpanId:{logRecord.SpanId}] ");
        }
        
        // Message
        if (!string.IsNullOrEmpty(logRecord.FormattedMessage))
        {
            sb.Append(logRecord.FormattedMessage);
        }
        else if (logRecord.Body != null)
        {
            sb.Append(logRecord.Body);
        }
        
        // Attributes
        if (logRecord.Attributes != null)
        {
            sb.Append(" | Attributes: {");
            var first = true;
            foreach (var kvp in logRecord.Attributes)
            {
                if (!first) sb.Append(", ");
                sb.Append($"{kvp.Key}={kvp.Value}");
                first = false;
            }
            sb.Append("}");
        }
        
        // Scopes
        logRecord.ForEachScope<System.Text.StringBuilder>((scope, builder) =>
        {
            builder.Append(" | Scope: {");
            var first = true;
            foreach (var kvp in scope)
            {
                if (!first) builder.Append(", ");
                builder.Append($"{kvp.Key}={kvp.Value}");
                first = false;
            }
            builder.Append("}");
        }, sb);
        
        // Exception
        if (logRecord.Exception != null)
        {
            sb.AppendLine();
            sb.Append($"  Exception: {logRecord.Exception.GetType().Name}: {logRecord.Exception.Message}");
            if (logRecord.Exception.InnerException != null)
            {
                sb.AppendLine();
                sb.Append($"  Inner: {logRecord.Exception.InnerException.GetType().Name}: {logRecord.Exception.InnerException.Message}");
            }
        }
        
        return sb.ToString();
    }
}
