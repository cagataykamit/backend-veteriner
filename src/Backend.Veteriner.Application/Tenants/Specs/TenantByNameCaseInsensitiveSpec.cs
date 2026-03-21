using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

/// <summary>
/// Kiracı adı eşleşmesi: veritabanında saklanan <see cref="Tenant.Name"/> ile
/// karşılaştırma için handler tarafında <c>Trim().ToLowerInvariant()</c> ile normalize edilmiş değer kullanılır.
/// EF Core SQL Server'da <c>ToLower()</c> çevirisini üretir (Türkçe i/İ ayrıntıları locale'e bağlıdır; gerekirse ileride collation ile sıkılaştırılır).
/// </summary>
public sealed class TenantByNameCaseInsensitiveSpec : Specification<Tenant>
{
    public TenantByNameCaseInsensitiveSpec(string normalizedNameLowerInvariant)
    {
        Query.Where(t => t.Name.ToLower() == normalizedNameLowerInvariant);
    }
}
