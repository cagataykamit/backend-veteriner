using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.LabResults.Access;

/// <summary>
/// Lab result write handler'larında klinik ataması doğrulaması (IDOR-7G).
/// <see cref="IClinicReadScopeResolver"/> ile read/write ile aynı UserClinic semantiği.
/// </summary>
internal static class LabResultClinicWriteScope
{
    public static async Task<Result> EnsureWriteAccessAsync(
        IClinicReadScopeResolver scopeResolver,
        Guid tenantId,
        Guid clinicId,
        CancellationToken ct)
    {
        var scopeResult = await scopeResolver.ResolveAsync(tenantId, clinicId, ct);
        return scopeResult.IsSuccess
            ? Result.Success()
            : Result.Failure(scopeResult.Error);
    }

    public static async Task<Result> EnsureEntityAndTargetWriteAccessAsync(
        IClinicReadScopeResolver scopeResolver,
        Guid tenantId,
        Guid entityClinicId,
        Guid targetClinicId,
        CancellationToken ct)
    {
        var entityAccess = await EnsureWriteAccessAsync(scopeResolver, tenantId, entityClinicId, ct);
        if (!entityAccess.IsSuccess)
            return entityAccess;

        if (entityClinicId != targetClinicId)
            return await EnsureWriteAccessAsync(scopeResolver, tenantId, targetClinicId, ct);

        return Result.Success();
    }
}
