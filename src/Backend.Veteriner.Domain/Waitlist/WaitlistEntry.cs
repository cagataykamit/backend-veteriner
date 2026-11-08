using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Waitlist;

/// <summary>Bekleme kaydının durumu (TIER-018). Sayısal değerler stabildir.</summary>
public enum WaitlistEntryStatus
{
    /// <summary>Sırada; boşalan saat için teklif alabilir.</summary>
    Waiting = 0,

    /// <summary>Teklifi kabul etti, randevu oluştu; kayıt kapandı.</summary>
    Booked = 1,

    /// <summary>Klinik kaydı kapattı.</summary>
    Closed = 2,
}

/// <summary>Uygun günler (İstanbul takvimi). Hiçbiri seçilmezse her gün uygundur.</summary>
[Flags]
public enum WaitlistWeekdays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    All = Monday | Tuesday | Wednesday | Thursday | Friday | Saturday | Sunday,
}

/// <summary>
/// Bekleme listesi kaydı (TIER-018; docs/WAITLIST_API_CONTRACT.md): klinik personeli hasta sahibini ve hayvanı listeye yazar; saat
/// boşalınca sıradaki uygun kayda teklif gider. Sıra önce eklenme zamanıdır (<see cref="SortKey"/>); klinik elle kaydırabilir.
/// Uygunluk kuralı (hekim, tarih aralığı, gün ve saat tercihi, süre) yalnız <see cref="Matches"/>'tedir; saatler İstanbul duvar saatidir.
/// </summary>
public sealed class WaitlistEntry : ITenantOwned, IClinicScoped
{
    public const int NoteMaxLength = 500;
    public const int CloseReasonMaxLength = 300;

    /// <summary>Tarih aralığının en uzun hâli (çalışma varsayımı): bekleme kaydı sınırsız açık kalmaz.</summary>
    public const int MaxRangeDays = 180;

    public const string ValidationCode = "Waitlist.Validation";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid PetId { get; private set; }

    /// <summary>İstenen hekim; boşsa herhangi bir hekim (ya da hekimsiz saat) uygundur.</summary>
    public Guid? PreferredVeterinarianUserId { get; private set; }

    public AppointmentType AppointmentType { get; private set; }

    /// <summary>Kliniğin randevu nedeni; varsa varsayılan süresi boşalan saate sığmalıdır.</summary>
    public Guid? ReasonId { get; private set; }

    /// <summary>Kaydın istediği süre (nedenin varsayılan süresi); boşsa boşalan saatin süresi kullanılır.</summary>
    public int? DurationMinutes { get; private set; }

    public DateOnly EarliestDate { get; private set; }
    public DateOnly LatestDate { get; private set; }
    public WaitlistWeekdays PreferredDays { get; private set; }
    public TimeOnly? PreferredFromTime { get; private set; }
    public TimeOnly? PreferredToTime { get; private set; }
    public string? Note { get; private set; }

    public WaitlistEntryStatus Status { get; private set; }

