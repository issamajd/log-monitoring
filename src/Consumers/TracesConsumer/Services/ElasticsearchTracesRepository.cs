using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Transport;
using Microsoft.Extensions.Options;
using LogMonitoring.Consumers.Traces.Models;

namespace LogMonitoring.Consumers.Traces.Services;

/// <summary>
/// Elasticsearch implementation of the traces repository.
/// Uses bulk API for high-performance indexing.
/// </summary>
public class ElasticsearchTracesRepository : ITracesRepository, IDisposable
{
    private readonly ILogger<ElasticsearchTracesRepository> _logger;
    private readonly ElasticsearchClient _client;
    private readonly string _indexAlias;

    public ElasticsearchTracesRepository(
        ILogger<ElasticsearchTracesRepository> logger,
        IOptions<ElasticsearchConfig> config)
    {
        _logger = logger;
        _indexAlias = config.Value.IndexAlias;

        var settings = new ElasticsearchClientSettings(new Uri(config.Value.Url))
            .DisableDirectStreaming()
            .RequestTimeout(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrEmpty(config.Value.Username) && !string.IsNullOrEmpty(config.Value.Password))
        {
            settings = settings.Authentication(new BasicAuthentication(config.Value.Username, config.Value.Password));
        }

        _client = new ElasticsearchClient(settings);
    }

    public async Task IndexBatchAsync(IReadOnlyList<SpanRecord> spans, CancellationToken cancellationToken = default)
    {
        if (spans.Count == 0)
            return;

        var bulkRequest = new BulkRequest(_indexAlias)
        {
            Operations = new List<IBulkOperation>()
        };

        foreach (var span in spans)
        {
            var indexOp = new BulkIndexOperation<SpanRecord>(span);
            bulkRequest.Operations.Add(indexOp);
        }

        var response = await _client.BulkAsync(bulkRequest, cancellationToken);

        if (!response.IsValidResponse)
        {
            // Transport-level failure (ES down, timeout, 5xx): throw so the worker
            // retries and does not commit Kafka offsets for this batch.
            throw new InvalidOperationException(
                $"Elasticsearch bulk request failed: {response.ApiCallDetails?.OriginalException?.Message ?? response.ApiCallDetails?.ToString() ?? "unknown error"}");
        }

        if (response.Errors)
        {
            var failedCount = response.ItemsWithErrors.Count();
            _logger.LogWarning(
                "Bulk index completed with {FailedCount} errors out of {TotalCount} documents",
                failedCount,
                spans.Count);

            foreach (var item in response.ItemsWithErrors.Take(5))
            {
                _logger.LogWarning(
                    "Failed to index document: {Error}",
                    item.Error?.Reason ?? "Unknown error");
            }
        }
        else
        {
            _logger.LogDebug(
                "Successfully indexed {Count} documents in {TookMs}ms",
                spans.Count,
                response.Took);
        }
    }

    public void Dispose()
    {
        // ElasticsearchClient doesn't need explicit disposal
    }
}
