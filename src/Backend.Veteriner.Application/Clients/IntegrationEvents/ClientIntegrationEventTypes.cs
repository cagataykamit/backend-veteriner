namespace Backend.Veteriner.Application.Clients.IntegrationEvents;

/// <summary>
/// Client integration event outbox mesaj tipleri (OutboxMessages.Type).
/// Tüm değerler <c>nvarchar(64)</c> sınırına uyar; CLR FullName kullanılmaz.
/// Appointment tipi alanından ayrı tutulur; ileride ayrı Client projection consumer'ı tüketecektir.
/// </summary>
public static class ClientIntegrationEventTypes
{
    public const string Created = "client.created.v1";
    public const string Updated = "client.updated.v1";

    public const int MaxTypeLength = 64;

    public static IReadOnlyList<string> All { get; } =
    [
        Created,
        Updated
    ];

    public static bool IsKnown(string eventType)
        => All.Contains(eventType, StringComparer.Ordinal);
}
