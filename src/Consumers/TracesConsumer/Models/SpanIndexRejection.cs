namespace LogMonitoring.Consumers.Traces.Models;

/// <summary>
/// Describes a span document rejected by Elasticsearch during bulk indexing.
/// </summary>
public class SpanIndexRejection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpanIndexRejection"/> class.
    /// </summary>
    /// <param name="document">The rejected span document.</param>
    /// <param name="errorType">The Elasticsearch error type.</param>
    /// <param name="errorReason">The Elasticsearch error reason.</param>
    /// <param name="statusCode">The HTTP status code for the rejected item.</param>
    public SpanIndexRejection(
        SpanRecord document,
        string errorType,
        string errorReason,
        int statusCode)
    {
        Document = document;
        ErrorType = errorType;
        ErrorReason = errorReason;
        StatusCode = statusCode;
    }

    /// <summary>
    /// Gets the rejected span document.
    /// </summary>
    public SpanRecord Document { get; }

    /// <summary>
    /// Gets the Elasticsearch error type.
    /// </summary>
    public string ErrorType { get; }

    /// <summary>
    /// Gets the Elasticsearch error reason.
    /// </summary>
    public string ErrorReason { get; }

    /// <summary>
    /// Gets the HTTP status code for the rejected item.
    /// </summary>
    public int StatusCode { get; }
}
