using System.Linq.Expressions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Hospitalizations;

/// <summary>
/// In-clinic hospitalization / observation stay for a pet; optionally linked to an examination and/or a visit.
/// </summary>
public sealed class Hospitalization : AggregateRoot, IClinicScoped, ITenantOwned
{
    /// <summary>Etkin yatış kuralı (tek yer): taburcu edilmemiş ve iptal edilmemiş. Sorgular bu ifadeyi kullanır.</summary>
    public static readonly Expression<Func<Hospitalization, bool>> IsActiveExpression
        = h => h.DischargedAtUtc == null && h.CancelledAtUtc == null;

    /// <summary>Taburcu edilmiş ve iptal edilmemiş (liste "geçmiş yatışlar" filtresi).</summary>
    public static readonly Expression<Func<Hospitalization, bool>> IsDischargedExpression
        = h => h.DischargedAtUtc != null && h.CancelledAtUtc == null;

    public static readonly Expression<Func<Hospitalization, bool>> IsNotCancelledExpression
        = h => h.CancelledAtUtc == null;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? ExaminationId { get; private set; }
    public Guid? VisitId { get; private set; }
    public DateTime AdmittedAtUtc { get; private set; }
    public DateTime? PlannedDischargeAtUtc { get; private set; }
    public DateTime? DischargedAtUtc { get; private set; }
    public string Reason { get; private set; } = default!;
    public string? Notes { get; private set; }

    /// <summary>
    /// Serbest metin konum (ör. "Kafes 3"); geriye dönük uyum için kalır. Oda atanınca (<see cref="RoomId"/>) metin
    /// odadan türetilir ("Bölüm / Oda"); kapasite/çakışma kontrolü yoktur.
    /// </summary>
    public string? Location { get; private set; }

    /// <summary>Klinik tanımlı oda (<see cref="HospitalizationRoom"/>); eski kayıtlarda ve oda seçilmemişse null.</summary>
    public Guid? RoomId { get; private set; }

