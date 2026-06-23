using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Examinations;

/// <summary>Examination write clinic assignment IDOR (IDOR-7B).</summary>
[Collection("pilot-smoke-api")]
public sealed class ExaminationWriteClinicAssignmentIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ExaminationWriteClinicAssignmentIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static readonly string[] ExaminationWritePermissions =
    [
        PermissionCatalog.Examinations.Create,
        PermissionCatalog.Examinations.Update,
    ];

    private async Task<string> IssueExaminationWriteTokenAsync(string email)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            ExaminationWritePermissions);

    private static readonly DateTime ValidExaminedAtUtc = DateTime.UtcNow.AddHours(-2);

    [Fact]
    public async Task Create_Should_Return403_When_NonTenantWideUserCreatesInUnassignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedExaminationWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, unassignedClinicId);

        var token = await IssueExaminationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/examinations", new
        {
            ClinicId = unassignedClinicId,
            PetId = petId,
            ExaminedAtUtc = ValidExaminedAtUtc,
            VisitReason = "Kontrol",
            Findings = "Bulgu",
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
            await IntegrationTestAuthHelper.SeedExaminationWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, assignedClinicId);

        var token = await IssueExaminationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/examinations", new
        {
            ClinicId = assignedClinicId,
            PetId = petId,
            ExaminedAtUtc = ValidExaminedAtUtc,
            VisitReason = "Kontrol",
            Findings = "Bulgu",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_Should_Return403_And_NotCompleteAppointment_When_UnassignedClinicAppointment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedExaminationWriterUserAsync(_factory.Services, hasher);
        var appointmentId = await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueExaminationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/examinations", new
        {
            AppointmentId = appointmentId,
            ExaminedAtUtc = ValidExaminedAtUtc,
            VisitReason = "Kontrol",
            Findings = "Bulgu",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        row.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task Update_Should_Return403_And_NotMutate_When_UnassignedClinicExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedExaminationWriterUserAsync(_factory.Services, hasher);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueExaminationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (clinicId, petId, visitReason) = await GetExaminationSnapshotAsync(seed.ExaminationId);

        var response = await client.PutAsJsonAsync($"/api/v1/examinations/{seed.ExaminationId}", new
        {
            ClinicId = clinicId,
            PetId = petId,
            ExaminedAtUtc = ValidExaminedAtUtc,
            VisitReason = "Mutated",
            Findings = "Bulgu",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");

        var (_, _, afterReason) = await GetExaminationSnapshotAsync(seed.ExaminationId);
        afterReason.Should().Be(visitReason);
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

        var createResponse = await client.PostAsJsonAsync("/api/v1/examinations", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            ExaminedAtUtc = ValidExaminedAtUtc,
            VisitReason = "Admin create",
            Findings = "Bulgu",
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var examinationId = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/examinations/{examinationId}", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            ExaminedAtUtc = ValidExaminedAtUtc,
            VisitReason = "Admin update",
            Findings = "Bulgu",
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_Should_Return404_When_ForeignTenantExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, _) =
            await IntegrationTestAuthHelper.SeedExaminationWriterUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var token = await IssueExaminationWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync($"/api/v1/examinations/{seed.ExaminationId}", new
        {
            ClinicId = Guid.NewGuid(),
            PetId = Guid.NewGuid(),
            ExaminedAtUtc = ValidExaminedAtUtc,
            VisitReason = "X",
            Findings = "Bulgu",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task Create_Should_Return403_When_UserHasNoExaminationCreatePermission()
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

        var response = await client.PostAsJsonAsync("/api/v1/examinations", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            ExaminedAtUtc = ValidExaminedAtUtc,
            VisitReason = "Kontrol",
            Findings = "Bulgu",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(Guid ClinicId, Guid PetId, string VisitReason)> GetExaminationSnapshotAsync(Guid examinationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Examinations.AsNoTracking().SingleAsync(e => e.Id == examinationId);
        return (row.ClinicId, row.PetId, row.VisitReason);
    }
}
