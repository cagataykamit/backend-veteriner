using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Examinations;

/// <summary>
/// Klinikte yapılan muayene (tıbbi) kaydı. Randevudan bağımsız oluşturulabilir; opsiyonel <see cref="AppointmentId"/> ile bağlanır.
/// Ekran bölümleri: şikâyet/anamnez (<see cref="VisitReason"/>, <see cref="Anamnesis"/>),
/// klinik bulgular/vital (<see cref="Findings"/>, vital alanları), değerlendirme (<see cref="Assessment"/>),
/// plan (<see cref="Plan"/>). <see cref="Notes"/> bölüm dışı genel nottur.
/// </summary>
public sealed class Examination : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? AppointmentId { get; private set; }

    /// <summary>Bağlı geliş (Visit); eski kayıtlarda ve gelişsiz muayenede null. Oluşturulduktan sonra değişmez.</summary>
    public Guid? VisitId { get; private set; }
    public DateTime ExaminedAtUtc { get; private set; }

    /// <summary>Başvuru nedeni / şikayet (vizit özeti).</summary>
    public string VisitReason { get; private set; } = default!;

    /// <summary>Anamnez / hikâye (opsiyonel).</summary>
    public string? Anamnesis { get; private set; }

    /// <summary>Bulgu ve muayene gözlemleri; boş string olabilir.</summary>
    public string Findings { get; private set; } = default!;

    /// <summary>Kilo (kg), opsiyonel.</summary>
    public decimal? WeightKg { get; private set; }

    /// <summary>Vücut sıcaklığı (°C), opsiyonel.</summary>
    public decimal? TemperatureC { get; private set; }

    /// <summary>Kalp hızı (atım/dk), opsiyonel.</summary>
    public int? HeartRateBpm { get; private set; }

    /// <summary>Solunum sayısı (solunum/dk), opsiyonel.</summary>
    public int? RespiratoryRatePerMin { get; private set; }

    /// <summary>
    /// Vital ölçüm zamanı. Vital yoksa null. İlk kez vital girildiğinde açıkça verilmemişse
    /// o anki <see cref="ExaminedAtUtc"/> değerini alır; sonradan yalnızca muayene zamanı değişirse değişmez.
    /// </summary>
    public DateTime? VitalsMeasuredAtUtc { get; private set; }

    /// <summary>Değerlendirme / ön tanı özeti (opsiyonel).</summary>
    public string? Assessment { get; private set; }

    /// <summary>Plan / tedavi planı (opsiyonel).</summary>
    public string? Plan { get; private set; }

    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private Examination() { }

    public Examination(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? appointmentId,
        DateTime examinedAtUtc,
        string visitReason,
        string? findings,
        string? assessment,
        string? notes,
        string? anamnesis = null,
        string? plan = null,
        decimal? weightKg = null,
        decimal? temperatureC = null,
        int? heartRateBpm = null,
        int? respiratoryRatePerMin = null,
        DateTime? vitalsMeasuredAtUtc = null,
        Guid? visitId = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId geçersiz.", nameof(petId));

        if (string.IsNullOrWhiteSpace(visitReason))
            throw new ArgumentException("Başvuru nedeni boş olamaz.", nameof(visitReason));

        var vitals = ValidateVitals(weightKg, temperatureC, heartRateBpm, respiratoryRatePerMin, vitalsMeasuredAtUtc);
        if (!vitals.IsSuccess)
            throw new ArgumentException(vitals.Error.Message);

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        AppointmentId = appointmentId;
        VisitId = visitId;
        ExaminedAtUtc = NormalizeUtc(examinedAtUtc);
        VisitReason = visitReason.Trim();
        Anamnesis = NormalizeOptional(anamnesis);
        Findings = findings?.Trim() ?? string.Empty;
        Assessment = NormalizeOptional(assessment);
        Plan = NormalizeOptional(plan);
        Notes = NormalizeOptional(notes);
        ApplyVitals(weightKg, temperatureC, heartRateBpm, respiratoryRatePerMin, vitalsMeasuredAtUtc);
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
    }

    /// <summary>
    /// Kısmi klinik içerik güncellemesi; <see cref="ClinicId"/>, <see cref="PetId"/> ve <see cref="AppointmentId"/> değişmez.
    /// Kural: <c>null</c> = mevcut değere dokunma; metinde boş/boşluk = temizle; vitalleri temizlemek için
    /// <see cref="ExaminationClinicalUpdate.ClearVitals"/> (vital değer/ölçüm zamanı ile birlikte verilemez).
    /// </summary>
    public Result UpdateClinicalContent(ExaminationClinicalUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.VisitReason))
            return Result.Failure("Examinations.Validation", "VisitReason bos olamaz.");

        if (update.ClearVitals && update.HasVitalInput)
        {
            return Result.Failure(
                "Examinations.Validation",
                "ClearVitals ile vital değer veya VitalsMeasuredAtUtc birlikte gönderilemez.");
        }

        var weightKg = update.ClearVitals ? null : update.WeightKg ?? WeightKg;
        var temperatureC = update.ClearVitals ? null : update.TemperatureC ?? TemperatureC;
        var heartRateBpm = update.ClearVitals ? null : update.HeartRateBpm ?? HeartRateBpm;
        var respiratoryRatePerMin = update.ClearVitals ? null : update.RespiratoryRatePerMin ?? RespiratoryRatePerMin;

        var vitals = ValidateVitals(weightKg, temperatureC, heartRateBpm, respiratoryRatePerMin, update.VitalsMeasuredAtUtc);
        if (!vitals.IsSuccess)
            return vitals;

        ExaminedAtUtc = NormalizeUtc(update.ExaminedAtUtc);
        VisitReason = update.VisitReason.Trim();
        Anamnesis = MergeOptional(Anamnesis, update.Anamnesis);
        Findings = MergeOptional(Findings, update.Findings) ?? string.Empty;
        Assessment = MergeOptional(Assessment, update.Assessment);
        Plan = MergeOptional(Plan, update.Plan);
        Notes = MergeOptional(Notes, update.Notes);
        ApplyVitals(weightKg, temperatureC, heartRateBpm, respiratoryRatePerMin, update.VitalsMeasuredAtUtc);
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Güncellemede istemcinin formu açarken aldığı sürümü eşzamanlılık belirteci olarak ayarlar;
    /// kalıcı yazma yalnızca veritabanındaki sürüm bu değere eşitse gerçekleşir.
    /// </summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }

    private void ApplyVitals(
        decimal? weightKg,
        decimal? temperatureC,
        int? heartRateBpm,
        int? respiratoryRatePerMin,
        DateTime? vitalsMeasuredAtUtc)
    {
        WeightKg = weightKg;
        TemperatureC = temperatureC;
        HeartRateBpm = heartRateBpm;
        RespiratoryRatePerMin = respiratoryRatePerMin;

        var hasVitals = weightKg.HasValue || temperatureC.HasValue || heartRateBpm.HasValue || respiratoryRatePerMin.HasValue;
        if (!hasVitals)
        {
            VitalsMeasuredAtUtc = null;
            return;
        }

        VitalsMeasuredAtUtc = vitalsMeasuredAtUtc.HasValue
            ? NormalizeUtc(vitalsMeasuredAtUtc.Value)
            : VitalsMeasuredAtUtc ?? ExaminedAtUtc;
    }

    private static Result ValidateVitals(
        decimal? weightKg,
        decimal? temperatureC,
        int? heartRateBpm,
        int? respiratoryRatePerMin,
        DateTime? vitalsMeasuredAtUtc)
    {
        if (weightKg is <= 0)
            return Result.Failure("Examinations.Validation", "WeightKg sıfırdan büyük olmalıdır.");
        if (temperatureC is <= 0)
            return Result.Failure("Examinations.Validation", "TemperatureC sıfırdan büyük olmalıdır.");
        if (heartRateBpm is <= 0)
            return Result.Failure("Examinations.Validation", "HeartRateBpm sıfırdan büyük olmalıdır.");
        if (respiratoryRatePerMin is <= 0)
            return Result.Failure("Examinations.Validation", "RespiratoryRatePerMin sıfırdan büyük olmalıdır.");

        var hasVitals = weightKg.HasValue || temperatureC.HasValue || heartRateBpm.HasValue || respiratoryRatePerMin.HasValue;
        if (vitalsMeasuredAtUtc.HasValue && !hasVitals)
        {
            return Result.Failure(
                "Examinations.Validation",
                "VitalsMeasuredAtUtc yalnızca en az bir vital değer ile gönderilebilir.");
        }

        if (vitalsMeasuredAtUtc == default(DateTime))
            return Result.Failure("Examinations.Validation", "VitalsMeasuredAtUtc geçersiz.");

        return Result.Success();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary><c>null</c> = mevcut değer korunur; aksi halde <see cref="NormalizeOptional"/> (boş/boşluk → null).</summary>
    private static string? MergeOptional(string? current, string? incoming)
        => incoming is null ? current : NormalizeOptional(incoming);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
