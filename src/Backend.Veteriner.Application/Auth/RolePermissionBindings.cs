using System.Collections.Generic;

namespace Backend.Veteriner.Application.Auth;

/// <summary>
/// Ürün varsayılan rol -> permission bağlama tablosu. Yeni rol izin atamalarını kodda
/// merkezi olarak tutar ve seed/backfill üzerinden idempotent uygular.
/// <para>
/// Kapsam: <c>Admin</c> için explicit alt küme (platform Admin yine <c>AdminClaimSeeder</c> ile tüm permission’ları alır);
/// <c>Owner</c> için reminder odaklı minimum set; <c>ClinicAdmin</c> için klinik operasyon yöneticisi — tenant/abonelik/
/// üye-davet ve global admin API’leri <b>dışarıda</b> (bkz. map içi yorumlar).
/// </para>
/// <para>
/// <b>Kural:</b> Buraya sadece ürün varsayılan minimum seti girer. Operasyonel olarak kullanıcı bazlı
/// özel atamalar mevcut admin permission yönetim API'si (<c>/api/v1/operation-claim-permissions</c>)
/// üzerinden yapılmaya devam eder. Tablo ROL eşleşmesiyle sınırlıdır; global admin mantığına kaymaz.
/// </para>
/// </summary>
public static class RolePermissionBindings
{
    /// <summary>
    /// OperationClaim adı -> bağlanacak permission kodu listesi. Anahtar karşılaştırma
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>; permission kodları
    /// <see cref="PermissionCatalog"/> ile birebir eşleşmelidir.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Map { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] = new[]
            {
                PermissionCatalog.Clinics.Update,
                PermissionCatalog.Reminders.Read,
                PermissionCatalog.Reminders.Manage,
            },
            ["Owner"] = new[]
            {
                PermissionCatalog.Reminders.Read,
                PermissionCatalog.Reminders.Manage,
            },
            // Klinik operasyon paneli: dashboard + çekirdek modüller; Tenants.* / Subscriptions.* / Users.Roles.Permissions /
            // Outbox / Clinics.Create / Species-Breeds yazma verilmez (ürün kararı).
            ["ClinicAdmin"] =
            [
                PermissionCatalog.Dashboard.Read,

                PermissionCatalog.Appointments.Read,
                PermissionCatalog.Appointments.Create,
                PermissionCatalog.Appointments.Cancel,
                PermissionCatalog.Appointments.Complete,
                PermissionCatalog.Appointments.Reschedule,

                PermissionCatalog.Clients.Read,
                PermissionCatalog.Clients.Create,

                PermissionCatalog.Pets.Read,
                PermissionCatalog.Pets.Create,

                PermissionCatalog.Clinics.Read,
                PermissionCatalog.Clinics.Update,

                PermissionCatalog.Examinations.Read,
                PermissionCatalog.Examinations.Create,
                PermissionCatalog.Examinations.Update,

                PermissionCatalog.Vaccinations.Read,
                PermissionCatalog.Vaccinations.Create,
                PermissionCatalog.Vaccinations.Update,

                PermissionCatalog.Payments.Read,
                PermissionCatalog.Payments.Create,
                PermissionCatalog.Payments.Update,

                PermissionCatalog.Prescriptions.Read,
                PermissionCatalog.Prescriptions.Create,
                PermissionCatalog.Prescriptions.Update,

                PermissionCatalog.Treatments.Read,
                PermissionCatalog.Treatments.Create,
                PermissionCatalog.Treatments.Update,

                PermissionCatalog.LabResults.Read,
                PermissionCatalog.LabResults.Create,
                PermissionCatalog.LabResults.Update,

                PermissionCatalog.Hospitalizations.Read,
                PermissionCatalog.Hospitalizations.Create,
                PermissionCatalog.Hospitalizations.Update,
                PermissionCatalog.Hospitalizations.Discharge,

                PermissionCatalog.Species.Read,
                PermissionCatalog.Breeds.Read,

                PermissionCatalog.Reminders.Read,
                PermissionCatalog.Reminders.Manage,
            ],
        };
}
