using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.OnlineAppointments;

/// <summary>
/// Online randevu testleri (ADR-042) için ortak bağlam: varsayılan kiracıda kendi kliniği, kliniğe atanmış ClinicAdmin
/// kullanıcısı ve izinli token. Tarihli testler İstanbul duvar saatiyle kurulur (CI UTC'dedir).
/// </summary>
internal sealed class OnlineAppointmentTestContext
{
    public static readonly string[] StaffPermissions =
    [
        PermissionCatalog.Clinics.Read,
        PermissionCatalog.Clinics.Update,
        PermissionCatalog.Appointments.Read,
        PermissionCatalog.Appointments.Create,
        PermissionCatalog.Appointments.Reschedule,
        PermissionCatalog.Appointments.Cancel,
        PermissionCatalog.AppointmentRequests.Manage,
        PermissionCatalog.Clients.Read,
        PermissionCatalog.Clients.Create,
        PermissionCatalog.Pets.Read,
        PermissionCatalog.Pets.Create,
        PermissionCatalog.Visits.Create,
    ];

    private static readonly TimeZoneInfo Istanbul = ResolveIstanbul();

    private readonly IServiceProvider _services;

    private OnlineAppointmentTestContext(IServiceProvider services, HttpClient http, Guid tenantId, Guid clinicId, Guid userId, string email)
    {
        _services = services;
        Http = http;
        TenantId = tenantId;
        ClinicId = clinicId;
        UserId = userId;
        Email = email;
    }

    public HttpClient Http { get; }
    public Guid TenantId { get; }
    public Guid ClinicId { get; }
    public Guid UserId { get; }
    public string Email { get; }

    public static async Task<OnlineAppointmentTestContext> CreateAsync(
        WebApplicationFactory<Program> factory,
        IReadOnlyCollection<string>? permissions = null)
    {
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(factory.Services);

        Guid tenantId, clinicId, userId;
        string email;
        await using (var scope = factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Online-{Guid.NewGuid():N}"[..15], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"online-staff-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            clinicId = clinic.Id;
            userId = user.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            factory.Services, email, permissions ?? StaffPermissions);
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new OnlineAppointmentTestContext(factory.Services, http, tenantId, clinicId, userId, email);
    }

    /// <summary>Aynı kullanıcı için farklı izinli istemci.</summary>
    public async Task<HttpClient> ClientWithPermissionsAsync(WebApplicationFactory<Program> factory, params string[] permissions)
    {
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_services, Email, permissions);
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    /// <summary>Verilen operasyon claim'li kullanıcı (ör. "Veteriner"); isteğe bağlı olarak kliniğe atanır.</summary>
    public async Task<Guid> SeedClinicUserAsync(string claimName, string? displayName, bool assignToClinic = true)
    {
        await using var scope = _services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var claim = await db.OperationClaims.SingleAsync(c => c.Name == claimName);
        var user = new User($"online-{claimName.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", hasher.Hash("123456"), DateTime.UtcNow);
        user.SetDisplayName(displayName, DateTime.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, TenantId, DateTime.UtcNow));
        if (assignToClinic)
            db.UserClinics.Add(new UserClinic(user.Id, ClinicId, DateTime.UtcNow));
        await db.SaveChangesAsync();
        return user.Id;
    }

    public async Task<(Guid ClientId, Guid PetId)> SeedClientWithPetAsync(string? email = null, string petName = "Pamuk")
    {
        await using var scope = _services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = new Client(TenantId, $"Sahip-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110088", email);
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(TenantId, client.Id, petName, speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();
        return (client.Id, pet.Id);
    }

    public async Task SetAllowOverlapAsync(bool allow)
    {
        var response = await Http.PutAsJsonAsync($"/api/v1/clinics/{ClinicId}/appointment-settings", new
        {
            defaultAppointmentDurationMinutes = 30,
            slotIntervalMinutes = 15,
            allowOverlappingAppointments = allow,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public async Task<JsonElement> CreateReasonAsync(
        string name,
        int durationMinutes = 20,
        bool online = true,
        IReadOnlyList<Guid>? veterinarianUserIds = null,
        int sortOrder = 0)
    {
        var response = await Http.PostAsJsonAsync($"/api/v1/clinics/{ClinicId}/appointment-reasons", new
        {
            name,
            defaultDurationMinutes = durationMinutes,
            isOnlineBookable = online,
            sortOrder,
            veterinarianUserIds = veterinarianUserIds ?? [],
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public Task<HttpResponseMessage> PostAppointmentAsync(
        Guid petId,
        DateTime scheduledAtUtc,
        Guid? reasonId = null,
        Guid? veterinarianUserId = null,
        int? durationMinutes = null)
        => Http.PostAsJsonAsync("/api/v1/appointments", new
        {
            clinicId = ClinicId,
            petId,
            scheduledAtUtc,
            appointmentType = 0,
            reasonId,
            veterinarianUserId,
            durationMinutes,
        });

    /// <summary>
    /// Bugünden en az <paramref name="minDaysAhead"/> gün sonraki ilk <paramref name="day"/> gününde İstanbul duvar saati
    /// <paramref name="hour"/>:<paramref name="minute"/>; UTC döner.
    /// </summary>
    public static DateTime NextIstanbul(DayOfWeek day, int hour, int minute = 0, int minDaysAhead = 2)
    {
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Istanbul).Date;
        var date = localToday.AddDays(minDaysAhead);
        while (date.DayOfWeek != day)
            date = date.AddDays(1);
        return IstanbulToUtc(date.AddHours(hour).AddMinutes(minute));
    }

    public static DateTime IstanbulToUtc(DateTime local)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Istanbul);

    public static DateOnly IstanbulDate(DateTime utc)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Istanbul));

    private static TimeZoneInfo ResolveIstanbul()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }
}
