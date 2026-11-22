using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Personel daveti ve üye yönetiminde kimin neyi yapabileceği (tek yer). <c>Tenants.InviteCreate</c> izni olan iki düzey vardır:
/// <list type="bullet">
///   <item>İşletme yöneticisi (kiracı geneli: Admin / Owner): kiracının bütün kliniklerinde ve bütün rollerde.</item>
///   <item>Klinik yöneticisi (ClinicAdmin, klinik kapsamlı): yalnız atandığı kliniklere davet ve klinik ataması yapar; işletme yöneticisi
///   rolünü atayamaz ve kaldıramaz, işletme yöneticisinin üyeliğine dokunamaz, kurum ayarlarını değiştiremez.</item>
/// </list>
/// Kapsam <see cref="IClinicAssignmentAccessGuard"/> ile aynı semantiktir (<see cref="TenantWideClaimNames"/>).
/// </summary>
public sealed class TenantMemberManagementAccess
{
    public const string ClinicAccessDeniedCode = "Members.ClinicAccessDenied";
    public const string RoleNotAssignableCode = "Members.RoleNotAssignable";
    public const string TargetIsTenantAdminCode = "Members.TenantAdminForbidden";
    public const string TenantSettingsDeniedCode = "Tenants.SettingsForbidden";

    private readonly IClientContext _clientContext;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IUserOperationClaimRepository _userOperationClaims;

    public TenantMemberManagementAccess(
        IClientContext clientContext,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IUserOperationClaimRepository userOperationClaims)
    {
        _clientContext = clientContext;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _userOperationClaims = userOperationClaims;
    }

    /// <summary>İşlemi yapan klinik kapsamlı mı (klinik yöneticisi); kullanıcı bağlamı yoksa kapsamlı sayılır.</summary>
    public async Task<bool> IsClinicScopedAsync(CancellationToken ct)
        => _clientContext.UserId is not { } userId || await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct);

    /// <summary>Rol bu kullanıcı tarafından atanabilir mi: klinik yöneticisi kiracı geneli rolü (işletme yöneticisi) atayamaz.</summary>
    public static bool CanAssignRole(string? operationClaimName, bool actorIsClinicScoped)
        => InviteAssignableOperationClaimsCatalog.IsAssignableName(operationClaimName)
           && !(actorIsClinicScoped && TenantWideClaimNames.IsTenantWide([operationClaimName!]));

    /// <summary>Klinik yöneticisi yalnız atandığı klinikte davet ve klinik ataması yapar.</summary>
    public async Task<Result> EnsureClinicAsync(Guid clinicId, CancellationToken ct)
    {
        if (!await IsClinicScopedAsync(ct))
            return Result.Success();

        return _clientContext.UserId is { } userId && await _userClinics.ExistsAsync(userId, clinicId, ct)
            ? Result.Success()
            : Result.Failure(ClinicAccessDeniedCode, "Yalnız yöneticisi olduğunuz klinik için personel daveti ve klinik ataması yapabilirsiniz.");
    }

    /// <summary>Rol atama/kaldırma: klinik yöneticisi işletme yöneticisi rolüyle işlem yapamaz.</summary>
    public async Task<Result> EnsureRoleAsync(string operationClaimName, CancellationToken ct)
        => CanAssignRole(operationClaimName, await IsClinicScopedAsync(ct))
            ? Result.Success()
            : Result.Failure(RoleNotAssignableCode, "İşletme yöneticisi rolünü yalnız işletme yöneticisi atayabilir veya kaldırabilir.");

    /// <summary>Üyeye dokunan işlemler: klinik yöneticisi işletme yöneticisinin rolünü, kliniğini ya da üyeliğini değiştiremez.</summary>
    public async Task<Result> EnsureMemberAsync(Guid memberId, CancellationToken ct)
    {
        if (!await IsClinicScopedAsync(ct))
            return Result.Success();

        var names = await _userOperationClaims.GetOperationClaimNamesByUserIdAsync(memberId, ct);
        return TenantWideClaimNames.IsTenantWide(names)
            ? Result.Failure(TargetIsTenantAdminCode, "İşletme yöneticisinin üyeliğini yalnız işletme yöneticisi değiştirebilir.")
            : Result.Success();
    }

    /// <summary>Kurum (kiracı) ayarları yalnız işletme yöneticisindedir.</summary>
    public async Task<Result> EnsureTenantSettingsAsync(CancellationToken ct)
        => await IsClinicScopedAsync(ct)
            ? Result.Failure(TenantSettingsDeniedCode, "Kurum ayarlarını yalnız işletme yöneticisi değiştirebilir.")
            : Result.Success();
}
