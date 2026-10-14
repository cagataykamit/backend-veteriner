namespace Backend.Veteriner.Domain.Catalog;

/// <summary>Klinik sözlük kaydının türü: muayenede tanı (değerlendirme) veya şikâyet (geliş nedeni) alanına önerilir.</summary>
public enum ClinicalTermKind
{
    Diagnosis = 1,
    Complaint = 2
}
