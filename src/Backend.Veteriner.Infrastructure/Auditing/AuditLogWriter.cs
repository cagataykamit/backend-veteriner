using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.Infrastructure.Auditing;

public sealed class AuditLogWriter : IAuditLogWriter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AuditLogWriter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task WriteAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var auditLog = new AuditLog(
            actorUserId: entry.ActorUserId,
            action: entry.Action,
            targetType: entry.TargetType,
            targetId: entry.TargetId,
            success: entry.Success,
            failureReason: entry.FailureReason,
            route: entry.Route,
            httpMethod: entry.HttpMethod,
            ipAddress: entry.IpAddress,
            userAgent: entry.UserAgent,
            correlationId: entry.CorrelationId,
            requestName: entry.RequestName,
            requestPayload: entry.RequestPayload,
            occurredAtUtc: entry.OccurredAtUtc);

        db.AuditLogs.Add(auditLog);
        await db.SaveChangesAsync(ct);
    }
}