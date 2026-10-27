using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Clinics.Access;

/// <inheritdoc cref="IClinicReadScopeResolver"/>
/// <remarks>
/// Kapsamlı (scoped) servistir: kullanıcının atanmış klinik kuralına tabi olup olmadığı (rol sorgusu) ve erişebildiği
/// klinikler (atama sorgusu) istek kapsamında kullanıcı/kiracı başına bir kez okunur; aynı istekte iç komutlar (satış
/// satırları, şablon kalemleri) bu sorguları yinelemez. Uzun ömürlü bir kapsamda (arka plan işi) rol veya klinik ataması
/// kapsam boyunca değişirse değişiklik o kapsamda görülmez.
/// </remarks>
public sealed class ClinicReadScopeResolver : IClinicReadScopeResolver
{
    private readonly IClientContext _clientContext;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly Dictionary<Guid, bool> _assignedScopeByUser = [];
    private readonly Dictionary<(Guid UserId, Guid TenantId), Guid[]> _accessibleClinicIds = [];

    public ClinicReadScopeResolver(
        IClientContext clientContext,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinics)
    {
        _clientContext = clientContext;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinics = clinics;
    }

    public async Task<Result<ClinicReadScope>> ResolveAsync(
        Guid tenantId,
        Guid? requestClinicId,
        CancellationToken ct)
    {
        if (_clientContext.UserId is not { } userId)
        {
            return Result<ClinicReadScope>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        if (await MustApplyAssignedClinicScopeAsync(userId, ct))
        {
            var accessibleIds = await AccessibleClinicIdsAsync(userId, tenantId, ct);

            if (requestClinicId.HasValue)
            {
                if (Array.IndexOf(accessibleIds, requestClinicId.Value) < 0)
                {
                    return Result<ClinicReadScope>.Failure(
                        "Clinics.AccessDenied",
                        "Bu klinik için atanmış üyeliğiniz yok.");
                }

                return Result<ClinicReadScope>.Success(new ClinicReadScope(requestClinicId.Value, null));
            }

            return Result<ClinicReadScope>.Success(new ClinicReadScope(null, accessibleIds));
        }

        if (requestClinicId.HasValue)
        {
            var clinic = await _clinics.FirstOrDefaultAsync(
                new ClinicByIdSpec(tenantId, requestClinicId.Value), ct);
            if (clinic is null)
            {
                return Result<ClinicReadScope>.Failure(
                    "Clinics.NotFound",
                    "Klinik bulunamadı veya kiracıya ait değil.");
            }

            return Result<ClinicReadScope>.Success(new ClinicReadScope(requestClinicId.Value, null));
        }

        return Result<ClinicReadScope>.Success(new ClinicReadScope(null, null));
    }

    private async Task<bool> MustApplyAssignedClinicScopeAsync(Guid userId, CancellationToken ct)
    {
        if (!_assignedScopeByUser.TryGetValue(userId, out var mustApply))
        {
            mustApply = await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct);
            _assignedScopeByUser[userId] = mustApply;
        }

        return mustApply;
    }

    private async Task<Guid[]> AccessibleClinicIdsAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        if (!_accessibleClinicIds.TryGetValue((userId, tenantId), out var ids))
        {
            var accessible = await _userClinics.ListAccessibleClinicsAsync(userId, tenantId, null, ct);
            ids = accessible.Select(c => c.Id).ToArray();
            _accessibleClinicIds[(userId, tenantId)] = ids;
        }

        return ids;
    }
}
