using Ardalis.Specification;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>Visit için klinik kapsamı (DashboardSpecificationClinicScope'taki diğer türlerle aynı kural).</summary>
internal static class PetVisitClinicScope
{
    internal static ISpecificationBuilder<Visit> Apply(
        ISpecificationBuilder<Visit> query,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        if (clinicId.HasValue)
            return query.Where(v => v.ClinicId == clinicId.Value);

        if (accessibleClinicIds is null)
            return query;

        if (accessibleClinicIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(v => accessibleClinicIds.Contains(v.ClinicId));
    }
}
