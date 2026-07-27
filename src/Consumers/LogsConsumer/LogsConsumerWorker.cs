using Confluent.Kafka;
using Microsoft.Extensions.Options;
using LogMonitoring.Consumers.Logs.Services;
using LogMonitoring.Consumers.Logs.Models;

namespace LogMonitoring.Consumers.Logs;

/// <summary>
/// Worker service that consumes logs from Kafka and stores them in Elasticsearch.
/// </summary>
public class LogsConsumerWorker : BackgroundService
{
    private readonly ILogger<LogsConsumerWorker> _logger;
    private readonly KafkaConfig _kafkaConfig;
    private readonly ILogsRepository _logsRepository;
    private readonly ILogsParser _logsParser;
    private readonly KafkaDlqProducer _dlqProducer;
    private IConsumer<Ignore, byte[]>? _consumer;
    
    private const int BatchSize = 500;
    private const int BatchTimeoutMs = 3000;

    public LogsConsumerWorker(
        ILogger<LogsConsumerWorker> logger,
        IOptions<KafkaConfig> kafkaConfig,
        ILogsRepository logsRepository,
        ILogsParser logsParser,
        KafkaDlqProducer dlqProducer)
    {
        _logger = logger;
        _kafkaConfig = kafkaConfig.Value;
        _logsRepository = logsRepository;
        _logsParser = logsParser;
        _dlqProducer = dlqProducer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Logs Consumer starting...");

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
                _consumer.Subscribe(_kafkaConfig.LogsTopic);
                _logger.LogInformation("Subscribed to topic: {Topic}", _kafkaConfig.LogsTopic);

                await ConsumeMessagesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Logs Consumer shutting down...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in Logs Consumer");
            throw;
        }
    }

    private async Task ConsumeMessagesAsync(CancellationToken stoppingToken)
    {
        var batch = new List<LogRecord>(BatchSize);
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
                        var logs = _logsParser.Parse(consumeResult.Message.Value);
                        batch.AddRange(logs);

                        _logger.LogDebug("Parsed {Count} log records from message", logs.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse logs message, skipping...");

                        try
                        {
                            _dlqProducer.PublishParseFailure(
                                consumeResult.Message.Value,
                                ex.Message);
                        }
                        catch (Exception dlqException)
                        {
                            _logger.LogError(
                                dlqException,
                                "Failed to publish logs parse failure to the DLQ");
                        }
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

    private async Task<bool> FlushBatchAsync(List<LogRecord> batch, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                IReadOnlyList<LogIndexRejection> rejections =
                    await _logsRepository.IndexBatchAsync(batch, cancellationToken);
                sw.Stop();

                _logger.LogInformation(
                    "Indexed {Count} logs to Elasticsearch in {ElapsedMs}ms",
                    batch.Count,
                    sw.ElapsedMilliseconds);

                foreach (LogIndexRejection rejection in rejections)
                {
                    try
                    {
                        _dlqProducer.PublishRejectedDocument(
                            rejection.Document,
                            rejection.ErrorType,
                            rejection.ErrorReason,
                            rejection.StatusCode);
                    }
                    catch (Exception dlqException)
                    {
                        _logger.LogError(
                            dlqException,
                            "Failed to publish rejected log document to the DLQ");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                if (attempt == 3)
                {
                    _logger.LogError(ex, "Failed to index {Count} logs to Elasticsearch", batch.Count);
                    return false;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
            }
        }

        return false;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Logs Consumer stopping...");
        // Let ExecuteAsync finish (final flush + commit) before closing the consumer,
        // otherwise the shutdown commit runs against an already-closed consumer.
        await base.StopAsync(cancellationToken);
        _consumer?.Close();
    }
}
