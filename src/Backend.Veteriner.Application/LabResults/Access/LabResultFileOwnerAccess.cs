using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files.Access;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.LabResults.Access;

/// <summary>
/// Lab sonucu dosyaları (ASAMA6 L3): okuma Lab detay kapsamıyla aynı; ekleme yalnız iptal edilmemiş kayda. Kesinleşmiş muayene
/// lab dosyasını engellemez (geç gelen sonuç, Aşama 6 D2).
/// </summary>
internal sealed class LabResultFileOwnerAccess(
    IClinicContext clinicContext,
    IClinicReadScopeResolver clinicScopeResolver,
    IReadRepository<LabResult> labResults) : IStoredFileOwnerAccess
{
    public StoredFileOwnerType OwnerType => StoredFileOwnerType.LabResult;

    public async Task<Result<Guid>> ResolveForReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct)
    {
        var lab = await FindInContextAsync(tenantId, ownerId, ct);
        if (lab is null)
            return NotFound();

        var scope = await clinicScopeResolver.ResolveAsync(tenantId, lab.ClinicId, ct);
        return scope.IsSuccess ? Result<Guid>.Success(lab.ClinicId) : NotFound();
    }

    public async Task<Result<Guid>> ResolveForAttachAsync(Guid tenantId, Guid ownerId, CancellationToken ct)
    {
        var lab = await FindInContextAsync(tenantId, ownerId, ct);
        if (lab is null)
            return NotFound();

        var clinicAccess = await LabResultClinicWriteScope.EnsureWriteAccessAsync(
            clinicScopeResolver, tenantId, lab.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<Guid>.Failure(clinicAccess.Error);

        if (lab.Status == LabResultStatus.Cancelled)
        {
            return Result<Guid>.Failure(
                "LabResults.AlreadyCancelled",
                "Laboratuvar kaydı iptal edilmiş; dosya eklenemez.");
        }

        return Result<Guid>.Success(lab.ClinicId);
    }

    private async Task<LabResult?> FindInContextAsync(Guid tenantId, Guid ownerId, CancellationToken ct)
    {
        var lab = await labResults.FirstOrDefaultAsync(new LabResultByIdSpec(tenantId, ownerId), ct);
        return lab is null || (clinicContext.ClinicId is { } contextClinicId && lab.ClinicId != contextClinicId)
            ? null
            : lab;
    }

    private static Result<Guid> NotFound()
        => Result<Guid>.Failure("LabResults.NotFound", "Laboratuvar sonucu bulunamadı.");
}