    /// <summary>Sıra anahtarı: küçük olan öndedir. Varsayılan eklenme zamanıdır; elle kaydırmada komşuyla yer değiştirir.</summary>
    public long SortKey { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public Guid? ClosedByUserId { get; private set; }
    public string? CloseReason { get; private set; }

    /// <summary>Kabul edilen teklifle oluşan randevu.</summary>
    public Guid? BookedAppointmentId { get; private set; }

    public byte[] RowVersion { get; private set; } = default!;

    public bool IsWaiting => Status == WaitlistEntryStatus.Waiting;

    private WaitlistEntry() { }

    public static Result<WaitlistEntry> Create(
        Guid tenantId,
        Guid clinicId,
        Guid clientId,
        Guid petId,
        Guid? preferredVeterinarianUserId,
        AppointmentType appointmentType,
        Guid? reasonId,
        int? durationMinutes,
        DateOnly earliestDate,
        DateOnly latestDate,
        WaitlistWeekdays preferredDays,
        TimeOnly? preferredFromTime,
        TimeOnly? preferredToTime,
        string? note,
        Guid createdByUserId,
        DateOnly todayLocal,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty || clinicId == Guid.Empty || clientId == Guid.Empty || petId == Guid.Empty || createdByUserId == Guid.Empty)
            throw new ArgumentException("Kimlik alanları boş olamaz.");
        if (preferredVeterinarianUserId == Guid.Empty || reasonId == Guid.Empty)
            throw new ArgumentException("Hekim ya da neden kimliği geçersiz.");
        if (!Enum.IsDefined(appointmentType))
            return Invalid("Randevu türü geçersiz.");
        if (durationMinutes is { } d && !Appointment.IsValidDurationMinutes(d))
            return Invalid("Süre 5-240 dakika arasında olmalıdır.");
        if (earliestDate < todayLocal)
            return Invalid("Başlangıç tarihi bugünden önce olamaz.");
        if (latestDate < earliestDate)
            return Invalid("Bitiş tarihi başlangıçtan önce olamaz.");
        if (latestDate.DayNumber - earliestDate.DayNumber > MaxRangeDays)
            return Invalid($"Tarih aralığı en çok {MaxRangeDays} gün olabilir.");
        if ((preferredDays & ~WaitlistWeekdays.All) != 0)
            return Invalid("Gün seçimi geçersiz.");
        if (preferredFromTime is { } from && preferredToTime is { } to && to <= from)
            return Invalid("Saat aralığının bitişi başlangıcından sonra olmalıdır.");

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > NoteMaxLength })
            return Invalid($"Not en çok {NoteMaxLength} karakter olabilir.");

        var now = NormalizeUtc(utcNow);
        return Result<WaitlistEntry>.Success(new WaitlistEntry
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            ClientId = clientId,
            PetId = petId,
            PreferredVeterinarianUserId = preferredVeterinarianUserId,
            AppointmentType = appointmentType,
            ReasonId = reasonId,
            DurationMinutes = durationMinutes,
            EarliestDate = earliestDate,
            LatestDate = latestDate,
            PreferredDays = preferredDays,
            PreferredFromTime = preferredFromTime,
            PreferredToTime = preferredToTime,
            Note = trimmedNote,
            Status = WaitlistEntryStatus.Waiting,
            SortKey = now.Ticks,
            CreatedAtUtc = now,
            CreatedByUserId = createdByUserId,
        });
    }

    /// <summary>
    /// Boşalan saat bu kayda uyar mı — uygunluğun tek yeri. Kayıt sırada olmalı; istenen hekim varsa saatin hekimiyle aynı olmalı
    /// (hekimsiz saat hekim isteyen kayda uymaz); yerel gün tarih aralığında ve seçili günlerde; saat tercihi varsa saat tamamen
    /// içinde; kaydın süresi saate sığmalı.
    /// </summary>
    public bool Matches(DateTime slotLocalStart, int slotDurationMinutes, Guid? slotVeterinarianUserId)
    {
        if (!IsWaiting)
            return false;
        if (PreferredVeterinarianUserId is { } vet && vet != slotVeterinarianUserId)
            return false;

        var date = DateOnly.FromDateTime(slotLocalStart);
        if (date < EarliestDate || date > LatestDate)
            return false;
        if (PreferredDays != WaitlistWeekdays.None && (PreferredDays & ToFlag(slotLocalStart.DayOfWeek)) == 0)
            return false;

        var needed = EffectiveDurationMinutes(slotDurationMinutes);
        if (needed > slotDurationMinutes)
            return false;

        var start = TimeOnly.FromDateTime(slotLocalStart);
        var end = TimeOnly.FromDateTime(slotLocalStart.AddMinutes(needed));
        if (PreferredFromTime is { } from && start < from)
            return false;
        if (PreferredToTime is { } to && (end > to || end < start))
            return false;

        return true;
    }

    /// <summary>Teklif edilen randevunun süresi: kaydın süresi varsa o, yoksa boşalan saatin süresi.</summary>
    public int EffectiveDurationMinutes(int slotDurationMinutes) => DurationMinutes ?? slotDurationMinutes;

    /// <summary>Tarih aralığı geçmiş kayıt teklif almaz; durum değişmez, liste "süresi geçti" gösterir.</summary>
    public bool IsPastRange(DateOnly todayLocal) => LatestDate < todayLocal;

    public Result Close(Guid userId, string? reason, DateTime utcNow)
    {
        if (!IsWaiting)
            return Result.Failure("Waitlist.EntryNotOpen", "Kayıt artık sırada değil.");

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed is { Length: > CloseReasonMaxLength })
            return Result.Failure(ValidationCode, $"Kapatma nedeni en çok {CloseReasonMaxLength} karakter olabilir.");

        Status = WaitlistEntryStatus.Closed;
        ClosedAtUtc = NormalizeUtc(utcNow);
        ClosedByUserId = userId;
        CloseReason = trimmed;
        return Result.Success();
    }

    /// <summary>Teklif kabul edildi, randevu oluştu; kayıt kapanır.</summary>
    public Result MarkBooked(Guid appointmentId, DateTime utcNow)
    {
        if (!IsWaiting)
            return Result.Failure("Waitlist.EntryNotOpen", "Kayıt artık sırada değil.");

        Status = WaitlistEntryStatus.Booked;
        BookedAppointmentId = appointmentId;
        ClosedAtUtc = NormalizeUtc(utcNow);
        return Result.Success();
    }

    /// <summary>Elle sıra değişikliği: iki kayıt sıra anahtarlarını değiştirir (aynı klinik, ikisi de sırada).</summary>
    public Result SwapOrderWith(WaitlistEntry other)
    {
        if (other.Id == Id || other.TenantId != TenantId || other.ClinicId != ClinicId)
            return Result.Failure(ValidationCode, "Kayıtlar aynı kliniğin bekleme listesinde olmalıdır.");
        if (!IsWaiting || !other.IsWaiting)
            return Result.Failure("Waitlist.EntryNotOpen", "Yalnız sıradaki kayıtların yeri değiştirilebilir.");

        (SortKey, other.SortKey) = (other.SortKey, SortKey);
        return Result.Success();
    }

    private static WaitlistWeekdays ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WaitlistWeekdays.Monday,
        DayOfWeek.Tuesday => WaitlistWeekdays.Tuesday,
        DayOfWeek.Wednesday => WaitlistWeekdays.Wednesday,
        DayOfWeek.Thursday => WaitlistWeekdays.Thursday,
        DayOfWeek.Friday => WaitlistWeekdays.Friday,
        DayOfWeek.Saturday => WaitlistWeekdays.Saturday,
        _ => WaitlistWeekdays.Sunday,
    };

    private static Result<WaitlistEntry> Invalid(string message) => Result<WaitlistEntry>.Failure(ValidationCode, message);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
