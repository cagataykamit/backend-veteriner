namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Kiracı içi klinik okuma/yazma yüzeylerinde tenant-wide olmayan kullanıcıları yalnızca
/// <see cref="Backend.Veteriner.Domain.Clinics.UserClinic"/> atamalarıyla sınırlar.
/// <see cref="Backend.Veteriner.Application.Clinics.Access.TenantWideClaimNames"/> (Admin / Owner / PlatformAdmin)
/// bu kısıta tabi değildir.
/// </summary>
public interface IClinicAssignmentAccessGuard
{
    /// <summary>
    /// Kullanıcı tenant-wide değilse <c>true</c> (ClinicAdmin, Veteriner, Sekreter vb.).
    /// Admin, Owner veya PlatformAdmin claim'lerinden en az biri varsa <c>false</c>.
    /// </summary>
    Task<bool> MustApplyAssignedClinicScopeAsync(Guid userId, CancellationToken ct);
}
