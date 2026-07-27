using LogMonitoring.Consumers.Traces.Models;

namespace LogMonitoring.Consumers.Traces.Services;

/// <summary>
/// Interface for trace storage operations.
/// </summary>
public interface ITracesRepository
{
    /// <summary>
    /// Indexes a batch of spans into the search engine.
    /// </summary>
    /// <param name="spans">The spans to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-document rejections returned by Elasticsearch.</returns>
    Task<IReadOnlyList<SpanIndexRejection>> IndexBatchAsync(
        IReadOnlyList<SpanRecord> spans,
        CancellationToken cancellationToken = default);
}
