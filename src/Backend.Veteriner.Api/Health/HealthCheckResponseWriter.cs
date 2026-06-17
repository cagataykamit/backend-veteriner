using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Backend.Veteriner.Api.Health;

internal static class HealthCheckResponseWriter
{
    public static async Task WriteReadyAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            results = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds,
                    data = ToSafeData(e.Value.Data)
                })
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    private static IReadOnlyDictionary<string, object?>? ToSafeData(IReadOnlyDictionary<string, object>? data)
    {
        if (data is null || data.Count == 0)
            return null;

        var safe = new Dictionary<string, object?>(data.Count, StringComparer.Ordinal);
        foreach (var (key, value) in data)
            safe[key] = ToSafeValue(value);

        return safe;
    }

    private static object? ToSafeValue(object? value)
        => value switch
        {
            null => null,
            string s => s,
            bool b => b,
            int i => i,
            long l => l,
            double d => d,
            float f => f,
            decimal m => m,
            DateTime dt => dt.ToString("O"),
            DateTimeOffset dto => dto.ToString("O"),
            _ => value.ToString()
        };
}
