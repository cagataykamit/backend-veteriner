using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Authorization;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Dashboard;

/// <summary>
/// GET /api/v1/dashboard/summary ve /operational-alerts clinic scope IDOR kontrolü (IDOR-4B.1).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class DashboardScopeIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DashboardScopeIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetSummary_Should_ReturnAssignedClinicAggregate_When_ClinicAdminHasNoActiveClinicContext()
    {
        var http = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);

        await SeedTodayAppointmentAsync(_factory.Services, assignedClinicId, AppointmentStatus.Scheduled);
        await SeedTodayAppointmentAsync(_factory.Services, unassignedClinicId, AppointmentStatus.Scheduled);

        var login = await IntegrationTestAuthHelper.LoginAsync(http, _factory.Services, email, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await http.GetAsync("/api/v1/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("todayAppointmentsCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetOperationalAlerts_Should_ReturnAssignedClinicAggregate_When_ClinicAdminHasNoActiveClinicContext()
    {
        var http = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);

        await SeedTodayAppointmentAsync(_factory.Services, assignedClinicId, AppointmentStatus.Cancelled);
        await SeedTodayAppointmentAsync(_factory.Services, unassignedClinicId, AppointmentStatus.Cancelled);

        var login = await IntegrationTestAuthHelper.LoginAsync(http, _factory.Services, email, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await http.GetAsync("/api/v1/dashboard/operational-alerts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("todayCancelledAppointmentsCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_Should_ReturnTenantWideAggregate_When_TenantAdminHasNoActiveClinicContext()
    {
        var http = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var defaultClinic = await db.Clinics.SingleAsync(c =>
                c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

            await SeedTodayAppointmentAsync(_factory.Services, defaultClinic.Id, AppointmentStatus.Scheduled);
        }

        await SeedTodayAppointmentAsync(_factory.Services, extraClinicId, AppointmentStatus.Scheduled);

        var login = await IntegrationTestAuthHelper.LoginAsync(http, _factory.Services, email, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await http.GetAsync("/api/v1/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("todayAppointmentsCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetSummary_Should_ReturnEmptyAggregate_When_NonTenantWideUserHasNoClinicAssignments()
    {
        var http = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password) = await SeedDashboardReaderWithoutClinicAssignmentAsync(hasher);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = await db.Clinics.SingleAsync(c =>
                c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);
            await SeedTodayAppointmentAsync(_factory.Services, clinic.Id, AppointmentStatus.Scheduled);
        }

        var login = await IntegrationTestAuthHelper.LoginAsync(http, _factory.Services, email, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await http.GetAsync("/api/v1/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("todayAppointmentsCount").GetInt32().Should().Be(0);
        json.GetProperty("upcomingAppointmentsCount").GetInt32().Should().Be(0);
        json.GetProperty("totalClientsCount").GetInt32().Should().Be(0);
        json.GetProperty("totalPetsCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetSummary_Should_Return403_When_UserLacksDashboardReadPermission()
    {
        var http = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password) = await SeedUserWithoutDashboardReadAsync(hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(http, _factory.Services, email, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await http.GetAsync("/api/v1/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task SeedTodayAppointmentAsync(
        IServiceProvider services,
        Guid clinicId,
        AppointmentStatus status)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);
        var client = new Client(clinic.TenantId, $"DashScope-{Guid.NewGuid():N}"[..14], "905551119988");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(clinic.TenantId, client.Id, $"DashPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        var (dayStart, dayEnd) = OperationDayBounds.ForUtcNow(DateTime.UtcNow);
        var scheduledAt = dayStart.AddHours(2);
        if (scheduledAt >= dayEnd)
            scheduledAt = dayStart.AddMinutes(30);

        db.Appointments.Add(new Appointment(
            clinic.TenantId,
            clinic.Id,
            pet.Id,
            scheduledAt,
            30,
            AppointmentType.Consultation,
            status));
        await db.SaveChangesAsync();
    }

    private async Task<(string Email, string Password)> SeedDashboardReaderWithoutClinicAssignmentAsync(
        IPasswordHasher hasher)
    {
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);

        const string claimName = "IntegrationDashboardReadOnly";
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            await db.SaveChangesAsync();
        }

        var permCode = PermissionCatalog.Dashboard.Read;
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == permCode);
        if (perm is null)
        {
            perm = new Permission(permCode, permCode, "Dashboard");
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

        var email = $"dash-no-clinic-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        await db.SaveChangesAsync();

        return (email, password);
    }

    private async Task<(string Email, string Password)> SeedUserWithoutDashboardReadAsync(IPasswordHasher hasher)
    {
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var clinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        const string claimName = "IntegrationClientsReadOnlyDash403";
        var claim = await db.OperationClaims.FirstOrDefaultAsync(c => c.Name == claimName);
        if (claim is null)
        {
            claim = new OperationClaim(claimName);
            db.OperationClaims.Add(claim);
            await db.SaveChangesAsync();
        }

        var permCode = PermissionCatalog.Clients.Read;
        var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == permCode);
        if (perm is null)
        {
            perm = new Permission(permCode, permCode, "Clients");
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

        var email = $"clients-only-dash-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
        await db.SaveChangesAsync();

        return (email, password);
    }
}
