namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Kiracı içi klinik API'lerinde ClinicAdmin kullanıcılarını yalnızca <see cref="Backend.Veteriner.Domain.Clinics.UserClinic"/>
/// atamalarıyla sınırlar. Tenant <c>Admin</c> operation claim (JWT ile uyumlu owner/kiracı yöneticisi) bu kısıta tabi değildir.
/// </summary>
public interface IClinicAssignmentAccessGuard
{
    /// <summary>
    /// Kullanıcıda <c>ClinicAdmin</c> claim'i var ve <c>Admin</c> claim'i yoksa <c>true</c>.
    /// Diğer roller (ör. Veteriner) için <c>false</c> — mevcut tenant geniş listelenir.
    /// </summary>
    Task<bool> MustApplyAssignedClinicScopeAsync(Guid userId, CancellationToken ct);
}
