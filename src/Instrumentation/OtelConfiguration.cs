using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LogMonitoring.Instrumentation;

/// <summary>
/// OpenTelemetry configuration for .NET applications.
/// Configures tracing, metrics, and logging export to OTLP endpoint.
/// </summary>
public static class OtelConfiguration
{
    private const string DefaultOtlpEndpoint = "http://localhost:4317";
    
    /// <summary>
    /// Configures OpenTelemetry for the application with default settings.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="serviceName">The name of the service.</param>
    /// <param name="serviceVersion">The version of the service.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddLogMonitoringTelemetry(
        this IServiceCollection services,
        string serviceName,
        string? serviceVersion = null)
    {
        var options = new OtelOptions
        {
            ServiceName = serviceName,
            ServiceVersion = serviceVersion ?? "1.0.0",
            OtlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? DefaultOtlpEndpoint
        };

        return services.AddLogMonitoringTelemetry(options);
    }

    /// <summary>
    /// Configures OpenTelemetry for the application with custom options.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="options">The telemetry options.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddLogMonitoringTelemetry(
        this IServiceCollection services,
        OtelOptions options)
    {
        // Build resource attributes
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: options.ServiceName,
                serviceVersion: options.ServiceVersion,
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new[]
            {
                new KeyValuePair<string, object>("deployment.environment", 
                    Environment.GetEnvironmentVariable("DEPLOYMENT_ENV") ?? "development"),
                new KeyValuePair<string, object>("host.name", Environment.MachineName),
                new KeyValuePair<string, object>("service.namespace", "log-monitoring")
            });

        // Configure Tracing
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(resourceBuilder)
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(options.TraceSampleRate)))
                    // Auto-instrumentation
                    .AddHttpClientInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                        opts.FilterHttpRequestMessage = request =>
                        {
                            // Filter out health checks
                            return !request.RequestUri?.PathAndQuery.Contains("health") ?? true;
                        };
                    })
                    .AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                        opts.Filter = context =>
                        {
                            // Filter out health checks and metrics endpoints
                            var path = context.Request.Path.Value ?? "";
                            return !path.Contains("health") && 
                                   !path.Contains("metrics") &&
                                   !path.Contains("favicon");
                        };
                        opts.EnrichWithHttpRequest = (activity, request) =>
                        {
                            activity.SetTag("http.client_ip", request.HttpContext.Connection.RemoteIpAddress?.ToString());
                            activity.SetTag("http.user_agent", request.Headers["User-Agent"].ToString());
                        };
                    })
                    .AddSqlClientInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                        opts.SetDbStatementForText = true;
                        opts.SetDbStatementForStoredProcedure = true;
                    })
                    // Add custom instrumentation sources
                    .AddSource(options.ServiceName)
                    .AddSource("LogMonitoring.*");

                // Add WCF instrumentation if available
                if (options.EnableWcfInstrumentation)
                {
                    // WCF instrumentation would be added here
                    // builder.AddWcfInstrumentation();
                }

                // Configure exporter
                if (options.EnableOtlpExporter)
                {
                    builder.AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = new Uri(options.OtlpEndpoint);
                        opts.Protocol = OtlpExportProtocol.Grpc;
                        opts.ExportProcessorType = ExportProcessorType.Batch;
                        opts.BatchExportProcessorOptions = new BatchExportProcessorOptions<System.Diagnostics.Activity>
                        {
                            MaxQueueSize = 2048,
                            ScheduledDelayMilliseconds = 5000,
                            ExporterTimeoutMilliseconds = 30000,
                            MaxExportBatchSize = 512
                        };
                    });
                }

                if (options.EnableConsoleExporter)
                {
                    builder.AddConsoleExporter();
                }
            })
            // Configure Metrics
            .WithMetrics(builder =>
            {
                builder
                    .SetResourceBuilder(resourceBuilder)
                    // Runtime metrics
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    // HTTP metrics
                    .AddHttpClientInstrumentation()
                    .AddAspNetCoreInstrumentation()
                    // Custom metrics
                    .AddMeter(options.ServiceName)
                    .AddMeter("LogMonitoring.*")
                    .AddMeter(CustomMetrics.MeterName);

                // Configure exporter
                if (options.EnableOtlpExporter)
                {
                    builder.AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = new Uri(options.OtlpEndpoint);
                        opts.Protocol = OtlpExportProtocol.Grpc;
                        opts.ExportProcessorType = ExportProcessorType.Batch;
                        opts.PeriodicExportingMetricReaderOptions = new PeriodicExportingMetricReaderOptions
                        {
                            ExportIntervalMilliseconds = 10000,
                            ExportTimeoutMilliseconds = 15000
                        };
                    });
                }

                if (options.EnableConsoleExporter)
                {
                    builder.AddConsoleExporter();
                }

                if (options.EnablePrometheusExporter)
                {
                    builder.AddPrometheusExporter();
                }
            });

        // Register custom metrics
        services.AddSingleton<CustomMetrics>();

        return services;
    }

    /// <summary>
    /// Configures OpenTelemetry logging for the application.
    /// Call this in your logging configuration.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="options">The telemetry options.</param>
    /// <returns>The configured logging builder.</returns>
    public static ILoggingBuilder AddLogMonitoringLogging(
        this ILoggingBuilder builder,
        OtelOptions options)
    {
        builder.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;

            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(
                    serviceName: options.ServiceName,
                    serviceVersion: options.ServiceVersion,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment",
                        Environment.GetEnvironmentVariable("DEPLOYMENT_ENV") ?? "development"),
                    new KeyValuePair<string, object>("host.name", Environment.MachineName),
                    new KeyValuePair<string, object>("service.namespace", "log-monitoring")
                });

            logging.SetResourceBuilder(resourceBuilder);

            if (options.EnableOtlpExporter)
            {
                logging.AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri(options.OtlpEndpoint);
                    opts.Protocol = OtlpExportProtocol.Grpc;
                });
            }

            if (options.EnableConsoleExporter)
            {
                logging.AddConsoleExporter();
            }
        });

        return builder;
    }
}

/// <summary>
/// Options for configuring OpenTelemetry.
/// </summary>
public class OtelOptions
{
    /// <summary>
    /// The name of the service.
    /// </summary>
    public required string ServiceName { get; set; }

    /// <summary>
    /// The version of the service.
    /// </summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>
    /// The OTLP endpoint URL.
    /// </summary>
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";

    /// <summary>
    /// The trace sample rate (0.0 to 1.0).
    /// </summary>
    public double TraceSampleRate { get; set; } = 1.0;

    /// <summary>
    /// Enable OTLP exporter.
    /// </summary>
    public bool EnableOtlpExporter { get; set; } = true;

    /// <summary>
    /// Enable console exporter for debugging.
    /// </summary>
    public bool EnableConsoleExporter { get; set; } = false;

    /// <summary>
    /// Enable Prometheus metrics exporter.
    /// </summary>
    public bool EnablePrometheusExporter { get; set; } = false;

    /// <summary>
    /// Enable WCF instrumentation.
    /// </summary>
    public bool EnableWcfInstrumentation { get; set; } = false;
}
