namespace Backend.Veteriner.Application.Visits.IntegrationEvents;

/// <summary>
/// Visit read-model projection için anlık görüntü; alanlar <c>VisitReadModels</c> kolonlarıyla hizalıdır.
/// Hayvan, sahip ve randevu bilgisi burada tutulmaz; Bugün sorgusu bunları kendi read model'lerinden birleştirir.
/// </summary>
public sealed record VisitProjectionSnapshot(
    Guid VisitId,
    Guid TenantId,
    Guid ClinicId,
    Guid PetId,
    Guid? AppointmentId,
    Guid? ResponsibleVeterinarianUserId,
    DateTime ArrivedAtUtc,
    int CareStatus,
    DateTime? VoidedAtUtc,
    long MutationSequence);
