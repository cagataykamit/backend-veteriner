using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Vaccinations;

/// <summary>
/// Pet bazlı aşı kaydı; opsiyonel olarak bir muayene ile ilişkilendirilebilir.
/// </summary>
public sealed class Vaccination : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid? ExaminationId { get; private set; }

    /// <summary>Serbest metin aşı adı (katalog bu turda yok).</summary>
    public string VaccineName { get; private set; } = default!;

    public DateTime? AppliedAtUtc { get; private set; }
    public DateTime? DueAtUtc { get; private set; }
    public VaccinationStatus Status { get; private set; }
    public string? Notes { get; private set; }

    private Vaccination() { }

    public Vaccination(
        Guid tenantId,
        Guid petId,
        Guid clinicId,
        Guid? examinationId,
        string vaccineName,
        VaccinationStatus status,
        DateTime? appliedAtUtc,
        DateTime? dueAtUtc,
        string? notes)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId geçersiz.", nameof(petId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (string.IsNullOrWhiteSpace(vaccineName))
            throw new ArgumentException("Aşı adı boş olamaz.", nameof(vaccineName));

        TenantId = tenantId;
        PetId = petId;
        ClinicId = clinicId;
        ExaminationId = examinationId;
        VaccineName = vaccineName.Trim();
        Status = status;
        AppliedAtUtc = appliedAtUtc.HasValue ? NormalizeUtc(appliedAtUtc.Value) : null;
        DueAtUtc = dueAtUtc.HasValue ? NormalizeUtc(dueAtUtc.Value) : null;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
