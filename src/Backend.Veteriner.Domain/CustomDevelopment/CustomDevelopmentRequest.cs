using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.CustomDevelopment;

/// <summary>Talebin konusu (TIER-010): Vetinity içinde kalan ekran, rapor ya da çıktı. Sayısal değerler stabildir.</summary>
public enum CustomDevelopmentTargetKind
{
    Screen = 0,
    Report = 1,

    /// <summary>Yazdırma, PDF, dışa aktarma gibi çıktı.</summary>
    Output = 2,
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
/// hasta verisi için değildir; denetim kaydında maskelenir.
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
        if (!Enum.IsDefined(targetKind))
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
    /// Platform yöneticisi durumu ilerletir: Alındı → İnceleniyor; İnceleniyor → Planlandı | Kapsam dışı; Planlandı → Tamamlandı.
    /// Kapsam dışında gerekçe zorunlu, başka durumda gerekçe yazılmaz. Not isteğe bağlıdır; boş gelirse önceki not kalır.
    /// </summary>
    public Result ChangeStatus(
        CustomDevelopmentRequestStatus target, string? decisionReason, string? responseNote, Guid userId, DateTime nowUtc)
    {
        if (userId == Guid.Empty)
            return Result.Failure(ValidationCode, "Kullanıcı geçersiz.");

        var allowed = (Status, target) switch
        {
            (CustomDevelopmentRequestStatus.Received, CustomDevelopmentRequestStatus.InReview) => true,
            (CustomDevelopmentRequestStatus.InReview, CustomDevelopmentRequestStatus.Planned or CustomDevelopmentRequestStatus.OutOfScope) => true,
            (CustomDevelopmentRequestStatus.Planned, CustomDevelopmentRequestStatus.Completed) => true,
            _ => false,
        };
        if (!allowed)
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
