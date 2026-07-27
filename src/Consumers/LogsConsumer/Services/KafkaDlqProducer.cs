using System.Text.Json;
using Confluent.Kafka;
using LogMonitoring.Consumers.Logs.Models;
using Microsoft.Extensions.Options;

namespace LogMonitoring.Consumers.Logs.Services;

/// <summary>
/// Publishes failed log documents to the Kafka dead-letter topic.
/// </summary>
public sealed class KafkaDlqProducer : IDisposable
{
    private const string ConsumerName = "logs-consumer";

    private readonly ILogger<KafkaDlqProducer> _logger;
    private readonly string _sourceTopic;
    private readonly string? _dlqTopic;
    private readonly IProducer<Null, string>? _producer;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaDlqProducer"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="kafkaConfig">The Kafka configuration.</param>
    public KafkaDlqProducer(
        ILogger<KafkaDlqProducer> logger,
        IOptions<KafkaConfig> kafkaConfig)
    {
        _logger = logger;
        _sourceTopic = kafkaConfig.Value.LogsTopic;
        _dlqTopic = kafkaConfig.Value.DlqTopic;

        if (string.IsNullOrEmpty(_dlqTopic))
            return;

        ProducerConfig producerConfig = new ProducerConfig
        {
            BootstrapServers = kafkaConfig.Value.BootstrapServers
        };

        _producer = new ProducerBuilder<Null, string>(producerConfig).Build();
    }

    /// <summary>
    /// Publishes an Elasticsearch-rejected log document.
    /// </summary>
    /// <param name="document">The rejected log document.</param>
    /// <param name="errorType">The Elasticsearch error type.</param>
    /// <param name="errorReason">The Elasticsearch error reason.</param>
    /// <param name="statusCode">The HTTP status code for the rejected item.</param>
    public void PublishRejectedDocument(
        LogRecord document,
        string errorType,
        string errorReason,
        int statusCode)
    {
        if (_producer is null)
            return;

        JsonElement documentJson = JsonSerializer.SerializeToElement(document);
        Publish(documentJson, null, errorType, errorReason, statusCode);
    }

    /// <summary>
    /// Publishes a Kafka payload that could not be parsed.
    /// </summary>
    /// <param name="rawPayload">The raw Kafka message payload.</param>
    /// <param name="errorReason">The parser exception message.</param>
    public void PublishParseFailure(byte[] rawPayload, string errorReason)
    {
        if (_producer is null)
            return;

        string rawPayloadBase64 = Convert.ToBase64String(rawPayload);
        Publish(null, rawPayloadBase64, "parse_failure", errorReason, null);
    }

    private void Publish(
        JsonElement? document,
        string? rawPayloadBase64,
        string errorType,
        string errorReason,
        int? statusCode)
    {
        if (_producer is null || string.IsNullOrEmpty(_dlqTopic))
            return;

        string payload = JsonSerializer.Serialize(new
        {
            source_topic = _sourceTopic,
            consumer = ConsumerName,
            error_type = errorType,
            error_reason = errorReason,
            status_code = statusCode,
            failed_at_utc = DateTimeOffset.UtcNow,
            document,
            raw_payload_base64 = rawPayloadBase64
        });

        Message<Null, string> message = new Message<Null, string>
        {
            Value = payload
        };

        _producer.Produce(_dlqTopic, message, deliveryReport =>
        {
            if (deliveryReport.Error.IsError)
            {
                _logger.LogError(
                    "Failed to publish log document to DLQ topic {Topic}: {Reason}",
                    _dlqTopic,
                    deliveryReport.Error.Reason);
            }
        });
    }

    /// <summary>
    /// Flushes and disposes the Kafka producer.
    /// </summary>
    public void Dispose()
    {
        if (_producer is null)
            return;

        try
        {
            int remainingMessages = _producer.Flush(TimeSpan.FromSeconds(10));
            if (remainingMessages > 0)
            {
                _logger.LogWarning(
                    "{RemainingCount} log DLQ messages remained after producer flush",
                    remainingMessages);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush the log DLQ producer");
        }
        finally
        {
            _producer.Dispose();
        }
    }
}
