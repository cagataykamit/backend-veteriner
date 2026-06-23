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

namespace Backend.IntegrationTests.Prescriptions;

/// <summary>Prescription write clinic assignment IDOR (IDOR-7D).</summary>
[Collection("pilot-smoke-api")]
public sealed class PrescriptionWriteClinicAssignmentIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PrescriptionWriteClinicAssignmentIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static readonly string[] PrescriptionWritePermissions =
    [
        PermissionCatalog.Prescriptions.Create,
        PermissionCatalog.Prescriptions.Update,
    ];

    private static readonly DateTime ValidPrescribedAtUtc = DateTime.UtcNow.AddHours(-2);

    private async Task<string> IssuePrescriptionWriteTokenAsync(string email)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            PrescriptionWritePermissions);

    [Fact]
    public async Task Create_Should_Return403_When_NonTenantWideUserCreatesInUnassignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedPrescriptionWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, unassignedClinicId);

        var token = await IssuePrescriptionWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/prescriptions", new
        {
            ClinicId = unassignedClinicId,
            PetId = petId,
            PrescribedAtUtc = ValidPrescribedAtUtc,
            Title = "Reçete",
            Content = "İçerik",
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
            await IntegrationTestAuthHelper.SeedPrescriptionWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, assignedClinicId);

        var token = await IssuePrescriptionWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/prescriptions", new
        {
            ClinicId = assignedClinicId,
            PetId = petId,
            PrescribedAtUtc = ValidPrescribedAtUtc,
            Title = "Reçete",
            Content = "İçerik",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_Should_Return403_And_NotPersist_When_UnassignedClinicTreatment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedPrescriptionWriterUserAsync(_factory.Services, hasher);
        var treatmentId = await IntegrationTestAuthHelper.SeedTreatmentInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var treatment = await db.Treatments.AsNoTracking().SingleAsync(t => t.Id == treatmentId);
            var prescriptionCountBefore = await db.Prescriptions.CountAsync();

            var token = await IssuePrescriptionWriteTokenAsync(email);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.PostAsJsonAsync("/api/v1/prescriptions", new
            {
                ClinicId = treatment.ClinicId,
                PetId = treatment.PetId,
                TreatmentId = treatment.Id,
                PrescribedAtUtc = ValidPrescribedAtUtc,
                Title = "Reçete",
                Content = "İçerik",
            });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

            var prescriptionCountAfter = await db.Prescriptions.CountAsync();
            prescriptionCountAfter.Should().Be(prescriptionCountBefore);
        }
    }

    [Fact]
    public async Task Update_Should_Return403_And_NotMutate_When_UnassignedClinicPrescription()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedPrescriptionWriterUserAsync(_factory.Services, hasher);
        var prescriptionId = await IntegrationTestAuthHelper.SeedPrescriptionInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssuePrescriptionWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (clinicId, petId, title) = await GetPrescriptionSnapshotAsync(prescriptionId);

        var response = await client.PutAsJsonAsync($"/api/v1/prescriptions/{prescriptionId}", new
        {
            Id = prescriptionId,
            ClinicId = clinicId,
            PetId = petId,
            PrescribedAtUtc = ValidPrescribedAtUtc,
            Title = "Mutated",
            Content = "İçerik",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        var (_, _, afterTitle) = await GetPrescriptionSnapshotAsync(prescriptionId);
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

        var createResponse = await client.PostAsJsonAsync("/api/v1/prescriptions", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            PrescribedAtUtc = ValidPrescribedAtUtc,
            Title = "Admin create",
            Content = "İçerik",
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var prescriptionId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/prescriptions/{prescriptionId}", new
        {
            Id = prescriptionId,
            ClinicId = extraClinicId,
            PetId = petId,
            PrescribedAtUtc = ValidPrescribedAtUtc,
            Title = "Admin update",
            Content = "İçerik",
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_Should_Return404_When_ForeignTenantPrescription()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, _) =
            await IntegrationTestAuthHelper.SeedPrescriptionWriterUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var prescriptionId = await IntegrationTestAuthHelper.SeedPrescriptionInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var token = await IssuePrescriptionWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync($"/api/v1/prescriptions/{prescriptionId}", new
        {
            Id = prescriptionId,
            ClinicId = Guid.NewGuid(),
            PetId = Guid.NewGuid(),
            PrescribedAtUtc = ValidPrescribedAtUtc,
            Title = "X",
            Content = "İçerik",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Prescriptions.NotFound");
    }

    [Fact]
    public async Task Create_Should_Return403_When_UserHasNoPrescriptionCreatePermission()
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

        var response = await client.PostAsJsonAsync("/api/v1/prescriptions", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            PrescribedAtUtc = ValidPrescribedAtUtc,
            Title = "Reçete",
            Content = "İçerik",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(Guid ClinicId, Guid PetId, string Title)> GetPrescriptionSnapshotAsync(Guid prescriptionId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Prescriptions.AsNoTracking().SingleAsync(p => p.Id == prescriptionId);
        return (row.ClinicId, row.PetId, row.Title);
    }
}