    /// <summary>Taburcu notu; giriş notunu (<see cref="Notes"/>) ezmemek için ayrı tutulur.</summary>
    public string? DischargeNote { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Gerekçeli iptal (ADR-043 AK-5): yanlış açılan yatış silinmez; iptal edilen yatış etkin sayılmaz ve değiştirilemez.
    /// Etkin ve taburcu edilmiş yatışta yapılabilir.
    /// </summary>
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancelReason { get; private set; }
    public bool IsCancelled => CancelledAtUtc.HasValue;

    /// <summary><see cref="IsActiveExpression"/> ile aynı kural, bellekteki kayıt için.</summary>
    public bool IsActive => DischargedAtUtc is null && !IsCancelled;

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private Hospitalization() { }

    public Hospitalization(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        DateTime admittedAtUtc,
        DateTime? plannedDischargeAtUtc,
        string reason,
        string? notes,
        DateTime utcNow,
        Guid? visitId = null,
        string? location = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is invalid.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId is invalid.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId is invalid.", nameof(petId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));

        var admitted = NormalizeUtc(admittedAtUtc);
        if (plannedDischargeAtUtc.HasValue && NormalizeUtc(plannedDischargeAtUtc.Value) < admitted)
            throw new ArgumentException("Planned discharge must not be before admission.", nameof(plannedDischargeAtUtc));

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        VisitId = visitId;
        AdmittedAtUtc = admitted;
        PlannedDischargeAtUtc = plannedDischargeAtUtc.HasValue ? NormalizeUtc(plannedDischargeAtUtc.Value) : null;
        DischargedAtUtc = null;
        Reason = reason.Trim();
        Notes = Trimmed(notes);
        Location = Trimmed(location);
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = null;
    }

    /// <summary>
    /// <paramref name="notes"/> tam değiştirilir (null/boş = temizle). <paramref name="location"/>: null = dokunma,
    /// boş/boşluk = temizle, dolu = değiştir.
    /// </summary>
    public Result UpdateDetails(
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        Guid? visitId,
        DateTime admittedAtUtc,
        DateTime? plannedDischargeAtUtc,
        string reason,
        string? notes,
        string? location,
        DateTime utcNow)
    {
        if (IsCancelled)
            return CancelledFailure();
        if (DischargedAtUtc.HasValue)
            return Result.Failure("Hospitalizations.AlreadyDischarged", "Discharged hospitalization cannot be updated.");

        if (clinicId == Guid.Empty)
            return Result.Failure("Hospitalizations.Validation", "ClinicId is invalid.");
        if (petId == Guid.Empty)
            return Result.Failure("Hospitalizations.Validation", "PetId is invalid.");
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure("Hospitalizations.Validation", "Reason is required.");
        var subject = RecordSubjectRule.EnsureUnchanged(ClinicId, PetId, clinicId, petId, "Hospitalizations");
        if (!subject.IsSuccess)
            return subject;

        var admitted = NormalizeUtc(admittedAtUtc);
        if (plannedDischargeAtUtc.HasValue && NormalizeUtc(plannedDischargeAtUtc.Value) < admitted)
        {
            return Result.Failure(
                "Hospitalizations.PlannedDischargeBeforeAdmission",
                "Planned discharge must not be before admission.");
        }

        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        VisitId = visitId;
        AdmittedAtUtc = admitted;
        PlannedDischargeAtUtc = plannedDischargeAtUtc.HasValue ? NormalizeUtc(plannedDischargeAtUtc.Value) : null;
        Reason = reason.Trim();
        Notes = Trimmed(notes);
        if (location is not null)
            Location = Trimmed(location);
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// <paramref name="dischargeNote"/>: null = dokunma, boş/boşluk = temizle, dolu = değiştir. Giriş notuna dokunulmaz.
    /// </summary>
    public Result Discharge(DateTime dischargedAtUtc, string? dischargeNote, DateTime utcNow)
    {
        if (IsCancelled)
            return CancelledFailure();
        if (DischargedAtUtc.HasValue)
            return Result.Failure("Hospitalizations.AlreadyDischarged", "Hospitalization is already discharged.");

        var d = NormalizeUtc(dischargedAtUtc);
        if (d < AdmittedAtUtc)
        {
            return Result.Failure(
                "Hospitalizations.DischargedBeforeAdmission",
                "Discharge time must not be before admission.");
        }

        DischargedAtUtc = d;
        if (dischargeNote is not null)
            DischargeNote = Trimmed(dischargeNote);

        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// Odayı bağlar ve konum metnini odadan türetir (<see cref="FormatRoomLocation"/>); serbest metin konum ezilir.
    /// Oda–bölüm–klinik tutarlılığı çağıranın (handler) sorumluluğudur; burada yalnız savunma amaçlı denetlenir.
    /// </summary>
    public Result AssignRoom(HospitalizationRoom room, HospitalizationWard ward)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(ward);

        if (IsCancelled)
            return CancelledFailure();
        if (DischargedAtUtc.HasValue)
            return Result.Failure("Hospitalizations.AlreadyDischarged", "Discharged hospitalization cannot be updated.");
        if (room.WardId != ward.Id)
            throw new ArgumentException("Room does not belong to the given ward.", nameof(room));
        if (room.TenantId != TenantId || room.ClinicId != ClinicId)
            throw new ArgumentException("Room does not belong to the hospitalization clinic.", nameof(room));

        RoomId = room.Id;
        Location = FormatRoomLocation(ward.Name, room.Name);
        return Result.Success();
    }

    /// <summary>Oda bağını kaldırır; konum metni (<see cref="Location"/>) olduğu gibi kalır.</summary>
    public Result ClearRoom()
    {
        if (IsCancelled)
            return CancelledFailure();
        if (DischargedAtUtc.HasValue)
            return Result.Failure("Hospitalizations.AlreadyDischarged", "Discharged hospitalization cannot be updated.");

        RoomId = null;
        return Result.Success();
    }

    /// <summary>Yatışı gerekçeyle iptal eder (silmez; kural <see cref="RecordCancellationRules"/>). Kesinleşmiş muayeneye bağlı yatışta da yapılabilir.</summary>
    public Result Cancel(Guid cancelledByUserId, string? reason, DateTime utcNow)
    {
        if (IsCancelled)
            return Result.Failure("Hospitalizations.AlreadyCancelled", "Yatış zaten iptal edilmiş.");

        var cancellation = RecordCancellationRules.Resolve(cancelledByUserId, reason, utcNow, "Hospitalizations.Validation");
        if (!cancellation.IsSuccess)
            return Result.Failure(cancellation.Error);

        CancelledAtUtc = cancellation.Value!.CancelledAtUtc;
        CancelledByUserId = cancellation.Value.CancelledByUserId;
        CancelReason = cancellation.Value.Reason;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    private static Result CancelledFailure()
        => Result.Failure("Hospitalizations.AlreadyCancelled", "İptal edilmiş yatış değiştirilemez.");

    /// <summary>Odadan türetilen konum metni biçimi tek yerde: "Bölüm / Oda".</summary>
    public static string FormatRoomLocation(string wardName, string roomName)
        => $"{wardName} / {roomName}";

    /// <summary>
    /// İstemcinin gördüğü sürüm: kalıcı yazma yalnızca veritabanındaki sürüm bu değere eşitse gerçekleşir.
    /// </summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
