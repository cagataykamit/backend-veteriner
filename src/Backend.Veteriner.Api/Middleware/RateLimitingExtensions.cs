using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.Api.Middleware;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

                context.HttpContext.Response.ContentType = "application/problem+json";

                var traceId = context.HttpContext.TraceIdentifier;
                var correlationId =
                    context.HttpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var cid) ? cid.ToString() : null;

                var retryAfterHeader = context.HttpContext.Response.Headers.RetryAfter.ToString();
                int? retryAfterSeconds = null;
                if (int.TryParse(retryAfterHeader, out var sec))
                    retryAfterSeconds = sec;

                var problem = new
                {
                    type = "https://httpstatuses.com/429",
                    title = "Too Many Requests",
                    status = 429,
                    errorCode = "rate_limit_exceeded",
                    detail = "�stek limiti a��ld�. L�tfen biraz sonra tekrar deneyin.",
                    traceId,
                    correlationId,
                    retryAfterSeconds
                };

                await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: token);
            };

            // -- Global limiter (iste�e ba�l�; geni� tutuldu)
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var key = GetClientIp(ctx);
                return RateLimitPartition.GetSlidingWindowLimiter(
                    key,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 200,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            // -- LOGIN: brute-force korumas� (IP bazl�)
            options.AddPolicy("login", httpContext =>
            {
                var key = GetClientIp(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter(
                    key,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            // -- REFRESH: IP + sub + UA
            options.AddPolicy("refresh", httpContext =>
            {
                var ip = GetClientIp(httpContext);
                var sub = httpContext.User.FindFirst("sub")?.Value
                          ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? "anon";
                var ua = httpContext.Request.Headers.UserAgent.ToString() ?? "ua";

                var key = $"{ip}:{sub}:{ua}";

                return RateLimitPartition.GetTokenBucketLimiter(
                    key,
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 30, // ~30/dk
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(2),
                        TokensPerPeriod = 1, // 1 token / 2 sn
                        AutoReplenishment = true
                    });
            });

            // -- PASSWORD RESET: request
            options.AddPolicy("password-reset-request", httpContext =>
            {
                var key = GetClientIp(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter(
                    key,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            // -- PASSWORD RESET: confirm
            options.AddPolicy("password-reset-confirm", httpContext =>
            {
                var key = GetClientIp(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter(
                    key,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            // -- EMAIL VERIFICATION: request
            options.AddPolicy("email-verify-request", httpContext =>
            {
                var key = GetClientIp(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter(
                    key,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            // -- EMAIL VERIFICATION: confirm
            options.AddPolicy("email-verify-confirm", httpContext =>
            {
                var key = GetClientIp(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter(
                    key,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            // -- CONTACT: spam korumas�
            options.AddPolicy("contact", httpContext =>
            {
                var key = GetClientIp(httpContext);
                return RateLimitPartition.GetSlidingWindowLimiter(
                    key,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });

        return services;
    }

    public static IApplicationBuilder UseAppRateLimiting(this IApplicationBuilder app)
        => app.UseRateLimiter();

    private static string GetClientIp(HttpContext ctx)
        => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
