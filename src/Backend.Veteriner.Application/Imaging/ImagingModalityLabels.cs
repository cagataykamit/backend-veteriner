using Backend.Veteriner.Domain.Imaging;

namespace Backend.Veteriner.Application.Imaging;

/// <summary>Görüntüleme türünün Türkçe adı (timeline başlığı; arayüz kendi etiketini de tutar).</summary>
public static class ImagingModalityLabels
{
    public static string Of(ImagingModality modality) => modality switch
    {
        ImagingModality.XRay => "Röntgen",
        ImagingModality.Ultrasound => "Ultrason",
        ImagingModality.CT => "BT",
        ImagingModality.MRI => "MR",
        ImagingModality.Endoscopy => "Endoskopi",
        ImagingModality.ClinicalPhoto => "Klinik fotoğraf",
        _ => "Görüntüleme"
    };
}
