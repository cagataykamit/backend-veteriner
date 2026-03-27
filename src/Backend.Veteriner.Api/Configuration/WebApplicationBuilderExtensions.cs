using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Health;
using Backend.Veteriner.Api.Middleware;
using Backend.Veteriner.Api.Swagger;
using Backend.Veteriner.Application;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Infrastructure;
using Backend.Veteriner.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.EntityFrameworkCore;
using OpenTelemetry.Instrumentation.Runtime;
using OpenTelemetry.Instrumentation.SqlClient;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Exceptions;
using Serilog.Exceptions.Core;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace Backend.Veteriner.Api.Configuration;

/// <summary>
/// Program.cs bootstrap: configuration kaynakları, Serilog ve servis kayıtları (sıra korunur).
/// </summary>
public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddBackendAppConfiguration(this WebApplicationBuilder builder)
    {
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddUserSecrets<Program>(optional: true);
        }

        return builder;
    }

    public static WebApplicationBuilder AddBackendSerilog(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((ctx, lc) =>
        {
            lc.ReadFrom.Configuration(ctx.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithExceptionDetails(new DestructuringOptionsBuilder()
                    .WithDefaultDestructurers());
        });

        return builder;
    }

    public static WebApplicationBuilder AddBackendServices(this WebApplicationBuilder builder)
    {
        const string serviceName = "Backend.Veteriner.Api";
        const string serviceVersion = "1.0.0";
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";

        builder.Services.AddControllers()
            .ConfigureApiBehaviorOptions(o =>
            {
                o.SuppressModelStateInvalidFilter = false;

                o.InvalidModelStateResponseFactory = context =>
                {
                    var http = context.HttpContext;
                    var traceId = Activity.Current?.Id ?? http.TraceIdentifier;

                    var correlationId =
                        http.Items.TryGetValue(Correlation.HeaderName, out var v) ? v?.ToString() :
                        http.Items.TryGetValue("CorrelationId", out var legacy) ? legacy?.ToString() :
                        http.Request.Headers.TryGetValue(Correlation.HeaderName, out var cid) ? cid.ToString() :
                        traceId;

                    var problem = new ValidationProblemDetails(context.ModelState)
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = "Doğrulama hatası",
                        Type = "https://httpstatuses.io/400",
                        Detail = "Gönderilen bilgiler doğrulanamadı. Aşağıdaki alanları kontrol edin.",
                        Instance = http.Request.Path
                    };

                    problem.Extensions["traceId"] = traceId;
                    problem.Extensions["correlationId"] = correlationId;
                    problem.Extensions["code"] = "Validation.ModelStateInvalid";
                    problem.Extensions["timestampUtc"] = DateTime.UtcNow;

                    return new BadRequestObjectResult(problem)
                    {
                        ContentTypes = { "application/problem+json" }
                    };
                };
            });

        builder.Services.AddApiVersioning(opt =>
        {
            opt.AssumeDefaultVersionWhenUnspecified = true;
            opt.DefaultApiVersion = new ApiVersion(1, 0);
            opt.ReportApiVersions = true;
            opt.ApiVersionReader = new Microsoft.AspNetCore.Mvc.Versioning.UrlSegmentApiVersionReader();
        });

        builder.Services.AddVersionedApiExplorer(opt =>
        {
            opt.GroupNameFormat = "'v'VVV";
            opt.SubstituteApiVersionInUrl = true;
        });

        builder.Services.AddApplication();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.ConfigureOptions<ConfigureSwaggerOptions>();

        builder.Services.Configure<ForwardedHeadersOptions>(opt =>
        {
            opt.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            opt.RequireHeaderSymmetry = false;
            opt.ForwardLimit = null;
        });

        var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                             ?? new[] { "http://localhost:4200" };

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("sql")
            .AddCheck<OutboxHealthCheck>("outbox");

        builder.Services.AddInfrastructure(builder.Configuration);

        var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
                  ?? throw new InvalidOperationException("Jwt ayarlar\uFFFD (appsettings:Jwt) bulunamad\uFFFD.");

        if (builder.Environment.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(jwt.Key))
                throw new InvalidOperationException("Jwt:Key bo\uFFFD. User-Secrets \uFFFDzerinden set edilmelidir.");

            if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection bo\uFFFD. User-Secrets \uFFFDzerinden set edilmelidir.");

            if (string.IsNullOrWhiteSpace(builder.Configuration["Smtp:Pass"]))
                throw new InvalidOperationException("Smtp:Pass bo\uFFFD. User-Secrets \uFFFDzerinden set edilmelidir.");
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opt =>
            {
                opt.MapInboundClaims = false;

                opt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ClockSkew = TimeSpan.Zero,

                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = "role"
                };
            });

        builder.Services.AddPermissionAuthorization();

        builder.Services.AddAppRateLimiting();

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(rb => rb
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                .AddTelemetrySdk()
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName),
                    new KeyValuePair<string, object>("service.instance.id", Environment.MachineName)
                }))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o =>
                {
                    o.RecordException = true;
                    o.Filter = _ => true;
                    o.EnrichWithHttpRequest = (activity, req) =>
                    {
                        activity.SetTag("http.request_content_length", req.ContentLength);
                        activity.SetTag("client.ip", req.HttpContext.Connection.RemoteIpAddress?.ToString());
                    };
                    o.EnrichWithHttpResponse = (activity, res) =>
                    {
                        activity.SetTag("http.response_content_length", res.ContentLength);
                    };
                })
                .AddHttpClientInstrumentation(o => o.RecordException = true)
                .AddSqlClientInstrumentation(o => o.RecordException = true)
                .AddEntityFrameworkCoreInstrumentation(o => { o.SetDbStatementForText = false; })
                .AddSource("Backend.Veteriner.Outbox")
                .AddSource("Backend.Veteriner.Mailing")
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(otlpEndpoint);
                    o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                })
            )
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Backend.Veteriner.Outbox")
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(otlpEndpoint);
                    o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                })
            );

        return builder;
    }
}
