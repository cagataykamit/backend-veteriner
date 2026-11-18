using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Ai;

/// <summary>
/// AI'nin ürettiği taslak (AI-009; ADR-008 madde 3): taslak → inceleme → kabul (isteğe bağlı düzenlemeyle) → kayıt. Kabul
/// edilmemiş taslak hiçbir klinik kayda yazılmaz. <see cref="Content"/> klinik veridir ve yalnız karar bekleyen taslakta
/// tutulur: kabul, atma ve süre dolumunda silinir (kabulde içerik artık hedef kayıttadır). Karar alanları ve kaynak
/// kimlikleri silinmez; <see cref="AiUsageRecord.DraftId"/> ile birlikte AI denetim izidir (AI-010).
/// </summary>
public sealed class AiDraft : ITenantOwned
{
    public const int TargetTypeMaxLength = AiSourceRef.TypeMaxLength;

    /// <summary>Yapılandırılmış içeriğin (JSON) en çok uzunluğu; düzenlenmiş içerik de bu sınıra tabidir.</summary>
    public const int ContentMaxLength = 50_000;

    public static readonly Error NotPendingError = new("AiDrafts.NotPending", "Bu AI taslağı için zaten karar verilmiş.");
    public static readonly Error ExpiredError = new("AiDrafts.Expired", "AI taslağının süresi doldu; yeniden üretin.");

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public AiFeature Feature { get; private set; }

    /// <summary>Kabulde yazılacak kaydın türü (ör. <c>Examination</c>) ve kimliği.</summary>
    public string TargetType { get; private set; } = default!;

    public Guid TargetId { get; private set; }

    /// <summary>Üretimde modele gönderilen kayıtlar (yalnız tür ve kimlik).</summary>
    public IReadOnlyList<AiSourceRef> SourceRefs { get; private set; } = [];

    /// <summary>Üretilen yapılandırılmış içerik (JSON nesnesi); karar verilince <c>null</c>.</summary>
    public string? Content { get; private set; }

    public string Model { get; private set; } = default!;
    public string PromptVersion { get; private set; } = default!;
    public AiDraftStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Bu andan sonra karar verilemez; taslak <see cref="AiDraftStatus.Expired"/> sayılır.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    public Guid? DecidedByUserId { get; private set; }

    /// <summary>Kabul, atma ya da süre dolumu anı.</summary>
    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>Kabulde yazılan/oluşan kayıt (uygulayıcının bildirdiği; çoğu zaman <see cref="TargetId"/>).</summary>
    public Guid? AppliedRecordId { get; private set; }

    public byte[] RowVersion { get; private set; } = default!;

    private AiDraft() { }

    public static AiDraft Create(
        Guid tenantId,
        Guid clinicId,
        AiFeature feature,
        string targetType,
        Guid targetId,
        IEnumerable<AiSourceRef>? sourceRefs,
        string content,
        string model,
        string promptVersion,
        Guid createdByUserId,
        DateTime utcNow,
        TimeSpan lifetime)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (string.IsNullOrWhiteSpace(targetType) || targetType.Trim().Length > TargetTypeMaxLength)
            throw new ArgumentException("Hedef kayıt türü geçersiz.", nameof(targetType));
        if (targetId == Guid.Empty)
            throw new ArgumentException("Hedef kayıt geçersiz.", nameof(targetId));
        if (string.IsNullOrWhiteSpace(content) || content.Length > ContentMaxLength)
            throw new ArgumentException("Taslak içeriği geçersiz.", nameof(content));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model zorunlu.", nameof(model));
        if (string.IsNullOrWhiteSpace(promptVersion))
            throw new ArgumentException("İstem sürümü zorunlu.", nameof(promptVersion));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("Kullanıcı geçersiz.", nameof(createdByUserId));
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        return new AiDraft
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            Feature = feature,
            TargetType = targetType.Trim(),
            TargetId = targetId,
            SourceRefs = AiSourceRef.Normalize(sourceRefs),
            Content = content,
            Model = model.Length > AiUsageRecord.ModelMaxLength ? model[..AiUsageRecord.ModelMaxLength] : model,
            PromptVersion = promptVersion.Length > AiUsageRecord.PromptVersionMaxLength
                ? promptVersion[..AiUsageRecord.PromptVersionMaxLength]
                : promptVersion,
            Status = AiDraftStatus.Draft,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = utcNow,
            ExpiresAtUtc = utcNow + lifetime,
        };
    }

    /// <summary>Okuma anındaki durum: süresi geçmiş ama henüz taranmamış taslak da <see cref="AiDraftStatus.Expired"/> görünür.</summary>
    public AiDraftStatus StatusAt(DateTime utcNow)
        => Status == AiDraftStatus.Draft && utcNow >= ExpiresAtUtc ? AiDraftStatus.Expired : Status;

    /// <summary>Karar verilebilir mi: yalnız süresi dolmamış taslak.</summary>
    public Result EnsureDecidable(DateTime utcNow)
        => StatusAt(utcNow) switch
        {
            AiDraftStatus.Draft => Result.Success(),
            AiDraftStatus.Expired when Status == AiDraftStatus.Draft => Result.Failure(ExpiredError),
            _ => Result.Failure(NotPendingError),
        };

    /// <summary>Kabul: içerik hedef kayda yazıldıktan sonra çağrılır; taslaktaki içerik silinir.</summary>
    public Result Accept(Guid userId, bool wasEdited, Guid? appliedRecordId, DateTime utcNow)
    {
        var decidable = EnsureDecidable(utcNow);
        if (!decidable.IsSuccess)
            return decidable;
        if (userId == Guid.Empty)
            return Result.Failure("AiDrafts.Validation", "Kullanıcı zorunludur.");

        AppliedRecordId = appliedRecordId;
        Decide(wasEdited ? AiDraftStatus.AcceptedWithEdits : AiDraftStatus.Accepted, userId, utcNow);
        return Result.Success();
    }

    /// <summary>Atma: hiçbir kayda yazılmaz; içerik silinir.</summary>
    public Result Discard(Guid userId, DateTime utcNow)
    {
        var decidable = EnsureDecidable(utcNow);
        if (!decidable.IsSuccess)
            return decidable;
        if (userId == Guid.Empty)
            return Result.Failure("AiDrafts.Validation", "Kullanıcı zorunludur.");

        Decide(AiDraftStatus.Discarded, userId, utcNow);
        return Result.Success();
    }

    /// <summary>Süresi geçmiş taslağı kapatır ve içeriğini siler; aksi halde değişiklik yok (<c>false</c>).</summary>
    public bool ExpireIfDue(DateTime utcNow)
    {
        if (Status != AiDraftStatus.Draft || utcNow < ExpiresAtUtc)
            return false;

        Decide(AiDraftStatus.Expired, null, utcNow);
        return true;
    }

    private void Decide(AiDraftStatus status, Guid? userId, DateTime utcNow)
    {
        Status = status;
        DecidedByUserId = userId;
        DecidedAtUtc = utcNow;
        Content = null;
    }
}
