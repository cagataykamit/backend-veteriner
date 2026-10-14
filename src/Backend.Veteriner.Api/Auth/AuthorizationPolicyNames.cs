namespace Backend.Veteriner.Api.Auth;

/// <summary>
/// Birden fazla permission'dan en az birini gerektiren composite authorization policy adları.
/// Bu sabitler <see cref="Application.Auth.PermissionCatalog"/> içinde yer ALMAZ; permission değil
/// policy aggregate adlarıdır. <see cref="PermissionPolicyProvider"/> tarafından tek-permission'a
/// düşmemeleri için <see cref="AuthorizationServiceCollectionExtensions"/> içinde isimle kaydedilirler.
/// </summary>
/// <remarks>
/// Adlandırma kuralı: nokta yerine alt-tire kullanılır ki <see cref="Application.Auth.PermissionCatalog"/>
/// içindeki <c>Modul.Action</c> kodlarıyla görsel/anlamsal çakışma olmasın.
/// </remarks>
public static class AuthorizationPolicyNames
{
    /// <summary>
    /// Klinik çalışma saatleri ve randevu varsayılanları okuma yetkisi: randevu planlama gerekçesiyle
    /// <c>Clinics.Read</c>, <c>Clinics.Update</c>, <c>Appointments.Create</c> veya
    /// <c>Appointments.Reschedule</c> permission'larından en az birine sahip kullanıcıya açıktır.
    /// PUT/UPDATE endpoint'leri buna dahil değildir; onlar <c>Clinics.Update</c> ile korunmaya devam eder.
    /// </summary>
    public const string ClinicsSchedulingRead = "Clinics_SchedulingRead";

    /// <summary>
    /// Klinik KVKK açık rıza formu şablonunu okuma (ADR-021): müşteri kartından form yazdıran
    /// (<c>Clients.Read</c>) ve Ayarlar'da metni düzenleyen (<c>Clinics.Read</c> / <c>Clinics.Update</c>)
    /// kullanıcıya açıktır. PUT <c>Clinics.Update</c> ile korunur.
    /// </summary>
    public const string ClinicsKvkkFormRead = "Clinics_KvkkFormRead";
}
