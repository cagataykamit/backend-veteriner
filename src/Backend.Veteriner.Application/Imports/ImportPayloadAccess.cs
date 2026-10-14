using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Imports;

/// <summary>
/// Satır sonuçlarını okuyan uçların (rows, errors) ortak durum kuralı: doğrulaması bitmemiş partide
/// <c>Imports.InvalidState</c> (409), saklama süresi sonunda temizlenmiş partide <c>Imports.PayloadPurged</c> (410).
/// </summary>
internal static class ImportPayloadAccess
{
    public const string PayloadPurgedCode = "Imports.PayloadPurged";

    public static Result Check(ImportBatch batch)
    {
        if (!batch.HasValidationResult)
        {
            return Result.Failure(
                "Imports.InvalidState",
                $"Parti '{batch.Status}' durumundayken satır sonuçları yok; önce doğrulayın.");
        }

        return batch.PayloadPurgedAtUtc is null
            ? Result.Success()
            : Result.Failure(PayloadPurgedCode, "Satır verisi saklama süresi sonunda temizlendi.");
    }
}
