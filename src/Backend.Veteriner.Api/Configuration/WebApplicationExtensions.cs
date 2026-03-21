using Backend.Veteriner.Api.Middleware;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using System.Diagnostics;
using System.Text.Json;

namespace Backend.Veteriner.Api.Configuration;

/// <summary>
/// Program.cs bootstrap: HTTP pipeline, endpoint haritaları ve migration/seed (sıra korunur).
/// </summary>
public static class WebApplicationExtensions
{
    public static async Task<WebApplication> ConfigureBackendAsync(this WebApplication app)
    {
        app.UseForwardedHeaders();

        app.UseSerilogRequestLogging();

        app.UseCorrelationId();
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

            var (title, type) = ctx.Response.StatusCode switch
            {
                401 => ("Unauthorized", "https://httpstatuses.io/401"),
                403 => ("Forbidden", "https://httpstatuses.io/403"),
                404 => ("Not found", "https://httpstatuses.io/404"),
                409 => ("Conflict", "https://httpstatuses.io/409"),
                429 => ("Too many requests", "https://httpstatuses.io/429"),
                _ => ("Request failed", $"https://httpstatuses.io/{ctx.Response.StatusCode}")
            };

            var pd = new ProblemDetails
            {
                Status = ctx.Response.StatusCode,
                Title = title,
                Type = type,
                Instance = ctx.Request.Path
            };

            pd.Extensions["traceId"] = traceId;
            pd.Extensions["correlationId"] = correlationId;
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

        if (!app.Environment.IsEnvironment("IntegrationTests"))
        {
            using var scope = app.Services.CreateScope();
            var services = scope.ServiceProvider;
            var logger = services.GetRequiredService<ILogger<Program>>();

            try
            {
                var db = services.GetRequiredService<AppDbContext>();
                var hasher = services.GetRequiredService<IPasswordHasher>();

                // Geçici tanılama: migration öncesi gerçek bağlantı hedefi (User Secrets / env override doğrulaması).
                var aspNetCoreEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                    ?? app.Environment.EnvironmentName;
                var rawConnectionString = db.Database.GetConnectionString();
                string? server = null;
                string? databaseName = null;
                if (!string.IsNullOrWhiteSpace(rawConnectionString))
                {
                    try
                    {
                        var csb = new SqlConnectionStringBuilder(rawConnectionString);
                        server = csb.DataSource;
                        databaseName = csb.InitialCatalog;
                    }
                    catch (Exception parseEx)
                    {
                        logger.LogWarning(parseEx, "[Startup DB] Connection string parse edilemedi.");
                    }
                }

                logger.LogWarning(
                    "[Startup DB] Migration öncesi — Environment={Environment} | DatabaseServer={DatabaseServer} | DatabaseName={DatabaseName} | FullConnectionString={FullConnectionString}",
                    aspNetCoreEnv,
                    server,
                    databaseName,
                    rawConnectionString ?? "(null)");

                await db.Database.MigrateAsync();

                // Sıra: önce permission'lar, sonra admin kullanıcı (DataSeeder), en son Admin claim ↔ user ↔ permission bağları.
                // AdminClaimSeeder kullanıcı yoksa erken çıktığı için DataSeeder'dan önce çalıştırılmamalı (ilk boot).
                await PermissionSeeder.SeedAsync(db);
                await DataSeeder.SeedAsync(db, hasher);
                await AdminClaimSeeder.SeedAsync(db);

                logger.LogInformation("Database migration and seeding completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Database seeding failed. Application startup aborted.");
                throw;
            }
        }

        return app;
    }
}
