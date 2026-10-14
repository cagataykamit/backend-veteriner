using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Imports.Access;

/// <summary>
/// Parti görünürlük kuralının tek yeri: kiracı denetimi spec'tedir; klinik bağlı parti yalnız o kliniğe erişebilen ve
/// aktif klinik bağlamı çelişmeyen kullanıcıya görünür. Görünmeyen parti varlığı sızdırılmaz (<c>Imports.BatchNotFound</c>).
/// </summary>
internal static class ImportBatchVisibility
{
    public const string NotFoundCode = "Imports.BatchNotFound";

    public static async Task<bool> IsVisibleAsync(
        ImportBatch batch,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        CancellationToken ct)
    {
        if (batch.ClinicId is not { } clinicId)
            return true;

        if (clinicContext.ClinicId is { } contextClinicId && contextClinicId != clinicId)
            return false;

        var scope = await scopeResolver.ResolveAsync(batch.TenantId, clinicId, ct);
        return scope.IsSuccess;
    }

    public static Error NotFound()
        => new(NotFoundCode, "İçe aktarma partisi bulunamadı.");
}
