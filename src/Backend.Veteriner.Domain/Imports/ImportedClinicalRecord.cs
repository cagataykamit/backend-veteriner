using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Imports;

/// <summary>
/// Eski programdan gelen salt okunur klinik geçmiş kaydı (ADR-048 T1/T2). <c>Examination</c> değildir: muayene defterine,
/// kesinleştirme ve ek not akışına, ücret önerisine, raporlara ve sahip portalına girmez. Reçete ve laboratuvar yalnız
/// metindir; reçete/lab kaydı, stok hareketi veya defter kaydı üretmez. Yalnız içe aktarma oluşturur, geri alma siler;
/// başka hiçbir yol değiştirmez.
/// </summary>
public sealed class ImportedClinicalRecord : IClinicScoped, ITenantOwned
{
    public const int MaxRecordTypeLength = 100;
    public const int MaxComplaintLength = 2000;
    public const int MaxHistoryLength = 4000;
    public const int MaxFindingsLength = 8000;
    public const int MaxDiagnosisLength = 2000;
    public const int MaxTreatmentLength = 4000;
    public const int MaxPrescriptionLength = 4000;
    public const int MaxLabResultsLength = 4000;
    public const int MaxNotesLength = 4000;
    public const int MaxVeterinarianNameLength = 200;
    public const decimal MinTemperatureC = 25m;
    public const decimal MaxTemperatureC = 45m;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }

    /// <summary>Hastanın içe aktarma anındaki sahibi (müşteri veri kopyası ve geri alma birimi için).</summary>
    public Guid ClientId { get; private set; }

    public Guid ImportBatchId { get; private set; }
    public DateOnly RecordedOn { get; private set; }
    public string? RecordType { get; private set; }
    public string? Complaint { get; private set; }
    public string? History { get; private set; }
    public string? Findings { get; private set; }
    public string? Diagnosis { get; private set; }
    public string? Treatment { get; private set; }
    public string? Prescription { get; private set; }
    public string? LabResults { get; private set; }
    public string? Notes { get; private set; }
    public string? VeterinarianName { get; private set; }
    public decimal? WeightKg { get; private set; }
    public decimal? TemperatureC { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private ImportedClinicalRecord() { }

    /// <summary>İçerik alanları (en az biri dolu olmalı).</summary>
    public sealed record Content(
        string? RecordType,
        string? Complaint,
        string? History,
        string? Findings,
        string? Diagnosis,
        string? Treatment,
        string? Prescription,
        string? LabResults,
        string? Notes,
        string? VeterinarianName,
        decimal? WeightKg,
        decimal? TemperatureC);

    public static Result<ImportedClinicalRecord> Create(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid clientId,
        Guid importBatchId,
        DateOnly recordedOn,
        Content content,
        DateTime createdAtUtc)
    {
        if (tenantId == Guid.Empty || clinicId == Guid.Empty || petId == Guid.Empty || clientId == Guid.Empty
            || importBatchId == Guid.Empty)
        {
            return Invalid("Kiracı, klinik, hasta, müşteri ve parti kimlikleri zorunludur.");
        }

        var c = content with
        {
            RecordType = Clean(content.RecordType),
            Complaint = Clean(content.Complaint),
            History = Clean(content.History),
            Findings = Clean(content.Findings),
            Diagnosis = Clean(content.Diagnosis),
            Treatment = Clean(content.Treatment),
            Prescription = Clean(content.Prescription),
            LabResults = Clean(content.LabResults),
            Notes = Clean(content.Notes),
            VeterinarianName = Clean(content.VeterinarianName),
        };

        if (c.Complaint is null && c.History is null && c.Findings is null && c.Diagnosis is null && c.Treatment is null
            && c.Prescription is null && c.LabResults is null && c.Notes is null)
        {
            return Invalid("Klinik geçmiş kaydının en az bir içerik alanı dolu olmalıdır.");
        }

        if (TooLong(c.RecordType, MaxRecordTypeLength) || TooLong(c.Complaint, MaxComplaintLength)
            || TooLong(c.History, MaxHistoryLength) || TooLong(c.Findings, MaxFindingsLength)
            || TooLong(c.Diagnosis, MaxDiagnosisLength) || TooLong(c.Treatment, MaxTreatmentLength)
            || TooLong(c.Prescription, MaxPrescriptionLength) || TooLong(c.LabResults, MaxLabResultsLength)
            || TooLong(c.Notes, MaxNotesLength) || TooLong(c.VeterinarianName, MaxVeterinarianNameLength))
        {
            return Invalid("Klinik geçmiş alanlarından biri izin verilen uzunluğu aşıyor.");
        }

        if (c.WeightKg is < 0 or >= 10000m)
            return Invalid("Kilo 0 ile 9999,99 arasında olmalıdır.");
        if (c.TemperatureC is { } t && (t < MinTemperatureC || t > MaxTemperatureC))
            return Invalid($"Ateş {MinTemperatureC}-{MaxTemperatureC} °C arasında olmalıdır.");

        return Result<ImportedClinicalRecord>.Success(new ImportedClinicalRecord
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            PetId = petId,
            ClientId = clientId,
            ImportBatchId = importBatchId,
            RecordedOn = recordedOn,
            RecordType = c.RecordType,
            Complaint = c.Complaint,
            History = c.History,
            Findings = c.Findings,
            Diagnosis = c.Diagnosis,
            Treatment = c.Treatment,
            Prescription = c.Prescription,
            LabResults = c.LabResults,
            Notes = c.Notes,
            VeterinarianName = c.VeterinarianName,
            WeightKg = c.WeightKg,
            TemperatureC = c.TemperatureC,
            CreatedAtUtc = createdAtUtc.Kind == DateTimeKind.Utc ? createdAtUtc : DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc),
        });
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TooLong(string? value, int max) => value is { Length: var n } && n > max;

    private static Result<ImportedClinicalRecord> Invalid(string message)
        => Result<ImportedClinicalRecord>.Failure("ImportedClinicalRecords.Validation", message);
}
