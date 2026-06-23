using System.Net;
using System.Net.Http.Headers;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Authorization;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Appointments;

/// <summary>
/// GET /api/v1/appointments ve /calendar list/calendar IDOR erişim kontrolü (IDOR-3C).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AppointmentListCalendarIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AppointmentListCalendarIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetList_Should_Return200_When_NonTenantWideUserReadsAssignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedAppointmentReaderUserAsync(_factory.Services, hasher);
        await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(_factory.Services, assignedClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync(
            $"/api/v1/appointments?Page=1&PageSize=25&clinicId={assignedClinicId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetList_Should_Return403_When_NonTenantWideUserReadsUnassignedClinicInSameTenant()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedAppointmentReaderUserAsync(_factory.Services, hasher);
        await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(_factory.Services, unassignedClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync(
            $"/api/v1/appointments?Page=1&PageSize=25&clinicId={unassignedClinicId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var code = await IntegrationTestProblemDetails.ReadCodeAsync(response);
        code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task GetList_Should_Return403_When_NonTenantWideUserReadsForeignTenantClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, _) =
            await IntegrationTestAuthHelper.SeedAppointmentReaderUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync(
            $"/api/v1/appointments?Page=1&PageSize=25&clinicId={foreignClinicId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var code = await IntegrationTestProblemDetails.ReadCodeAsync(response);
        code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task GetList_Should_Return200_When_TenantAdminReadsOtherClinicInTenant()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(_factory.Services, extraClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync(
            $"/api/v1/appointments?Page=1&PageSize=25&clinicId={extraClinicId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCalendar_Should_Return200_When_NonTenantWideUserReadsAssignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedAppointmentReaderUserAsync(_factory.Services, hasher);
        await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(_factory.Services, assignedClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var from = DateTime.UtcNow.Date;
        var to = from.AddDays(7);
        var response = await client.GetAsync(
            $"/api/v1/appointments/calendar?dateFromUtc={from:O}&dateToUtc={to:O}&clinicId={assignedClinicId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCalendar_Should_Return403_When_NonTenantWideUserReadsUnassignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedAppointmentReaderUserAsync(_factory.Services, hasher);
        await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(_factory.Services, unassignedClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var from = DateTime.UtcNow.Date;
        var to = from.AddDays(7);
        var response = await client.GetAsync(
            $"/api/v1/appointments/calendar?dateFromUtc={from:O}&dateToUtc={to:O}&clinicId={unassignedClinicId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var code = await IntegrationTestProblemDetails.ReadCodeAsync(response);
        code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task GetList_Should_Return403_When_UserLacksAppointmentsReadPermission()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, clinicId) = await SeedUserWithoutAppointmentsReadAsync(hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync(
            $"/api/v1/appointments?Page=1&PageSize=25&clinicId={clinicId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(string Email, string Password, Guid ClinicId)> SeedUserWithoutAppointmentsReadAsync(
        IPasswordHasher hasher)
    {
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var clinic = await db.Clinics.SingleAsync(c =>
            c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);

        const string claimName = "IntegrationClientsReadOnly";
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

        var email = $"clients-only-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
        await db.SaveChangesAsync();

        return (email, password, clinic.Id);
    }
}
