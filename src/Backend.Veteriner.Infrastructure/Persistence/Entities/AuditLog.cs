namespace Backend.Veteriner.Infrastructure.Persistence.Entities;

public sealed class AuditLog
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid? ActorUserId { get; private set; }

    public string Action { get; private set; } = default!;

    public string? TargetType { get; private set; }

    public string? TargetId { get; private set; }

    public bool Success { get; private set; }

    public string? FailureReason { get; private set; }

    public string? Route { get; private set; }

    public string? HttpMethod { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? CorrelationId { get; private set; }

    public string RequestName { get; private set; } = default!;

    public string? RequestPayload { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    private AuditLog() { }

    public AuditLog(
        Guid? actorUserId,
        string action,
        string? targetType,
        string? targetId,
        bool success,
        string? failureReason,
        string? route,
        string? httpMethod,
        string? ipAddress,
        string? userAgent,
        string? correlationId,
        string requestName,
        string? requestPayload,
        DateTime occurredAtUtc)
    {
        ActorUserId = actorUserId;
        Action = NormalizeRequired(action);
        TargetType = Normalize(targetType);
        TargetId = Normalize(targetId);
        Success = success;
        FailureReason = Normalize(failureReason);
        Route = Normalize(route);
        HttpMethod = Normalize(httpMethod);
        IpAddress = Normalize(ipAddress);
        UserAgent = Normalize(userAgent);
        CorrelationId = Normalize(correlationId);
        RequestName = NormalizeRequired(requestName);
        RequestPayload = requestPayload;
        OccurredAtUtc = occurredAtUtc;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeRequired(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Zorunlu alan boş olamaz.", nameof(value));

        return value.Trim();
    }
}