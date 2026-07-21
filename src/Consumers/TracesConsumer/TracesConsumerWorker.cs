using Confluent.Kafka;
using Microsoft.Extensions.Options;
using LogMonitoring.Consumers.Traces.Services;
using LogMonitoring.Consumers.Traces.Models;

namespace LogMonitoring.Consumers.Traces;

/// <summary>
/// Worker service that consumes traces from Kafka and stores them in Elasticsearch.
/// </summary>
public class TracesConsumerWorker : BackgroundService
{
    private readonly ILogger<TracesConsumerWorker> _logger;
    private readonly KafkaConfig _kafkaConfig;
    private readonly ITracesRepository _tracesRepository;
    private readonly ITracesParser _tracesParser;
    private IConsumer<Ignore, byte[]>? _consumer;
    
    private const int BatchSize = 500;
    private const int BatchTimeoutMs = 3000;

    public TracesConsumerWorker(
        ILogger<TracesConsumerWorker> logger,
        IOptions<KafkaConfig> kafkaConfig,
        ITracesRepository tracesRepository,
        ITracesParser tracesParser)
    {
        _logger = logger;
        _kafkaConfig = kafkaConfig.Value;
        _tracesRepository = tracesRepository;
        _tracesParser = tracesParser;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Traces Consumer starting...");

        // Wait a bit for dependencies to be ready
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        var config = new ConsumerConfig
        {
            BootstrapServers = _kafkaConfig.BootstrapServers,
            GroupId = _kafkaConfig.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = false,
            MaxPollIntervalMs = 300000,
            SessionTimeoutMs = 45000,
            HeartbeatIntervalMs = 3000,
            FetchMinBytes = 1,
            FetchMaxBytes = 52428800,
            MaxPartitionFetchBytes = 2097152
        };

        try
        {
            using (_consumer = new ConsumerBuilder<Ignore, byte[]>(config)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Reason}", e.Reason))
                .SetPartitionsAssignedHandler((c, partitions) =>
                {
                    _logger.LogInformation("Partitions assigned: {Partitions}",
                        string.Join(", ", partitions.Select(p => $"{p.Topic}-{p.Partition}")));
                })
                .SetPartitionsRevokedHandler((c, partitions) =>
                {
                    _logger.LogInformation("Partitions revoked: {Partitions}",
                        string.Join(", ", partitions.Select(p => $"{p.Topic}-{p.Partition}")));
                })
                .Build())
            {
                _consumer.Subscribe(_kafkaConfig.TracesTopic);
                _logger.LogInformation("Subscribed to topic: {Topic}", _kafkaConfig.TracesTopic);

                await ConsumeMessagesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Traces Consumer shutting down...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in Traces Consumer");
            throw;
        }
    }

    private async Task ConsumeMessagesAsync(CancellationToken stoppingToken)
    {
        var batch = new List<SpanRecord>(BatchSize);
        var lastFlush = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = _consumer!.Consume(TimeSpan.FromMilliseconds(100));

                if (consumeResult != null)
                {
                    try
                    {
                        var spans = _tracesParser.Parse(consumeResult.Message.Value);
                        batch.AddRange(spans);

                        _logger.LogDebug("Parsed {Count} span records from message", spans.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse traces message, skipping...");
                    }
                }

                // Flush batch if size or time threshold reached
                var timeSinceFlush = DateTime.UtcNow - lastFlush;
                if (batch.Count >= BatchSize || timeSinceFlush.TotalMilliseconds >= BatchTimeoutMs)
                {
                    if (batch.Count > 0)
                    {
                        if (await FlushBatchAsync(batch, stoppingToken))
                        {
                            _consumer.Commit();
                            batch.Clear();
                            lastFlush = DateTime.UtcNow;
                        }
                    }
                }
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Error consuming message: {Reason}", ex.Error.Reason);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        // Final flush
        if (batch.Count > 0)
        {
            if (await FlushBatchAsync(batch, CancellationToken.None))
            {
                _consumer?.Commit();
            }
        }
    }

    private async Task<bool> FlushBatchAsync(List<SpanRecord> batch, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await _tracesRepository.IndexBatchAsync(batch, cancellationToken);
                sw.Stop();

                _logger.LogInformation(
                    "Indexed {Count} spans to Elasticsearch in {ElapsedMs}ms",
                    batch.Count,
                    sw.ElapsedMilliseconds);

                return true;
            }
            catch (Exception ex)
            {
                if (attempt == 3)
                {
                    _logger.LogError(ex, "Failed to index {Count} spans to Elasticsearch", batch.Count);
                    // TODO: Send to dead letter queue
                    return false;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
            }
        }

        return false;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Traces Consumer stopping...");
        // Let ExecuteAsync finish (final flush + commit) before closing the consumer,
        // otherwise the shutdown commit runs against an already-closed consumer.
        await base.StopAsync(cancellationToken);
        _consumer?.Close();
    }
}
