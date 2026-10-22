using System.Collections.Generic;

namespace Backend.Veteriner.Application.Auth;

/// <summary>
/// Ürün varsayılan rol -> permission bağlama tablosu. Yeni rol izin atamalarını kodda
/// merkezi olarak tutar ve seed/backfill üzerinden idempotent uygular.
/// <para>
/// Kapsam (Faz 4B-5):
/// <list type="bullet">
///   <item><c>Admin</c>: tenant içi yönetici. Tüm klinik operasyon modüllerinin Read/Create/Update,
///   randevu lifecycle, klinik yönetimi (Read/Create/Update), reminder yönetimi, tenant okuma + üye davet,
///   abonelik bilgisini okuma. <b>Sistem yetkileri</b> (Permissions/Roles/Users yazma, Outbox, Diagnostics)
///   <b>verilmez</b>; bu yetkiler platform yöneticisine aittir ve platform Admin claim'i için
///   <c>AdminClaimSeeder</c> tarafından tüm permission'lar zaten otomatik bağlanır.</item>
///   <item><c>Owner</c>: tenant sahibi; <c>Admin</c> ile aynı operasyonel paket. (DB'de Owner OperationClaim
///   yoksa <c>RolePermissionBindingSeeder</c> skip eder; mevcut idempotent davranış korunur.)</item>
///   <item><c>ClinicAdmin</c>: klinik operasyon yöneticisi. Tüm klinik modüllerinin Read/Create/Update,
///   randevu lifecycle, klinik okuma + güncelleme (klinik profili/working hours), reminder yönetimi.
///   <b>Verilmez:</b> <c>Clinics.Create</c> (yeni klinik açma yalnız tenant Admin/Owner),
///   <c>Tenants.*</c> / <c>Subscriptions.Manage</c> / <c>Roles.*</c> / <c>Permissions.*</c> / <c>Users.*</c>
///   / <c>Outbox.*</c> / <c>Admin.Diagnostics</c>.</item>
///   <item><c>Veteriner</c>: tıbbi operasyon rolü. Klinik kayıtlarının (muayene, aşı, tedavi, reçete, lab,
///   yatış) Read/Create/Update; randevu Read/Create/Complete/Reschedule (Cancel verilmez); pet okuma + güncelleme
///   (kilo/medikal not). <b>Verilmez:</b> müşteri C/U, ödeme C/U, klinik/tenant/abonelik ve sistem yetkileri,
    ///   düzeltme izinleri (<c>*.Correct</c>; yönetici düzeyi: Admin, Owner, ClinicAdmin).</item>
