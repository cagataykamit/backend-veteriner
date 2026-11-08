using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Waitlist;

/// <summary>Kayıt geçmişi olayı (TIER-018). Sayısal değerler stabildir.</summary>
public enum WaitlistEntryEventType
{
    Added = 0,
    OfferSent = 1,
    OfferExpired = 2,
    OfferDeclined = 3,
    OfferAccepted = 4,

    /// <summary>Sahip kabul etti ama saat dolmuştu.</summary>
    OfferSlotTaken = 5,

    OfferWithdrawn = 6,
    Closed = 7,
    MovedUp = 8,
    MovedDown = 9,
}

/// <summary>
/// Bekleme kaydının geçmiş satırı (TIER-018): teklif gitti, süre doldu, kabul etti… Kim (personel; sahip ya da işleyici için boş)
/// ve ne zaman. Yalnız eklenir, değişmez.
/// </summary>
public sealed class WaitlistEntryEvent : ITenantOwned
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid WaitlistEntryId { get; private set; }
    public WaitlistEntryEventType Type { get; private set; }
    public Guid? OfferId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    private WaitlistEntryEvent() { }

    public static WaitlistEntryEvent Create(
        WaitlistEntry entry,
        WaitlistEntryEventType type,
        Guid? offerId,
        Guid? actorUserId,
        DateTime utcNow)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));

        return new WaitlistEntryEvent
        {
            TenantId = entry.TenantId,
            WaitlistEntryId = entry.Id,
            Type = type,
            OfferId = offerId,
            ActorUserId = actorUserId,
            OccurredAtUtc = utcNow.Kind == DateTimeKind.Utc ? utcNow : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
        };
    }

    /// <summary>Teklifin kapanış durumuna karşılık gelen geçmiş olayı.</summary>
    public static WaitlistEntryEventType ForOfferStatus(WaitlistOfferStatus status) => status switch
    {
        WaitlistOfferStatus.Accepted => WaitlistEntryEventType.OfferAccepted,
        WaitlistOfferStatus.Declined => WaitlistEntryEventType.OfferDeclined,
        WaitlistOfferStatus.Expired => WaitlistEntryEventType.OfferExpired,
        WaitlistOfferStatus.SlotTaken => WaitlistEntryEventType.OfferSlotTaken,
        WaitlistOfferStatus.Withdrawn => WaitlistEntryEventType.OfferWithdrawn,
        _ => WaitlistEntryEventType.OfferSent,
    };
}
