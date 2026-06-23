using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Hospitalizations;

/// <summary>Hospitalization write clinic assignment IDOR (IDOR-7F).</summary>
[Collection("pilot-smoke-api")]
public sealed class HospitalizationWriteClinicAssignmentIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HospitalizationWriteClinicAssignmentIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static readonly string[] HospitalizationWritePermissions =
    [
        PermissionCatalog.Hospitalizations.Create,
        PermissionCatalog.Hospitalizations.Update,
        PermissionCatalog.Hospitalizations.Discharge,
    ];

    private static readonly DateTime ValidAdmittedAtUtc = DateTime.UtcNow.AddHours(-2);

    private async Task<string> IssueHospitalizationWriteTokenAsync(string email, Guid? clinicId = null)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            HospitalizationWritePermissions,
            clinicId);

    [Fact]
    public async Task Create_Should_Return403_When_NonTenantWideUserCreatesInUnassignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedHospitalizationWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, unassignedClinicId);

        var token = await IssueHospitalizationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/hospitalizations", new
        {
            ClinicId = unassignedClinicId,
            PetId = petId,
            AdmittedAtUtc = ValidAdmittedAtUtc,
            Reason = "Yatış",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Create_Should_Return201_When_NonTenantWideUserCreatesInAssignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedHospitalizationWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, assignedClinicId);

        var token = await IssueHospitalizationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/hospitalizations", new
        {
            ClinicId = assignedClinicId,
            PetId = petId,
            AdmittedAtUtc = ValidAdmittedAtUtc,
            Reason = "Yatış",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Update_Should_Return403_And_NotMutate_When_UnassignedClinicHospitalization()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedHospitalizationWriterUserAsync(_factory.Services, hasher);
        var hospitalizationId = await IntegrationTestAuthHelper.SeedHospitalizationInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueHospitalizationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (clinicId, petId, reason, admittedAt) = await GetHospitalizationSnapshotAsync(hospitalizationId);

        var response = await client.PutAsJsonAsync($"/api/v1/hospitalizations/{hospitalizationId}", new
        {
            ClinicId = clinicId,
            PetId = petId,
            AdmittedAtUtc = admittedAt,
            Reason = "Mutated",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        var (_, _, afterReason, _) = await GetHospitalizationSnapshotAsync(hospitalizationId);
        afterReason.Should().Be(reason);
    }

    [Fact]
    public async Task Discharge_Should_Return403_And_NotMutate_When_UnassignedClinicHospitalization()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedHospitalizationWriterUserAsync(_factory.Services, hasher);
        var hospitalizationId = await IntegrationTestAuthHelper.SeedHospitalizationInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueHospitalizationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (_, _, _, admittedAt) = await GetHospitalizationSnapshotAsync(hospitalizationId);

        var response = await client.PostAsJsonAsync($"/api/v1/hospitalizations/{hospitalizationId}/discharge", new
        {
            DischargedAtUtc = admittedAt.AddHours(1),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Hospitalizations.AsNoTracking().SingleAsync(h => h.Id == hospitalizationId);
        row.DischargedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Update_Should_Return403_And_NotPull_When_EntityInOtherClinic_WithActiveAssignedContext()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedHospitalizationWriterUserAsync(_factory.Services, hasher);
        var hospitalizationId = await IntegrationTestAuthHelper.SeedHospitalizationInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueHospitalizationWriteTokenAsync(email, assignedClinicId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (_, petId, reason, admittedAt) = await GetHospitalizationSnapshotAsync(hospitalizationId);

        var response = await client.PutAsJsonAsync($"/api/v1/hospitalizations/{hospitalizationId}", new
        {
            ClinicId = assignedClinicId,
            PetId = petId,
            AdmittedAtUtc = admittedAt,
            Reason = "Pulled",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        var (afterClinicId, _, afterReason, _) = await GetHospitalizationSnapshotAsync(hospitalizationId);
        afterClinicId.Should().Be(unassignedClinicId);
        afterReason.Should().Be(reason);
    }

    [Fact]
    public async Task CreateUpdateDischarge_Should_Succeed_When_TenantAdminInOtherClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, extraClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/hospitalizations", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            AdmittedAtUtc = ValidAdmittedAtUtc,
            Reason = "Admin create",
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var hospitalizationId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/hospitalizations/{hospitalizationId}", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            AdmittedAtUtc = ValidAdmittedAtUtc,
            Reason = "Admin update",
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var dischargeResponse = await client.PostAsJsonAsync($"/api/v1/hospitalizations/{hospitalizationId}/discharge", new
        {
            DischargedAtUtc = ValidAdmittedAtUtc.AddHours(4),
        });
        dischargeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateAndDischarge_Should_Return404_When_ForeignTenantHospitalization()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, _) =
            await IntegrationTestAuthHelper.SeedHospitalizationWriterUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var hospitalizationId = await IntegrationTestAuthHelper.SeedHospitalizationInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var token = await IssueHospitalizationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/hospitalizations/{hospitalizationId}", new
        {
            ClinicId = Guid.NewGuid(),
            PetId = Guid.NewGuid(),
            AdmittedAtUtc = ValidAdmittedAtUtc,
            Reason = "X",
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(updateResponse)).Should().Be("Hospitalizations.NotFound");

        var dischargeResponse = await client.PostAsJsonAsync($"/api/v1/hospitalizations/{hospitalizationId}/discharge", new
        {
            DischargedAtUtc = ValidAdmittedAtUtc.AddHours(1),
        });
        dischargeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(dischargeResponse)).Should().Be("Hospitalizations.NotFound");
    }

    [Fact]
    public async Task Create_Should_Return403_When_UserHasNoHospitalizationCreatePermission()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (_, _, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, extraClinicId);

        var (plainEmail, plainPassword) =
            await IntegrationTestAuthHelper.SeedPlainTenantMemberAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, plainEmail, plainPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/hospitalizations", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            AdmittedAtUtc = ValidAdmittedAtUtc,
            Reason = "Yatış",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(Guid ClinicId, Guid PetId, string Reason, DateTime AdmittedAtUtc)> GetHospitalizationSnapshotAsync(
        Guid hospitalizationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Hospitalizations.AsNoTracking().SingleAsync(h => h.Id == hospitalizationId);
        return (row.ClinicId, row.PetId, row.Reason, row.AdmittedAtUtc);
    }
}
