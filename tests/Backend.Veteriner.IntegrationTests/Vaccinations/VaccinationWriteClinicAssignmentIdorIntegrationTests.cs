using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Vaccinations;

/// <summary>Vaccination write clinic assignment IDOR (IDOR-7E).</summary>
[Collection("pilot-smoke-api")]
public sealed class VaccinationWriteClinicAssignmentIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public VaccinationWriteClinicAssignmentIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static readonly string[] VaccinationWritePermissions =
    [
        PermissionCatalog.Vaccinations.Create,
        PermissionCatalog.Vaccinations.Update,
    ];

    private static readonly DateTime ValidDueAtUtc = DateTime.UtcNow.AddDays(7);

    private async Task<string> IssueVaccinationWriteTokenAsync(string email, Guid? clinicId = null)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            VaccinationWritePermissions,
            clinicId);

    [Fact]
    public async Task Create_Should_Return403_When_NonTenantWideUserCreatesInUnassignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedVaccinationWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, unassignedClinicId);
        var vaccineDefId = await GetFirstVaccineDefinitionIdAsync();

        var token = await IssueVaccinationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/vaccinations", new
        {
            ClinicId = unassignedClinicId,
            PetId = petId,
            VaccineDefinitionId = vaccineDefId,
            Status = VaccinationStatus.Scheduled,
            DueAtUtc = ValidDueAtUtc,
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
            await IntegrationTestAuthHelper.SeedVaccinationWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, assignedClinicId);
        var vaccineDefId = await GetFirstVaccineDefinitionIdAsync();

        var token = await IssueVaccinationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/vaccinations", new
        {
            ClinicId = assignedClinicId,
            PetId = petId,
            VaccineDefinitionId = vaccineDefId,
            Status = VaccinationStatus.Scheduled,
            DueAtUtc = ValidDueAtUtc,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Update_Should_Return403_And_NotMutate_When_UnassignedClinicVaccination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedVaccinationWriterUserAsync(_factory.Services, hasher);
        var vaccinationId = await IntegrationTestAuthHelper.SeedVaccinationInClinicAsync(
            _factory.Services,
            unassignedClinicId);
        var vaccineDefId = await GetFirstVaccineDefinitionIdAsync();

        var token = await IssueVaccinationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (clinicId, petId, notes) = await GetVaccinationSnapshotAsync(vaccinationId);

        var response = await client.PutAsJsonAsync($"/api/v1/vaccinations/{vaccinationId}", new
        {
            Id = vaccinationId,
            ClinicId = clinicId,
            PetId = petId,
            VaccineDefinitionId = vaccineDefId,
            Status = VaccinationStatus.Scheduled,
            DueAtUtc = ValidDueAtUtc,
            Notes = "Mutated",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        var (_, _, afterNotes) = await GetVaccinationSnapshotAsync(vaccinationId);
        afterNotes.Should().Be(notes);
    }

    [Fact]
    public async Task Update_Should_Return403_And_NotPull_When_EntityInOtherClinic_WithActiveAssignedContext()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedVaccinationWriterUserAsync(_factory.Services, hasher);
        var vaccinationId = await IntegrationTestAuthHelper.SeedVaccinationInClinicAsync(
            _factory.Services,
            unassignedClinicId);
        var vaccineDefId = await GetFirstVaccineDefinitionIdAsync();

        var token = await IssueVaccinationWriteTokenAsync(email, assignedClinicId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (_, petId, notes) = await GetVaccinationSnapshotAsync(vaccinationId);

        var response = await client.PutAsJsonAsync($"/api/v1/vaccinations/{vaccinationId}", new
        {
            Id = vaccinationId,
            ClinicId = assignedClinicId,
            PetId = petId,
            VaccineDefinitionId = vaccineDefId,
            Status = VaccinationStatus.Scheduled,
            DueAtUtc = ValidDueAtUtc,
            Notes = "Pulled",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        var (afterClinicId, _, afterNotes) = await GetVaccinationSnapshotAsync(vaccinationId);
        afterClinicId.Should().Be(unassignedClinicId);
        afterNotes.Should().Be(notes);
    }

    [Fact]
    public async Task CreateAndUpdate_Should_Succeed_When_TenantAdminInOtherClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, extraClinicId);
        var vaccineDefId = await GetFirstVaccineDefinitionIdAsync();

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/vaccinations", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            VaccineDefinitionId = vaccineDefId,
            Status = VaccinationStatus.Scheduled,
            DueAtUtc = ValidDueAtUtc,
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var vaccinationId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/vaccinations/{vaccinationId}", new
        {
            Id = vaccinationId,
            ClinicId = extraClinicId,
            PetId = petId,
            VaccineDefinitionId = vaccineDefId,
            Status = VaccinationStatus.Scheduled,
            DueAtUtc = ValidDueAtUtc.AddDays(1),
            Notes = "Admin update",
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_Should_Return404_When_ForeignTenantVaccination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, _) =
            await IntegrationTestAuthHelper.SeedVaccinationWriterUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var vaccinationId = await IntegrationTestAuthHelper.SeedVaccinationInClinicAsync(
            _factory.Services,
            foreignClinicId);
        var vaccineDefId = await GetFirstVaccineDefinitionIdAsync();

        var token = await IssueVaccinationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync($"/api/v1/vaccinations/{vaccinationId}", new
        {
            Id = vaccinationId,
            ClinicId = Guid.NewGuid(),
            PetId = Guid.NewGuid(),
            VaccineDefinitionId = vaccineDefId,
            Status = VaccinationStatus.Scheduled,
            DueAtUtc = ValidDueAtUtc,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Vaccinations.NotFound");
    }

    [Fact]
    public async Task Create_Should_Return403_When_UserHasNoVaccinationCreatePermission()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (_, _, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, extraClinicId);
        var vaccineDefId = await GetFirstVaccineDefinitionIdAsync();

        var (plainEmail, plainPassword) =
            await IntegrationTestAuthHelper.SeedPlainTenantMemberAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, plainEmail, plainPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/vaccinations", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            VaccineDefinitionId = vaccineDefId,
            Status = VaccinationStatus.Scheduled,
            DueAtUtc = ValidDueAtUtc,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> GetFirstVaccineDefinitionIdAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.VaccineDefinitions.OrderBy(v => v.Code).Select(v => v.Id).FirstAsync();
    }

    private async Task<(Guid ClinicId, Guid PetId, string? Notes)> GetVaccinationSnapshotAsync(Guid vaccinationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Vaccinations.AsNoTracking().SingleAsync(v => v.Id == vaccinationId);
        return (row.ClinicId, row.PetId, row.Notes);
    }
}
