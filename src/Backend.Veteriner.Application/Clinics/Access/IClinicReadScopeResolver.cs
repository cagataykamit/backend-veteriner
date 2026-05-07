using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Clinics.Access;

/// <summary>
/// Tenant geneli okuma yüzeyleri (raporlar, klinik liste handler'ları) için klinik scope çözümleyicisi.
/// </summary>
/// <remarks>
/// Beklenen mantık:
/// <list type="bullet">
/// <item><description>Admin / Owner gibi tenant-genel rol → scope uygulanmaz, mevcut tenant-wide davranış korunur.</description></item>
/// <item><description>ClinicAdmin → sadece atanmış klinikler. <c>requestClinicId</c> verilmediyse <c>AccessibleClinicIds</c> kümesi
/// ile <c>IN (...)</c> filtresi uygulanır.</description></item>
/// <item><description><c>requestClinicId</c> varsa atanmış klinikse <c>SingleClinicId</c>; atanmamış klinikse
/// <c>Clinics.AccessDenied</c>.</description></item>
/// <item><description>Admin/Owner için <c>requestClinicId</c> tenant'a ait değilse <c>Clinics.NotFound</c>.</description></item>
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
