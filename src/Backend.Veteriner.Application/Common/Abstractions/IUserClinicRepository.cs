namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>Kullanıcı–klinik atama sorguları (/me/clinics, select-clinic).</summary>
public interface IUserClinicRepository
{
    /// <summary>Kullanıcının bu kliniğe atanmış olup olmadığı (klinik kaydı ayrıca tenant/aktif kontrolünden geçer).</summary>
    Task<bool> ExistsAsync(Guid userId, Guid clinicId, CancellationToken ct);

    /// <summary>Tenant içinde kullanıcının yetkili olduğu klinikler (isim sırası).</summary>
    Task<IReadOnlyList<Backend.Veteriner.Domain.Clinics.Clinic>> ListAccessibleClinicsAsync(Guid userId, Guid tenantId, bool? isActive, CancellationToken ct);
}
