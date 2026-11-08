using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.DataExports;

/// <summary>Tüm veri dışa aktarma işinin durumu (DATA_EXPORT_API_CONTRACT.md §3). Sayısal değerler stabildir.</summary>
public enum DataExportJobStatus
{
    /// <summary>Talep alındı, arka plan işçisini bekliyor.</summary>
    Queued = 0,

    /// <summary>Paket hazırlanıyor (işçide kira var).</summary>
    Running = 1,

    /// <summary>Paket hazır; <see cref="DataExportJob.ExpiresAtUtc"/> anına kadar indirilebilir.</summary>
    Ready = 2,

    /// <summary>Hazırlanamadı (deneme hakkı bitti); yeni talep açılabilir.</summary>
    Failed = 3,

    /// <summary>Saklama süresi doldu, dosya silindi.</summary>
    Expired = 4,
}

/// <summary>
/// Kiracının tüm verisini tek ZIP pakette dışa aktarma işi (veri taşınabilirliği; veri işleme sözleşmesi m.6). İş kuyruğu bu
/// tablodur: işçi <see cref="DataExportJobStatus.Queued"/> veya kirası dolmuş <see cref="DataExportJobStatus.Running"/> işi
/// kira ile alır (rowversion: iki örnekten biri). Kiracıda aynı anda tek açık iş vardır (filtreli benzersiz indeks
/// <see cref="OpenJobIndexName"/>); saklama ve talep sınırı kuralları bu sınıfın sabitlerindedir.
/// </summary>
public sealed class DataExportJob : ITenantOwned
{
    public const string OpenJobIndexName = "UX_DataExportJobs_TenantId_OpenJob";
    public const int MaxLeaseOwnerLength = 100;

    /// <summary>Hazır paketin saklama süresi; süre sonunda dosya silinir, iş <see cref="DataExportJobStatus.Expired"/> olur.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>Talep sınırı penceresi ve pencere içindeki en çok talep (başarısız işler sayılmaz).</summary>
    public static readonly TimeSpan RequestLimitWindow = TimeSpan.FromHours(24);
    public const int MaxRequestsPerWindow = 3;

    /// <summary>Hazırlama kirası; işçi her dosyadan sonra yeniler, çöken işi kira dolunca başka işçi devralır.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    /// <summary>Kira alma sayısı bu sınıra ulaşınca iş yeniden denenmez, <see cref="DataExportJobStatus.Failed"/> olur.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Açık iş durumları (tek açık iş indeksi ve talep kuralı aynı kümeyi kullanır).</summary>
    public static readonly IReadOnlyList<DataExportJobStatus> OpenStatuses = [DataExportJobStatus.Queued, DataExportJobStatus.Running];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DataExportJobStatus Status { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }

    /// <summary>Hazır ya da başarısız olarak sonlandığı an (süre dolumu bu anı değiştirmez).</summary>
    public DateTime? CompletedAtUtc { get; private set; }

    public DateTime? ExpiresAtUtc { get; private set; }

    /// <summary>Paketin depo anahtarı; yalnız hazırken dolu (süre dolunca dosya silinir, anahtar temizlenir).</summary>
    public string? StorageKey { get; private set; }

