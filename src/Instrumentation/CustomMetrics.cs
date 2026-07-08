using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LogMonitoring.Instrumentation;

/// <summary>
/// Custom metrics for business and application monitoring.
/// Provides counters, histograms, and gauges for tracking application performance.
/// </summary>
public class CustomMetrics : IDisposable
{
    public const string MeterName = "LogMonitoring.CustomMetrics";
    
    private readonly Meter _meter;
    private bool _disposed;

    // Request metrics
    private readonly Counter<long> _requestCounter;
    private readonly Counter<long> _requestErrorCounter;
    private readonly Histogram<double> _requestDuration;
    
    // Business metrics
    private readonly Counter<long> _businessOperationCounter;
    private readonly Counter<long> _businessErrorCounter;
    private readonly Histogram<double> _businessOperationDuration;
    
    // Database metrics
    private readonly Counter<long> _dbQueryCounter;
    private readonly Counter<long> _dbErrorCounter;
    private readonly Histogram<double> _dbQueryDuration;
    
    // Queue metrics
    private readonly Counter<long> _messagePublishedCounter;
    private readonly Counter<long> _messageConsumedCounter;
    private readonly Counter<long> _messageFailedCounter;
    
    // Cache metrics
    private readonly Counter<long> _cacheHitCounter;
    private readonly Counter<long> _cacheMissCounter;
    
    // Authentication metrics
    private readonly Counter<long> _authSuccessCounter;
    private readonly Counter<long> _authFailureCounter;
    
    // Custom gauges for current state
    private readonly ObservableGauge<int> _activeConnectionsGauge;
    private readonly ObservableGauge<int> _queueDepthGauge;
    
    // State for observable gauges
    private int _activeConnections;
    private int _queueDepth;

    public CustomMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        // Initialize request metrics
        _requestCounter = _meter.CreateCounter<long>(
            "app.requests.total",
            unit: "{requests}",
            description: "Total number of requests processed");

        _requestErrorCounter = _meter.CreateCounter<long>(
            "app.requests.errors.total",
            unit: "{requests}",
            description: "Total number of request errors");

        _requestDuration = _meter.CreateHistogram<double>(
            "app.requests.duration",
            unit: "ms",
            description: "Duration of request processing in milliseconds");

        // Initialize business metrics
        _businessOperationCounter = _meter.CreateCounter<long>(
            "app.business.operations.total",
            unit: "{operations}",
            description: "Total number of business operations");

        _businessErrorCounter = _meter.CreateCounter<long>(
            "app.business.errors.total",
            unit: "{errors}",
            description: "Total number of business operation errors");

        _businessOperationDuration = _meter.CreateHistogram<double>(
            "app.business.duration",
            unit: "ms",
            description: "Duration of business operations in milliseconds");

        // Initialize database metrics
        _dbQueryCounter = _meter.CreateCounter<long>(
            "app.db.queries.total",
            unit: "{queries}",
            description: "Total number of database queries");

        _dbErrorCounter = _meter.CreateCounter<long>(
            "app.db.errors.total",
            unit: "{errors}",
            description: "Total number of database errors");

        _dbQueryDuration = _meter.CreateHistogram<double>(
            "app.db.query.duration",
            unit: "ms",
            description: "Duration of database queries in milliseconds");

        // Initialize queue metrics
        _messagePublishedCounter = _meter.CreateCounter<long>(
            "app.queue.messages.published.total",
            unit: "{messages}",
            description: "Total number of messages published to queue");

        _messageConsumedCounter = _meter.CreateCounter<long>(
            "app.queue.messages.consumed.total",
            unit: "{messages}",
            description: "Total number of messages consumed from queue");

        _messageFailedCounter = _meter.CreateCounter<long>(
            "app.queue.messages.failed.total",
            unit: "{messages}",
            description: "Total number of failed message processing");

        // Initialize cache metrics
        _cacheHitCounter = _meter.CreateCounter<long>(
            "app.cache.hits.total",
            unit: "{hits}",
            description: "Total number of cache hits");

        _cacheMissCounter = _meter.CreateCounter<long>(
            "app.cache.misses.total",
            unit: "{misses}",
            description: "Total number of cache misses");

        // Initialize auth metrics
        _authSuccessCounter = _meter.CreateCounter<long>(
            "app.auth.success.total",
            unit: "{authentications}",
            description: "Total successful authentications");

        _authFailureCounter = _meter.CreateCounter<long>(
            "app.auth.failures.total",
            unit: "{authentications}",
            description: "Total failed authentications");

        // Initialize observable gauges
        _activeConnectionsGauge = _meter.CreateObservableGauge(
            "app.connections.active",
            () => _activeConnections,
            unit: "{connections}",
            description: "Current number of active connections");

