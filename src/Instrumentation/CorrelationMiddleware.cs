using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LogMonitoring.Instrumentation;

/// <summary>
/// Middleware that ensures correlation IDs are propagated through the request pipeline.
/// Adds correlation context to logs and traces for distributed tracing.
/// </summary>
public class CorrelationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationMiddleware> _logger;

    public const string CorrelationIdHeader = "X-Correlation-ID";
    public const string RequestIdHeader = "X-Request-ID";
    public const string SessionIdHeader = "X-Session-ID";
    public const string UserIdHeader = "X-User-ID";

    public CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract or generate correlation ID
        var correlationId = GetOrCreateCorrelationId(context);
        var requestId = GetOrCreateRequestId(context);
        
        // Try to extract user and session info from headers or auth context
        var userId = ExtractUserId(context);
        var sessionId = ExtractSessionId(context);

        // Add to current activity for trace context
        var activity = Activity.Current;
        if (activity != null)
        {
            activity.SetTag("correlation.id", correlationId);
            activity.SetTag("request.id", requestId);
            
            if (!string.IsNullOrEmpty(userId))
            {
                activity.SetTag("user.id", userId);
            }
            
            if (!string.IsNullOrEmpty(sessionId))
            {
                activity.SetTag("session.id", sessionId);
            }

            // Add HTTP context info
            activity.SetTag("http.client_ip", context.Connection.RemoteIpAddress?.ToString());
            activity.SetTag("http.user_agent", context.Request.Headers["User-Agent"].ToString());
        }

        // Store in HttpContext.Items for access throughout the request
        context.Items["CorrelationId"] = correlationId;
        context.Items["RequestId"] = requestId;
        context.Items["UserId"] = userId;
        context.Items["SessionId"] = sessionId;

        // Add to response headers for client-side correlation
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            context.Response.Headers[RequestIdHeader] = requestId;
            return Task.CompletedTask;
        });

        // Create a logging scope with correlation context
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["RequestId"] = requestId,
            ["UserId"] = userId,
            ["SessionId"] = sessionId,
            ["TraceId"] = activity?.TraceId.ToString(),
            ["SpanId"] = activity?.SpanId.ToString(),
            ["ClientIp"] = context.Connection.RemoteIpAddress?.ToString(),
            ["UserAgent"] = context.Request.Headers["User-Agent"].ToString(),
            ["RequestPath"] = context.Request.Path.Value,
            ["RequestMethod"] = context.Request.Method
        }))
        {
            try
            {
                _logger.LogDebug(
                    "Request started: {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path);

                await _next(context);

                _logger.LogDebug(
                    "Request completed: {Method} {Path} - {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Request failed: {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path);
                throw;
            }
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        // Check for incoming correlation ID header
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId) &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId.ToString();
        }

        // Use trace ID if available
        var activity = Activity.Current;
        if (activity != null)
        {
            return activity.TraceId.ToString();
        }

        // Generate new GUID
        return Guid.NewGuid().ToString("N");
    }

    private static string GetOrCreateRequestId(HttpContext context)
    {
        // Check for incoming request ID header
        if (context.Request.Headers.TryGetValue(RequestIdHeader, out var requestId) &&
            !string.IsNullOrWhiteSpace(requestId))
        {
            return requestId.ToString();
        }

        // Use span ID if available
        var activity = Activity.Current;
        if (activity != null)
        {
            return activity.SpanId.ToString();
        }

        // Generate new short ID
        return Guid.NewGuid().ToString("N")[..16];
    }

    private static string? ExtractUserId(HttpContext context)
    {
        // Try header first
        if (context.Request.Headers.TryGetValue(UserIdHeader, out var userId) &&
            !string.IsNullOrWhiteSpace(userId))
        {
            return userId.ToString();
        }

        // Try authenticated user
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            return context.User.Identity.Name ??
                   context.User.FindFirst("sub")?.Value ??
                   context.User.FindFirst("user_id")?.Value;
        }

        return null;
    }

    private static string? ExtractSessionId(HttpContext context)
    {
        // Try header first
        if (context.Request.Headers.TryGetValue(SessionIdHeader, out var sessionId) &&
            !string.IsNullOrWhiteSpace(sessionId))
        {
            return sessionId.ToString();
        }

        // Try session cookie or claim
        return context.User?.FindFirst("session_id")?.Value;
    }
}

/// <summary>
/// Extension methods for correlation middleware.
/// </summary>
public static class CorrelationMiddlewareExtensions
{
    /// <summary>
    /// Adds correlation ID middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseCorrelation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CorrelationMiddleware>();
    }

    /// <summary>
    /// Gets the correlation ID from the current HTTP context.
    /// </summary>
    public static string? GetCorrelationId(this HttpContext context)
    {
        return context.Items.TryGetValue("CorrelationId", out var value) ? value?.ToString() : null;
    }

    /// <summary>
    /// Gets the request ID from the current HTTP context.
    /// </summary>
    public static string? GetRequestId(this HttpContext context)
    {
        return context.Items.TryGetValue("RequestId", out var value) ? value?.ToString() : null;
    }

    /// <summary>
    /// Gets the user ID from the current HTTP context.
    /// </summary>
    public static string? GetUserId(this HttpContext context)
    {
        return context.Items.TryGetValue("UserId", out var value) ? value?.ToString() : null;
    }

    /// <summary>
    /// Gets the session ID from the current HTTP context.
    /// </summary>
    public static string? GetSessionId(this HttpContext context)
    {
        return context.Items.TryGetValue("SessionId", out var value) ? value?.ToString() : null;
    }
}
