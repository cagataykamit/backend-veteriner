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

namespace Backend.IntegrationTests.LabResults;

/// <summary>Lab result write clinic assignment IDOR (IDOR-7G).</summary>
[Collection("pilot-smoke-api")]
public sealed class LabResultWriteClinicAssignmentIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LabResultWriteClinicAssignmentIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static readonly string[] LabResultWritePermissions =
    [
        PermissionCatalog.LabResults.Create,
        PermissionCatalog.LabResults.Update,
    ];

    private static readonly DateTime ValidResultDateUtc = DateTime.UtcNow.AddHours(-2);

    private async Task<string> IssueLabResultWriteTokenAsync(string email, Guid? clinicId = null)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            LabResultWritePermissions,
            clinicId);

    [Fact]
    public async Task Create_Should_Return403_When_NonTenantWideUserCreatesInUnassignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedLabResultWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, unassignedClinicId);

        var token = await IssueLabResultWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/lab-results", new
        {
            ClinicId = unassignedClinicId,
            PetId = petId,
            ResultDateUtc = ValidResultDateUtc,
            TestName = "CBC",
            ResultText = "Normal",
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
            await IntegrationTestAuthHelper.SeedLabResultWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, assignedClinicId);

        var token = await IssueLabResultWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/lab-results", new
        {
            ClinicId = assignedClinicId,
            PetId = petId,
            ResultDateUtc = ValidResultDateUtc,
            TestName = "CBC",
            ResultText = "Normal",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_Should_Return403_And_NotPersist_When_UnassignedClinicExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedLabResultWriterUserAsync(_factory.Services, hasher);
        var exam = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var petId = await db.Examinations.AsNoTracking()
                .Where(e => e.Id == exam.ExaminationId)
                .Select(e => e.PetId)
                .SingleAsync();

            var token = await IssueLabResultWriteTokenAsync(email);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsJsonAsync("/api/v1/lab-results", new
            {
                ClinicId = unassignedClinicId,
                PetId = petId,
                ExaminationId = exam.ExaminationId,
                ResultDateUtc = ValidResultDateUtc,
                TestName = "CBC",
                ResultText = "Normal",
            });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await verifyDb.LabResults.CountAsync(lr =>
            lr.ExaminationId == exam.ExaminationId);
        count.Should().Be(0);
    }

    [Fact]
    public async Task Update_Should_Return403_And_NotMutate_When_UnassignedClinicLabResult()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedLabResultWriterUserAsync(_factory.Services, hasher);
        var labResultId = await IntegrationTestAuthHelper.SeedLabResultInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueLabResultWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (clinicId, petId, testName, resultDate) = await GetLabResultSnapshotAsync(labResultId);

        var response = await client.PutAsJsonAsync($"/api/v1/lab-results/{labResultId}", new
        {
            ClinicId = clinicId,
            PetId = petId,
            ResultDateUtc = resultDate,
            TestName = "Mutated",
            ResultText = "Changed",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        var (_, _, afterTestName, _) = await GetLabResultSnapshotAsync(labResultId);
        afterTestName.Should().Be(testName);
    }

    [Fact]
    public async Task Update_Should_Return403_And_NotPull_When_EntityInOtherClinic_WithActiveAssignedContext()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedLabResultWriterUserAsync(_factory.Services, hasher);
        var labResultId = await IntegrationTestAuthHelper.SeedLabResultInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueLabResultWriteTokenAsync(email, assignedClinicId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (_, petId, testName, resultDate) = await GetLabResultSnapshotAsync(labResultId);

        var response = await client.PutAsJsonAsync($"/api/v1/lab-results/{labResultId}", new
        {
            ClinicId = assignedClinicId,
            PetId = petId,
            ResultDateUtc = resultDate,
            TestName = "Pulled",
            ResultText = "Changed",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        var (afterClinicId, _, afterTestName, _) = await GetLabResultSnapshotAsync(labResultId);
        afterClinicId.Should().Be(unassignedClinicId);
        afterTestName.Should().Be(testName);
    }

    [Fact]
    public async Task CreateUpdate_Should_Succeed_When_TenantAdminInOtherClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, extraClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/lab-results", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            ResultDateUtc = ValidResultDateUtc,
            TestName = "Admin CBC",
            ResultText = "Normal",
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var labResultId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/lab-results/{labResultId}", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            ResultDateUtc = ValidResultDateUtc,
            TestName = "Admin update",
            ResultText = "Updated",
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_Should_Return404_When_ForeignTenantLabResult()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, _) =
            await IntegrationTestAuthHelper.SeedLabResultWriterUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var labResultId = await IntegrationTestAuthHelper.SeedLabResultInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var token = await IssueLabResultWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/lab-results/{labResultId}", new
        {
            ClinicId = Guid.NewGuid(),
            PetId = Guid.NewGuid(),
            ResultDateUtc = ValidResultDateUtc,
            TestName = "X",
            ResultText = "Y",
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(updateResponse)).Should().Be("LabResults.NotFound");
    }

    [Fact]
    public async Task Create_Should_Return403_When_UserHasNoLabResultCreatePermission()
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

        var response = await client.PostAsJsonAsync("/api/v1/lab-results", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            ResultDateUtc = ValidResultDateUtc,
            TestName = "CBC",
            ResultText = "Normal",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(Guid ClinicId, Guid PetId, string TestName, DateTime ResultDateUtc)> GetLabResultSnapshotAsync(
        Guid labResultId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.LabResults.AsNoTracking().SingleAsync(lr => lr.Id == labResultId);
        return (row.ClinicId, row.PetId, row.TestName, row.ResultDateUtc);
    }
}
