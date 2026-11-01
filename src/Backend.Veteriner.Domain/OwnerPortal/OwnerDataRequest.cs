using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.OwnerPortal;

/// <summary>KVKK m.11 başvuru türü (PORTAL-007). Sayısal değerler stabildir.</summary>
public enum OwnerDataRequestType
{
    /// <summary>Verilerime erişim / kopya.</summary>
    Access = 0,

    /// <summary>Yanlış ya da eksik verinin düzeltilmesi.</summary>
    Rectification = 1,

    /// <summary>Silme, yok etme ya da anonimleştirme.</summary>
    Erasure = 2,

    /// <summary>İşlemeye itiraz.</summary>
    Objection = 3,

    Other = 4,
}

public enum OwnerDataRequestStatus
{
    Open = 0,
    InProgress = 1,
    Completed = 2,
    Rejected = 3,

    /// <summary>Sahip vazgeçti (yalnız <see cref="Open"/> iken).</summary>
    Cancelled = 4,
}

/// <summary>
/// Hasta sahibinin portaldan, bağın kliniğindeki klinik kayıtları için yaptığı KVKK başvurusu (PORTAL-007; ADR-031 K4). Klinik veri
/// sorumlusudur ve süreç belgesine göre yanıtlar; sistem talebi, süreyi ve yanıtı izler, silme/anonimleştirmeyi kendisi yapmaz.
/// Portal hesabının silinmesi bu akış değildir (ADR-041 ÇV-3).
/// </summary>
public sealed class OwnerDataRequest : ITenantOwned, IClinicScoped
{
    public const int MessageMaxLength = 2000;
    public const int ResponseNoteMaxLength = 2000;

    /// <summary>Yanıt süresi: başvurudan itibaren 30 gün (KVKK m.13; çalışma varsayımı, hukukçu farklı derse tek yer).</summary>
    public const int ResponseDueDays = 30;

    /// <summary>Bir bağda aynı anda açık (Open/InProgress) en fazla talep.</summary>
    public const int MaxOpenPerLink = 3;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid OwnerAccountId { get; private set; }
    public Guid LinkId { get; private set; }
    public OwnerDataRequestType Type { get; private set; }
    public string Message { get; private set; } = default!;
    public OwnerDataRequestStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime DueAtUtc { get; private set; }
    public Guid? HandledByUserId { get; private set; }
    public DateTime? HandledAtUtc { get; private set; }
    public string? ResponseNote { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    /// <summary>Başarılı mutasyon sayacı; optimistic concurrency token.</summary>
    public long MutationSequence { get; private set; }

    public bool IsOpen => Status is OwnerDataRequestStatus.Open or OwnerDataRequestStatus.InProgress;

    public bool IsOverdue(DateTime utcNow) => IsOpen && utcNow > DueAtUtc;

    private OwnerDataRequest() { }

    public static Result<OwnerDataRequest> Create(
        OwnerAccountClientLink link,
        OwnerDataRequestType type,
        string? message,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (!Enum.IsDefined(type))
            return Result<OwnerDataRequest>.Failure("Portal.DataRequests.Validation", "Talep türü geçersiz.");

        var text = message?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MessageMaxLength)
            return Result<OwnerDataRequest>.Failure("Portal.DataRequests.Validation", $"Açıklama 1-{MessageMaxLength} karakter olmalıdır.");

        var now = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        return Result<OwnerDataRequest>.Success(new OwnerDataRequest
        {
            TenantId = link.TenantId,
            ClinicId = link.ClinicId,
            ClientId = link.ClientId,
            OwnerAccountId = link.OwnerAccountId,
            LinkId = link.Id,
            Type = type,
            Message = text,
            Status = OwnerDataRequestStatus.Open,
            CreatedAtUtc = now,
            DueAtUtc = now.AddDays(ResponseDueDays),
        });
    }

    /// <summary>Sahip yalnız henüz ele alınmamış (Open) talebi geri çekebilir.</summary>
    public Result Cancel(DateTime utcNow)
    {
        if (Status != OwnerDataRequestStatus.Open)
            return Result.Failure("Portal.DataRequests.NotOpen", "Yalnız henüz ele alınmamış talep geri çekilebilir.");

        Status = OwnerDataRequestStatus.Cancelled;
        CancelledAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        MutationSequence++;
        return Result.Success();
    }

    /// <summary>
    /// Klinik durumu değiştirir: Open → InProgress | Completed | Rejected; InProgress → Completed | Rejected. Kapanışta yanıt notu
    /// zorunludur (sahibe gider). Kapanmış talep değişmez.
    /// </summary>
    public Result Handle(OwnerDataRequestStatus target, string? responseNote, Guid userId, DateTime utcNow)
    {
        if (userId == Guid.Empty)
            return Result.Failure("PortalInbox.Validation", "Kullanıcı geçersiz.");

        var allowed = (Status, target) switch
        {
            (OwnerDataRequestStatus.Open, OwnerDataRequestStatus.InProgress) => true,
            (OwnerDataRequestStatus.Open or OwnerDataRequestStatus.InProgress, OwnerDataRequestStatus.Completed or OwnerDataRequestStatus.Rejected) => true,
            _ => false,
        };
        if (!allowed)
            return Result.Failure("PortalInbox.InvalidStatusTransition", $"Talep {Status} durumundan {target} durumuna geçemez.");

        var note = responseNote?.Trim();
        var closing = target is OwnerDataRequestStatus.Completed or OwnerDataRequestStatus.Rejected;
        if (closing && string.IsNullOrEmpty(note))
            return Result.Failure("PortalInbox.ResponseNoteRequired", "Talep kapatılırken hasta sahibine gidecek yanıt notu zorunludur.");
        if (note is { Length: > ResponseNoteMaxLength })
            return Result.Failure("PortalInbox.Validation", $"Yanıt notu en fazla {ResponseNoteMaxLength} karakter olabilir.");

        Status = target;
        HandledByUserId = userId;
        HandledAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        if (!string.IsNullOrEmpty(note))
            ResponseNote = note;
        MutationSequence++;
        return Result.Success();
    }
}
