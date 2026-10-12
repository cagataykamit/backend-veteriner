using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.LabResults.Access;

/// <summary>
/// Lab kaydının geliş (visit) bağını tek yerde çözer: açık <c>visitId</c> yoksa muayenenin gelişi miras alınır;
/// ikisi de varsa aynı olmalıdır. Geliş kiracıya ait, iptal edilmemiş ve aynı hayvan/klinik olmalıdır.
/// </summary>
internal static class LabResultVisitResolver
{
    public static async Task<Result<Guid?>> ResolveAsync(
        IReadRepository<Visit> visits,
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Examination? examination,
        Guid? requestedVisitId,
        CancellationToken ct)
    {
        var examinationVisitId = examination?.VisitId;
        if (requestedVisitId.HasValue && examinationVisitId.HasValue && requestedVisitId != examinationVisitId)
        {
            return Result<Guid?>.Failure(
                "LabResults.ExaminationVisitMismatch",
                "Muayenenin geliş kaydı ile istekteki geliş kaydı uyuşmuyor.");
        }

        var visitId = requestedVisitId ?? examinationVisitId;
        if (visitId is null)
            return Result<Guid?>.Success(null);

        var visit = await visits.FirstOrDefaultAsync(new VisitByIdSpec(tenantId, visitId.Value, asNoTracking: true), ct);
        if (visit is null || visit.IsVoided)
            return Result<Guid?>.Failure("Visits.NotFound", "Geliş kaydı bulunamadı veya kiracıya ait değil.");

        if (visit.PetId != petId)
            return Result<Guid?>.Failure("LabResults.VisitPetMismatch", "Geliş kaydının hayvanı lab kaydı hayvanı ile uyuşmuyor.");

        if (visit.ClinicId != clinicId)
            return Result<Guid?>.Failure("LabResults.VisitClinicMismatch", "Geliş kaydının kliniği lab kaydı kliniği ile uyuşmuyor.");

        return Result<Guid?>.Success(visitId);
    }
}
