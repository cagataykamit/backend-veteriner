namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IAuditableRequest
{
    string AuditAction { get; }
    string? AuditTarget { get; }
}