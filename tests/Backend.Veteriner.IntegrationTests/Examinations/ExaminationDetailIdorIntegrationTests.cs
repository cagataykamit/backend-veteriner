using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Examinations;

/// <summary>
/// GET /api/v1/examinations/{id} ve related-summary muayene detay IDOR erişim kontrolü (FAZ 2B).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ExaminationDetailIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ExaminationDetailIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<string> IssueExaminationReadTokenAsync(string email)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            new[] { PermissionCatalog.Examinations.Read });

    [Fact]
    public async Task GetById_Should_Return200_When_NonTenantWideUserReadsAssignedClinicExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedExaminationReaderUserAsync(_factory.Services, hasher);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            assignedClinicId);

        var token = await IssueExaminationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/examinations/{seed.ExaminationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return404_When_NonTenantWideUserReadsUnassignedClinicExaminationInSameTenant()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedExaminationReaderUserAsync(_factory.Services, hasher);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueExaminationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/examinations/{seed.ExaminationId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return404_When_NonTenantWideUserWithoutClinicClaimReadsUnassignedClinicExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedExaminationReaderUserAsync(_factory.Services, hasher);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueExaminationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/examinations/{seed.ExaminationId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return200_When_TenantAdminReadsOtherClinicExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            extraClinicId);

        var token = await IssueExaminationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/examinations/{seed.ExaminationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return404_When_UserReadsForeignTenantExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, _) =
            await IntegrationTestAuthHelper.SeedExaminationReaderUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var token = await IssueExaminationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/examinations/{seed.ExaminationId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task GetExaminationRelatedSummary_Should_Return200_When_NonTenantWideUserReadsAssignedClinicExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedExaminationReaderUserAsync(_factory.Services, hasher);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            assignedClinicId,
            includeRelatedTreatment: true);

        var token = await IssueExaminationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/examinations/{seed.ExaminationId}/related-summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("treatments").GetArrayLength().Should().Be(1);
        json.GetProperty("treatments")[0].GetProperty("title").GetString().Should().Be("İlgili Tedavi");
    }

    [Fact]
    public async Task GetExaminationRelatedSummary_Should_Return404_When_NonTenantWideUserReadsUnassignedClinicExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedExaminationReaderUserAsync(_factory.Services, hasher);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            unassignedClinicId,
            includeRelatedTreatment: true);

        var token = await IssueExaminationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/examinations/{seed.ExaminationId}/related-summary");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task GetExaminationRelatedSummary_Should_Return404_When_TenantAdminReadsForeignTenantExamination()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            foreignClinicId,
            includeRelatedTreatment: true);

        var token = await IssueExaminationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/examinations/{seed.ExaminationId}/related-summary");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task GetExaminationRelatedSummary_Should_NotLeakRelatedData_When_UserNotAssignedToExaminationClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedExaminationReaderUserAsync(_factory.Services, hasher);
        var seed = await IntegrationTestAuthHelper.SeedExaminationInClinicAsync(
            _factory.Services,
            unassignedClinicId,
            includeRelatedTreatment: true);
        seed.RelatedTreatmentId.Should().NotBeNull("test verisi bağlı tedavi içermeli");

        var token = await IssueExaminationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/examinations/{seed.ExaminationId}/related-summary");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(seed.RelatedTreatmentId!.Value.ToString("D"));
        body.Should().NotContain("İlgili Tedavi");
    }
}
