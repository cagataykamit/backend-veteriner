using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Clinics.Access;

/// <summary>
/// Tenant geneli okuma yüzeyleri (raporlar, klinik liste handler'ları) için klinik scope çözümleyicisi.
/// </summary>
/// <remarks>
/// Beklenen mantık:
/// <list type="bullet">
/// <item><description>Admin / Owner / PlatformAdmin (tenant-wide) → scope uygulanmaz; <c>requestClinicId</c> yoksa tenant-wide okuma.</description></item>
/// <item><description>Tenant-wide olmayan roller (ClinicAdmin, Veteriner, Sekreter vb.) → yalnız atanmış klinikler.
/// <c>requestClinicId</c> verilmediyse <c>AccessibleClinicIds</c> kümesi ile <c>IN (...)</c> filtresi uygulanır.</description></item>
/// <item><description><c>requestClinicId</c> varsa atanmış klinikse <c>SingleClinicId</c>; atanmamış klinikse
/// <c>Clinics.AccessDenied</c>.</description></item>
/// <item><description>Tenant-wide roller için <c>requestClinicId</c> tenant'a ait değilse <c>Clinics.NotFound</c>.</description></item>
/// </list>
/// </remarks>
public interface IClinicReadScopeResolver
{
    /// <summary>İstek için efektif klinik kapsamını çözer.</summary>
    Task<Result<ClinicReadScope>> ResolveAsync(
        Guid tenantId,
        Guid? requestClinicId,
        CancellationToken ct);
}
