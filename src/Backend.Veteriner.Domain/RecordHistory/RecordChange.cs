using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.RecordHistory;

/// <summary>
/// Klinik kaydın değişiklik geçmişi satırı (RECORD-002): kim, ne zaman, hangi alanı neden neye çevirdi. Append-only;
/// kayıt kaydedilirken altyapı tarafından yazılır, uygulama kodu üretmez.
/// </summary>
public sealed class RecordChange : ITenantOwned, IClinicScoped
{
    public const int RecordTypeMaxLength = 50;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public string RecordType { get; private set; } = default!;
    public Guid RecordId { get; private set; }
    public RecordChangeKind Kind { get; private set; }

    /// <summary>Değişikliği yapan kullanıcı; kullanıcısız (sistem) değişiklikler kaydedilmez.</summary>
    public Guid ActorUserId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>Yalnız <see cref="RecordChangeKind.Updated"/> için dolu; oluşturma ve silmede boştur.</summary>
    public IReadOnlyList<RecordFieldChange> Changes { get; private set; } = Array.Empty<RecordFieldChange>();

    private RecordChange() { }

    public RecordChange(
        Guid tenantId,
        Guid clinicId,
        string recordType,
        Guid recordId,
        RecordChangeKind kind,
        Guid actorUserId,
        DateTime occurredAtUtc,
        IReadOnlyList<RecordFieldChange> changes)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is invalid.", nameof(tenantId));
        if (recordId == Guid.Empty)
            throw new ArgumentException("RecordId is invalid.", nameof(recordId));
        if (actorUserId == Guid.Empty)
            throw new ArgumentException("ActorUserId is invalid.", nameof(actorUserId));
        if (string.IsNullOrWhiteSpace(recordType) || recordType.Length > RecordTypeMaxLength)
            throw new ArgumentException("RecordType is invalid.", nameof(recordType));
        if (kind == RecordChangeKind.Updated && changes.Count == 0)
            throw new ArgumentException("An update must change at least one field.", nameof(changes));

        TenantId = tenantId;
        ClinicId = clinicId;
        RecordType = recordType;
        RecordId = recordId;
        Kind = kind;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
        Changes = kind == RecordChangeKind.Updated ? changes.ToList() : Array.Empty<RecordFieldChange>();
    }
}
