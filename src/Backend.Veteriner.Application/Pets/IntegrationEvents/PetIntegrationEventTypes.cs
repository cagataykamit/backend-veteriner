namespace Backend.Veteriner.Application.Pets.IntegrationEvents;

/// <summary>
/// Pet integration event outbox mesaj tipleri (OutboxMessages.Type).
/// Tüm değerler <c>nvarchar(64)</c> sınırına uyar; CLR FullName kullanılmaz.
/// Appointment/Client tipi alanlarından ayrı tutulur; ileride ayrı Pet projection consumer'ı tüketecektir.
/// </summary>
public static class PetIntegrationEventTypes
{
    public const string Created = "pet.created.v1";
    public const string Updated = "pet.updated.v1";

    public const int MaxTypeLength = 64;

    public static IReadOnlyList<string> All { get; } =
    [
        Created,
        Updated
    ];

    public static bool IsKnown(string eventType)
        => All.Contains(eventType, StringComparer.Ordinal);
}
