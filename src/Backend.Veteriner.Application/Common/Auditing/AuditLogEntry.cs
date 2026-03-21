namespace Backend.Veteriner.Application.Common.Auditing;

public sealed record AuditLogEntry(
    Guid? ActorUserId,
    string Action,
    string? TargetType,
    string? TargetId,
    bool Success,
    string? FailureReason,
    string? Route,
    string? HttpMethod,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId,
    string RequestName,
    string? RequestPayload,
    DateTime OccurredAtUtc);