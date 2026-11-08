using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.CustomDevelopment;

/// <summary>
/// Talebin konusu (TIER-010): Vetinity içinde kalan ekran, rapor ya da çıktı; ayrıca rakip programdan geçiş desteği (TIER-021).
/// Sayısal değerler stabildir.
/// </summary>
public enum CustomDevelopmentTargetKind
{
    Screen = 0,
    Report = 1,

    /// <summary>Yazdırma, PDF, dışa aktarma gibi çıktı.</summary>
    Output = 2,

    /// <summary>
    /// Geçiş desteği ve kurulum görüşmesi (TIER-021). Her pakette açılır; özel geliştirme sınırına sayılmaz, kendi sınırı vardır
    /// (<see cref="CustomDevelopmentRequest.MaxOpenMigrationPerTenant"/>).
    /// </summary>
    Migration = 3,
}

/// <summary>Geçiş desteği talebinde yaklaşık kayıt (müşteri + hasta) sayısı. Sayısal değerler stabildir.</summary>
public enum MigrationRecordVolume
{
    Unknown = 0,
    UpTo1000 = 1,
    UpTo5000 = 2,
    UpTo10000 = 3,
    Over10000 = 4,
}

/// <summary>Kurulum görüşmesi için uygun günler (bayrak).</summary>
[Flags]
public enum MigrationMeetingDays
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

/// <summary>Kurulum görüşmesi için uygun saat aralıkları (bayrak; İstanbul saati).</summary>
[Flags]
public enum MigrationMeetingTimes
{
    None = 0,

    /// <summary>09.00–12.00</summary>
    Morning = 1,

    /// <summary>12.00–15.00</summary>
    Midday = 2,

    /// <summary>15.00–18.00</summary>
    Afternoon = 4,

    All = Morning | Midday | Afternoon,
}

/// <summary>
/// Geçiş desteğinin ücret bilgisi; talep anındaki pakete göre yazılır (kural uygulama katmanında tek yerde). Tutar tutulmaz:
/// ücretliyse fiyat kliniğe ayrıca bildirilir.
/// </summary>
public enum MigrationSupportFee
{
    Free = 0,

    /// <summary>Ücretli; fiyat kliniğe bildirilecek.</summary>
    Quoted = 1,
}

public enum CustomDevelopmentPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
}

/// <summary>Alındı → İnceleniyor → Planlandı ya da Kapsam dışı; Planlandı → Tamamlandı. Sayısal değerler stabildir.</summary>
public enum CustomDevelopmentRequestStatus
{
    Received = 0,
    InReview = 1,
    Planned = 2,
    OutOfScope = 3,
    Completed = 4,
}

/// <summary>
/// Kurumsal kiracının "size özel geliştirme" talebi (TIER-010; docs/CUSTOM_DEVELOPMENT_REQUESTS_API_CONTRACT.md). Kiracı açar,
/// yalnız platform yöneticisi durumunu değiştirir. Kapsam dışı kararında gerekçe zorunludur ve kliniğe görünür. Metinler kişisel ya da
/// hasta verisi için değildir; denetim kaydında maskelenir. Geçiş desteği talebi (TIER-021) aynı kayıt, durum akışı ve platform listesini
/// kullanır (<see cref="CreateMigrationSupport"/>).
/// </summary>
public sealed class CustomDevelopmentRequest : ITenantOwned
{
    public const int MaxTitleLength = 120;
    public const int MaxRequestTextLength = 2000;
    public const int MaxTargetLength = 120;
    public const int MaxDecisionReasonLength = 1000;
    public const int MaxResponseNoteLength = 1000;

    /// <summary>Kiracıda aynı anda açık (<see cref="OpenStatuses"/>) en çok talep; sınırın tek yeri (TIER-010 çalışma varsayımı).</summary>
    public const int MaxOpenPerTenant = 3;

    /// <summary>Kiracıda aynı anda açık en çok geçiş desteği talebi (TIER-021 çalışma varsayımı); özel geliştirme sınırından ayrıdır.</summary>
    public const int MaxOpenMigrationPerTenant = 1;

    /// <summary>Kurulum görüşmesinin süresi (TIER-021 kullanıcı kararı 2026-11-09).</summary>
    public const int MeetingDurationMinutes = 60;

    public const string MigrationTitle = "Geçiş desteği ve kurulum görüşmesi";

    public const string ValidationCode = "CustomDevelopment.Validation";

    /// <summary>Sonuçlanmamış talepler; sınır bunları sayar.</summary>
    public static readonly CustomDevelopmentRequestStatus[] OpenStatuses =
    [
        CustomDevelopmentRequestStatus.Received, CustomDevelopmentRequestStatus.InReview, CustomDevelopmentRequestStatus.Planned,
    ];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string RequestTitle { get; private set; } = default!;

