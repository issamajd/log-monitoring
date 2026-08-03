using LogMonitoring.Consumers.Logs.Models;

namespace LogMonitoring.Consumers.Logs.Services;

/// <summary>
/// Interface for log storage operations.
/// </summary>
public interface ILogsRepository
{
    /// <summary>
    /// Indexes a batch of logs into the search engine.
    /// </summary>
    /// <param name="logs">The logs to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-document rejections returned by Elasticsearch.</returns>
    Task<IReadOnlyList<LogIndexRejection>> IndexBatchAsync(
        IReadOnlyList<LogRecord> logs,
        CancellationToken cancellationToken = default);
}
