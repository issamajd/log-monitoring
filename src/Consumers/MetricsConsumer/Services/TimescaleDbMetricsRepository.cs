using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using LogMonitoring.Consumers.Metrics.Models;

namespace LogMonitoring.Consumers.Metrics.Services;

/// <summary>
/// TimescaleDB implementation of the metrics repository.
/// Uses batch COPY for high-performance inserts.
/// </summary>
public class TimescaleDbMetricsRepository : IMetricsRepository
{
    private readonly ILogger<TimescaleDbMetricsRepository> _logger;
    private readonly string _connectionString;

    public TimescaleDbMetricsRepository(
        ILogger<TimescaleDbMetricsRepository> logger,
        IOptions<TimescaleDbConfig> config)
    {
        _logger = logger;
        _connectionString = config.Value.ConnectionString;
    }

    public async Task InsertBatchAsync(IReadOnlyList<MetricDataPoint> metrics, CancellationToken cancellationToken = default)
    {
        if (metrics.Count == 0)
            return;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Use COPY for high-performance bulk insert
        await using var writer = await connection.BeginBinaryImportAsync(
            @"COPY metrics.raw_metrics (time, metric_name, value, service_name, service_version, host_name, environment, labels, metric_type, unit) 
              FROM STDIN (FORMAT BINARY)",
            cancellationToken);

        foreach (var metric in metrics)
        {
            await writer.StartRowAsync(cancellationToken);
            
            await writer.WriteAsync(metric.Time.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
            await writer.WriteAsync(metric.MetricName, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(metric.Value, NpgsqlDbType.Double, cancellationToken);
            await writer.WriteAsync(metric.ServiceName ?? (object)DBNull.Value, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(metric.ServiceVersion ?? (object)DBNull.Value, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(metric.HostName ?? (object)DBNull.Value, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(metric.Environment ?? (object)DBNull.Value, NpgsqlDbType.Text, cancellationToken);
            
            // Serialize labels to JSON
            var labelsJson = metric.Labels.Count > 0 
                ? JsonSerializer.Serialize(metric.Labels) 
                : "{}";
            await writer.WriteAsync(labelsJson, NpgsqlDbType.Jsonb, cancellationToken);
            
            await writer.WriteAsync(metric.MetricType ?? (object)DBNull.Value, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(metric.Unit ?? (object)DBNull.Value, NpgsqlDbType.Text, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);

        _logger.LogDebug("Inserted {Count} metrics using COPY", metrics.Count);
    }
}
