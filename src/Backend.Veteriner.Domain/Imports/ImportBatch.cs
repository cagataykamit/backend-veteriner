using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Imports;

/// <summary>
/// Müşteri / hasta / aşı geçmişi içe aktarma partisi (ADR-019). Parti hem kullanıcıya görünen iş kaydı hem de arka plan
/// iş kuyruğudur. Durum geçişlerinin tek yeri bu sınıftır; tabloda olmayan geçiş <c>Imports.InvalidState</c> döner.
/// Satır verisi (kişisel veri) burada değil <see cref="ImportBatchRow"/> içinde tutulur.
/// </summary>
public sealed class ImportBatch : AggregateRoot
{
    public const int MaxNameLength = 100;
    public const int ContentHashLength = 64;
    public const int MaxLeaseOwnerLength = 100;

    /// <summary>Kiracı başına tek aktif iş güvencesi olan filtreli benzersiz indeksin adı (çarpışma tespiti için).</summary>
    public const string ActiveJobIndexName = "UX_ImportBatches_TenantId_ActiveJob";

    /// <summary>Kiracı başına açık taslak sınırı (uygulama düzeyi).</summary>
    public const int MaxOpenDraftsPerTenant = 5;

    /// <summary>Hareketsiz taslağın süresinin dolması için geçmesi gereken süre.</summary>
    public static readonly TimeSpan DraftLifetime = TimeSpan.FromDays(7);

    /// <summary>Doğrulama kirası; çöken istek sonrası takılı kalan <c>Validating</c> parti bu süre sonunda devralınır.</summary>
    public static readonly TimeSpan ValidationLeaseDuration = TimeSpan.FromMinutes(5);

    /// <summary>Senkron doğrulamanın kira sahibi (arka plan işçisi B4'te kendi adını kullanır).</summary>
    public const string ValidationLeaseOwner = "api";

