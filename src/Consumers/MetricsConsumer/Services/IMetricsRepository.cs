using LogMonitoring.Consumers.Metrics.Models;

namespace LogMonitoring.Consumers.Metrics.Services;

/// <summary>
/// Interface for metrics storage operations.
/// </summary>
public interface IMetricsRepository
{
    /// <summary>
    /// Inserts a batch of metrics into the database.
    /// </summary>
    /// <param name="metrics">The metrics to insert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InsertBatchAsync(IReadOnlyList<MetricDataPoint> metrics, CancellationToken cancellationToken = default);
}
