namespace Backend.Veteriner.Application.Treatments.Specs;

/// <summary>Treatments listesi için SQL projection satırı (Description/Notes hariç).</summary>
public sealed record TreatmentListRow(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    DateTime TreatmentDateUtc,
    string Title,
    Guid? ExaminationId,
    DateTime? FollowUpDateUtc);
