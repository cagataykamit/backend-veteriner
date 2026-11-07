using System.Text.Json.Serialization;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Pakete bağlı özellikler (TIER-001). Yeni üst paket işi buraya ve <see cref="SubscriptionPlanCatalog"/> tablosuna eklenir.
/// Yasal kayıt defterleri, KVKK araçları ve klinik web sitesi bu listeye girmez: hiçbir pakette kilitlenmez (ADR-035, ADR-045 K13).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlanFeature>))]
public enum PlanFeature
{
    /// <summary>Hasta sahibi portalı: davet ve bağ kapsamlı portal uçları.</summary>
    OwnerPortal = 0,

    /// <summary>Portaldan randevu talebi. Kliniğe özel kimliksiz bağlantı bu kuralın dışındadır; her pakette açık (ADR-042).</summary>
    PortalAppointmentRequests = 1,

    /// <summary>Kiracıda birden fazla aktif klinik.</summary>
    MultiClinic = 2,
}