        _queueDepthGauge = _meter.CreateObservableGauge(
            "app.queue.depth",
            () => _queueDepth,
            unit: "{messages}",
            description: "Current queue depth");
    }

    #region Request Metrics

    /// <summary>
    /// Records a request with its duration.
    /// </summary>
    public void RecordRequest(string endpoint, string method, int statusCode, double durationMs)
    {
        var tags = new TagList
        {
            { "endpoint", endpoint },
            { "method", method },
            { "status_code", statusCode.ToString() },
            { "status_class", GetStatusClass(statusCode) }
        };

        _requestCounter.Add(1, tags);
        _requestDuration.Record(durationMs, tags);

        if (statusCode >= 400)
        {
            _requestErrorCounter.Add(1, tags);
        }
    }

    /// <summary>
    /// Creates a scope for measuring request duration.
    /// </summary>
    public RequestDurationScope MeasureRequestDuration(string endpoint, string method)
    {
        return new RequestDurationScope(this, endpoint, method);
    }

    #endregion

    #region Business Metrics

    /// <summary>
    /// Records a business operation.
    /// </summary>
    public void RecordBusinessOperation(string operationType, bool success, double durationMs, string? customerId = null)
    {
        var tags = new TagList
        {
            { "operation_type", operationType },
            { "success", success.ToString().ToLower() }
        };

        if (!string.IsNullOrEmpty(customerId))
        {
            tags.Add("customer_id", customerId);
        }

        _businessOperationCounter.Add(1, tags);
        _businessOperationDuration.Record(durationMs, tags);

        if (!success)
        {
            _businessErrorCounter.Add(1, tags);
        }
    }

    /// <summary>
    /// Creates a scope for measuring business operation duration.
    /// </summary>
    public BusinessOperationScope MeasureBusinessOperation(string operationType, string? customerId = null)
    {
        return new BusinessOperationScope(this, operationType, customerId);
    }

    #endregion

    #region Database Metrics

    /// <summary>
    /// Records a database query.
    /// </summary>
    public void RecordDbQuery(string operationType, string tableName, bool success, double durationMs)
    {
        var tags = new TagList
        {
            { "operation_type", operationType },
            { "table_name", tableName },
            { "success", success.ToString().ToLower() }
        };

        _dbQueryCounter.Add(1, tags);
        _dbQueryDuration.Record(durationMs, tags);

        if (!success)
        {
            _dbErrorCounter.Add(1, tags);
        }
    }

    #endregion

    #region Queue Metrics

    /// <summary>
    /// Records a message published to a queue.
    /// </summary>
    public void RecordMessagePublished(string topic, int count = 1)
    {
        _messagePublishedCounter.Add(count, new TagList { { "topic", topic } });
    }

    /// <summary>
    /// Records a message consumed from a queue.
    /// </summary>
    public void RecordMessageConsumed(string topic, bool success, int count = 1)
    {
        var tags = new TagList
        {
            { "topic", topic },
            { "success", success.ToString().ToLower() }
        };

        _messageConsumedCounter.Add(count, tags);

        if (!success)
        {
            _messageFailedCounter.Add(count, tags);
        }
    }

    /// <summary>
    /// Sets the current queue depth.
    /// </summary>
    public void SetQueueDepth(int depth)
    {
        _queueDepth = depth;
    }

    #endregion

    #region Cache Metrics

    /// <summary>
    /// Records a cache access.
    /// </summary>
    public void RecordCacheAccess(string cacheName, bool hit)
    {
        var tags = new TagList { { "cache_name", cacheName } };

        if (hit)
        {
            _cacheHitCounter.Add(1, tags);
        }
        else
        {
            _cacheMissCounter.Add(1, tags);
        }
    }

    #endregion

    #region Auth Metrics

    /// <summary>
    /// Records an authentication attempt.
    /// </summary>
    public void RecordAuthAttempt(bool success, string? provider = null, string? reason = null)
    {
        var tags = new TagList();
        
        if (!string.IsNullOrEmpty(provider))
        {
            tags.Add("provider", provider);
        }

        if (success)
        {
            _authSuccessCounter.Add(1, tags);
        }
        else
        {
            if (!string.IsNullOrEmpty(reason))
            {
                tags.Add("failure_reason", reason);
            }
            _authFailureCounter.Add(1, tags);
        }
    }

    #endregion

    #region Connection Metrics

    /// <summary>
    /// Sets the number of active connections.
    /// </summary>
    public void SetActiveConnections(int count)
    {
        _activeConnections = count;
    }

    /// <summary>
    /// Increments active connections.
    /// </summary>
    public void IncrementActiveConnections()
    {
        Interlocked.Increment(ref _activeConnections);
    }

    /// <summary>
    /// Decrements active connections.
    /// </summary>
    public void DecrementActiveConnections()
    {
        Interlocked.Decrement(ref _activeConnections);
    }

    #endregion

    #region Helper Methods

    private static string GetStatusClass(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        >= 500 => "5xx",
        _ => "unknown"
    };

    #endregion

    #region Disposable

    public void Dispose()
    {
        if (_disposed) return;
        _meter.Dispose();
        _disposed = true;
    }

    #endregion

    #region Scope Classes

    public readonly struct RequestDurationScope : IDisposable
    {
        private readonly CustomMetrics _metrics;
        private readonly string _endpoint;
        private readonly string _method;
        private readonly Stopwatch _stopwatch;
        private int _statusCode;

        internal RequestDurationScope(CustomMetrics metrics, string endpoint, string method)
        {
            _metrics = metrics;
            _endpoint = endpoint;
            _method = method;
            _stopwatch = Stopwatch.StartNew();
            _statusCode = 200;
        }

        public void SetStatusCode(int statusCode)
        {
            _statusCode = statusCode;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _metrics.RecordRequest(_endpoint, _method, _statusCode, _stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    public readonly struct BusinessOperationScope : IDisposable
    {
        private readonly CustomMetrics _metrics;
        private readonly string _operationType;
        private readonly string? _customerId;
        private readonly Stopwatch _stopwatch;
        private bool _success;

        internal BusinessOperationScope(CustomMetrics metrics, string operationType, string? customerId)
        {
            _metrics = metrics;
            _operationType = operationType;
            _customerId = customerId;
            _stopwatch = Stopwatch.StartNew();
            _success = true;
        }

        public void SetFailed()
        {
            _success = false;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _metrics.RecordBusinessOperation(_operationType, _success, _stopwatch.Elapsed.TotalMilliseconds, _customerId);
        }
    }

    #endregion
}
