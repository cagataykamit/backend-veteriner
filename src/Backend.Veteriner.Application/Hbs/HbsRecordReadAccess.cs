using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.Veteriner.Application.Hbs;

/// <summary>
/// HBS hazır veri ucunun klinik görünürlüğü; reçete/aşı detay uçlarıyla aynı kural: JWT klinik bağlamı farklı klinikteki kaydı ve
/// atanmadığı klinikteki kaydı (kiracı geneli rol hariç) göstermez. Görünmeyen kayıt "bulunamadı" sayılır.
/// </summary>
internal static class HbsRecordReadAccess
{
    public static async Task<bool> CanReadAsync(
        IClinicContext clinicContext,
        IUserOperationClaimRepository userOperationClaims,
        IUserClinicRepository userClinics,
        Guid userId,
        Guid recordClinicId,
        CancellationToken ct)
    {
        if (clinicContext.ClinicId is { } clinicId && recordClinicId != clinicId)
            return false;

        var claimNames = await userOperationClaims.GetOperationClaimNamesByUserIdAsync(userId, ct);
        return TenantWideClaimNames.IsTenantWide(claimNames) || await userClinics.ExistsAsync(userId, recordClinicId, ct);
    }
}
