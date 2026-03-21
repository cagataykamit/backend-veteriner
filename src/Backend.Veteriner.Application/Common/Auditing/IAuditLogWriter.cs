namespace Backend.Veteriner.Application.Common.Auditing;

public interface IAuditLogWriter
{
    Task WriteAsync(AuditLogEntry entry, CancellationToken ct = default);
}