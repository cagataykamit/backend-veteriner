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

namespace Backend.IntegrationTests.Treatments;

/// <summary>Treatment write clinic assignment IDOR (IDOR-7C).</summary>
[Collection("pilot-smoke-api")]
public sealed class TreatmentWriteClinicAssignmentIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TreatmentWriteClinicAssignmentIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static readonly string[] TreatmentWritePermissions =
    [
        PermissionCatalog.Treatments.Create,
        PermissionCatalog.Treatments.Update,
    ];

    private static readonly DateTime ValidTreatmentAtUtc = DateTime.UtcNow.AddHours(-2);

    private async Task<string> IssueTreatmentWriteTokenAsync(string email)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            TreatmentWritePermissions);

    [Fact]
    public async Task Create_Should_Return403_When_NonTenantWideUserCreatesInUnassignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedTreatmentWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, unassignedClinicId);

        var token = await IssueTreatmentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/treatments", new
        {
            ClinicId = unassignedClinicId,
            PetId = petId,
            TreatmentDateUtc = ValidTreatmentAtUtc,
            Title = "Tedavi",
            Description = "Açıklama",
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
            await IntegrationTestAuthHelper.SeedTreatmentWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, assignedClinicId);

        var token = await IssueTreatmentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/treatments", new
        {
            ClinicId = assignedClinicId,
            PetId = petId,
            TreatmentDateUtc = ValidTreatmentAtUtc,
            Title = "Tedavi",
            Description = "Açıklama",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_Should_Return403_And_NotPersist_When_UnassignedClinicExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedTreatmentWriterUserAsync(_factory.Services, hasher);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exam = await db.Examinations.AsNoTracking().SingleAsync(e => e.Id == seed.ExaminationId);
            var treatmentCountBefore = await db.Treatments.CountAsync();

            var token = await IssueTreatmentWriteTokenAsync(email);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsJsonAsync("/api/v1/treatments", new
            {
                ClinicId = exam.ClinicId,
                PetId = exam.PetId,
                ExaminationId = exam.Id,
                TreatmentDateUtc = ValidTreatmentAtUtc,
                Title = "Tedavi",
                Description = "Açıklama",
            });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

            var treatmentCountAfter = await db.Treatments.CountAsync();
            treatmentCountAfter.Should().Be(treatmentCountBefore);
        }
    }

    [Fact]
    public async Task Update_Should_Return403_And_NotMutate_When_UnassignedClinicTreatment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedTreatmentWriterUserAsync(_factory.Services, hasher);
        var treatmentId = await IntegrationTestAuthHelper.SeedTreatmentInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueTreatmentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (clinicId, petId, title) = await GetTreatmentSnapshotAsync(treatmentId);

        var response = await client.PutAsJsonAsync($"/api/v1/treatments/{treatmentId}", new
        {
            ClinicId = clinicId,
            PetId = petId,
            TreatmentDateUtc = ValidTreatmentAtUtc,
            Title = "Mutated",
            Description = "Açıklama",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        var (_, _, afterTitle) = await GetTreatmentSnapshotAsync(treatmentId);
        afterTitle.Should().Be(title);
    }

    [Fact]
    public async Task CreateAndUpdate_Should_Succeed_When_TenantAdminInOtherClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, extraClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/treatments", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            TreatmentDateUtc = ValidTreatmentAtUtc,
            Title = "Admin create",
            Description = "Açıklama",
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var treatmentId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/treatments/{treatmentId}", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            TreatmentDateUtc = ValidTreatmentAtUtc,
            Title = "Admin update",
            Description = "Açıklama",
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_Should_Return404_When_ForeignTenantTreatment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, _) =
            await IntegrationTestAuthHelper.SeedTreatmentWriterUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var treatmentId = await IntegrationTestAuthHelper.SeedTreatmentInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var token = await IssueTreatmentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync($"/api/v1/treatments/{treatmentId}", new
        {
            ClinicId = Guid.NewGuid(),
            PetId = Guid.NewGuid(),
            TreatmentDateUtc = ValidTreatmentAtUtc,
            Title = "X",
            Description = "Açıklama",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Treatments.NotFound");
    }

    [Fact]
    public async Task Create_Should_Return403_When_UserHasNoTreatmentCreatePermission()
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

        var response = await client.PostAsJsonAsync("/api/v1/treatments", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            TreatmentDateUtc = ValidTreatmentAtUtc,
            Title = "Tedavi",
            Description = "Açıklama",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(Guid ClinicId, Guid PetId, string Title)> GetTreatmentSnapshotAsync(Guid treatmentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Treatments.AsNoTracking().SingleAsync(t => t.Id == treatmentId);
        return (row.ClinicId, row.PetId, row.Title);
    }
}
