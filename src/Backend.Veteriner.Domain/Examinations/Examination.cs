using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Examinations;

/// <summary>
/// Klinikte yapılan muayene (tıbbi) kaydı. Randevudan bağımsız oluşturulabilir; opsiyonel <see cref="AppointmentId"/> ile bağlanır.
/// </summary>
public sealed class Examination : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public DateTime ExaminedAtUtc { get; private set; }

    /// <summary>Başvuru nedeni / şikayet (vizit özeti).</summary>
    public string VisitReason { get; private set; } = default!;

    /// <summary>Bulgu ve muayene gözlemleri.</summary>
    public string Findings { get; private set; } = default!;

    /// <summary>Değerlendirme / ön tanı özeti (opsiyonel).</summary>
    public string? Assessment { get; private set; }

    public string? Notes { get; private set; }

    private Examination() { }

    public Examination(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? appointmentId,
        DateTime examinedAtUtc,
        string visitReason,
        string findings,
        string? assessment,
        string? notes)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId geçersiz.", nameof(petId));

        if (string.IsNullOrWhiteSpace(visitReason))
            throw new ArgumentException("Başvuru nedeni boş olamaz.", nameof(visitReason));
        if (string.IsNullOrWhiteSpace(findings))
            throw new ArgumentException("Bulgular boş olamaz.", nameof(findings));

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        AppointmentId = appointmentId;
        ExaminedAtUtc = NormalizeUtc(examinedAtUtc);
        VisitReason = visitReason.Trim();
        Findings = findings.Trim();
        Assessment = string.IsNullOrWhiteSpace(assessment) ? null : assessment.Trim();
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
