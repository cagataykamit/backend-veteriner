using Ardalis.Specification;
using Backend.Veteriner.Domain.Administrations;

namespace Backend.Veteriner.Application.Administrations.Specs;

public sealed class AdministrationByRequestIdSpec : Specification<Administration>
{
    public AdministrationByRequestIdSpec(Guid tenantId, Guid requestId)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && a.RequestId == requestId);
    }
}

public sealed class AdministrationByIdSpec : Specification<Administration>
{
    public AdministrationByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && a.Id == id);
    }
}

/// <summary>İzlenen (tracked) uygulama; iptal ucu için. İptal edilmiş kayıt da gelir (<c>Administrations.AlreadyVoided</c> ayrımı için).</summary>
public sealed class AdministrationForUpdateSpec : Specification<Administration>
{
    public AdministrationForUpdateSpec(Guid tenantId, Guid id)
    {
        Query.Where(a => a.TenantId == tenantId && a.Id == id);
    }
}

/// <summary>Klinik kapsamlı uygulama filtresi; sayfalama <see cref="AdministrationsFilteredPagedSpec"/> ile eklenir.</summary>
public class AdministrationsFilteredSpec : Specification<Administration>
{
    public AdministrationsFilteredSpec(
        Guid tenantId,
        Guid clinicId,
        Guid? petId,
        Guid? examinationId,
        Guid? treatmentId,
        Guid? productId,
        bool includeVoided)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && a.ClinicId == clinicId);
        if (petId.HasValue)
            Query.Where(a => a.PetId == petId.Value);
        if (examinationId.HasValue)
            Query.Where(a => a.ExaminationId == examinationId.Value);
        if (treatmentId.HasValue)
            Query.Where(a => a.TreatmentId == treatmentId.Value);
        if (productId.HasValue)
            Query.Where(a => a.ProductId == productId.Value);
        if (!includeVoided)
            Query.Where(a => a.VoidedAtUtc == null);
    }
}

public sealed class AdministrationsFilteredPagedSpec : AdministrationsFilteredSpec
{
    public AdministrationsFilteredPagedSpec(
        Guid tenantId,
        Guid clinicId,
        Guid? petId,
        Guid? examinationId,
        Guid? treatmentId,
        Guid? productId,
        bool includeVoided,
        int page,
        int pageSize)
        : base(tenantId, clinicId, petId, examinationId, treatmentId, productId, includeVoided)
    {
        Query.OrderByDescending(a => a.AdministeredAtUtc)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
