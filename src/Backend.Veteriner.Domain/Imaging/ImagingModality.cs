namespace Backend.Veteriner.Domain.Imaging;

/// <summary>Görüntüleme türü (ADR-007). Cihaz/DICOM bağı yoktur; tür yalnız klinik bağlamı taşır.</summary>
public enum ImagingModality
{
    XRay = 1,
    Ultrasound = 2,
    CT = 3,
    MRI = 4,
    Endoscopy = 5,
    ClinicalPhoto = 6,
    Other = 99
}
