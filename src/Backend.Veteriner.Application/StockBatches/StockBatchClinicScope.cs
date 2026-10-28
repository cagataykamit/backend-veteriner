using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.StockBatches;

/// <summary>Parti okuma uçlarının ortak klinik kapsamı kuralı: kapsamsız (kiracı geneli) okuma yoktur.</summary>
internal static class StockBatchClinicScope
{
    public static async Task<Result<Guid>> ResolveAsync(
        Guid tenantId,
        Guid? requestedClinicId,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        CancellationToken ct)
    {
        if (ClinicContextRule.Conflicts(requestedClinicId, clinicContext.ClinicId))
        {
            return Result<Guid>.Failure(ClinicContextRule.Mismatch("StockBatches"));
        }

        if ((requestedClinicId ?? clinicContext.ClinicId) is not { } clinicId)
        {
            return Result<Guid>.Failure(
                "StockBatches.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi.");
        }

        var scope = await scopeResolver.ResolveAsync(tenantId, clinicId, ct);
        return scope.IsSuccess ? Result<Guid>.Success(clinicId) : Result<Guid>.Failure(scope.Error);
    }
}
