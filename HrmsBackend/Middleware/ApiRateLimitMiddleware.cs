using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HrmsBackend.Services;
using Microsoft.AspNetCore.Routing;

namespace HrmsBackend.Middleware;

public class ApiRateLimitMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly ConcurrentDictionary<string, RateLimitCounter> Counters = new();

    // {id:int} / {id?} / {id=5}  ->  {id}
    // Same normalisation as ApiRateLimitController.GetEndpoints
    private static readonly Regex RouteParamRegex =
        new(@"\{([^:}?=]+)[^}]*\}", RegexOptions.Compiled);

    public ApiRateLimitMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ApiRateLimitService rateLimitService)
    {
        // Use the matched route template (e.g. /api/employees/{id}) so it
        // lines up with the URLs stored from the endpoints dropdown.
        // Fall back to the raw path when no endpoint was matched.
        var routeTemplate =
            (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;

        var path =
            routeTemplate != null
                ? RouteParamRegex.Replace("/" + routeTemplate.TrimStart('/'), "{$1}")
                : context.Request.Path.Value ?? "/";

        var method = context.Request.Method;

        // Get rate-limit configuration from database
        var config = await rateLimitService.GetConfigAsync(path, method);

        // API is not configured for rate limiting
        if (config == null)
        {
            await _next(context);
            return;
        }

        string counterKey;

        // =========================================================
        // LOGGED-IN API
        // =========================================================
        if (config.LoggedIn)
        {
            /*
             * logged_in = 1
             *
             * This API requires an authenticated user.
             */

            var userId = GetUserId(context);

            // User ID is required
            if (string.IsNullOrWhiteSpace(userId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    message = "Please login to access this API."
                });

                return;
            }

            // User based counter
            counterKey = $"USER:{userId}:{method}:{path}";
        }
        else
        {
            // =====================================================
            // NON-LOGGED-IN API
            // =====================================================

            /*
             * logged_in = 0
             *
             * No login required.
             * Rate limit is applied using IP.
             */

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            counterKey = $"IP:{ip}:{method}:{path}";
        }

        // =========================================================
        // RATE LIMIT COUNTER
        // =========================================================

        var now = DateTime.UtcNow;

        var counter = Counters.GetOrAdd(
            counterKey,
            _ => new RateLimitCounter
            {
                WindowStart = now,
                Count = 0
            });

        int retryAfter = 0;
        bool limitExceeded = false;
        int remainingRequests = 0;

        lock (counter)
        {
            // New time window
            if (now - counter.WindowStart >=
                TimeSpan.FromSeconds(config.LimitTime))
            {
                counter.WindowStart = now;
                counter.Count = 0;
            }

            // Rate limit exceeded
            if (counter.Count >= config.RateLimit)
            {
                var elapsed =
                    now - counter.WindowStart;

                var remaining =
                    TimeSpan.FromSeconds(config.LimitTime)
                    - elapsed;

                retryAfter = Math.Max(
                    1,
                    (int)Math.Ceiling(
                        remaining.TotalSeconds));

                limitExceeded = true;
            }
            else
            {
                counter.Count++;

                remainingRequests =
                    Math.Max(
                        0,
                        config.RateLimit - counter.Count);
            }
        }

        // =========================================================
        // 429 RESPONSE
        // =========================================================

        if (limitExceeded)
        {
            context.Response.StatusCode =
                StatusCodes.Status429TooManyRequests;

            context.Response.ContentType =
                "application/json";

            context.Response.Headers["Retry-After"] =
                retryAfter.ToString();

            context.Response.Headers["X-RateLimit-Limit"] =
                config.RateLimit.ToString();

            context.Response.Headers["X-RateLimit-Remaining"] =
                "0";

            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message =
                    "Too many requests. Please try again later.",
                retry_after = retryAfter
            });

            return;
        }

        // =========================================================
        // RATE LIMIT HEADERS
        // =========================================================

        context.Response.Headers["X-RateLimit-Limit"] =
            config.RateLimit.ToString();

        context.Response.Headers["X-RateLimit-Remaining"] =
            remainingRequests.ToString();

        // Continue request
        await _next(context);
    }


    // =============================================================
    // GET USER ID FROM JWT
    // =============================================================

    private static string? GetUserId(HttpContext context)
    {
        /*
         * JWT must already be authenticated before this middleware.
         *
         * We check common claim names, then the claims issued by
         * TokenService (accountId / employeeId):
         *
         * nameidentifier
         * user_id
         * userid
         * sub
         * accountId
         * employeeId
         */

        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userId =
            context.User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier
            )?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return userId;
        }

        userId =
            context.User.FindFirst("user_id")?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return userId;
        }

        userId =
            context.User.FindFirst("userid")?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return userId;
        }

        userId =
            context.User.FindFirst("sub")?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return userId;
        }

        // Claims issued by TokenService
        return context.User.FindFirst("accountId")?.Value
            ?? context.User.FindFirst("employeeId")?.Value;
    }


    // =============================================================
    // COUNTER
    // =============================================================

    private class RateLimitCounter
    {
        public DateTime WindowStart { get; set; }

        public int Count { get; set; }
    }
}