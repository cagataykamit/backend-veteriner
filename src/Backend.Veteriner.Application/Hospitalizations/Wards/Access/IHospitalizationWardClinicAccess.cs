using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Access;

/// <summary>
/// Bölüm/oda tanımlarında klinik erişim kuralı tek yerde (klinik ayarları deseni: çalışma saatleri ile aynı):
/// kiracı ve kullanıcı bağlamı, klinik kiracıya ait, tenant-wide olmayan kullanıcı yalnız atandığı klinik.
/// Yazma ayrıca <c>Clinics.Update</c> iznini ister.
/// </summary>
public interface IHospitalizationWardClinicAccess
{
    /// <summary>Başarıda kiracı kimliğini döner.</summary>
    Task<Result<Guid>> EnsureReadAccessAsync(Guid clinicId, CancellationToken ct);

    /// <summary>Başarıda kiracı kimliğini döner; önce izin, sonra bağlam ve klinik denetlenir.</summary>
    Task<Result<Guid>> EnsureWriteAccessAsync(Guid clinicId, CancellationToken ct);
}
