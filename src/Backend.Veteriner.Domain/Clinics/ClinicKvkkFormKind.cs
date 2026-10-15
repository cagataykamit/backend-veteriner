namespace Backend.Veteriner.Domain.Clinics;

/// <summary>Klinik KVKK şablonunun türü (ADR-021, ADR-024). Aydınlatma ile açık rıza ayrı belgelerdir.</summary>
public enum ClinicKvkkFormKind
{
    /// <summary>Yazdırılıp imzalatılan açık rıza beyanı (ADR-021).</summary>
    Consent = 0,

    /// <summary>Kişisel verilerin işlenmesine ilişkin aydınlatma metni (rıza değil, bilgilendirme; ADR-024).</summary>
    Disclosure = 1,
}
