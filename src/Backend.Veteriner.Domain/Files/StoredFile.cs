using System.Linq.Expressions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Files;

/// <summary>
/// Yüklenen dosyanın meta kaydı (ASAMA6 L7). İçerik depolamada tahmin edilemez anahtarla (<see cref="StorageKey"/>) durur,
/// orijinal ad yalnız burada tutulur. Sıra (R4): meta <see cref="StoredFileStatus.Uploading"/> → içerik yazılır →
/// <see cref="MarkReady"/>; yazma bitmeden kalan kayıt bakım işiyle <see cref="StoredFileStatus.Deleted"/> olur.
/// Her dosya tek sahip kayda (<see cref="OwnerType"/> + <see cref="OwnerId"/>) bağlıdır.
/// </summary>
public sealed class StoredFile : AggregateRoot
{
    public const int StorageKeyMaxLength = 200;
    public const int ContentTypeMaxLength = 50;
    public const int Sha256HexLength = 64;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public StoredFileOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public string StorageKey { get; private set; } = default!;
    public string OriginalFileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public string? Sha256 { get; private set; }
    public StoredFileStatus Status { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReadyAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    /// <summary>Boşsa silme sistem işidir (sahip kaydın iptali değil, yarım yükleme temizliği).</summary>
    public Guid? DeletedByUserId { get; private set; }

    /// <summary>SQL Server rowversion — temizlik işi ile yüklemenin çakışmasını önler.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private StoredFile() { }

    /// <summary>Yükleme başlangıcı: içerik henüz yazılmadı; tür içerikten tespit edilmiş olmalıdır.</summary>
    public StoredFile(
        Guid tenantId,
        Guid clinicId,
        StoredFileOwnerType ownerType,
        Guid ownerId,
        string? originalFileName,
        StoredFileType type,
        Guid uploadedByUserId,
        DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is invalid.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId is invalid.", nameof(clinicId));
        if (!Enum.IsDefined(ownerType))
            throw new ArgumentException("OwnerType is invalid.", nameof(ownerType));
        if (ownerId == Guid.Empty)
            throw new ArgumentException("OwnerId is invalid.", nameof(ownerId));
        if (uploadedByUserId == Guid.Empty)
            throw new ArgumentException("UploadedByUserId is invalid.", nameof(uploadedByUserId));
        ArgumentNullException.ThrowIfNull(type);

        TenantId = tenantId;
        ClinicId = clinicId;
        OwnerType = ownerType;
        OwnerId = ownerId;
        OriginalFileName = StoredFileName.Sanitize(originalFileName);
        ContentType = type.ContentType;
        Status = StoredFileStatus.Uploading;
        UploadedByUserId = uploadedByUserId;
        CreatedAtUtc = nowUtc;
        StorageKey = BuildStorageKey(tenantId, clinicId, nowUtc, Id);
    }

    public StoredFileType Type => StoredFileType.FromContentType(ContentType)
        ?? throw new InvalidOperationException("Kayıtlı içerik türü izinli türlerden değil.");

    /// <summary>Depolama anahtarı: kiracı/klinik/yıl/ay alt dizinleri ve tahmin edilemez dosya kimliği (uzantısız).</summary>
    public static string BuildStorageKey(Guid tenantId, Guid clinicId, DateTime nowUtc, Guid fileId)
        => $"{tenantId:N}/{clinicId:N}/{nowUtc:yyyy}/{nowUtc:MM}/{fileId:N}";

    public Result MarkReady(long sizeBytes, string sha256Hex, DateTime nowUtc)
    {
        if (Status != StoredFileStatus.Uploading)
            return Result.Failure("Files.InvalidState", "Dosya yükleme durumunda değil.");
        if (sizeBytes <= 0)
            return Result.Failure("Files.Validation", "Dosya boş olamaz.");
        if (string.IsNullOrWhiteSpace(sha256Hex) || sha256Hex.Length != Sha256HexLength)
            return Result.Failure("Files.Validation", "SHA-256 özeti geçersiz.");

        SizeBytes = sizeBytes;
        Sha256 = sha256Hex.ToLowerInvariant();
        Status = StoredFileStatus.Ready;
        ReadyAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Erişimi kapatır (yumuşak silme, D10); fiziksel içerik bu yöntemle silinmez. Tekrar çağrı etkisizdir.</summary>
    public void MarkDeleted(Guid? deletedByUserId, DateTime nowUtc)
    {
        if (Status == StoredFileStatus.Deleted)
            return;

        Status = StoredFileStatus.Deleted;
        DeletedAtUtc = nowUtc;
        DeletedByUserId = deletedByUserId;
    }

    /// <summary>Yarım kalmış yükleme (bakım işi kuralı; R4): <paramref name="staleAfter"/> süresinden eski <see cref="StoredFileStatus.Uploading"/>.</summary>
    public static Expression<Func<StoredFile, bool>> IsStaleUpload(DateTime nowUtc, TimeSpan staleAfter)
    {
        var cutoff = nowUtc - staleAfter;
        return f => f.Status == StoredFileStatus.Uploading && f.CreatedAtUtc <= cutoff;
    }
}
