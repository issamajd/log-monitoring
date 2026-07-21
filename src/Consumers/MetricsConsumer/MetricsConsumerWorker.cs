using Confluent.Kafka;
using Microsoft.Extensions.Options;
using LogMonitoring.Consumers.Metrics.Services;
using LogMonitoring.Consumers.Metrics.Models;

namespace LogMonitoring.Consumers.Metrics;

/// <summary>
/// Worker service that consumes metrics from Kafka and stores them in TimescaleDB.
/// </summary>
public class MetricsConsumerWorker : BackgroundService
{
    private readonly ILogger<MetricsConsumerWorker> _logger;
    private readonly KafkaConfig _kafkaConfig;
    private readonly IMetricsRepository _metricsRepository;
    private readonly IMetricsParser _metricsParser;
    private IConsumer<Ignore, byte[]>? _consumer;
    
    private const int BatchSize = 1000;
    private const int BatchTimeoutMs = 5000;

    public MetricsConsumerWorker(
        ILogger<MetricsConsumerWorker> logger,
        IOptions<KafkaConfig> kafkaConfig,
        IMetricsRepository metricsRepository,
        IMetricsParser metricsParser)
    {
        _logger = logger;
        _kafkaConfig = kafkaConfig.Value;
        _metricsRepository = metricsRepository;
        _metricsParser = metricsParser;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Metrics Consumer starting...");

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
            MaxPartitionFetchBytes = 1048576
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
                _consumer.Subscribe(_kafkaConfig.MetricsTopic);
                _logger.LogInformation("Subscribed to topic: {Topic}", _kafkaConfig.MetricsTopic);

                await ConsumeMessagesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Metrics Consumer shutting down...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in Metrics Consumer");
            throw;
        }
    }

    private async Task ConsumeMessagesAsync(CancellationToken stoppingToken)
    {
        var batch = new List<MetricDataPoint>(BatchSize);
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
                        var metrics = _metricsParser.Parse(consumeResult.Message.Value);
                        batch.AddRange(metrics);

                        _logger.LogDebug("Parsed {Count} metrics from message", metrics.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse metrics message, skipping...");
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

    private async Task<bool> FlushBatchAsync(List<MetricDataPoint> batch, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await _metricsRepository.InsertBatchAsync(batch, cancellationToken);
                sw.Stop();

                _logger.LogInformation(
                    "Flushed {Count} metrics to TimescaleDB in {ElapsedMs}ms",
                    batch.Count,
                    sw.ElapsedMilliseconds);

                return true;
            }
            catch (Exception ex)
            {
                if (attempt == 3)
                {
                    _logger.LogError(ex, "Failed to flush {Count} metrics to TimescaleDB", batch.Count);
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
        _logger.LogInformation("Metrics Consumer stopping...");
        // Let ExecuteAsync finish (final flush + commit) before closing the consumer,
        // otherwise the shutdown commit runs against an already-closed consumer.
        await base.StopAsync(cancellationToken);
        _consumer?.Close();
    }
}