    public long? FileSizeBytes { get; private set; }
    public int Attempts { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = default!;

    private DataExportJob() { }

    public static DataExportJob Create(Guid tenantId, Guid requestedByUserId, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (requestedByUserId == Guid.Empty)
            throw new ArgumentException("Talep eden kullanıcı geçersiz.", nameof(requestedByUserId));

        return new DataExportJob
        {
            TenantId = tenantId,
            RequestedByUserId = requestedByUserId,
            Status = DataExportJobStatus.Queued,
            RequestedAtUtc = NormalizeUtc(nowUtc),
        };
    }

    public bool IsOpen => OpenStatuses.Contains(Status);

    /// <summary>İndirilebilir: hazır ve saklama süresi dolmamış.</summary>
    public bool IsDownloadable(DateTime nowUtc) => Status == DataExportJobStatus.Ready && ExpiresAtUtc > NormalizeUtc(nowUtc);

    /// <summary>Hazır ve saklama süresi dolmuş: bakım işi dosyayı silip işi kapatır.</summary>
    public bool IsExpiryDue(DateTime nowUtc) => Status == DataExportJobStatus.Ready && ExpiresAtUtc <= NormalizeUtc(nowUtc);

    /// <summary>Paket dosyasının depo anahtarı: <c>{kiracı}/exports/{yyyy}/{MM}/{iş}</c> (yalnız sunucu üretir).</summary>
    public static string BuildStorageKey(Guid tenantId, DateTime nowUtc, Guid jobId)
        => $"{tenantId:N}/exports/{nowUtc:yyyy}/{nowUtc:MM}/{jobId:N}";

    /// <summary>İşçi kuyruktaki ya da kirası dolmuş işi alır; her alış bir deneme sayılır.</summary>
    public Result ClaimLease(string owner, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > MaxLeaseOwnerLength)
            return Result.Failure("DataExports.Validation", $"Kira sahibi boş olamaz ve en fazla {MaxLeaseOwnerLength} karakter olabilir.");
        if (!IsOpen)
            return InvalidState($"İş '{Status}' durumunda; alınamaz.");

        var now = NormalizeUtc(nowUtc);
        if (LeaseUntilUtc > now && LeaseOwner != owner)
            return Result.Failure("DataExports.AnotherJobRunning", "İşin kirası başka bir işçide.");
        if (Attempts >= MaxAttempts)
            return InvalidState("İşin deneme hakkı bitti.");

        Status = DataExportJobStatus.Running;
        StartedAtUtc ??= now;
        Attempts++;
        LeaseOwner = owner;
        LeaseUntilUtc = now + LeaseDuration;
        return Result.Success();
    }

    /// <summary>Kirayı uzatır; iş başka işçiye geçtiyse reddedilir ve eski işçi durmalıdır.</summary>
    public Result RenewLease(string owner, DateTime nowUtc)
    {
        if (Status != DataExportJobStatus.Running || LeaseOwner != owner)
            return Result.Failure("DataExports.LeaseLost", "İşin kirası bu işçide değil.");

        LeaseUntilUtc = NormalizeUtc(nowUtc) + LeaseDuration;
        return Result.Success();
    }

    public Result MarkReady(string owner, string storageKey, long fileSizeBytes, DateTime nowUtc)
    {
        if (Status != DataExportJobStatus.Running || LeaseOwner != owner)
            return Result.Failure("DataExports.LeaseLost", "İşin kirası bu işçide değil.");
        if (string.IsNullOrWhiteSpace(storageKey) || fileSizeBytes <= 0)
            return Result.Failure("DataExports.Validation", "Paket dosyası boş olamaz.");

        var now = NormalizeUtc(nowUtc);
        Status = DataExportJobStatus.Ready;
        StorageKey = storageKey;
        FileSizeBytes = fileSizeBytes;
        CompletedAtUtc = now;
        ExpiresAtUtc = now + Retention;
        ReleaseLease();
        return Result.Success();
    }

    /// <summary>Açık işi başarısız kapatır (deneme hakkı bitti ya da beklenmeyen hata).</summary>
    public Result MarkFailed(DateTime nowUtc)
    {
        if (!IsOpen)
            return InvalidState($"İş '{Status}' durumunda; başarısız işaretlenemez.");

        Status = DataExportJobStatus.Failed;
        CompletedAtUtc = NormalizeUtc(nowUtc);
        ReleaseLease();
        return Result.Success();
    }

    /// <summary>Saklama süresi dolan hazır işi kapatır; dosya çağıran tarafından (önce) silinir.</summary>
    public Result Expire(DateTime nowUtc)
    {
        if (!IsExpiryDue(nowUtc))
            return InvalidState("İşin saklama süresi dolmadı ya da iş hazır değil.");

        Status = DataExportJobStatus.Expired;
        StorageKey = null;
        return Result.Success();
    }

    private void ReleaseLease()
    {
        LeaseOwner = null;
        LeaseUntilUtc = null;
    }

    private static Result InvalidState(string message) => Result.Failure("DataExports.InvalidState", message);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
