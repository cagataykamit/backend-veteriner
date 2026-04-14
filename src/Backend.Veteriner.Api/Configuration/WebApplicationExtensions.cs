using Backend.Veteriner.Api.Middleware;
using Backend.Veteriner.Application.Common.Constants;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using System.Diagnostics;
using System.Text.Json;

namespace Backend.Veteriner.Api.Configuration;

/// <summary>
/// Program.cs bootstrap: HTTP pipeline ve endpoint haritaları. Migration/seed ayrı DbMigrator aracıyla.
/// </summary>
public static class WebApplicationExtensions
{
    public static Task<WebApplication> ConfigureBackendAsync(this WebApplication app)
    {
        app.UseForwardedHeaders();

        app.UseSerilogRequestLogging();

        app.UseCorrelationId();
        app.UseMiddleware<ActiveHttpRequestCountingMiddleware>();
        app.UseRequestEnrichment();

        app.UseGlobalExceptionHandler(app.Environment);

        if (app.Environment.IsDevelopment())
        {
            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Path == "/" || ctx.Request.Path == "/index.html")
                {
                    ctx.Response.Redirect("/swagger/index.html");
                    return;
                }

                await next();
            });
        }

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
            foreach (var desc in provider.ApiVersionDescriptions)
            {
                c.SwaggerEndpoint($"/swagger/{desc.GroupName}/swagger.json", $"Backend Veteriner API {desc.GroupName}");
            }

            c.DisplayRequestDuration();
            c.EnablePersistAuthorization();
        });

        app.UseHttpsRedirection();

        app.UseRouting();

        app.UseCors("AllowFrontend");
        app.UseAppRateLimiting();

        app.UseAuthentication();
        app.UseMiddleware<TenantContextMiddleware>();
        app.UseMiddleware<ClinicContextMiddleware>();
        app.UseAuthorization();

        // 401/403/404/429 gibi exception olmayan durumları da ProblemDetails'e bağla
        app.UseStatusCodePages(async context =>
        {
            var ctx = context.HttpContext;

            if (ctx.Response.StatusCode < 400) return;
            if (ctx.Response.HasStarted) return;

            // Controller zaten yazdıysa bozma
            if (!string.IsNullOrWhiteSpace(ctx.Response.ContentType)) return;

            var traceId = Activity.Current?.Id ?? ctx.TraceIdentifier;

            var correlationId =
                ctx.Items.TryGetValue(Correlation.HeaderName, out var cidVal) ? cidVal?.ToString() :
                ctx.Request.Headers.TryGetValue(Correlation.HeaderName, out var cidHeader) ? cidHeader.ToString() :
                traceId;

            ctx.Response.ContentType = "application/problem+json";

            var (title, type, detail, code) = ctx.Response.StatusCode switch
            {
                401 => ("Unauthorized", "https://httpstatuses.io/401", "Kimlik dogrulamasi gecersiz veya eksik.", "Auth.Unauthorized"),
                403 => ("Forbidden", "https://httpstatuses.io/403", "Bu islem icin yetkiniz bulunmuyor.", "Auth.Forbidden"),
                404 => ("Not found", "https://httpstatuses.io/404", "Istenen kaynak bulunamadi.", "Errors.NotFound"),
                409 => ("Conflict", "https://httpstatuses.io/409", "Istek mevcut durum ile cakisiyor.", "Errors.Conflict"),
                429 => ("Too many requests", "https://httpstatuses.io/429", "Istek limiti asildi. Lutfen biraz sonra tekrar deneyin.", "RateLimit.Exceeded"),
                _ => ("Request failed", $"https://httpstatuses.io/{ctx.Response.StatusCode}", "Istek islenemedi.", $"Errors.Http{ctx.Response.StatusCode}")
            };

            var pd = new ProblemDetails
            {
                Status = ctx.Response.StatusCode,
                Title = title,
                Type = type,
                Detail = detail,
                Instance = ctx.Request.Path
            };

            pd.Extensions["traceId"] = traceId;
            pd.Extensions["correlationId"] = correlationId;
            pd.Extensions["code"] = code;
            pd.Extensions["timestampUtc"] = DateTime.UtcNow;

            await ctx.Response.WriteAsJsonAsync(pd);
        });

        app.MapControllers();

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            ResponseWriter = async (ctx, report) =>
            {
                ctx.Response.ContentType = "application/json";
                var payload = new
                {
                    status = report.Status.ToString(),
                    results = report.Entries.ToDictionary(
                        e => e.Key,
                        e => new
                        {
                            status = e.Value.Status.ToString(),
                            description = e.Value.Description,
                            duration = e.Value.Duration.TotalMilliseconds
                        })
                };
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(payload));
            }
        });

        app.MapHealthChecks("/health");

        app.Logger.LogInformation(
            "API startup does not run EF migrations or database seeding. Apply schema with `dotnet ef database update` or run: dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate | seed | all");

        return Task.FromResult(app);
    }
}
