using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Clinics.Access;

/// <inheritdoc cref="IClinicReadScopeResolver"/>
public sealed class ClinicReadScopeResolver : IClinicReadScopeResolver
{
    private readonly IClientContext _clientContext;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinics;

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

        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct))
        {
            var accessible = await _userClinics.ListAccessibleClinicsAsync(userId, tenantId, null, ct);
            var accessibleIds = accessible.Select(c => c.Id).ToArray();

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
}
