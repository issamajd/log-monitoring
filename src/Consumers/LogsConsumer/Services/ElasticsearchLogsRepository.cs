using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Transport;
using Microsoft.Extensions.Options;
using LogMonitoring.Consumers.Logs.Models;

namespace LogMonitoring.Consumers.Logs.Services;

/// <summary>
/// Elasticsearch implementation of the logs repository.
/// Uses bulk API for high-performance indexing.
/// </summary>
public class ElasticsearchLogsRepository : ILogsRepository, IDisposable
{
    private const int MaxBulkAttempts = 5;
    private const int BaseRetryDelayMs = 500;

    private readonly ILogger<ElasticsearchLogsRepository> _logger;
    private readonly ElasticsearchClient _client;
    private readonly string _indexAlias;

    public ElasticsearchLogsRepository(
        ILogger<ElasticsearchLogsRepository> logger,
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

    public async Task<IReadOnlyList<LogIndexRejection>> IndexBatchAsync(
        IReadOnlyList<LogRecord> logs,
        CancellationToken cancellationToken = default)
    {
        if (logs.Count == 0)
            return Array.Empty<LogIndexRejection>();

        List<LogIndexRejection> rejections = new();
        List<LogRecord> pendingLogs = new(logs);
        int exhaustedRetryCount = 0;
        long totalTookMs = 0;

        for (int attempt = 1; attempt <= MaxBulkAttempts; attempt++)
        {
            BulkRequest bulkRequest = new BulkRequest(_indexAlias)
            {
                Operations = new List<IBulkOperation>()
            };

            foreach (LogRecord log in pendingLogs)
            {
                BulkIndexOperation<LogRecord> indexOperation = new BulkIndexOperation<LogRecord>(log);
                bulkRequest.Operations.Add(indexOperation);
            }

            BulkResponse response = await _client.BulkAsync(bulkRequest, cancellationToken);

            if (!response.IsValidResponse)
            {
                // Transport-level failure (ES down, timeout, 5xx): throw so the worker
                // retries and does not commit Kafka offsets for this batch.
                throw new InvalidOperationException(
                    $"Elasticsearch bulk request failed: {response.ApiCallDetails?.OriginalException?.Message ?? response.ApiCallDetails?.ToString() ?? "unknown error"}");
            }

            totalTookMs += response.Took;
            List<LogRecord> retriableLogs = new();

            for (int index = 0; index < response.Items.Count; index++)
            {
                ResponseItem item = response.Items[index];
                if (item.IsValid)
                    continue;

                LogRecord log = pendingLogs[index];
                bool isRetriable = item.Status is 429 or 503;
                if (isRetriable && attempt < MaxBulkAttempts)
                {
                    // This list preserves the response-position-to-document mapping
                    // after each retry request is narrowed to a smaller subset.
                    retriableLogs.Add(log);
                    continue;
                }

                rejections.Add(new LogIndexRejection(
                    log,
                    item.Error?.Type ?? "unknown",
                    item.Error?.Reason ?? "Unknown error",
                    item.Status));

                if (isRetriable)
                    exhaustedRetryCount++;
            }

            if (retriableLogs.Count == 0)
                break;

            TimeSpan retryDelay = TimeSpan.FromMilliseconds(
                BaseRetryDelayMs * (1 << (attempt - 1)));
            await Task.Delay(retryDelay, cancellationToken);
            pendingLogs = retriableLogs;
        }

        if (exhaustedRetryCount > 0)
        {
            _logger.LogWarning(
                "Elasticsearch bulk retries were exhausted for {FailedCount} documents",
                exhaustedRetryCount);
        }

        if (rejections.Count > 0)
        {
            _logger.LogWarning(
                "Bulk index completed with {FailedCount} errors out of {TotalCount} documents",
                rejections.Count,
                logs.Count);

            foreach (LogIndexRejection rejection in rejections.Take(5))
            {
                _logger.LogWarning(
                    "Failed to index document: {Error}",
                    rejection.ErrorReason);
            }
        }
        else
        {
            _logger.LogDebug(
                "Successfully indexed {Count} documents in {TookMs}ms",
                logs.Count,
                totalTookMs);
        }

        return rejections;
    }

    public void Dispose()
    {
        // ElasticsearchClient doesn't need explicit disposal
    }
}
