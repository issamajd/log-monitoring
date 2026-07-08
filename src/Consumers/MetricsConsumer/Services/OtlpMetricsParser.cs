using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Metrics.V1;
using LogMonitoring.Consumers.Metrics.Models;

namespace LogMonitoring.Consumers.Metrics.Services;

/// <summary>
/// Interface for parsing metrics from various formats.
/// </summary>
public interface IMetricsParser
{
    /// <summary>
    /// Parses metrics from raw bytes (OTLP proto format).
    /// </summary>
    List<MetricDataPoint> Parse(byte[] data);
}

/// <summary>
/// Parses OTLP proto-encoded metrics into MetricDataPoint objects.
/// </summary>
public class OtlpMetricsParser : IMetricsParser
{
    private readonly ILogger<OtlpMetricsParser> _logger;

    public OtlpMetricsParser(ILogger<OtlpMetricsParser> logger)
    {
        _logger = logger;
    }

    public List<MetricDataPoint> Parse(byte[] data)
    {
        var result = new List<MetricDataPoint>();

        try
        {
            var request = ExportMetricsServiceRequest.Parser.ParseFrom(data);

            foreach (var resourceMetrics in request.ResourceMetrics)
            {
                // Extract resource attributes
                var resourceAttrs = ExtractResourceAttributes(resourceMetrics);

                foreach (var scopeMetrics in resourceMetrics.ScopeMetrics)
                {
                    foreach (var metric in scopeMetrics.Metrics)
                    {
                        var dataPoints = ExtractDataPoints(metric, resourceAttrs);
                        result.AddRange(dataPoints);
                    }
                }
            }
        }
        catch (InvalidProtocolBufferException ex)
        {
            _logger.LogWarning(ex, "Failed to parse OTLP metrics proto");
        }

        return result;
    }

    private static ResourceAttributes ExtractResourceAttributes(
        OpenTelemetry.Proto.Metrics.V1.ResourceMetrics resourceMetrics)
    {
        var attrs = new ResourceAttributes();

        foreach (var attr in resourceMetrics.Resource.Attributes)
        {
            switch (attr.Key)
            {
                case "service.name":
                    attrs.ServiceName = attr.Value.StringValue;
                    break;
                case "service.version":
                    attrs.ServiceVersion = attr.Value.StringValue;
                    break;
                case "host.name":
                    attrs.HostName = attr.Value.StringValue;
                    break;
                case "deployment.environment":
                    attrs.Environment = attr.Value.StringValue;
                    break;
            }
        }

        return attrs;
    }

    private List<MetricDataPoint> ExtractDataPoints(Metric metric, ResourceAttributes resourceAttrs)
    {
        var result = new List<MetricDataPoint>();
        var metricName = metric.Name;
        var unit = metric.Unit;

        switch (metric.DataCase)
        {
            case Metric.DataOneofCase.Gauge:
                result.AddRange(ExtractGaugeDataPoints(metric.Gauge, metricName, unit, resourceAttrs));
                break;

            case Metric.DataOneofCase.Sum:
                result.AddRange(ExtractSumDataPoints(metric.Sum, metricName, unit, resourceAttrs));
                break;

            case Metric.DataOneofCase.Histogram:
                result.AddRange(ExtractHistogramDataPoints(metric.Histogram, metricName, unit, resourceAttrs));
                break;

            case Metric.DataOneofCase.Summary:
                result.AddRange(ExtractSummaryDataPoints(metric.Summary, metricName, unit, resourceAttrs));
                break;

            case Metric.DataOneofCase.ExponentialHistogram:
                // Handle exponential histogram if needed
                break;
        }

        return result;
    }

    private static IEnumerable<MetricDataPoint> ExtractGaugeDataPoints(
        Gauge gauge, string metricName, string unit, ResourceAttributes resourceAttrs)
    {
        foreach (var dp in gauge.DataPoints)
        {
            var value = dp.ValueCase == NumberDataPoint.ValueOneofCase.AsDouble
                ? dp.AsDouble
                : dp.AsInt;

            yield return CreateDataPoint(
                metricName, value, "gauge", unit,
                DateTimeOffset.FromUnixTimeMilliseconds((long)(dp.TimeUnixNano / 1_000_000)),
                ExtractLabels(dp.Attributes),
                resourceAttrs);
        }
    }

