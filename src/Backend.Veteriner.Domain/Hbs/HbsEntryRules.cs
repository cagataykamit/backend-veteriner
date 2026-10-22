using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Hbs;

/// <summary>Doğrulanmış HBS geri kaydı; reçete ve aşı kaydı alanlarına aynen yazılır.</summary>
public sealed record HbsEntry(
    HbsEntryStatus Status,
    string? ReferenceNumber,
    string? NotRequiredReason,
    DateTime? MarkedAtUtc,
    Guid? MarkedByUserId);

/// <summary>
/// HBS geri kaydı kuralı (tek yer, ADR-036 Dilim 2): Girildi → numara isteğe bağlı; Gerekmiyor → gerekçe zorunlu;
/// Girilmedi → tüm alanlar temizlenir. İşaretleyen ve zaman Girildi/Gerekmiyor yazılırken atanır.
/// </summary>
public static class HbsEntryRules
{
    /// <summary>e-Reçete No / ATS belge seri no; biçimi bilinmediği için yalnız uzunluk sınırlıdır.</summary>
    public const int ReferenceNumberMaxLength = 50;

    public static Result<HbsEntry> Resolve(
        HbsEntryStatus status,
        string? referenceNumber,
        string? notRequiredReason,
        Guid markedByUserId,
        DateTime nowUtc,
        string errorCode)
    {
        if (markedByUserId == Guid.Empty)
            return Result<HbsEntry>.Failure(errorCode, "İşaretleyen kullanıcı geçersiz.");

        switch (status)
        {
            case HbsEntryStatus.NotEntered:
                return Result<HbsEntry>.Success(new HbsEntry(HbsEntryStatus.NotEntered, null, null, null, null));

            case HbsEntryStatus.Entered:
                var number = string.IsNullOrWhiteSpace(referenceNumber) ? null : referenceNumber.Trim();
                if (number is { Length: > ReferenceNumberMaxLength })
                {
                    return Result<HbsEntry>.Failure(
                        errorCode,
                        $"HBS numarası en fazla {ReferenceNumberMaxLength} karakter olabilir.");
                }

                return Result<HbsEntry>.Success(new HbsEntry(HbsEntryStatus.Entered, number, null, nowUtc, markedByUserId));

            case HbsEntryStatus.NotRequired:
                var reasonCheck = CorrectionReason.Validate(notRequiredReason, errorCode);
                if (!reasonCheck.IsSuccess)
                    return Result<HbsEntry>.Failure(reasonCheck.Error);

                return Result<HbsEntry>.Success(
                    new HbsEntry(HbsEntryStatus.NotRequired, null, notRequiredReason!.Trim(), nowUtc, markedByUserId));

            default:
                return Result<HbsEntry>.Failure(errorCode, "HBS durumu geçersiz.");
        }
    }
}
