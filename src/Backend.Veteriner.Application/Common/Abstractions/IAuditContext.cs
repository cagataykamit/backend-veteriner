namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IAuditContext
{
    Guid? UserId { get; }
    string? Route { get; }
    string? HttpMethod { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}