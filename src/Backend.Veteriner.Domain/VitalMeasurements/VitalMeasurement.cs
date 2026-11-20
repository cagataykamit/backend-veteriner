using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.VitalMeasurements;

/// <summary>
/// Zaman damgalı vital ölçümü (HOSP-004, ADR-043 AK-9): yatan hastada gün içinde tekrarlanan ateş, nabız, solunum ve kilo.
/// Yatışa veya muayeneye bağlanabilir (ikisine birden değil); hasta, klinik ve bağ oluşturulduktan sonra değişmez (ADR-043 AK-10).
/// Silinmez, gerekçeyle iptal edilir (ADR-043 K4).
/// </summary>
public sealed class VitalMeasurement : AggregateRoot, IClinicScoped, ITenantOwned
{
    public const int MaxNoteLength = 500;
    public const string ValidationCode = "VitalMeasurements.Validation";
    public const string AlreadyCancelledCode = "VitalMeasurements.AlreadyCancelled";
    public const string LateEntryReasonRequiredCode = "VitalMeasurements.LateEntryReasonRequired";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? HospitalizationId { get; private set; }
    public Guid? ExaminationId { get; private set; }
    public DateTime MeasuredAtUtc { get; private set; }

    /// <summary>Vücut sıcaklığı (°C); birim ve aralık <see cref="VitalSigns"/>.</summary>
    public decimal? TemperatureC { get; private set; }

    /// <summary>Kalp hızı (atım/dk).</summary>
    public int? HeartRateBpm { get; private set; }

    /// <summary>Solunum sayısı (solunum/dk).</summary>
    public int? RespiratoryRatePerMin { get; private set; }

    /// <summary>Kilo (kg).</summary>
    public decimal? WeightKg { get; private set; }

    public string? Note { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Gerekçeli iptal (ADR-043 K4): iptal edilen ölçüm değiştirilemez, eğilim ve listede varsayılan gizlidir.</summary>
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancelReason { get; private set; }
    public bool IsCancelled => CancelledAtUtc.HasValue;

    /// <summary>Bağlı muayene kayıt anında kesinleşmişse true (ADR-043 AK-6); gerekçe <see cref="LateEntryReason"/>.</summary>
    public bool AddedAfterFinalization { get; private set; }
    public string? LateEntryReason { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için (ADR-043 K5).</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private VitalMeasurement() { }

    public static Result<VitalMeasurement> Create(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? hospitalizationId,
        Guid? examinationId,
        DateTime measuredAtUtc,
        VitalSigns values,
        string? note,
        Guid createdByUserId,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is invalid.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId is invalid.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId is invalid.", nameof(petId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId is invalid.", nameof(createdByUserId));
        ArgumentNullException.ThrowIfNull(values);

        if (hospitalizationId.HasValue && examinationId.HasValue)
            return Result<VitalMeasurement>.Failure(ValidationCode, "Ölçüm yatışa ya da muayeneye bağlanır; ikisine birden bağlanamaz.");

        var details = ValidateDetails(measuredAtUtc, note);
        if (!details.IsSuccess)
            return Result<VitalMeasurement>.Failure(details.Error);

        var measurement = new VitalMeasurement
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            PetId = petId,
            HospitalizationId = hospitalizationId,
            ExaminationId = examinationId,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = utcNow,
        };
        measurement.Apply(measuredAtUtc, values, note);
        return Result<VitalMeasurement>.Success(measurement);
    }

    /// <summary>Zaman, değerler ve not tam değiştirilir; hasta, klinik ve bağ değişmez.</summary>
    public Result Update(DateTime measuredAtUtc, VitalSigns values, string? note, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (IsCancelled)
            return Result.Failure(AlreadyCancelledCode, "İptal edilmiş vital ölçümü değiştirilemez.");

        var details = ValidateDetails(measuredAtUtc, note);
        if (!details.IsSuccess)
            return details;

        Apply(measuredAtUtc, values, note);
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Ölçümü gerekçeyle iptal eder (silmez; kural <see cref="RecordCancellationRules"/>).</summary>
    public Result Cancel(Guid cancelledByUserId, string? reason, DateTime utcNow)
    {
        if (IsCancelled)
            return Result.Failure(AlreadyCancelledCode, "Vital ölçümü zaten iptal edilmiş.");

        var cancellation = RecordCancellationRules.Resolve(cancelledByUserId, reason, utcNow, ValidationCode);
        if (!cancellation.IsSuccess)
            return Result.Failure(cancellation.Error);

        CancelledAtUtc = cancellation.Value!.CancelledAtUtc;
        CancelledByUserId = cancellation.Value.CancelledByUserId;
        CancelReason = cancellation.Value.Reason;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Kesinleşmiş muayeneye sonradan eklenen ölçümü işaretler; gerekçe zorunlu (5–500). Yalnız oluşturmada çağrılır.</summary>
    public Result MarkAddedAfterFinalization(string? reason)
    {
        var check = CorrectionReason.Validate(reason, LateEntryReasonRequiredCode);
        if (!check.IsSuccess)
            return check;

        AddedAfterFinalization = true;
        LateEntryReason = reason!.Trim();
        return Result.Success();
    }

    /// <summary>İstemcinin gördüğü sürüm: kalıcı yazma yalnızca veritabanındaki sürüm bu değere eşitse gerçekleşir.</summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }

    private void Apply(DateTime measuredAtUtc, VitalSigns values, string? note)
    {
        MeasuredAtUtc = NormalizeUtc(measuredAtUtc);
        TemperatureC = values.TemperatureC;
        HeartRateBpm = values.HeartRateBpm;
        RespiratoryRatePerMin = values.RespiratoryRatePerMin;
        WeightKg = values.WeightKg;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    private static Result ValidateDetails(DateTime measuredAtUtc, string? note)
    {
        if (measuredAtUtc == default)
            return Result.Failure(ValidationCode, "Ölçüm zamanı zorunludur.");
        if (note is not null && note.Trim().Length > MaxNoteLength)
            return Result.Failure(ValidationCode, $"Not en fazla {MaxNoteLength} karakter olabilir.");

        return Result.Success();
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
