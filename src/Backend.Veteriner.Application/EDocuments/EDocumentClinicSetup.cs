using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.EDocuments;

/// <summary>Belge kesmeden önce klinikte tamamlanması gereken ayar.</summary>
public enum EDocumentSetupIssueCode
{
    TaxpayerTypeMissing = 0,
    NotConnected = 1,
    TaxpayerIdentityMissing = 2
}

/// <summary>
/// Önizlemenin klinik hazırlık listesi (gösterim sırası: mükellef türü, VKN / TCKN, entegratör). Kural kendi yerinde kalır, burada
/// yalnız toplanır: mükellef ayarları taslak kurucuda (<see cref="EDocumentDraftBuilder.ClinicSetupIssues"/>), bağlantı sağlayıcı
/// açılışında (<see cref="EDocumentProviderGateway.Open"/>). Kesme aynı denetimlerde ilk eksikle reddeder.
/// </summary>
public static class EDocumentClinicSetup
{
    public static IReadOnlyList<EDocumentSetupIssueCode> Issues(EDocumentDraftSet draft, Result<EDocumentProviderSession> opened)
    {
        var issues = draft.ClinicSetupIssues.ToList();
        if (!opened.IsSuccess && opened.Error.Code == EDocumentProviderGateway.NotConnectedCode)
            issues.Add(EDocumentSetupIssueCode.NotConnected);
        return issues;
    }
}
