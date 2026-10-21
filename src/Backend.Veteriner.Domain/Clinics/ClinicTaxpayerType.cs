namespace Backend.Veteriner.Domain.Clinics;

/// <summary>Kliniğin mükellef türü (ADR-036 AK-3): hizmet için e-SMM, ticari satış için fatura veya ikisi (karma).</summary>
public enum ClinicTaxpayerType
{
    /// <summary>Serbest meslek (şahıs, serbest meslek kazancı).</summary>
    SelfEmployed = 1,

    /// <summary>Şirket.</summary>
    Company = 2,

    /// <summary>İkisi: hizmet serbest meslek, ürün satışı şirket/ticari.</summary>
    Both = 3
}
