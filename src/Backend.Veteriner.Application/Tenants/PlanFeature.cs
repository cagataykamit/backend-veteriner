using System.Text.Json.Serialization;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Pakete bağlı özellikler (TIER-001). Yeni üst paket işi buraya ve <see cref="SubscriptionPlanCatalog"/> tablosuna eklenir.
/// Yasal kayıt defterleri, KVKK araçları ve klinik web sitesi bu listeye girmez: hiçbir pakette kilitlenmez (ADR-035, ADR-045 K13).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlanFeature>))]
public enum PlanFeature
{
    /// <summary>Hasta sahibi portalı: yeni davet ve bekleyen davetin kabulü. Mevcut bağ paket düşünce açık kalır.</summary>
    OwnerPortal = 0,

    /// <summary>
    /// Portaldan randevu talebi. Ayrı kapısı yoktur: bağ yoksa uç zaten kapalıdır, paket düşünce mevcut bağ açık kalır
    /// (docs/PLAN_FEATURES_API_CONTRACT.md §4). Kliniğe özel kimliksiz bağlantı bu kuralın dışındadır; her pakette açık (ADR-042).
    /// </summary>
    PortalAppointmentRequests = 1,

    /// <summary>Kiracıda birden fazla aktif klinik.</summary>
    MultiClinic = 2,

    /// <summary>
    /// Klinik başına otomatik hatırlatıcı kuralları (TIER-003; docs/AUTOMATED_REMINDERS_API_CONTRACT.md). Bugünkü aşı ve randevu
    /// e-posta hatırlatması bu kuralın dışındadır; her pakette kalır.
    /// </summary>
    AutomatedReminders = 3,

    /// <summary>
    /// Gelişmiş raporlar (TIER-004; docs/PERFORMANCE_BREAKDOWNS_API_CONTRACT.md): hekim, kalem, tür kırılımı ve dönem karşılaştırması.
    /// Bugünkü raporlar ve işletme performansı özeti (önceki dönemle) bu kuralın dışındadır; her pakette kalır.
    /// </summary>
    AdvancedReports = 4,

    /// <summary>
    /// Otomatik aşı sihirbazı (TIER-005; docs/VACCINATION_SCHEDULE_API_CONTRACT.md): klinik aşı takvimi ve takvimden önerilen plan.
    /// Bugünkü aşı kaydı, planlı aşı ve aşı hatırlatması bu kuralın dışındadır; her pakette kalır.
    /// </summary>
    VaccinationWizard = 5,

    /// <summary>
    /// Telefon kamerasıyla barkod okutma (TIER-006; docs/MOBILE_BARCODE_API_CONTRACT.md). Okuma tarayıcıda yapılır, ayrı sunucu kapısı
    /// yoktur; satır <c>access-state</c> ve paket tablosu içindir. Ürün kartındaki barkod alanı ve elle ya da okuyucuyla girilen
    /// barkodla arama bu kuralın dışındadır; her pakette açık.
    /// </summary>
    MobileBarcode = 6,

    /// <summary>
    /// Patron ekranı (TIER-007; docs/CLINICS_OVERVIEW_API_CONTRACT.md): erişilebilen tüm aktif şubelerin ciro, tahsilat, açık alacak ve
    /// iş yükü tek tabloda, önceki dönemle. Şube başına işletme performansı özeti her pakette kalır.
    /// </summary>
    ClinicsOverview = 7,
}
