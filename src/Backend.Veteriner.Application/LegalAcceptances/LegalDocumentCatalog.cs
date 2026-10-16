using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.LegalAcceptances;

/// <summary>
/// Hukuki belgelerin güncel sürümleri ve kabul kuralı — tek yer (ADR-029).
/// Bir sürüm değiştirilince o belgeyi gerektiren herkes yeniden kabul eder. "0.x" taslak sürümdür
/// (hukuki onay bekliyor; ürün deposu <c>legal/</c>).
/// </summary>
public static class LegalDocumentCatalog
{
    /// <summary>Belge türü → güncel sürüm. Sürümü değiştirmek yeniden kabul ister.</summary>
    public static IReadOnlyDictionary<LegalDocumentType, string> CurrentVersions { get; } =
        new Dictionary<LegalDocumentType, string>
        {
            [LegalDocumentType.Terms] = "0.1",
            [LegalDocumentType.Privacy] = "0.1",
            [LegalDocumentType.DataProcessing] = "0.1",
        };

    public static string CurrentVersion(LegalDocumentType documentType) => CurrentVersions[documentType];

    /// <summary>
    /// Kullanıcının kabul etmesi gereken belgeler: herkes Kullanım Şartları + Gizlilik; Veri İşleme Sözleşmesi'ni
    /// yalnız klinik yöneticisi (kiracı <c>Admin</c> / <c>Owner</c> claim'i) kabul eder.
    /// </summary>
    public static IReadOnlyList<LegalDocumentType> RequiredFor(IEnumerable<string>? operationClaimNames)
    {
        var isClinicAdministrator = operationClaimNames?.Any(n =>
            string.Equals(n, TenantWideClaimNames.TenantAdmin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, TenantWideClaimNames.TenantOwner, StringComparison.OrdinalIgnoreCase)) == true;

        return isClinicAdministrator
            ? [LegalDocumentType.Terms, LegalDocumentType.Privacy, LegalDocumentType.DataProcessing]
            : [LegalDocumentType.Terms, LegalDocumentType.Privacy];
    }

    /// <summary>Gereken belgelerden güncel sürümü henüz kabul edilmemiş olanlar.</summary>
    public static IReadOnlyList<LegalDocumentType> Pending(
        IEnumerable<LegalDocumentType> required,
        IEnumerable<LegalAcceptance> acceptances)
    {
        var accepted = acceptances
            .Where(a => a.Version == CurrentVersion(a.DocumentType))
            .Select(a => a.DocumentType)
            .ToHashSet();

        return required.Where(t => !accepted.Contains(t)).ToArray();
    }

    /// <summary>
    /// Gönderilen kabullerin gereken her belgenin güncel sürümünü kapsadığını doğrular. Gerekmeyen belgeler yok sayılır.
    /// Eksik belge <c>LegalAcceptances.Required</c> (400), eski sürüm <c>LegalAcceptances.VersionOutdated</c> (409).
    /// </summary>
    public static Result EnsureAcceptsCurrent(
        IEnumerable<LegalDocumentType> required,
        IReadOnlyCollection<LegalDocumentAcceptanceInput>? submitted)
    {
        foreach (var documentType in required)
        {
            var input = submitted?.FirstOrDefault(s => s.DocumentType == documentType);
            if (input is null)
            {
                return Result.Failure(
                    "LegalAcceptances.Required",
                    $"Devam etmek için {DisplayName(documentType)} kabul edilmelidir.");
            }

            var current = CurrentVersion(documentType);
            if (!string.Equals(input.Version?.Trim(), current, StringComparison.Ordinal))
            {
                return Result.Failure(
                    "LegalAcceptances.VersionOutdated",
                    $"{DisplayName(documentType)} güncellendi (güncel sürüm {current}). Sayfayı yenileyip güncel metni kabul edin.");
            }
        }

        return Result.Success();
    }

    /// <summary>Belgelerin güncel sürümü için yeni kabul satırları.</summary>
    public static IReadOnlyList<LegalAcceptance> CreateAcceptances(
        Guid userId,
        Guid tenantId,
        IEnumerable<LegalDocumentType> documentTypes,
        DateTime utcNow) =>
        documentTypes
            .Select(t => LegalAcceptance.Record(userId, tenantId, t, CurrentVersion(t), utcNow))
            .ToArray();

    public static IReadOnlyList<LegalDocumentVersionDto> ToDtos(IEnumerable<LegalDocumentType> documentTypes) =>
        documentTypes.Select(t => new LegalDocumentVersionDto(t, CurrentVersion(t))).ToArray();

    private static string DisplayName(LegalDocumentType documentType) => documentType switch
    {
        LegalDocumentType.Terms => "Kullanım Şartları",
        LegalDocumentType.Privacy => "Gizlilik Politikası",
        LegalDocumentType.DataProcessing => "Veri İşleme Sözleşmesi",
        _ => documentType.ToString(),
    };
}
