namespace Backend.Veteriner.Application.Visits.IntegrationEvents;

/// <summary>
/// Visit integration event outbox mesaj tipleri (OutboxMessages.Type); <c>nvarchar(64)</c> sınırına uyar.
/// </summary>
public static class VisitIntegrationEventTypes
{
    public const string Created = "visit.created.v1";
    public const string Updated = "visit.updated.v1";

    public const int MaxTypeLength = 64;

    public static IReadOnlyList<string> All { get; } =
    [
        Created,
        Updated
    ];

    public static bool IsKnown(string eventType)
        => All.Contains(eventType, StringComparer.Ordinal);
}