    /// <summary>Satır verisinin (kişisel veri) saklama süresi; son durum değişikliğinden sayılır (ADR-019 K9).</summary>
    public static readonly TimeSpan PayloadRetention = TimeSpan.FromDays(30);

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }

    /// <summary>Aşı satırlarının yazılacağı klinik; aşı tablosu yoksa boş olabilir.</summary>
    public Guid? ClinicId { get; private set; }

    /// <summary>İstemci tekrar koruması anahtarı; kiracıda benzersizdir.</summary>
    public Guid RequestId { get; private set; }

    /// <summary>Yüklenen içeriğin SHA-256 özeti (hex, 64 karakter); dosya yüklenene kadar boştur.</summary>
    public string? ContentHash { get; private set; }

    /// <summary>Listede görünen etiket; kişisel veri içermemelidir.</summary>
    public string? Name { get; private set; }

    public ImportBatchStatus Status { get; private set; }
    public string? MappingJson { get; private set; }
    public string? OptionsJson { get; private set; }

    /// <summary>Son başarılı doğrulamanın kimliği; uygulama yalnız bununla kabul edilir. Eşleme değişince silinir.</summary>
    public Guid? ValidationId { get; private set; }

    public ImportTableCounts CustomerCounts { get; private set; } = ImportTableCounts.Empty;
    public ImportTableCounts PetCounts { get; private set; } = ImportTableCounts.Empty;
    public ImportTableCounts VaccinationCounts { get; private set; } = ImportTableCounts.Empty;

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Son durum/içerik değişikliği; taslak süresi bundan (yoksa oluşturma anından) sayılır.</summary>
    public DateTime? UpdatedAtUtc { get; private set; }

    public DateTime? ValidatedAtUtc { get; private set; }
    public DateTime? CommittedAtUtc { get; private set; }
    public DateTime? RolledBackAtUtc { get; private set; }

    /// <summary>Doluysa partinin aşıları hatırlatma adayı olabilir; boşken hatırlatma işçisi bu partinin aşılarını dışlar.</summary>
    public DateTime? RemindersReleasedAtUtc { get; private set; }

    /// <summary>Satır verisinin (kişisel veri) temizlendiği an; sayaçlar ve özet kalır.</summary>
    public DateTime? PayloadPurgedAtUtc { get; private set; }

    /// <summary>Arka plan işçisi kirası; süresi dolan kira başka örnekçe devralınır.</summary>
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı durum geçişlerinde ikinci yazma reddedilir.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    public bool IsDraft => ImportBatchStatusSets.Draft.Contains(Status);

    /// <summary>
    /// Satır verisinin temizlenmesi gereken an (<see cref="PayloadRetention"/>); temizlenmişse veya satırları saklanan
    /// bir son duruma ulaşmamışsa null. Taslak silinirken veri zaten temizlenir (<see cref="Discard"/>).
    /// </summary>
    public DateTime? PayloadPurgeDueAtUtc
        => PayloadPurgedAtUtc is null && ImportBatchStatusSets.PayloadRetained.Contains(Status)
            ? (UpdatedAtUtc ?? CreatedAtUtc) + PayloadRetention
            : null;

    /// <summary>Eşlemenin kaydedilebildiği durumlar; <see cref="ApplyMapping"/> geçiş tablosuyla aynı küme.</summary>
    private static readonly ImportBatchStatus[] MappableFrom =
    [
        ImportBatchStatus.Uploaded, ImportBatchStatus.Mapped, ImportBatchStatus.Validated, ImportBatchStatus.ValidationFailed,
    ];

    public bool CanMap => MappableFrom.Contains(Status);

    private ImportBatch() { }

    public static Result<ImportBatch> Create(
        Guid tenantId,
        Guid? clinicId,
        Guid requestId,
        string? name,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        if (tenantId == Guid.Empty)
            return Invalid("TenantId geçersiz.");
        if (clinicId == Guid.Empty)
            return Invalid("ClinicId verilirse geçerli bir kimlik olmalıdır.");
        if (requestId == Guid.Empty)
            return Invalid("RequestId zorunludur.");
        if (createdByUserId == Guid.Empty)
            return Invalid("Oluşturan kullanıcı geçersiz.");

        var normalizedName = NormalizeName(name);
        if (normalizedName is { Length: > MaxNameLength })
            return Invalid($"Parti adı en fazla {MaxNameLength} karakter olabilir.");

        return Result<ImportBatch>.Success(new ImportBatch
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            RequestId = requestId,
            Name = normalizedName,
            Status = ImportBatchStatus.Uploaded,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = NormalizeUtc(createdAtUtc),
        });
    }

    /// <summary>Aynı <see cref="RequestId"/> ile gelen istek bu partiyi mi tarif ediyor (tekrar) yoksa başka içerik mi.</summary>
    public bool MatchesRequest(Guid? clinicId, string? name, string? contentHash)
        => ClinicId == clinicId
           && Name == NormalizeName(name)
           && string.Equals(ContentHash, contentHash, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Yüklenen içeriğin özetini ve tablo başına satır sayısını kaydeder (B2). Yalnız dosyası henüz bağlanmamış
    /// <c>Uploaded</c> parti için; sayaçların yalnız <c>Total</c> alanı dolar, doğrulama sonrası diğerleri gelir.
    /// </summary>
    public Result AttachFile(string contentHash, int customerRows, int petRows, int vaccinationRows)
    {
        if (Status != ImportBatchStatus.Uploaded || ContentHash is not null)
            return InvalidState("Dosya yalnız yeni oluşturulan partiye bağlanabilir.");

        if (contentHash is not { Length: ContentHashLength } || !contentHash.All(Uri.IsHexDigit))
            return Result.Failure("Imports.Validation", $"İçerik özeti {ContentHashLength} karakterlik onaltılık dizge olmalıdır.");

        if (customerRows < 0 || petRows < 0 || vaccinationRows < 0)
            return Result.Failure("Imports.Validation", "Satır sayısı negatif olamaz.");

        ContentHash = contentHash.ToLowerInvariant();
        CustomerCounts = ImportTableCounts.Empty with { Total = customerRows };
        PetCounts = ImportTableCounts.Empty with { Total = petRows };
        VaccinationCounts = ImportTableCounts.Empty with { Total = vaccinationRows };
        return Result.Success();
    }

    /// <summary>Eşlemeyi tam değiştirir; doğrulanmış parti yeniden eşlenince önizleme geçersizleşir.</summary>
    public Result ApplyMapping(string mappingJson, string? optionsJson, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(mappingJson))
            return Result.Failure("Imports.Validation", "Eşleme boş olamaz.");

        var moved = MoveTo(ImportBatchStatus.Mapped, nowUtc, MappableFrom);
        if (!moved.IsSuccess)
            return moved;

        MappingJson = mappingJson;
        OptionsJson = optionsJson;
        ValidationId = null;
        return Result.Success();
    }

    /// <summary>Takılı kalmış <c>Validating</c> partinin kirası dolduysa yeniden doğrulama başlatılabilir.</summary>
    public bool IsValidationLeaseExpired(DateTime nowUtc)
        => Status == ImportBatchStatus.Validating
           && (LeaseUntilUtc is null || LeaseUntilUtc <= NormalizeUtc(nowUtc));

    /// <summary>Doğrulama başlatılabilir mi (geçiş tablosu; kirası dolmuş <c>Validating</c> dahil).</summary>
    public bool CanStartValidation(DateTime nowUtc)
        => IsValidationLeaseExpired(nowUtc)
           || Status is ImportBatchStatus.Mapped or ImportBatchStatus.Validated or ImportBatchStatus.ValidationFailed;

    /// <summary>Doğrulaması en az bir kez bitmiş partide satır sonuçları, önizleme ve hata raporu okunabilir.</summary>
    public bool HasValidationResult => ImportBatchStatusSets.ValidationResultAvailable.Contains(Status);

    public Result StartValidation(DateTime nowUtc)
    {
        var now = NormalizeUtc(nowUtc);
        var moved = IsValidationLeaseExpired(now)
            ? MoveTo(ImportBatchStatus.Validating, now, ImportBatchStatus.Validating)
            : MoveTo(
                ImportBatchStatus.Validating,
                now,
                ImportBatchStatus.Mapped, ImportBatchStatus.Validated, ImportBatchStatus.ValidationFailed);
        if (!moved.IsSuccess)
            return moved;

        LeaseOwner = ValidationLeaseOwner;
        LeaseUntilUtc = now + ValidationLeaseDuration;
        return Result.Success();
    }

    public Result CompleteValidation(
        Guid validationId,
        ImportTableCounts customers,
        ImportTableCounts pets,
        ImportTableCounts vaccinations,
        DateTime nowUtc)
    {
        if (validationId == Guid.Empty)
            return Result.Failure("Imports.Validation", "ValidationId geçersiz.");

        var moved = MoveTo(ImportBatchStatus.Validated, nowUtc, ImportBatchStatus.Validating);
        if (!moved.IsSuccess)
            return moved;

        ValidationId = validationId;
        ValidatedAtUtc = UpdatedAtUtc;
        ReleaseLease();
        CustomerCounts = customers;
        PetCounts = pets;
        VaccinationCounts = vaccinations;
        return Result.Success();
    }

    /// <summary>
    /// Dosya düzeyi sorun (bağlantı sütunu yok, hiç geçerli satır yok) veya beklenmeyen hata. Önceki doğrulamanın
    /// sayaçları geçersizleşir: verilmeyen tablo yalnız <c>Total</c> taşır; verilenler (örn. "hepsi hatalı") korunur.
    /// </summary>
    public Result FailValidation(
        DateTime nowUtc,
        ImportTableCounts? customers = null,
        ImportTableCounts? pets = null,
        ImportTableCounts? vaccinations = null)
    {
        var moved = MoveTo(ImportBatchStatus.ValidationFailed, nowUtc, ImportBatchStatus.Validating);
        if (!moved.IsSuccess)
            return moved;

        ValidationId = null;
        ReleaseLease();
        CustomerCounts = customers ?? ImportTableCounts.Empty with { Total = CustomerCounts.Total };
        PetCounts = pets ?? ImportTableCounts.Empty with { Total = PetCounts.Total };
        VaccinationCounts = vaccinations ?? ImportTableCounts.Empty with { Total = VaccinationCounts.Total };
        return Result.Success();
    }

    /// <summary>Kullanıcı tam olarak önizlediğini onaylar: yalnız en son doğrulamanın kimliği kabul edilir.</summary>
    public Result StartCommit(Guid validationId, DateTime nowUtc)
    {
        if (Status == ImportBatchStatus.Validated && ValidationId != validationId)
        {
            return Result.Failure(
                "Imports.ValidationStale",
                "Önizleme güncel değil; eşleme değişmiş veya parti yeniden doğrulanmış.");
        }

        return MoveTo(ImportBatchStatus.Committing, nowUtc, ImportBatchStatus.Validated);
    }

    public Result CompleteCommit(
        ImportTableCounts customers,
        ImportTableCounts pets,
        ImportTableCounts vaccinations,
        DateTime nowUtc)
    {
        var moved = MoveTo(ImportBatchStatus.Committed, nowUtc, ImportBatchStatus.Committing);
        if (!moved.IsSuccess)
            return moved;

        CommittedAtUtc = UpdatedAtUtc;
        CustomerCounts = customers;
        PetCounts = pets;
        VaccinationCounts = vaccinations;
        return Result.Success();
    }

    /// <summary>Beklenmeyen hata: uygulanmış satırlar korunur ve geri alınabilir.</summary>
    public Result FailCommit(DateTime nowUtc)
        => MoveTo(ImportBatchStatus.CommitFailed, nowUtc, ImportBatchStatus.Committing);

    public Result StartRollback(DateTime nowUtc)
        => MoveTo(
            ImportBatchStatus.RollingBack,
            nowUtc,
            ImportBatchStatus.Committed, ImportBatchStatus.PartiallyRolledBack,
            ImportBatchStatus.CommitFailed, ImportBatchStatus.RollbackFailed);

    /// <summary><paramref name="partial"/>: engelli kayıtlar bırakıldı (<c>KeepBlocked</c>).</summary>
    public Result CompleteRollback(bool partial, DateTime nowUtc)
    {
        var target = partial ? ImportBatchStatus.PartiallyRolledBack : ImportBatchStatus.RolledBack;
        var moved = MoveTo(target, nowUtc, ImportBatchStatus.RollingBack);
        if (moved.IsSuccess)
            RolledBackAtUtc = UpdatedAtUtc;
        return moved;
    }

    public Result FailRollback(DateTime nowUtc)
        => MoveTo(ImportBatchStatus.RollbackFailed, nowUtc, ImportBatchStatus.RollingBack);

    /// <summary>Tek yönlü: içe aktarılan aşılar hatırlatma adayı olur. Durum değişmez.</summary>
    public Result ReleaseReminders(DateTime nowUtc)
    {
        if (Status is not (ImportBatchStatus.Committed or ImportBatchStatus.PartiallyRolledBack))
            return InvalidState("Hatırlatmalar yalnız uygulanmış partide serbest bırakılabilir.");

        if (RemindersReleasedAtUtc.HasValue)
            return InvalidState("Bu partinin hatırlatmaları zaten serbest bırakılmış.");

        RemindersReleasedAtUtc = NormalizeUtc(nowUtc);
        UpdatedAtUtc = RemindersReleasedAtUtc;
        return Result.Success();
    }

    /// <summary>
    /// Kullanıcı taslağı siler; ürün verisine dokunulmaz, kayıt geçmişte kalır. Hazırlık satırları (kişisel veri) aynı
    /// işlemde silindiğinden temizlik anı da işlenir.
    /// </summary>
    public Result Discard(DateTime nowUtc)
    {
        var moved = MoveTo(ImportBatchStatus.Discarded, nowUtc, [.. ImportBatchStatusSets.IdleDraft]);
        if (moved.IsSuccess)
            PayloadPurgedAtUtc = UpdatedAtUtc;
        return moved;
    }

    /// <summary>
    /// Saklama süresi dolan partinin satır verisi temizlendi (satırlar ayrıca <see cref="ImportBatchRow.PurgePayload"/>
    /// ile boşaltılır); sayaçlar ve özet kalır. Süre henüz dolmadıysa veya zaten temizlenmişse reddedilir.
    /// </summary>
    public Result PurgePayload(DateTime nowUtc)
    {
        var now = NormalizeUtc(nowUtc);
        if (PayloadPurgeDueAtUtc is not { } due || now < due)
            return InvalidState("Satır verisinin saklama süresi henüz dolmadı veya veri zaten temizlendi.");

        PayloadPurgedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Son hareketten <see cref="DraftLifetime"/> geçmiş taslağın süresi dolar.</summary>
    public Result Expire(DateTime nowUtc)
    {
        var now = NormalizeUtc(nowUtc);
        if (ImportBatchStatusSets.IdleDraft.Contains(Status) && now - (UpdatedAtUtc ?? CreatedAtUtc) < DraftLifetime)
            return InvalidState($"Taslak son hareketten {DraftLifetime.TotalDays:0} gün geçmeden süresi dolmuş sayılamaz.");

        return MoveTo(ImportBatchStatus.Expired, now, [.. ImportBatchStatusSets.IdleDraft]);
    }

    private Result MoveTo(ImportBatchStatus target, DateTime nowUtc, params ImportBatchStatus[] allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
            return InvalidState($"Parti '{Status}' durumundan '{target}' durumuna geçemez.");

        Status = target;
        UpdatedAtUtc = NormalizeUtc(nowUtc);
        return Result.Success();
    }

    private void ReleaseLease()
    {
        LeaseOwner = null;
        LeaseUntilUtc = null;
    }

    private static string? NormalizeName(string? name)
        => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    private static Result InvalidState(string message)
        => Result.Failure("Imports.InvalidState", message);

    private static Result<ImportBatch> Invalid(string message)
        => Result<ImportBatch>.Failure("Imports.Validation", message);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