    private static IEnumerable<MetricDataPoint> ExtractSumDataPoints(
        Sum sum, string metricName, string unit, ResourceAttributes resourceAttrs)
    {
        var metricType = sum.IsMonotonic ? "counter" : "gauge";

        foreach (var dp in sum.DataPoints)
        {
            var value = dp.ValueCase == NumberDataPoint.ValueOneofCase.AsDouble
                ? dp.AsDouble
                : dp.AsInt;

            yield return CreateDataPoint(
                metricName, value, metricType, unit,
                DateTimeOffset.FromUnixTimeMilliseconds((long)(dp.TimeUnixNano / 1_000_000)),
                ExtractLabels(dp.Attributes),
                resourceAttrs);
        }
    }

    private static IEnumerable<MetricDataPoint> ExtractHistogramDataPoints(
        Histogram histogram, string metricName, string unit, ResourceAttributes resourceAttrs)
    {
        foreach (var dp in histogram.DataPoints)
        {
            var time = DateTimeOffset.FromUnixTimeMilliseconds((long)(dp.TimeUnixNano / 1_000_000));
            var labels = ExtractLabels(dp.Attributes);

            // Emit count
            yield return CreateDataPoint(
                $"{metricName}_count", dp.Count, "histogram", unit, time, labels, resourceAttrs);

            // Emit sum if available
            if (dp.HasSum)
            {
                yield return CreateDataPoint(
                    $"{metricName}_sum", dp.Sum, "histogram", unit, time, labels, resourceAttrs);
            }

            // Emit bucket counts
            for (int i = 0; i < dp.BucketCounts.Count; i++)
            {
                var bound = i < dp.ExplicitBounds.Count ? dp.ExplicitBounds[i] : double.PositiveInfinity;
                var bucketLabels = new Dictionary<string, string>(labels)
                {
                    ["le"] = bound == double.PositiveInfinity ? "+Inf" : bound.ToString()
                };

                yield return CreateDataPoint(
                    $"{metricName}_bucket", dp.BucketCounts[i], "histogram", unit, time, bucketLabels, resourceAttrs);
            }
        }
    }

    private static IEnumerable<MetricDataPoint> ExtractSummaryDataPoints(
        Summary summary, string metricName, string unit, ResourceAttributes resourceAttrs)
    {
        foreach (var dp in summary.DataPoints)
        {
            var time = DateTimeOffset.FromUnixTimeMilliseconds((long)(dp.TimeUnixNano / 1_000_000));
            var labels = ExtractLabels(dp.Attributes);

            // Emit count
            yield return CreateDataPoint(
                $"{metricName}_count", dp.Count, "summary", unit, time, labels, resourceAttrs);

            // Emit sum
            yield return CreateDataPoint(
                $"{metricName}_sum", dp.Sum, "summary", unit, time, labels, resourceAttrs);

            // Emit quantiles
            foreach (var quantile in dp.QuantileValues)
            {
                var quantileLabels = new Dictionary<string, string>(labels)
                {
                    ["quantile"] = quantile.Quantile.ToString()
                };

                yield return CreateDataPoint(
                    metricName, quantile.Value, "summary", unit, time, quantileLabels, resourceAttrs);
            }
        }
    }

    private static Dictionary<string, string> ExtractLabels(
        Google.Protobuf.Collections.RepeatedField<OpenTelemetry.Proto.Common.V1.KeyValue> attributes)
    {
        var labels = new Dictionary<string, string>();

        foreach (var attr in attributes)
        {
            var value = attr.Value.ValueCase switch
            {
                OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.StringValue => attr.Value.StringValue,
                OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.IntValue => attr.Value.IntValue.ToString(),
                OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.DoubleValue => attr.Value.DoubleValue.ToString(),
                OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.BoolValue => attr.Value.BoolValue.ToString(),
                _ => null
            };

            if (value != null)
            {
                labels[attr.Key] = value;
            }
        }

        return labels;
    }

    private static MetricDataPoint CreateDataPoint(
        string metricName, double value, string metricType, string unit,
        DateTimeOffset time, Dictionary<string, string> labels, ResourceAttributes resourceAttrs)
    {
        return new MetricDataPoint
        {
            Time = time,
            MetricName = metricName,
            Value = value,
            MetricType = metricType,
            Unit = unit,
            ServiceName = resourceAttrs.ServiceName,
            ServiceVersion = resourceAttrs.ServiceVersion,
            HostName = resourceAttrs.HostName,
            Environment = resourceAttrs.Environment,
            Labels = labels
        };
    }

    private class ResourceAttributes
    {
        public string? ServiceName { get; set; }
        public string? ServiceVersion { get; set; }
        public string? HostName { get; set; }
        public string? Environment { get; set; }
    }
}
