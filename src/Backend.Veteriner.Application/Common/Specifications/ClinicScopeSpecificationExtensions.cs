using Ardalis.Specification;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Common.Specifications;

/// <summary>
/// Klinik kapsam kuralı (tek yer): seçili klinik varsa yalnız o klinik; yoksa ve erişilebilir klinik listesi verilmişse
/// yalnız o klinikler (boş liste = hiçbir kayıt); liste de yoksa (kiracı geneli yetki) filtre uygulanmaz.
/// Kiracı filtresi (<c>TenantId</c>) ayrıca uygulanır; bu yardımcı yalnız klinik boyutunu daraltır.
/// </summary>
public static class ClinicScopeSpecificationExtensions
{
    public static ISpecificationBuilder<T> ApplyClinicScope<T>(
        this ISpecificationBuilder<T> query,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
        where T : class, IClinicScoped
    {
        if (clinicId.HasValue)
            return query.Where(e => e.ClinicId == clinicId.Value);

        if (accessibleClinicIds is null)
            return query;

        if (accessibleClinicIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(e => accessibleClinicIds.Contains(e.ClinicId));
    }
}
