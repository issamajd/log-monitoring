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

    public async Task IndexBatchAsync(IReadOnlyList<LogRecord> logs, CancellationToken cancellationToken = default)
    {
        if (logs.Count == 0)
            return;

        var bulkRequest = new BulkRequest(_indexAlias)
        {
            Operations = new List<IBulkOperation>()
        };

        foreach (var log in logs)
        {
            var indexOp = new BulkIndexOperation<LogRecord>(log);
            bulkRequest.Operations.Add(indexOp);
        }

        var response = await _client.BulkAsync(bulkRequest, cancellationToken);

        if (response.Errors)
        {
            var failedCount = response.ItemsWithErrors.Count();
            _logger.LogWarning(
                "Bulk index completed with {FailedCount} errors out of {TotalCount} documents",
                failedCount,
                logs.Count);

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
                logs.Count,
                response.Took);
        }
    }

    public void Dispose()
    {
        // ElasticsearchClient doesn't need explicit disposal
    }
}
