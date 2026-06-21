namespace Backend.Veteriner.Application.Payments.IntegrationEvents;

/// <summary>
/// Payment integration event outbox mesaj tipleri (OutboxMessages.Type).
/// Tüm değerler <c>nvarchar(64)</c> sınırına uyar; CLR FullName kullanılmaz.
/// Dashboard finance projection consumer'ı (13C+) bu tipleri tüketecektir.
/// </summary>
public static class PaymentIntegrationEventTypes
{
    public const string Created = "payment.created.v1";
    public const string Updated = "payment.updated.v1";

    public const int SchemaVersion = 1;
    public const int MaxTypeLength = 64;

    public static IReadOnlyList<string> All { get; } =
    [
        Created,
        Updated
    ];

    public static bool IsKnown(string eventType)
        => All.Contains(eventType, StringComparer.Ordinal);
}
