using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Vaccinations;

/// <summary>
/// Pet bazlı aşı kaydı; opsiyonel olarak bir muayene ile ilişkilendirilebilir.
/// Aşı adı <see cref="VaccineName"/> alanında seçilen <c>VaccineDefinition</c> adının snapshot'ı olarak tutulur.
/// </summary>
public sealed class Vaccination : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid? ExaminationId { get; private set; }

    /// <summary>Katalog tanımı kimliği; yeni kayıtlarda dolu olmalı.</summary>
    public Guid? VaccineDefinitionId { get; private set; }

    /// <summary>Seçilen tanım adının uygulama anı snapshot'ı.</summary>
    public string VaccineName { get; private set; } = default!;

    public DateTime? AppliedAtUtc { get; private set; }
    public DateTime? DueAtUtc { get; private set; }
    public VaccinationStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private Vaccination() { }

    public Vaccination(
        Guid tenantId,
        Guid petId,
        Guid clinicId,
        Guid? examinationId,
        Guid vaccineDefinitionId,
        string vaccineNameSnapshot,
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
        if (vaccineDefinitionId == Guid.Empty)
            throw new ArgumentException("VaccineDefinitionId geçersiz.", nameof(vaccineDefinitionId));
        if (string.IsNullOrWhiteSpace(vaccineNameSnapshot))
            throw new ArgumentException("Aşı adı snapshot boş olamaz.", nameof(vaccineNameSnapshot));

        TenantId = tenantId;
        PetId = petId;
        ClinicId = clinicId;
        ExaminationId = examinationId;
        VaccineDefinitionId = vaccineDefinitionId;
        VaccineName = vaccineNameSnapshot.Trim();
        Status = status;
        AppliedAtUtc = appliedAtUtc.HasValue ? NormalizeUtc(appliedAtUtc.Value) : null;
        DueAtUtc = dueAtUtc.HasValue ? NormalizeUtc(dueAtUtc.Value) : null;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
    }

    public Result UpdateDetails(
        Guid petId,
        Guid clinicId,
        Guid? examinationId,
        Guid vaccineDefinitionId,
        string vaccineNameSnapshot,
        VaccinationStatus status,
        DateTime? appliedAtUtc,
        DateTime? dueAtUtc,
        string? notes)
    {
        if (petId == Guid.Empty)
            return Result.Failure("Vaccinations.Validation", "PetId gecersiz.");
        if (clinicId == Guid.Empty)
            return Result.Failure("Vaccinations.Validation", "ClinicId gecersiz.");
        if (vaccineDefinitionId == Guid.Empty)
            return Result.Failure("Vaccinations.Validation", "VaccineDefinitionId gecersiz.");
        if (string.IsNullOrWhiteSpace(vaccineNameSnapshot))
            return Result.Failure("Vaccinations.Validation", "Asi adi snapshot bos olamaz.");

        PetId = petId;
        ClinicId = clinicId;
        ExaminationId = examinationId;
        VaccineDefinitionId = vaccineDefinitionId;
        VaccineName = vaccineNameSnapshot.Trim();
        Status = status;
        AppliedAtUtc = appliedAtUtc.HasValue ? NormalizeUtc(appliedAtUtc.Value) : null;
        DueAtUtc = dueAtUtc.HasValue ? NormalizeUtc(dueAtUtc.Value) : null;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
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
