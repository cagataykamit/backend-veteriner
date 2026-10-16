namespace Backend.Veteriner.Domain.Legal;

/// <summary>Sürümlü kabul gerektiren Vetinity belgeleri (ADR-029). Aydınlatma/açık rıza klinik belgeleridir, burada yoktur.</summary>
public enum LegalDocumentType
{
    /// <summary>Kullanım Şartları.</summary>
    Terms = 0,

    /// <summary>Gizlilik Politikası.</summary>
    Privacy = 1,

    /// <summary>Veri İşleme Sözleşmesi; yalnız klinik yöneticisi kabul eder.</summary>
    DataProcessing = 2,

    /// <summary>Kullanım Şartları — hasta sahibi portalı varyantı (ADR-031 K4); yalnız portal hesabı kabul eder.</summary>
    PortalTerms = 3,

    /// <summary>Gizlilik Politikası — hasta sahibi portalı varyantı (ADR-031 K4); yalnız portal hesabı kabul eder.</summary>
    PortalPrivacy = 4,
}