///   <item><c>Sekreter</c>: resepsiyon/operasyon rolü. Müşteri/pet R/C/U; randevu R/C/Cancel/Reschedule
///   (Complete verilmez); ödeme R/C/U; tıbbi modüller sadece Read. <b>Verilmez:</b> randevu Complete,
///   muayene/aşı/tedavi/reçete/lab/yatış C/U, klinik/tenant/abonelik ve sistem yetkileri.</item>
/// </list>
/// </para>
/// <para>
/// <b>Kural:</b> Buraya sadece ürün varsayılan minimum seti girer. Operasyonel olarak kullanıcı bazlı
/// özel atamalar mevcut admin permission yönetim API'si (<c>/api/v1/operation-claim-permissions</c>)
/// üzerinden yapılmaya devam eder. Tablo ROL eşleşmesiyle sınırlıdır; global admin mantığına kaymaz.
/// </para>
/// <para>
/// <b>Seed davranışı:</b> <see cref="Backend.Veteriner.Application.Auth.RolePermissionBindings"/>'ı uygulayan
/// seeder mevcut bağları silmez; sadece eksik olanları ekler — idempotent (bkz.
/// <c>RolePermissionBindingSeeder</c>).
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
            // Tenant içi yönetici. Operasyonun tamamı + tenant okuma + üye davet + abonelik bilgisi okuma.
            // Sistem yetkileri (Permissions/Roles/Users yazma, Outbox, Diagnostics) verilmez.
            // Not: Platform Admin claim'i AdminClaimSeeder ile zaten tüm permission'lara erişir; bu set
            // doğal "Admin rolü"nün ürün anlamını dokümante eder ve idempotent seed sırasında ek garanti üretir.
            ["Admin"] =
            [
                PermissionCatalog.Dashboard.Read,

                PermissionCatalog.Clients.Read,
                PermissionCatalog.Clients.Create,
                PermissionCatalog.Clients.Update,

                PermissionCatalog.Pets.Read,
                PermissionCatalog.Pets.Create,
                PermissionCatalog.Pets.Update,

                PermissionCatalog.Appointments.Read,
                PermissionCatalog.Appointments.Create,
                PermissionCatalog.Appointments.Cancel,
                PermissionCatalog.Appointments.NoShow,
                PermissionCatalog.Appointments.Complete,
                PermissionCatalog.Appointments.Reschedule,

                PermissionCatalog.Examinations.Read,
                PermissionCatalog.Examinations.Create,
                PermissionCatalog.Examinations.Update,
                PermissionCatalog.Examinations.Finalize,
                PermissionCatalog.Examinations.Addendum,
                PermissionCatalog.Visits.Read,
                PermissionCatalog.Visits.Create,
                PermissionCatalog.Visits.Update,
                PermissionCatalog.Visits.Correct,

                PermissionCatalog.Vaccinations.Read,
                PermissionCatalog.Vaccinations.Create,
                PermissionCatalog.Vaccinations.Update,

                PermissionCatalog.VaccineDefinitions.Read,
                PermissionCatalog.VaccineDefinitions.Create,
                PermissionCatalog.VaccineDefinitions.Update,

                PermissionCatalog.Payments.Read,
                PermissionCatalog.Payments.Create,
                PermissionCatalog.Payments.Update,
                PermissionCatalog.Payments.Correct,

                PermissionCatalog.Charges.Read,
                PermissionCatalog.Charges.Create,
                PermissionCatalog.Charges.Correct,

                PermissionCatalog.Administrations.Read,
                PermissionCatalog.Administrations.Create,
                PermissionCatalog.Administrations.Correct,

                PermissionCatalog.Sales.Read,
                PermissionCatalog.Sales.Create,
                PermissionCatalog.Sales.Correct,

                PermissionCatalog.Treatments.Read,
                PermissionCatalog.Treatments.Create,
                PermissionCatalog.Treatments.Update,

                PermissionCatalog.Prescriptions.Read,
                PermissionCatalog.Prescriptions.Create,
                PermissionCatalog.Prescriptions.Update,

                PermissionCatalog.LabResults.Read,
                PermissionCatalog.LabResults.Create,
                PermissionCatalog.LabResults.Update,

                PermissionCatalog.Imaging.Read,
                PermissionCatalog.Imaging.Create,
                PermissionCatalog.Imaging.Update,
                PermissionCatalog.Imaging.Cancel,

                PermissionCatalog.Hospitalizations.Read,
                PermissionCatalog.Hospitalizations.Create,
                PermissionCatalog.Hospitalizations.Update,
                PermissionCatalog.Hospitalizations.Discharge,
                PermissionCatalog.CareInstructions.Read,
                PermissionCatalog.CareInstructions.Create,
                PermissionCatalog.CareInstructions.Update,

                PermissionCatalog.Clinics.Read,
                PermissionCatalog.Clinics.Create,
                PermissionCatalog.Clinics.Update,

                PermissionCatalog.Reminders.Read,
                PermissionCatalog.Reminders.Manage,

                PermissionCatalog.Tenants.Read,
                PermissionCatalog.Tenants.InviteCreate,

                PermissionCatalog.Subscriptions.Read,
                PermissionCatalog.Subscriptions.Manage,

                PermissionCatalog.Species.Read,
                PermissionCatalog.Species.Create,
                PermissionCatalog.Species.Update,
                PermissionCatalog.Breeds.Read,
                PermissionCatalog.Breeds.Create,
                PermissionCatalog.Breeds.Update,

                PermissionCatalog.ProductCategories.Read,
                PermissionCatalog.ProductCategories.Create,
                PermissionCatalog.ProductCategories.Update,
                PermissionCatalog.ProductCategories.Deactivate,

                PermissionCatalog.ClinicalTerms.Read,
                PermissionCatalog.ClinicalTerms.Create,
                PermissionCatalog.ClinicalTerms.Update,
                PermissionCatalog.ClinicalTerms.Deactivate,

                PermissionCatalog.ExaminationTemplates.Read,
                PermissionCatalog.ExaminationTemplates.Create,
                PermissionCatalog.ExaminationTemplates.Update,
                PermissionCatalog.ExaminationTemplates.Deactivate,
                PermissionCatalog.ExaminationTemplates.Apply,

                PermissionCatalog.Products.Read,
                PermissionCatalog.Products.Create,
                PermissionCatalog.Products.Update,
                PermissionCatalog.Products.Deactivate,

                PermissionCatalog.StockMovements.Read,
                PermissionCatalog.StockMovements.Create,
                PermissionCatalog.StockMovements.Correct,

                // İçe aktarma yalnız yönetici düzeyi (ADR-019 K8).
                PermissionCatalog.Imports.Read,
                PermissionCatalog.Imports.Manage,
                PermissionCatalog.Imports.Commit,

                // Hasta sahibi portalı daveti ve erişim yönetimi yalnız yönetici düzeyi (ADR-031).
                PermissionCatalog.OwnerPortal.Manage,
            ],

            // Tenant sahibi: Admin ile aynı operasyonel paket (ürün kararı: Owner ≈ Admin).
            ["Owner"] =
            [
                PermissionCatalog.Dashboard.Read,

                PermissionCatalog.Clients.Read,
                PermissionCatalog.Clients.Create,
                PermissionCatalog.Clients.Update,

                PermissionCatalog.Pets.Read,
                PermissionCatalog.Pets.Create,
                PermissionCatalog.Pets.Update,

                PermissionCatalog.Appointments.Read,
                PermissionCatalog.Appointments.Create,
                PermissionCatalog.Appointments.Cancel,
                PermissionCatalog.Appointments.NoShow,
                PermissionCatalog.Appointments.Complete,
                PermissionCatalog.Appointments.Reschedule,

                PermissionCatalog.Examinations.Read,
                PermissionCatalog.Examinations.Create,
                PermissionCatalog.Examinations.Update,
                PermissionCatalog.Examinations.Finalize,
                PermissionCatalog.Examinations.Addendum,
                PermissionCatalog.Visits.Read,
                PermissionCatalog.Visits.Create,
                PermissionCatalog.Visits.Update,
                PermissionCatalog.Visits.Correct,

                PermissionCatalog.Vaccinations.Read,
                PermissionCatalog.Vaccinations.Create,
                PermissionCatalog.Vaccinations.Update,

                PermissionCatalog.VaccineDefinitions.Read,
                PermissionCatalog.VaccineDefinitions.Create,
                PermissionCatalog.VaccineDefinitions.Update,

                PermissionCatalog.Payments.Read,
                PermissionCatalog.Payments.Create,
                PermissionCatalog.Payments.Update,
                PermissionCatalog.Payments.Correct,

                PermissionCatalog.Charges.Read,
                PermissionCatalog.Charges.Create,
                PermissionCatalog.Charges.Correct,

                PermissionCatalog.Administrations.Read,
                PermissionCatalog.Administrations.Create,
                PermissionCatalog.Administrations.Correct,

                PermissionCatalog.Sales.Read,
                PermissionCatalog.Sales.Create,
                PermissionCatalog.Sales.Correct,

                PermissionCatalog.Treatments.Read,
                PermissionCatalog.Treatments.Create,
                PermissionCatalog.Treatments.Update,

                PermissionCatalog.Prescriptions.Read,
                PermissionCatalog.Prescriptions.Create,
                PermissionCatalog.Prescriptions.Update,

                PermissionCatalog.LabResults.Read,
                PermissionCatalog.LabResults.Create,
                PermissionCatalog.LabResults.Update,

                PermissionCatalog.Imaging.Read,
                PermissionCatalog.Imaging.Create,
                PermissionCatalog.Imaging.Update,
                PermissionCatalog.Imaging.Cancel,

                PermissionCatalog.Hospitalizations.Read,
                PermissionCatalog.Hospitalizations.Create,
                PermissionCatalog.Hospitalizations.Update,
                PermissionCatalog.Hospitalizations.Discharge,
                PermissionCatalog.CareInstructions.Read,
                PermissionCatalog.CareInstructions.Create,
                PermissionCatalog.CareInstructions.Update,

                PermissionCatalog.Clinics.Read,
                PermissionCatalog.Clinics.Create,
                PermissionCatalog.Clinics.Update,

                PermissionCatalog.Reminders.Read,
                PermissionCatalog.Reminders.Manage,

                PermissionCatalog.Tenants.Read,
                PermissionCatalog.Tenants.InviteCreate,

                PermissionCatalog.Subscriptions.Read,
                PermissionCatalog.Subscriptions.Manage,

                PermissionCatalog.Species.Read,
                PermissionCatalog.Species.Create,
                PermissionCatalog.Species.Update,
                PermissionCatalog.Breeds.Read,
                PermissionCatalog.Breeds.Create,
                PermissionCatalog.Breeds.Update,

                PermissionCatalog.ProductCategories.Read,
                PermissionCatalog.ProductCategories.Create,
                PermissionCatalog.ProductCategories.Update,
                PermissionCatalog.ProductCategories.Deactivate,

                PermissionCatalog.ClinicalTerms.Read,
                PermissionCatalog.ClinicalTerms.Create,
                PermissionCatalog.ClinicalTerms.Update,
                PermissionCatalog.ClinicalTerms.Deactivate,

                PermissionCatalog.ExaminationTemplates.Read,
                PermissionCatalog.ExaminationTemplates.Create,
                PermissionCatalog.ExaminationTemplates.Update,
                PermissionCatalog.ExaminationTemplates.Deactivate,
                PermissionCatalog.ExaminationTemplates.Apply,

                PermissionCatalog.Products.Read,
                PermissionCatalog.Products.Create,
                PermissionCatalog.Products.Update,
                PermissionCatalog.Products.Deactivate,

                PermissionCatalog.StockMovements.Read,
                PermissionCatalog.StockMovements.Create,
                PermissionCatalog.StockMovements.Correct,

                PermissionCatalog.Imports.Read,
                PermissionCatalog.Imports.Manage,
                PermissionCatalog.Imports.Commit,

                // Hasta sahibi portalı daveti ve erişim yönetimi yalnız yönetici düzeyi (ADR-031).
                PermissionCatalog.OwnerPortal.Manage,
            ],

            // Klinik operasyon paneli: dashboard + çekirdek modüller; klinik profilini
            // güncelleme yetkisi ürün gereği ClinicAdmin'de tutulur (working hours / break time / vb.).
            // Tenants.* / Subscriptions.Manage / Users.Roles.Permissions / Outbox / Clinics.Create verilmez (Create: Admin/Owner).
            // Species/Breeds Create/Update tür-ırk kataloğu yönetimi için ClinicAdmin'de verilir (Faz 11B-3).
            // Subscriptions.Read ek bilgi amacıyla verilir.
            ["ClinicAdmin"] =
            [
                PermissionCatalog.Dashboard.Read,

                PermissionCatalog.Appointments.Read,
                PermissionCatalog.Appointments.Create,
                PermissionCatalog.Appointments.Cancel,
                PermissionCatalog.Appointments.NoShow,
                PermissionCatalog.Appointments.Complete,
                PermissionCatalog.Appointments.Reschedule,

                PermissionCatalog.Clients.Read,
                PermissionCatalog.Clients.Create,
                PermissionCatalog.Clients.Update,

                PermissionCatalog.Pets.Read,
                PermissionCatalog.Pets.Create,
                PermissionCatalog.Pets.Update,

                PermissionCatalog.Clinics.Read,
                PermissionCatalog.Clinics.Update,

                PermissionCatalog.Examinations.Read,
                PermissionCatalog.Examinations.Create,
                PermissionCatalog.Examinations.Update,
                PermissionCatalog.Examinations.Finalize,
                PermissionCatalog.Examinations.Addendum,
                PermissionCatalog.Visits.Read,
                PermissionCatalog.Visits.Create,
                PermissionCatalog.Visits.Update,
                PermissionCatalog.Visits.Correct,

                PermissionCatalog.Vaccinations.Read,
                PermissionCatalog.Vaccinations.Create,
                PermissionCatalog.Vaccinations.Update,

                PermissionCatalog.VaccineDefinitions.Read,
                PermissionCatalog.VaccineDefinitions.Create,
                PermissionCatalog.VaccineDefinitions.Update,

                PermissionCatalog.Payments.Read,
                PermissionCatalog.Payments.Create,
                PermissionCatalog.Payments.Update,
                PermissionCatalog.Payments.Correct,

                PermissionCatalog.Charges.Read,
                PermissionCatalog.Charges.Create,
                PermissionCatalog.Charges.Correct,

                PermissionCatalog.Prescriptions.Read,
                PermissionCatalog.Prescriptions.Create,
                PermissionCatalog.Prescriptions.Update,

                PermissionCatalog.Administrations.Read,
                PermissionCatalog.Administrations.Create,
                PermissionCatalog.Administrations.Correct,

                PermissionCatalog.Sales.Read,
                PermissionCatalog.Sales.Create,
                PermissionCatalog.Sales.Correct,

                PermissionCatalog.Treatments.Read,
                PermissionCatalog.Treatments.Create,
                PermissionCatalog.Treatments.Update,

                PermissionCatalog.LabResults.Read,
                PermissionCatalog.LabResults.Create,
                PermissionCatalog.LabResults.Update,

                PermissionCatalog.Imaging.Read,
                PermissionCatalog.Imaging.Create,
                PermissionCatalog.Imaging.Update,
                PermissionCatalog.Imaging.Cancel,

                PermissionCatalog.Hospitalizations.Read,
                PermissionCatalog.Hospitalizations.Create,
                PermissionCatalog.Hospitalizations.Update,
                PermissionCatalog.Hospitalizations.Discharge,
                PermissionCatalog.CareInstructions.Read,
                PermissionCatalog.CareInstructions.Create,
                PermissionCatalog.CareInstructions.Update,

                PermissionCatalog.Species.Read,
                PermissionCatalog.Species.Create,
                PermissionCatalog.Species.Update,
                PermissionCatalog.Breeds.Read,
                PermissionCatalog.Breeds.Create,
                PermissionCatalog.Breeds.Update,

                PermissionCatalog.ProductCategories.Read,
                PermissionCatalog.ProductCategories.Create,
                PermissionCatalog.ProductCategories.Update,
                PermissionCatalog.ProductCategories.Deactivate,

                PermissionCatalog.ClinicalTerms.Read,
                PermissionCatalog.ClinicalTerms.Create,
                PermissionCatalog.ClinicalTerms.Update,
                PermissionCatalog.ClinicalTerms.Deactivate,

                PermissionCatalog.ExaminationTemplates.Read,
                PermissionCatalog.ExaminationTemplates.Create,
                PermissionCatalog.ExaminationTemplates.Update,
                PermissionCatalog.ExaminationTemplates.Deactivate,
                PermissionCatalog.ExaminationTemplates.Apply,

                PermissionCatalog.Products.Read,
                PermissionCatalog.Products.Create,
                PermissionCatalog.Products.Update,
                PermissionCatalog.Products.Deactivate,

                PermissionCatalog.StockMovements.Read,
                PermissionCatalog.StockMovements.Create,
                PermissionCatalog.StockMovements.Correct,

                PermissionCatalog.Reminders.Read,
                PermissionCatalog.Reminders.Manage,

                PermissionCatalog.Subscriptions.Read,

                PermissionCatalog.Imports.Read,
                PermissionCatalog.Imports.Manage,
                PermissionCatalog.Imports.Commit,

                // Hasta sahibi portalı daveti ve erişim yönetimi yalnız yönetici düzeyi (ADR-031).
                PermissionCatalog.OwnerPortal.Manage,
            ],

            // Tıbbi operasyon: klinik kayıtlarını (muayene/aşı/tedavi/reçete/lab/yatış) yönetir.
            // Randevu Cancel verilmez (resepsiyon/clinic admin işi). Müşteri C/U verilmez (sekreter işi).
            // Pets.Update verilir (kilo/medikal not güncellemesi). Payments sadece Read.
            ["Veteriner"] =
            [
                PermissionCatalog.Dashboard.Read,

                PermissionCatalog.Clients.Read,

                PermissionCatalog.Pets.Read,
                PermissionCatalog.Pets.Update,

                PermissionCatalog.Appointments.Read,
                PermissionCatalog.Appointments.Create,
                PermissionCatalog.Appointments.Complete,
                PermissionCatalog.Appointments.Reschedule,

                PermissionCatalog.Examinations.Read,
                PermissionCatalog.Examinations.Create,
                PermissionCatalog.Examinations.Update,
                PermissionCatalog.Examinations.Finalize,
                PermissionCatalog.Examinations.Addendum,
                PermissionCatalog.Visits.Read,
                PermissionCatalog.Visits.Create,
                PermissionCatalog.Visits.Update,

                PermissionCatalog.Vaccinations.Read,
                PermissionCatalog.Vaccinations.Create,
                PermissionCatalog.Vaccinations.Update,

                PermissionCatalog.VaccineDefinitions.Read,

                PermissionCatalog.Administrations.Read,
                PermissionCatalog.Administrations.Create,

                PermissionCatalog.Treatments.Read,
                PermissionCatalog.Treatments.Create,
                PermissionCatalog.Treatments.Update,

                PermissionCatalog.Prescriptions.Read,
                PermissionCatalog.Prescriptions.Create,
                PermissionCatalog.Prescriptions.Update,

                PermissionCatalog.LabResults.Read,
                PermissionCatalog.LabResults.Create,
                PermissionCatalog.LabResults.Update,

                PermissionCatalog.Imaging.Read,
                PermissionCatalog.Imaging.Create,
                PermissionCatalog.Imaging.Update,
                PermissionCatalog.Imaging.Cancel,

                PermissionCatalog.Hospitalizations.Read,
                PermissionCatalog.Hospitalizations.Create,
                PermissionCatalog.Hospitalizations.Update,
                PermissionCatalog.Hospitalizations.Discharge,
                PermissionCatalog.CareInstructions.Read,
                PermissionCatalog.CareInstructions.Create,
                PermissionCatalog.CareInstructions.Update,

                PermissionCatalog.Payments.Read,

                PermissionCatalog.Charges.Read,

                PermissionCatalog.Species.Read,
                PermissionCatalog.Breeds.Read,

                PermissionCatalog.ClinicalTerms.Read,
                PermissionCatalog.ClinicalTerms.Create,
                PermissionCatalog.ClinicalTerms.Update,

                PermissionCatalog.ExaminationTemplates.Read,
                PermissionCatalog.ExaminationTemplates.Create,
                PermissionCatalog.ExaminationTemplates.Update,
                PermissionCatalog.ExaminationTemplates.Apply,

                PermissionCatalog.ProductCategories.Read,
                PermissionCatalog.Products.Read,
                PermissionCatalog.StockMovements.Read,

                PermissionCatalog.Reminders.Read,
            ],

            // Resepsiyon/operasyon: müşteri/pet, randevu (Complete hariç), ödeme yönetimi.
            // Tıbbi modüller (muayene/aşı/tedavi/reçete/lab/yatış) sadece Read. Discharge verilmez.
            ["Sekreter"] =
            [
                PermissionCatalog.Dashboard.Read,

                PermissionCatalog.Clients.Read,
                PermissionCatalog.Clients.Create,
                PermissionCatalog.Clients.Update,

                PermissionCatalog.Pets.Read,
                PermissionCatalog.Pets.Create,
                PermissionCatalog.Pets.Update,

                PermissionCatalog.Appointments.Read,
                PermissionCatalog.Appointments.Create,
                PermissionCatalog.Appointments.Cancel,
                PermissionCatalog.Appointments.NoShow,
                PermissionCatalog.Appointments.Reschedule,

                PermissionCatalog.Examinations.Read,
                PermissionCatalog.Visits.Read,
                PermissionCatalog.Visits.Create,
                PermissionCatalog.Visits.Update,
                PermissionCatalog.Vaccinations.Read,
                PermissionCatalog.VaccineDefinitions.Read,
                PermissionCatalog.Administrations.Read,

                PermissionCatalog.Treatments.Read,
                PermissionCatalog.Prescriptions.Read,
                PermissionCatalog.LabResults.Read,
                PermissionCatalog.Imaging.Read,
                PermissionCatalog.Hospitalizations.Read,
                PermissionCatalog.CareInstructions.Read,

                PermissionCatalog.Payments.Read,
                PermissionCatalog.Payments.Create,
                PermissionCatalog.Payments.Update,

                PermissionCatalog.Charges.Read,
                PermissionCatalog.Charges.Create,

                PermissionCatalog.Sales.Read,
                PermissionCatalog.Sales.Create,

                PermissionCatalog.Species.Read,
                PermissionCatalog.Breeds.Read,

                PermissionCatalog.ProductCategories.Read,
                PermissionCatalog.Products.Read,
                PermissionCatalog.StockMovements.Read,
                PermissionCatalog.StockMovements.Create,

                PermissionCatalog.ClinicalTerms.Read,
                PermissionCatalog.ExaminationTemplates.Read,

                PermissionCatalog.Reminders.Read,
            ],
        };

    /// <summary>
    /// Varsayılan paketten bilinçli çıkarılan (kullanıcı kararı) rol -> permission bağları. Seed bunları
    /// <see cref="Map"/> dışında kalan eski bağlar olarak bırakmaz, siler; yoksa karar yalnız yeni kurulumlarda geçerli olurdu.
    /// Yalnız burada listelenen çift etkilenir; diğer manuel atamalara dokunulmaz.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Revoked { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // Uygulama iptali yönetici düzeyine ait (ADR-012, karar 2026-10-13).
            ["Veteriner"] = [PermissionCatalog.Administrations.Correct],
        };
}
