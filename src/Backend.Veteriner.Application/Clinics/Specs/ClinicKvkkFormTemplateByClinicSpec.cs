using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

/// <summary>
/// İzlenmeyen şablon kaydı; güncellemede istemcinin gönderdiği RowVersion beklenen sürüm olarak eklenir.
/// </summary>
public sealed class ClinicKvkkFormTemplateByClinicSpec : Specification<ClinicKvkkFormTemplate>
{
    public ClinicKvkkFormTemplateByClinicSpec(Guid tenantId, Guid clinicId, ClinicKvkkFormKind kind)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId && x.Kind == kind);
    }
}