    /// <summary>Ne istendiği.</summary>
    public string RequestText { get; private set; } = default!;

    public CustomDevelopmentTargetKind TargetKind { get; private set; }

    /// <summary>Hangi ekran, rapor ya da çıktı (ör. "Muayene ekranı").</summary>
    public string RequestTarget { get; private set; } = default!;

    public CustomDevelopmentPriority Priority { get; private set; }
    public CustomDevelopmentRequestStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Kapsam dışı gerekçesi; kliniğe görünür.</summary>
    public string? DecisionReason { get; private set; }

    /// <summary>Platform yöneticisinin kliniğe notu; son yazılan kalır.</summary>
    public string? ResponseNote { get; private set; }

    public DateTime? StatusChangedAtUtc { get; private set; }
    public Guid? StatusChangedByUserId { get; private set; }

    /// <summary>Başarılı mutasyon sayacı; optimistic concurrency token.</summary>
    public long MutationSequence { get; private set; }

    /// <summary>Geçiş desteği ayrıntıları (yalnız <see cref="CustomDevelopmentTargetKind.Migration"/>; diğer konularda boş).</summary>
    public ImportSourceProfile? MigrationSourceProgram { get; private set; }

    public MigrationRecordVolume? MigrationVolume { get; private set; }
    public MigrationMeetingDays? MigrationDays { get; private set; }
    public MigrationMeetingTimes? MigrationTimes { get; private set; }

    /// <summary>Talep anındaki pakete göre ücret bilgisi; sonradan paket değişse de değişmez.</summary>
    public MigrationSupportFee? MigrationFee { get; private set; }

    public bool IsOpen => OpenStatuses.Contains(Status);

    private CustomDevelopmentRequest() { }

    public static Result<CustomDevelopmentRequest> Create(
        Guid tenantId,
        string? requestTitle,
        string? requestText,
        CustomDevelopmentTargetKind targetKind,
        string? requestTarget,
        CustomDevelopmentPriority priority,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Invalid("TenantId geçersiz.");
        if (createdByUserId == Guid.Empty)
            return Invalid("Talebi açan kullanıcı geçersiz.");
        if (!Enum.IsDefined(targetKind) || targetKind == CustomDevelopmentTargetKind.Migration)
            return Invalid("Talebin konusu geçersiz.");
        if (!Enum.IsDefined(priority))
            return Invalid("Öncelik geçersiz.");

        var title = requestTitle?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > MaxTitleLength)
            return Invalid($"Başlık 1-{MaxTitleLength} karakter olmalıdır.");

