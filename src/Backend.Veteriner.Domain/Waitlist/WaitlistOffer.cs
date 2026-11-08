using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Waitlist;

/// <summary>Teklifin durumu (TIER-018). Yalnız <see cref="Open"/> değişebilir. Sayısal değerler stabildir.</summary>
public enum WaitlistOfferStatus
{
    Open = 0,
    Accepted = 1,
    Declined = 2,
    Expired = 3,

    /// <summary>Kabul anında saat doluydu; randevu oluşmadı, sıradakine teklif açılmaz.</summary>
    SlotTaken = 4,

    /// <summary>Klinik kaydı kapattı; açık teklif geri çekildi.</summary>
    Withdrawn = 5,
}

/// <summary>
/// Boşalan saat için bekleme kaydına giden teklif (TIER-018). Saat, iptal edilen randevudan gelir (<see cref="SourceAppointmentId"/>);
/// aynı saat için aynı anda yalnız bir açık teklif olur ve saat en çok bir kez kabul edilir (veritabanında süzülmüş benzersiz indeks).
/// Süre kuralı yalnız <see cref="ComputeExpiry"/>'dedir. Kabul bağlantısı TIER-019 bağlantı tablosundadır
/// (<c>AppointmentOwnerLink</c>, amaç <c>WaitlistOffer</c>).
/// </summary>
public sealed class WaitlistOffer : ITenantOwned, IClinicScoped
{
    /// <summary>Teklif en çok bu kadar açık kalır (çalışma varsayımı).</summary>
    public static readonly TimeSpan OfferWindow = TimeSpan.FromHours(2);

    /// <summary>Randevuya bundan az kaldıysa teklif randevudan bu kadar önce kapanır (çalışma varsayımı).</summary>
    public static readonly TimeSpan CloseBeforeStart = TimeSpan.FromHours(1);

    /// <summary>Bundan kısa sürecek teklif gönderilmez (çalışma varsayımı): sahibin yanıt verecek zamanı olmalı.</summary>
    public static readonly TimeSpan MinimumWindow = TimeSpan.FromMinutes(15);

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid WaitlistEntryId { get; private set; }

    /// <summary>Saati boşaltan (iptal edilen) randevu.</summary>
    public Guid SourceAppointmentId { get; private set; }

    public DateTime SlotStartUtc { get; private set; }
    public int DurationMinutes { get; private set; }
    public Guid? VeterinarianUserId { get; private set; }

    /// <summary>Otomatik (iptal sonrası işleyici) ya da personelin elle gönderdiği teklif.</summary>
    public bool IsAutomatic { get; private set; }

    public Guid? CreatedByUserId { get; private set; }
    public WaitlistOfferStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }

    /// <summary>Kabulde oluşan randevu.</summary>
    public Guid? AppointmentId { get; private set; }

    public byte[] RowVersion { get; private set; } = default!;

    public bool IsOpen => Status == WaitlistOfferStatus.Open;

    private WaitlistOffer() { }

    /// <summary>
    /// Teklifin bitişi — tek kural: gönderimden 2 saat sonra; randevuya 2 saatten az kaldıysa randevudan 1 saat önce. Bitiş
    /// <see cref="MinimumWindow"/>'dan yakınsa teklif gönderilmez (<c>null</c>).
    /// </summary>
    public static DateTime? ComputeExpiry(DateTime slotStartUtc, DateTime utcNow)
    {
        var now = NormalizeUtc(utcNow);
        var byWindow = now + OfferWindow;
        var beforeStart = NormalizeUtc(slotStartUtc) - CloseBeforeStart;
        var expires = byWindow <= beforeStart ? byWindow : beforeStart;
        return expires - now >= MinimumWindow ? expires : null;
    }

    public static Result<WaitlistOffer> Create(
        WaitlistEntry entry,
        Guid sourceAppointmentId,
        DateTime slotStartUtc,
        int durationMinutes,
        Guid? veterinarianUserId,
        bool isAutomatic,
        Guid? createdByUserId,
        DateTime utcNow)
    {
        if (sourceAppointmentId == Guid.Empty)
            throw new ArgumentException("Kaynak randevu geçersiz.", nameof(sourceAppointmentId));
        if (!entry.IsWaiting)
            return Result<WaitlistOffer>.Failure("Waitlist.EntryNotOpen", "Kayıt artık sırada değil.");

        var expires = ComputeExpiry(slotStartUtc, utcNow);
        if (expires is null)
            return Result<WaitlistOffer>.Failure("WaitlistOffers.TooLate", "Randevuya çok az kaldı; teklif gönderilemez.");

        return Result<WaitlistOffer>.Success(new WaitlistOffer
        {
            TenantId = entry.TenantId,
            ClinicId = entry.ClinicId,
            WaitlistEntryId = entry.Id,
            SourceAppointmentId = sourceAppointmentId,
            SlotStartUtc = NormalizeUtc(slotStartUtc),
            DurationMinutes = durationMinutes,
            VeterinarianUserId = veterinarianUserId,
            IsAutomatic = isAutomatic,
            CreatedByUserId = createdByUserId,
            Status = WaitlistOfferStatus.Open,
            CreatedAtUtc = NormalizeUtc(utcNow),
            ExpiresAtUtc = expires.Value,
        });
    }

    /// <summary>Açık ve süresi dolmamış: kabul ve ret yalnız bu durumda yapılır.</summary>
    public bool IsActionable(DateTime utcNow) => IsOpen && NormalizeUtc(utcNow) < ExpiresAtUtc;

    public Result Accept(Guid appointmentId, DateTime utcNow)
    {
        if (!IsActionable(utcNow))
            return NotOpen();
        AppointmentId = appointmentId;
        return CloseAs(WaitlistOfferStatus.Accepted, utcNow);
    }

    public Result Decline(DateTime utcNow) => IsActionable(utcNow) ? CloseAs(WaitlistOfferStatus.Declined, utcNow) : NotOpen();

    public Result MarkSlotTaken(DateTime utcNow) => IsActionable(utcNow) ? CloseAs(WaitlistOfferStatus.SlotTaken, utcNow) : NotOpen();

    /// <summary>Süresi dolan açık teklif kapanır (işleyici).</summary>
    public Result Expire(DateTime utcNow)
        => IsOpen && NormalizeUtc(utcNow) >= ExpiresAtUtc ? CloseAs(WaitlistOfferStatus.Expired, utcNow) : NotOpen();

    public Result Withdraw(DateTime utcNow) => IsOpen ? CloseAs(WaitlistOfferStatus.Withdrawn, utcNow) : NotOpen();

    private Result CloseAs(WaitlistOfferStatus status, DateTime utcNow)
    {
        Status = status;
        ClosedAtUtc = NormalizeUtc(utcNow);
        return Result.Success();
    }

    private static Result NotOpen() => Result.Failure("WaitlistOffers.NotOpen", "Teklif artık açık değil.");

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
