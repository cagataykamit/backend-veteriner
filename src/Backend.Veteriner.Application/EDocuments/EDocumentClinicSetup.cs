using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.EDocuments;

/// <summary>Belge kesmeden önce klinikte tamamlanması gereken ayar (önizleme listesi; sıra pencerede gösterim sırasıdır).</summary>
public enum EDocumentSetupIssueCode
{
    TaxpayerTypeMissing = 0,
    NotConnected = 1
}

/// <summary>
/// Önizlemenin klinik hazırlık listesi. Kural kendi yerinde kalır, burada yalnız toplanır: mükellef türü taslak kurucuda
/// (<see cref="EDocumentDraftBuilder.TaxpayerTypeMissing"/>), bağlantı sağlayıcı açılışında
/// (<see cref="EDocumentProviderGateway.Open"/>). Kesme aynı iki denetimde ilk eksikle reddeder.
/// </summary>
public static class EDocumentClinicSetup
{
    public static IReadOnlyList<EDocumentSetupIssueCode> Issues(EDocumentDraftSet draft, Result<EDocumentProviderSession> opened)
    {
        var issues = new List<EDocumentSetupIssueCode>();
        if (draft.TaxpayerType is null)
            issues.Add(EDocumentSetupIssueCode.TaxpayerTypeMissing);
        if (!opened.IsSuccess && opened.Error.Code == EDocumentProviderGateway.NotConnectedCode)
            issues.Add(EDocumentSetupIssueCode.NotConnected);
        return issues;
    }
}