        var text = requestText?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxRequestTextLength)
            return Invalid($"Ne istendiği 1-{MaxRequestTextLength} karakter olmalıdır.");

        var target = requestTarget?.Trim();
        if (string.IsNullOrEmpty(target) || target.Length > MaxTargetLength)
            return Invalid($"Ekran, rapor ya da çıktı adı 1-{MaxTargetLength} karakter olmalıdır.");

        return Result<CustomDevelopmentRequest>.Success(new CustomDevelopmentRequest
        {
            TenantId = tenantId,
            RequestTitle = title,
            RequestText = text,
            TargetKind = targetKind,
            RequestTarget = target,
            Priority = priority,
            Status = CustomDevelopmentRequestStatus.Received,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = NormalizeUtc(nowUtc),
        });
    }

    /// <summary>
    /// Geçiş desteği ve kurulum görüşmesi talebi (TIER-021). Başlık sabittir, <see cref="RequestTarget"/> programın adı,
    /// <see cref="RequestText"/> isteğe bağlı not (boş olabilir). Ücret bilgisi talep anındaki pakete göre çağıran tarafından verilir.
    /// Dosya ve kişisel veri taşımaz: klinik dosyayı kendisi içe aktarma ekranına yükler.
    /// </summary>
    public static Result<CustomDevelopmentRequest> CreateMigrationSupport(
        Guid tenantId,
        ImportSourceProfile sourceProgram,
        string? programName,
        MigrationRecordVolume recordVolume,
        MigrationMeetingDays meetingDays,
        MigrationMeetingTimes meetingTimes,
        string? note,
        MigrationSupportFee fee,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Invalid("TenantId geçersiz.");
        if (createdByUserId == Guid.Empty)
            return Invalid("Talebi açan kullanıcı geçersiz.");
        if (!Enum.IsDefined(sourceProgram))
            return Invalid("Program geçersiz.");
        if (!Enum.IsDefined(recordVolume))
            return Invalid("Yaklaşık kayıt sayısı geçersiz.");
        if (meetingDays == MigrationMeetingDays.None || (meetingDays & ~MigrationMeetingDays.All) != 0)
            return Invalid("Kurulum görüşmesi için en az bir uygun gün seçilmelidir.");
        if (meetingTimes == MigrationMeetingTimes.None || (meetingTimes & ~MigrationMeetingTimes.All) != 0)
            return Invalid("Kurulum görüşmesi için en az bir uygun saat aralığı seçilmelidir.");
        if (!Enum.IsDefined(fee))
            return Invalid("Ücret bilgisi geçersiz.");

        var target = programName?.Trim();
        if (string.IsNullOrEmpty(target) || target.Length > MaxTargetLength)
            return Invalid($"Program adı 1-{MaxTargetLength} karakter olmalıdır.");

        var text = note?.Trim() ?? "";
        if (text.Length > MaxRequestTextLength)
            return Invalid($"Not en fazla {MaxRequestTextLength} karakter olabilir.");

        return Result<CustomDevelopmentRequest>.Success(new CustomDevelopmentRequest
        {
            TenantId = tenantId,
            RequestTitle = MigrationTitle,
            RequestText = text,
            TargetKind = CustomDevelopmentTargetKind.Migration,
            RequestTarget = target,
            Priority = CustomDevelopmentPriority.Normal,
            Status = CustomDevelopmentRequestStatus.Received,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = NormalizeUtc(nowUtc),
            MigrationSourceProgram = sourceProgram,
            MigrationVolume = recordVolume,
            MigrationDays = meetingDays,
            MigrationTimes = meetingTimes,
            MigrationFee = fee,
        });
    }

    /// <summary>
    /// Platform yöneticisi durumu ilerletir: Alındı → İnceleniyor; İnceleniyor → Planlandı | Kapsam dışı; Planlandı → Tamamlandı.
    /// Kapsam dışında gerekçe zorunlu, başka durumda gerekçe yazılmaz. Not isteğe bağlıdır; boş gelirse önceki not kalır.
    /// </summary>
    public Result ChangeStatus(
        CustomDevelopmentRequestStatus target, string? decisionReason, string? responseNote, Guid userId, DateTime nowUtc)
    {
        if (userId == Guid.Empty)
            return Result.Failure(ValidationCode, "Kullanıcı geçersiz.");

        if (!NextStatuses(Status).Contains(target))
            return Result.Failure("CustomDevelopment.InvalidStatusTransition", $"Talep {Status} durumundan {target} durumuna geçemez.");

        var reason = string.IsNullOrWhiteSpace(decisionReason) ? null : decisionReason.Trim();
        if (target == CustomDevelopmentRequestStatus.OutOfScope && reason is null)
            return Result.Failure(ValidationCode, "Kapsam dışı kararında kliniğe görünecek gerekçe zorunludur.");
        if (target != CustomDevelopmentRequestStatus.OutOfScope && reason is not null)
            return Result.Failure(ValidationCode, "Gerekçe yalnız kapsam dışı kararında yazılır; açıklamayı nota yazın.");
        if (reason?.Length > MaxDecisionReasonLength)
            return Result.Failure(ValidationCode, $"Gerekçe en fazla {MaxDecisionReasonLength} karakter olabilir.");

        var note = string.IsNullOrWhiteSpace(responseNote) ? null : responseNote.Trim();
        if (note?.Length > MaxResponseNoteLength)
            return Result.Failure(ValidationCode, $"Not en fazla {MaxResponseNoteLength} karakter olabilir.");

        Status = target;
        if (reason is not null)
            DecisionReason = reason;
        if (note is not null)
            ResponseNote = note;
        StatusChangedAtUtc = NormalizeUtc(nowUtc);
        StatusChangedByUserId = userId;
        MutationSequence++;
        return Result.Success();
    }

    /// <summary>Durum akışının tek yeri; platform ekranı geçerli sonraki durumları buradan alır. Kapanmış talepte boş.</summary>
    public static IReadOnlyList<CustomDevelopmentRequestStatus> NextStatuses(CustomDevelopmentRequestStatus from)
        => from switch
        {
            CustomDevelopmentRequestStatus.Received => [CustomDevelopmentRequestStatus.InReview],
            CustomDevelopmentRequestStatus.InReview => [CustomDevelopmentRequestStatus.Planned, CustomDevelopmentRequestStatus.OutOfScope],
            CustomDevelopmentRequestStatus.Planned => [CustomDevelopmentRequestStatus.Completed],
            _ => [],
        };

    private static Result<CustomDevelopmentRequest> Invalid(string message)
        => Result<CustomDevelopmentRequest>.Failure(ValidationCode, message);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
