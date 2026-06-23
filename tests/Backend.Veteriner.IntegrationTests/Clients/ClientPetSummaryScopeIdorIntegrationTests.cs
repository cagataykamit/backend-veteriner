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
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Clients;

/// <summary>
/// Client/Pet child summary clinic scope IDOR kontrolü (IDOR-4B.3).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ClientPetSummaryScopeIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ClientPetSummaryScopeIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetPaymentSummary_Should_ReturnOnlyAssignedClinicPayments_When_ClinicAdminHasNoActiveClinicContext()
    {
        await ResetChildSummaryDataAsync(_factory.Services);
        var (clientId, _) = await EnsureClientWithPetAsync(_factory.Services);

        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);

        await SeedPaymentForClientAsync(_factory.Services, assignedClinicId, clientId, 100m);
        await SeedPaymentForClientAsync(_factory.Services, unassignedClinicId, clientId, 200m);

        var http = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(http, _factory.Services, email, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await http.GetAsync($"/api/v1/clients/{clientId:D}/payment-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalPaymentsCount").GetInt32().Should().Be(1);
        json.GetProperty("recentPayments").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("amount").GetDecimal().Should().Be(100m);
    }

    [Fact]
    public async Task GetRecentSummary_Should_ReturnOnlyAssignedClinicRecords_When_ClinicAdminHasNoActiveClinicContext()
    {
        await ResetChildSummaryDataAsync(_factory.Services);
        var (clientId, petId) = await EnsureClientWithPetAsync(_factory.Services);

        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);

        await SeedAppointmentForPetAsync(_factory.Services, assignedClinicId, petId);
        await SeedAppointmentForPetAsync(_factory.Services, unassignedClinicId, petId);
        await SeedExaminationForPetAsync(_factory.Services, assignedClinicId, petId);
        await SeedExaminationForPetAsync(_factory.Services, unassignedClinicId, petId);

        var http = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(http, _factory.Services, email, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await http.GetAsync($"/api/v1/clients/{clientId:D}/recent-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("recentAppointments").EnumerateArray().Should().HaveCount(1);
        json.GetProperty("recentExaminations").EnumerateArray().Should().HaveCount(1);
    }

    [Fact]
    public async Task GetHistorySummary_Should_ReturnOnlyAssignedClinicHistory_When_ClinicAdminHasNoActiveClinicContext()
    {
        await ResetChildSummaryDataAsync(_factory.Services);
        var (clientId, petId) = await EnsureClientWithPetAsync(_factory.Services);

        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);

        await SeedAppointmentForPetAsync(_factory.Services, assignedClinicId, petId);
        await SeedAppointmentForPetAsync(_factory.Services, unassignedClinicId, petId);
        await SeedPaymentForClientAsync(_factory.Services, assignedClinicId, clientId, 100m, petId);
        await SeedPaymentForClientAsync(_factory.Services, unassignedClinicId, clientId, 200m, petId);

        var http = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(http, _factory.Services, email, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await http.GetAsync($"/api/v1/pets/{petId:D}/history-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("recentAppointments").EnumerateArray().Should().HaveCount(1);
        json.GetProperty("recentPayments").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("amount").GetDecimal().Should().Be(100m);
    }

    [Fact]
    public async Task GetPaymentSummary_Should_ReturnTenantWideData_When_TenantAdminHasNoActiveClinicContext()
    {
        await ResetChildSummaryDataAsync(_factory.Services);
        var (clientId, _) = await EnsureClientWithPetAsync(_factory.Services);

        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);

        Guid defaultClinicId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            defaultClinicId = await db.Clinics
                .Where(c => c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName)
                .Select(c => c.Id)
                .SingleAsync();
        }

        await SeedPaymentForClientAsync(_factory.Services, defaultClinicId, clientId, 100m);
        await SeedPaymentForClientAsync(_factory.Services, extraClinicId, clientId, 200m);

        var http = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(http, _factory.Services, email, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await http.GetAsync($"/api/v1/clients/{clientId:D}/payment-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalPaymentsCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetPaymentSummary_Should_ReturnEmpty_When_NonTenantWideUserHasNoClinicAssignments()
    {
        await ResetChildSummaryDataAsync(_factory.Services);
        var (clientId, _) = await EnsureClientWithPetAsync(_factory.Services);

        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password) = await SeedClientsReaderWithoutClinicAssignmentAsync(hasher);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = await db.Clinics.SingleAsync(c =>
                c.TenantId == tenant.Id && c.Name == DataSeeder.DefaultSeedClinicName);
            await SeedPaymentForClientAsync(_factory.Services, clinic.Id, clientId, 150m);
        }

        var http = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(http, _factory.Services, email, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await http.GetAsync($"/api/v1/clients/{clientId:D}/payment-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalPaymentsCount").GetInt32().Should().Be(0);
        json.GetProperty("recentPayments").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task GetPaymentSummary_Should_Return403_When_UserLacksClientsReadPermission()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password) = await IntegrationTestAuthHelper.SeedPlainTenantMemberAsync(_factory.Services, hasher);

        var http = _factory.CreateClient();
        var login = await IntegrationTestAuthHelper.LoginAsync(http, _factory.Services, email, password);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await http.GetAsync($"/api/v1/clients/{Guid.NewGuid():D}/payment-summary");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task ResetChildSummaryDataAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        await db.Payments.Where(p => p.TenantId == tenant.Id).ExecuteDeleteAsync();
        await db.Appointments.Where(a => a.TenantId == tenant.Id).ExecuteDeleteAsync();
        await db.Examinations.Where(e => e.TenantId == tenant.Id).ExecuteDeleteAsync();
    }

    private static async Task<(Guid ClientId, Guid PetId)> EnsureClientWithPetAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);

        var client = new Client(tenant.Id, $"ScopeClient-{Guid.NewGuid():N}"[..14], "905551117766");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, $"ScopePet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        return (client.Id, pet.Id);
    }

    private static async Task SeedPaymentForClientAsync(
        IServiceProvider services,
        Guid clinicId,
        Guid clientId,
        decimal amount,
        Guid? petId = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);

        var (dayStart, dayEnd) = OperationDayBounds.ForUtcNow(DateTime.UtcNow);
        var paidAt = dayStart.AddHours(2);
        if (paidAt >= dayEnd)
            paidAt = dayStart.AddMinutes(30);

        db.Payments.Add(new Payment(
            clinic.TenantId,
            clinic.Id,
            clientId,
            petId,
            null,
            null,
            amount,
            "TRY",
            PaymentMethod.Cash,
            paidAt,
            "scope test"));
        await db.SaveChangesAsync();
    }

    private static async Task SeedAppointmentForPetAsync(IServiceProvider services, Guid clinicId, Guid petId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);

        db.Appointments.Add(new Appointment(
            clinic.TenantId,
            clinic.Id,
            petId,
            DateTime.UtcNow.AddDays(1),
            30,
            AppointmentType.Consultation,
            AppointmentStatus.Scheduled));
        await db.SaveChangesAsync();
    }

    private static async Task SeedExaminationForPetAsync(IServiceProvider services, Guid clinicId, Guid petId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);

        db.Examinations.Add(new Examination(
            clinic.TenantId,
            clinic.Id,
            petId,
            null,
            DateTime.UtcNow.AddHours(-1),
            "Kontrol",
            "Bulgu",
            null,
            null));
        await db.SaveChangesAsync();
    }

    private async Task<(string Email, string Password)> SeedClientsReaderWithoutClinicAssignmentAsync(IPasswordHasher hasher)
    {
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);

        const string claimName = "IntegrationClientsReadNoClinic";
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

        var email = $"clients-no-clinic-{Guid.NewGuid():N}@example.com";
        const string password = "123456";
        var user = new User(email, hasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        await db.SaveChangesAsync();

        return (email, password);
    }
}
