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
}
