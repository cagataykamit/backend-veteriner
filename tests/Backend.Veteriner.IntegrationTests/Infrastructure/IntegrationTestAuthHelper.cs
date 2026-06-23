using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Authorization;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

internal sealed record IntegrationLoginTokens(
    string AccessToken,
    string RefreshToken,
    Guid UserId,
    Guid TenantId);

internal static class IntegrationTestAuthHelper
{
    public static async Task EnsureRolePermissionBindingsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RolePermissionBindingSeeder.SeedAsync(db);
    }

    public static async Task<IntegrationLoginTokens> LoginAsync(HttpClient client, IServiceProvider services, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = password });
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = json.GetProperty("accessToken").GetString()!;
        var refreshToken = json.GetProperty("refreshToken").GetString()!;
        var tenantId = json.GetProperty("resolvedTenantId").GetGuid();

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync();

        return new IntegrationLoginTokens(accessToken, refreshToken, userId, tenantId);
    }

    /// <summary>
    /// Login rate-limit'ine takılmadan, DB'deki kullanıcı için access token üretir (IDOR integration testleri).
    /// OperationClaim/permission kontrolleri handler tarafında DB'den okunmaya devam eder.
    /// </summary>
    public static async Task<string> IssueUserAccessTokenAsync(
        IServiceProvider services,
        string email,
        IReadOnlyCollection<string> permissions)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var user = await db.Users.SingleAsync(u => u.Email == email);
        var tenantId = await db.UserTenants
            .Where(ut => ut.UserId == user.Id)
            .Select(ut => ut.TenantId)
            .SingleAsync();

        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenantId.ToString("D")))
            .ToList();

        var (accessToken, _, _) = jwt.Create(user.Id, user.Email, Array.Empty<string>(), claims);
        return accessToken;
    }

    /// <summary>
    /// List pagination smoke testleri: tenant/kliniğe <see cref="UserClinic"/> atanmış non-tenant-wide reader
    /// kullanıcı seed eder; gerçek userId + operation claim + JWT permission ile access token döndürür.
    /// </summary>
    public static async Task<string> SeedScopedListReaderAndIssueTokenAsync(
        AppDbContext db,
        IJwtTokenService jwt,
        IPasswordHasher hasher,
        Guid tenantId,
        Guid clinicId,
        string permissionCode)
    {
        var claim = await EnsureReadClaimForPermissionAsync(db, permissionCode);

        var email = $"list-reader-{Guid.NewGuid():N}@example.com";
        var user = new User(email, hasher.Hash("123456"));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenantId));
        db.UserClinics.Add(new UserClinic(user.Id, clinicId));
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("permission", permissionCode),
            new(VeterinerClaims.TenantId, tenantId.ToString("D"))
        };

        var (accessToken, _, _) = jwt.Create(user.Id, email, Array.Empty<string>(), claims);
        return accessToken;
    }

    /// <summary>
    /// Report/export endpoint smoke testleri: tenant/kliniğe atanmış non-tenant-wide reader
    /// (<see cref="SeedScopedListReaderAndIssueTokenAsync"/> ile aynı).
    /// </summary>
    public static Task<string> SeedReportReaderAndIssueTokenAsync(
        AppDbContext db,
        IJwtTokenService jwt,
        IPasswordHasher hasher,
        Guid tenantId,
        Guid clinicId,
        string permissionCode)
        => SeedScopedListReaderAndIssueTokenAsync(db, jwt, hasher, tenantId, clinicId, permissionCode);

    /// <summary>
    /// Tenant-wide Admin claim'li kullanıcı; bilinmeyen clinicId için Clinics.NotFound senaryolarında kullanılır.
    /// </summary>
    public static async Task<string> SeedTenantWideAdminAndIssueTokenAsync(
        AppDbContext db,
        IJwtTokenService jwt,
        IPasswordHasher hasher,
        Guid tenantId,
        IReadOnlyCollection<string> jwtPermissions)
    {
        var adminClaim = await db.OperationClaims.AsNoTracking().FirstAsync(c => c.Name == "Admin");

        var email = $"rep-admin-{Guid.NewGuid():N}@example.com";
        var user = new User(email, hasher.Hash("123456"));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, adminClaim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenantId));
        await db.SaveChangesAsync();

        var claims = jwtPermissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenantId.ToString("D")))
            .ToList();

        var (accessToken, _, _) = jwt.Create(user.Id, email, Array.Empty<string>(), claims);
        return accessToken;
    }

    /// <summary>
    /// Tenant Admin claim'li kullanıcı; tenant-wide /me/clinics ve Clinics.Create smoke için.
    /// </summary>
    public static async Task<(string Email, string Password, Guid ExtraClinicId)> SeedTenantAdminUserAsync(
        IServiceProvider services,
        IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var adminClaim = await db.OperationClaims.SingleAsync(c => c.Name == "Admin");

        var email = $"tenant-admin-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, adminClaim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        await db.SaveChangesAsync();

        var extraClinic = new Clinic(tenant.Id, $"Extra-{Guid.NewGuid():N}"[..14], "Ankara");
        db.Clinics.Add(extraClinic);
        await db.SaveChangesAsync();

        return (email, password, extraClinic.Id);
    }

    /// <summary>
    /// ClinicAdmin claim'li kullanıcı; yalnızca atanmış kliniği görür, Clinics.Create yoktur.
    /// </summary>
    public static async Task<(string Email, string Password, Guid AssignedClinicId, Guid UnassignedClinicId)>
        SeedClinicAdminUserAsync(IServiceProvider services, IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var assignedClinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var unassignedClinic = new Clinic(tenant.Id, $"Hidden-{Guid.NewGuid():N}"[..14], "İzmir");
        db.Clinics.Add(unassignedClinic);

        var clinicAdminClaim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
        var email = $"clinicadmin-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, clinicAdminClaim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, assignedClinic.Id));
        await db.SaveChangesAsync();

        return (email, password, assignedClinic.Id, unassignedClinic.Id);
    }

    /// <summary>
    /// Tenant-wide olmayan "normal" kullanıcı (Veteriner / Sekreter benzeri): <c>Clinics.Read</c> iznine sahip
    /// ama Admin / Owner / PlatformAdmin / ClinicAdmin claim'i yok. Yalnız atandığı kliniği okuyabilmeli;
    /// aynı tenant içindeki atanmadığı kliniği okuyamamalı.
    /// </summary>
    public static async Task<(string Email, string Password, Guid AssignedClinicId, Guid UnassignedClinicId)>
        SeedClinicReaderUserAsync(IServiceProvider services, IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var assignedClinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var unassignedClinic = new Clinic(tenant.Id, $"Reader-{Guid.NewGuid():N}"[..14], "Konya");
        db.Clinics.Add(unassignedClinic);
        await db.SaveChangesAsync();

        // Tenant-wide olmayan, ClinicAdmin de olmayan özel okuma claim'i → JWT'de Clinics.Read permission'ı taşır
        // ama TenantWideClaimNames whitelist'inde yer almaz.
        var claim = await EnsureClinicsReadClaimAsync(db);

        var email = $"clinic-reader-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, assignedClinic.Id));
        await db.SaveChangesAsync();

        return (email, password, assignedClinic.Id, unassignedClinic.Id);
    }

    /// <summary>
    /// Tenant-wide olmayan kullanıcı: <c>Appointments.Read</c> iznine sahip, Admin / Owner / PlatformAdmin /
    /// ClinicAdmin claim'i yok. Yalnız atandığı kliniğin randevu detayını okuyabilmeli.
    /// </summary>
    public static async Task<(string Email, string Password, Guid AssignedClinicId, Guid UnassignedClinicId)>
        SeedAppointmentReaderUserAsync(IServiceProvider services, IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var assignedClinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var unassignedClinic = new Clinic(tenant.Id, $"ApptReader-{Guid.NewGuid():N}"[..14], "Konya");
        db.Clinics.Add(unassignedClinic);
        await db.SaveChangesAsync();

        var claim = await EnsureAppointmentsReadClaimAsync(db);

        var email = $"appt-reader-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, assignedClinic.Id));
        await db.SaveChangesAsync();

        return (email, password, assignedClinic.Id, unassignedClinic.Id);
    }

    /// <summary>Belirtilen klinikte tek randevu oluşturur (zaman bağımsız: UTC now + offset).</summary>
    public static async Task<Guid> SeedAppointmentInClinicAsync(
        IServiceProvider services,
        Guid clinicId,
        TimeSpan? scheduledOffsetFromUtcNow = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);
        var client = new Client(clinic.TenantId, $"ApptClient-{Guid.NewGuid():N}"[..14], "905551110099");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(clinic.TenantId, client.Id, $"ApptPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        var scheduledAt = DateTime.UtcNow.Add(scheduledOffsetFromUtcNow ?? TimeSpan.FromDays(2));
        var appointment = new Appointment(
            clinic.TenantId,
            clinic.Id,
            pet.Id,
            scheduledAt,
            30,
            AppointmentType.Consultation,
            AppointmentStatus.Scheduled);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return appointment.Id;
    }

    /// <summary>
    /// Tenant-wide olmayan kullanıcı: <c>Examinations.Read</c> iznine sahip, Admin / Owner / PlatformAdmin /
    /// ClinicAdmin claim'i yok. Yalnız atandığı kliniğin muayene detayını okuyabilmeli.
    /// </summary>
    public static async Task<(string Email, string Password, Guid AssignedClinicId, Guid UnassignedClinicId)>
        SeedExaminationReaderUserAsync(IServiceProvider services, IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var assignedClinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var unassignedClinic = new Clinic(tenant.Id, $"ExamReader-{Guid.NewGuid():N}"[..14], "Konya");
        db.Clinics.Add(unassignedClinic);
        await db.SaveChangesAsync();

        var claim = await EnsureExaminationsReadClaimAsync(db);

        var email = $"exam-reader-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, assignedClinic.Id));
        await db.SaveChangesAsync();

        return (email, password, assignedClinic.Id, unassignedClinic.Id);
    }

    /// <summary>Belirtilen klinikte tek muayene oluşturur; isteğe bağlı bağlı tedavi kaydı ekler.</summary>
    public static async Task<ExaminationSeedResult> SeedExaminationInClinicAsync(
        IServiceProvider services,
        Guid clinicId,
        bool includeRelatedTreatment = false,
        TimeSpan? examinedOffsetFromUtcNow = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);
        var client = new Client(clinic.TenantId, $"ExamClient-{Guid.NewGuid():N}"[..14], "905551110088");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(clinic.TenantId, client.Id, $"ExamPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        var examinedAt = DateTime.UtcNow.Add(examinedOffsetFromUtcNow ?? TimeSpan.FromHours(-3));
        var examination = new Examination(
            clinic.TenantId,
            clinic.Id,
            pet.Id,
            appointmentId: null,
            examinedAt,
            "Kontrol",
            "Bulgu",
            null,
            null);
        db.Examinations.Add(examination);
        await db.SaveChangesAsync();

        Guid? treatmentId = null;
        if (includeRelatedTreatment)
        {
            var treatment = new Treatment(
                clinic.TenantId,
                clinic.Id,
                pet.Id,
                examination.Id,
                examinedAt.AddMinutes(15),
                "İlgili Tedavi",
                "Tedavi açıklaması",
                null,
                null);
            db.Treatments.Add(treatment);
            await db.SaveChangesAsync();
            treatmentId = treatment.Id;
        }

        return new ExaminationSeedResult(examination.Id, treatmentId);
    }

    internal sealed record ExaminationSeedResult(Guid ExaminationId, Guid? RelatedTreatmentId);

    /// <summary>
    /// Tenant-wide olmayan kullanıcı: <c>Treatments.Read</c> iznine sahip, Admin / Owner / PlatformAdmin /
    /// ClinicAdmin claim'i yok. Yalnız atandığı kliniğin tedavi detayını okuyabilmeli.
    /// </summary>
    public static async Task<(string Email, string Password, Guid AssignedClinicId, Guid UnassignedClinicId)>
        SeedTreatmentReaderUserAsync(IServiceProvider services, IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var assignedClinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var unassignedClinic = new Clinic(tenant.Id, $"TreatReader-{Guid.NewGuid():N}"[..14], "Konya");
        db.Clinics.Add(unassignedClinic);
        await db.SaveChangesAsync();

        var claim = await EnsureTreatmentsReadClaimAsync(db);

        var email = $"treat-reader-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, assignedClinic.Id));
        await db.SaveChangesAsync();

        return (email, password, assignedClinic.Id, unassignedClinic.Id);
    }

    /// <summary>Belirtilen klinikte tek tedavi kaydı oluşturur.</summary>
    public static async Task<Guid> SeedTreatmentInClinicAsync(
        IServiceProvider services,
        Guid clinicId,
        TimeSpan? treatmentOffsetFromUtcNow = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);
        var client = new Client(clinic.TenantId, $"TreatClient-{Guid.NewGuid():N}"[..14], "905551110077");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(clinic.TenantId, client.Id, $"TreatPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        var treatmentDate = DateTime.UtcNow.Add(treatmentOffsetFromUtcNow ?? TimeSpan.FromHours(-2));
        var treatment = new Treatment(
            clinic.TenantId,
            clinic.Id,
            pet.Id,
            examinationId: null,
            treatmentDate,
            "IDOR Test Tedavi",
            "Tedavi açıklaması",
            null,
            null);
        db.Treatments.Add(treatment);
        await db.SaveChangesAsync();

        return treatment.Id;
    }

    /// <summary>
    /// Tenant-wide olmayan kullanıcı: <c>Prescriptions.Read</c> iznine sahip, Admin / Owner / PlatformAdmin /
    /// ClinicAdmin claim'i yok. Yalnız atandığı kliniğin reçete detayını okuyabilmeli.
    /// </summary>
    public static async Task<(string Email, string Password, Guid AssignedClinicId, Guid UnassignedClinicId)>
        SeedPrescriptionReaderUserAsync(IServiceProvider services, IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var assignedClinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var unassignedClinic = new Clinic(tenant.Id, $"PrescReader-{Guid.NewGuid():N}"[..14], "Konya");
        db.Clinics.Add(unassignedClinic);
        await db.SaveChangesAsync();

        var claim = await EnsurePrescriptionsReadClaimAsync(db);

        var email = $"presc-reader-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, assignedClinic.Id));
        await db.SaveChangesAsync();

        return (email, password, assignedClinic.Id, unassignedClinic.Id);
    }

    /// <summary>Belirtilen klinikte tek reçete kaydı oluşturur.</summary>
    public static async Task<Guid> SeedPrescriptionInClinicAsync(
        IServiceProvider services,
        Guid clinicId,
        TimeSpan? prescribedOffsetFromUtcNow = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);
        var client = new Client(clinic.TenantId, $"PrescClient-{Guid.NewGuid():N}"[..14], "905551110066");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(clinic.TenantId, client.Id, $"PrescPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        var prescribedAt = DateTime.UtcNow.Add(prescribedOffsetFromUtcNow ?? TimeSpan.FromHours(-2));
        var prescription = new Prescription(
            clinic.TenantId,
            clinic.Id,
            pet.Id,
            examinationId: null,
            treatmentId: null,
            prescribedAt,
            "IDOR Test Reçete",
            "Reçete içeriği",
            null,
            null);
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();

        return prescription.Id;
    }

    /// <summary>
    /// Tenant-wide olmayan kullanıcı: <c>Vaccinations.Read</c> iznine sahip, Admin / Owner / PlatformAdmin /
    /// ClinicAdmin claim'i yok. Yalnız atandığı kliniğin aşı detayını okuyabilmeli.
    /// </summary>
    public static async Task<(string Email, string Password, Guid AssignedClinicId, Guid UnassignedClinicId)>
        SeedVaccinationReaderUserAsync(IServiceProvider services, IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var assignedClinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var unassignedClinic = new Clinic(tenant.Id, $"VaccReader-{Guid.NewGuid():N}"[..14], "Konya");
        db.Clinics.Add(unassignedClinic);
        await db.SaveChangesAsync();

        var claim = await EnsureVaccinationsReadClaimAsync(db);

        var email = $"vacc-reader-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, assignedClinic.Id));
        await db.SaveChangesAsync();

        return (email, password, assignedClinic.Id, unassignedClinic.Id);
    }

    /// <summary>Belirtilen klinikte tek aşı kaydı oluşturur.</summary>
    public static async Task<Guid> SeedVaccinationInClinicAsync(
        IServiceProvider services,
        Guid clinicId,
        TimeSpan? appliedOffsetFromUtcNow = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);
        var client = new Client(clinic.TenantId, $"VaccClient-{Guid.NewGuid():N}"[..14], "905551110055");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(clinic.TenantId, client.Id, $"VaccPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        var vaccineDef = await db.VaccineDefinitions
            .OrderBy(v => v.Code)
            .Select(v => new { v.Id, v.Name })
            .FirstAsync();

        var appliedAt = DateTime.UtcNow.Add(appliedOffsetFromUtcNow ?? TimeSpan.FromHours(-2));
        var vaccination = new Vaccination(
            clinic.TenantId,
            pet.Id,
            clinic.Id,
            examinationId: null,
            vaccineDef.Id,
            vaccineDef.Name,
            VaccinationStatus.Applied,
            appliedAt,
            null,
            null);
        db.Vaccinations.Add(vaccination);
        await db.SaveChangesAsync();

        return vaccination.Id;
    }

    /// <summary>
    /// Tenant-wide olmayan kullanıcı: <c>Hospitalizations.Read</c> iznine sahip, Admin / Owner / PlatformAdmin /
    /// ClinicAdmin claim'i yok. Yalnız atandığı kliniğin yatış detayını okuyabilmeli.
    /// </summary>
    public static async Task<(string Email, string Password, Guid AssignedClinicId, Guid UnassignedClinicId)>
        SeedHospitalizationReaderUserAsync(IServiceProvider services, IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var assignedClinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var unassignedClinic = new Clinic(tenant.Id, $"HospReader-{Guid.NewGuid():N}"[..14], "Konya");
        db.Clinics.Add(unassignedClinic);
        await db.SaveChangesAsync();

        var claim = await EnsureHospitalizationsReadClaimAsync(db);

        var email = $"hosp-reader-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, assignedClinic.Id));
        await db.SaveChangesAsync();

        return (email, password, assignedClinic.Id, unassignedClinic.Id);
    }

    /// <summary>Belirtilen klinikte tek yatış kaydı oluşturur.</summary>
    public static async Task<Guid> SeedHospitalizationInClinicAsync(
        IServiceProvider services,
        Guid clinicId,
        TimeSpan? admittedOffsetFromUtcNow = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);
        var client = new Client(clinic.TenantId, $"HospClient-{Guid.NewGuid():N}"[..14], "905551110044");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(clinic.TenantId, client.Id, $"HospPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        var admittedAt = DateTime.UtcNow.Add(admittedOffsetFromUtcNow ?? TimeSpan.FromHours(-2));
        var hospitalization = new Hospitalization(
            clinic.TenantId,
            clinic.Id,
            pet.Id,
            examinationId: null,
            admittedAt,
            plannedDischargeAtUtc: admittedAt.AddDays(2),
            reason: "IDOR Test Yatış",
            notes: null);
        db.Hospitalizations.Add(hospitalization);
        await db.SaveChangesAsync();

        return hospitalization.Id;
    }

    /// <summary>
    /// Tenant-wide olmayan kullanıcı: <c>LabResults.Read</c> iznine sahip, Admin / Owner / PlatformAdmin /
    /// ClinicAdmin claim'i yok. Yalnız atandığı kliniğin lab result detayını okuyabilmeli.
    /// </summary>
    public static async Task<(string Email, string Password, Guid AssignedClinicId, Guid UnassignedClinicId)>
        SeedLabResultReaderUserAsync(IServiceProvider services, IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var assignedClinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var unassignedClinic = new Clinic(tenant.Id, $"LabReader-{Guid.NewGuid():N}"[..14], "Konya");
        db.Clinics.Add(unassignedClinic);
        await db.SaveChangesAsync();

        var claim = await EnsureLabResultsReadClaimAsync(db);

        var email = $"lab-reader-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, assignedClinic.Id));
        await db.SaveChangesAsync();

        return (email, password, assignedClinic.Id, unassignedClinic.Id);
    }

    /// <summary>Belirtilen klinikte tek lab result kaydı oluşturur.</summary>
    public static async Task<Guid> SeedLabResultInClinicAsync(
        IServiceProvider services,
        Guid clinicId,
        TimeSpan? resultOffsetFromUtcNow = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);
        var client = new Client(clinic.TenantId, $"LabClient-{Guid.NewGuid():N}"[..14], "905551110033");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(clinic.TenantId, client.Id, $"LabPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        var resultDate = DateTime.UtcNow.Add(resultOffsetFromUtcNow ?? TimeSpan.FromHours(-2));
        var labResult = new LabResult(
            clinic.TenantId,
            clinic.Id,
            pet.Id,
            examinationId: null,
            resultDate,
            testName: "IDOR Test CBC",
            resultText: "Normal",
            interpretation: null,
            notes: null);
        db.LabResults.Add(labResult);
        await db.SaveChangesAsync();

        return labResult.Id;
    }

    /// <summary>
    /// Tenant-wide olmayan kullanıcı: <c>Payments.Read</c> iznine sahip, Admin / Owner / PlatformAdmin /
    /// ClinicAdmin claim'i yok. Yalnız atandığı kliniğin payment detayını okuyabilmeli.
    /// </summary>
    public static async Task<(string Email, string Password, Guid AssignedClinicId, Guid UnassignedClinicId)>
        SeedPaymentReaderUserAsync(IServiceProvider services, IPasswordHasher hasher)
    {
        await EnsureRolePermissionBindingsAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var assignedClinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        var unassignedClinic = new Clinic(tenant.Id, $"PayReader-{Guid.NewGuid():N}"[..14], "Konya");
        db.Clinics.Add(unassignedClinic);
        await db.SaveChangesAsync();

        var claim = await EnsurePaymentsReadClaimAsync(db);

        var email = $"pay-reader-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, assignedClinic.Id));
        await db.SaveChangesAsync();

        return (email, password, assignedClinic.Id, unassignedClinic.Id);
    }

    /// <summary>Belirtilen klinikte tek payment kaydı oluşturur.</summary>
    public static async Task<Guid> SeedPaymentInClinicAsync(
        IServiceProvider services,
        Guid clinicId,
        TimeSpan? paidOffsetFromUtcNow = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);
        var client = new Client(clinic.TenantId, $"PayClient-{Guid.NewGuid():N}"[..14], "905551110022");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var paidAt = DateTime.UtcNow.Add(paidOffsetFromUtcNow ?? TimeSpan.FromHours(-2));
        var payment = new Payment(
            clinic.TenantId,
            clinic.Id,
            client.Id,
            petId: null,
            appointmentId: null,
            examinationId: null,
            amount: 150m,
            currency: "TRY",
            PaymentMethod.Cash,
            paidAt,
            notes: "IDOR Test Payment");
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        return payment.Id;
    }

    /// <summary>
    /// Operation claim'i olmayan düz tenant üyesi: hiçbir permission taşımaz, dolayısıyla
    /// <c>Clinics.Read</c> policy'si authorization katmanında 403 ile engellenir.
    /// </summary>
    public static async Task<(string Email, string Password)> SeedPlainTenantMemberAsync(
        IServiceProvider services,
        IPasswordHasher hasher)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);

        var email = $"plain-member-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        await db.SaveChangesAsync();

        return (email, password);
    }

    /// <summary>
    /// Başka bir tenant ve o tenant'a ait bir klinik oluşturur (cross-tenant IDOR senaryosu için).
    /// </summary>
    public static async Task<Guid> SeedClinicInForeignTenantAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var foreignTenant = new Tenant($"Foreign-{Guid.NewGuid():N}"[..16]);
        db.Tenants.Add(foreignTenant);
        await db.SaveChangesAsync();

        var foreignClinic = new Clinic(foreignTenant.Id, $"Foreign-{Guid.NewGuid():N}"[..14], "Adana");
        db.Clinics.Add(foreignClinic);
        await db.SaveChangesAsync();

        return foreignClinic.Id;
    }

    private static async Task<OperationClaim> EnsureClinicsReadClaimAsync(AppDbContext db)
    {
        const string claimName = "IntegrationClinicReader";
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            await db.SaveChangesAsync();
        }

        var code = PermissionCatalog.Clinics.Read;
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
        if (perm is null)
        {
            perm = new Permission(code, code, "Clinics");
            db.Permissions.Add(perm);
            await db.SaveChangesAsync();
        }

        var linked = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == claim.Id && x.PermissionId == perm.Id);
        if (!linked)
        {
            db.OperationClaimPermissions.Add(new OperationClaimPermission(claim.Id, perm.Id));
            await db.SaveChangesAsync();
        }

        return claim;
    }

    private static Task<OperationClaim> EnsureReadClaimForPermissionAsync(AppDbContext db, string permissionCode)
        => permissionCode switch
        {
            _ when permissionCode == PermissionCatalog.Appointments.Read => EnsureAppointmentsReadClaimAsync(db),
            _ when permissionCode == PermissionCatalog.Treatments.Read => EnsureTreatmentsReadClaimAsync(db),
            _ when permissionCode == PermissionCatalog.Vaccinations.Read => EnsureVaccinationsReadClaimAsync(db),
            _ when permissionCode == PermissionCatalog.Examinations.Read => EnsureExaminationsReadClaimAsync(db),
            _ when permissionCode == PermissionCatalog.Prescriptions.Read => EnsurePrescriptionsReadClaimAsync(db),
            _ when permissionCode == PermissionCatalog.Hospitalizations.Read => EnsureHospitalizationsReadClaimAsync(db),
            _ when permissionCode == PermissionCatalog.LabResults.Read => EnsureLabResultsReadClaimAsync(db),
            _ when permissionCode == PermissionCatalog.Payments.Read => EnsurePaymentsReadClaimAsync(db),
            _ => throw new ArgumentOutOfRangeException(
                nameof(permissionCode),
                permissionCode,
                "SeedScopedListReaderAndIssueTokenAsync için desteklenmeyen permission kodu.")
        };

    private static async Task<OperationClaim> EnsureAppointmentsReadClaimAsync(AppDbContext db)
    {
        const string claimName = "IntegrationAppointmentReader";
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            await db.SaveChangesAsync();
        }

        var code = PermissionCatalog.Appointments.Read;
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
        if (perm is null)
        {
            perm = new Permission(code, code, "Appointments");
            db.Permissions.Add(perm);
            await db.SaveChangesAsync();
        }

        var linked = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == claim.Id && x.PermissionId == perm.Id);
        if (!linked)
        {
            db.OperationClaimPermissions.Add(new OperationClaimPermission(claim.Id, perm.Id));
            await db.SaveChangesAsync();
        }

        return claim;
    }

    private static async Task<OperationClaim> EnsureExaminationsReadClaimAsync(AppDbContext db)
    {
        const string claimName = "IntegrationExaminationReader";
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            await db.SaveChangesAsync();
        }

        var code = PermissionCatalog.Examinations.Read;
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
        if (perm is null)
        {
            perm = new Permission(code, code, "Examinations");
            db.Permissions.Add(perm);
            await db.SaveChangesAsync();
        }

        var linked = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == claim.Id && x.PermissionId == perm.Id);
        if (!linked)
        {
            db.OperationClaimPermissions.Add(new OperationClaimPermission(claim.Id, perm.Id));
            await db.SaveChangesAsync();
        }

        return claim;
    }

    private static async Task<OperationClaim> EnsureTreatmentsReadClaimAsync(AppDbContext db)
    {
        const string claimName = "IntegrationTreatmentReader";
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            await db.SaveChangesAsync();
        }

        var code = PermissionCatalog.Treatments.Read;
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
        if (perm is null)
        {
            perm = new Permission(code, code, "Treatments");
            db.Permissions.Add(perm);
            await db.SaveChangesAsync();
        }

        var linked = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == claim.Id && x.PermissionId == perm.Id);
        if (!linked)
        {
            db.OperationClaimPermissions.Add(new OperationClaimPermission(claim.Id, perm.Id));
            await db.SaveChangesAsync();
        }

        return claim;
    }

    private static async Task<OperationClaim> EnsurePrescriptionsReadClaimAsync(AppDbContext db)
    {
        const string claimName = "IntegrationPrescriptionReader";
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            await db.SaveChangesAsync();
        }

        var code = PermissionCatalog.Prescriptions.Read;
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
        if (perm is null)
        {
            perm = new Permission(code, code, "Prescriptions");
            db.Permissions.Add(perm);
            await db.SaveChangesAsync();
        }

        var linked = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == claim.Id && x.PermissionId == perm.Id);
        if (!linked)
        {
            db.OperationClaimPermissions.Add(new OperationClaimPermission(claim.Id, perm.Id));
            await db.SaveChangesAsync();
        }

        return claim;
    }

    private static async Task<OperationClaim> EnsureVaccinationsReadClaimAsync(AppDbContext db)
    {
        const string claimName = "IntegrationVaccinationReader";
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            await db.SaveChangesAsync();
        }

        var code = PermissionCatalog.Vaccinations.Read;
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
        if (perm is null)
        {
            perm = new Permission(code, code, "Vaccinations");
            db.Permissions.Add(perm);
            await db.SaveChangesAsync();
        }

        var linked = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == claim.Id && x.PermissionId == perm.Id);
        if (!linked)
        {
            db.OperationClaimPermissions.Add(new OperationClaimPermission(claim.Id, perm.Id));
            await db.SaveChangesAsync();
        }

        return claim;
    }

    private static async Task<OperationClaim> EnsureHospitalizationsReadClaimAsync(AppDbContext db)
    {
        const string claimName = "IntegrationHospitalizationReader";
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            await db.SaveChangesAsync();
        }

        var code = PermissionCatalog.Hospitalizations.Read;
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
        if (perm is null)
        {
            perm = new Permission(code, code, "Hospitalizations");
            db.Permissions.Add(perm);
            await db.SaveChangesAsync();
        }

        var linked = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == claim.Id && x.PermissionId == perm.Id);
        if (!linked)
        {
            db.OperationClaimPermissions.Add(new OperationClaimPermission(claim.Id, perm.Id));
            await db.SaveChangesAsync();
        }

        return claim;
    }

    private static async Task<OperationClaim> EnsureLabResultsReadClaimAsync(AppDbContext db)
    {
        const string claimName = "IntegrationLabResultReader";
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            await db.SaveChangesAsync();
        }

        var code = PermissionCatalog.LabResults.Read;
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
        if (perm is null)
        {
            perm = new Permission(code, code, "LabResults");
            db.Permissions.Add(perm);
            await db.SaveChangesAsync();
        }

        var linked = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == claim.Id && x.PermissionId == perm.Id);
        if (!linked)
        {
            db.OperationClaimPermissions.Add(new OperationClaimPermission(claim.Id, perm.Id));
            await db.SaveChangesAsync();
        }

        return claim;
    }

    private static async Task<OperationClaim> EnsurePaymentsReadClaimAsync(AppDbContext db)
    {
        const string claimName = "IntegrationPaymentReader";
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            await db.SaveChangesAsync();
        }

        var code = PermissionCatalog.Payments.Read;
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
        if (perm is null)
        {
            perm = new Permission(code, code, "Payments");
            db.Permissions.Add(perm);
            await db.SaveChangesAsync();
        }

        var linked = await db.OperationClaimPermissions
            .AnyAsync(x => x.OperationClaimId == claim.Id && x.PermissionId == perm.Id);
        if (!linked)
        {
            db.OperationClaimPermissions.Add(new OperationClaimPermission(claim.Id, perm.Id));
            await db.SaveChangesAsync();
        }

        return claim;
    }

    /// <summary>
    /// Read-only tenant için JWT (Products smoke ile aynı desen).
    /// </summary>
    public static async Task<string> IssueReadOnlyTenantTokenAsync(
        IServiceProvider services,
        IJwtTokenService jwt,
        IReadOnlyCollection<string> permissions)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = new Tenant($"RO-{Guid.NewGuid():N}"[..16]);
        db.Tenants.Add(tenant);

        var now = DateTime.UtcNow;
        var subscription = TenantSubscription.StartTrial(
            tenant.Id,
            SubscriptionPlanCode.Basic,
            now.AddDays(-40),
            trialDays: 7);
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();

        var (accessToken, _, _) = jwt.Create(
            Guid.NewGuid(),
            $"ro-{Guid.NewGuid():N}@example.com",
            Array.Empty<string>(),
            claims);

        return accessToken;
    }
}
