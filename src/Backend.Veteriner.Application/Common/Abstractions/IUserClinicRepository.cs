namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Kullanıcı–klinik atama erişimi (/me/clinics, select-clinic, tenant paneli klinik üyelik yönetimi).
/// Yazım metotları <see cref="IUserOperationClaimRepository"/> ile aynı çizgidedir: SaveChanges çağırmaz,
/// commit sınırı application katmanında <see cref="IUnitOfWork"/> üzerinden yönetilir.
/// </summary>
public interface IUserClinicRepository
{
    /// <summary>Kullanıcının bu kliniğe atanmış olup olmadığı (klinik kaydı ayrıca tenant/aktif kontrolünden geçer).</summary>
    Task<bool> ExistsAsync(Guid userId, Guid clinicId, CancellationToken ct);
    /// <summary>Kullanıcının tenant içindeki aktif kliniğe atanmış olup olmadığı (tek sorgu doğrulaması).</summary>
    Task<bool> ExistsActiveInTenantAsync(Guid userId, Guid tenantId, Guid clinicId, CancellationToken ct);

    /// <summary>Tenant içinde kullanıcının yetkili olduğu klinikler (isim sırası).</summary>
    Task<IReadOnlyList<Backend.Veteriner.Domain.Clinics.Clinic>> ListAccessibleClinicsAsync(Guid userId, Guid tenantId, bool? isActive, CancellationToken ct);

    /// <summary>Yeni <see cref="Backend.Veteriner.Domain.Clinics.UserClinic"/> satırı ekler (tenant/klinik doğrulaması handler'da yapılır). SaveChanges çağırmaz.</summary>
    Task AddAsync(Backend.Veteriner.Domain.Clinics.UserClinic entity, CancellationToken ct);

    /// <summary><c>(userId, clinicId)</c> ikilisine ait <see cref="UserClinic"/> satırı varsa siler; yoksa no-op. SaveChanges çağırmaz.</summary>
    Task RemoveAsync(Guid userId, Guid clinicId, CancellationToken ct);
}
