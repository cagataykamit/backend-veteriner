using System.Net;
using System.Net.Http.Headers;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Treatments;

/// <summary>
/// GET /api/v1/treatments/{id} tedavi detay IDOR erişim kontrolü (FAZ IDOR-2C.1).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class TreatmentDetailIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TreatmentDetailIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<string> IssueTreatmentReadTokenAsync(string email)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            new[] { PermissionCatalog.Treatments.Read });

    [Fact]
    public async Task GetById_Should_Return200_When_NonTenantWideUserReadsAssignedClinicTreatment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedTreatmentReaderUserAsync(_factory.Services, hasher);
        var treatmentId = await IntegrationTestAuthHelper.SeedTreatmentInClinicAsync(
            _factory.Services,
            assignedClinicId);

        var token = await IssueTreatmentReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/treatments/{treatmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return404_When_NonTenantWideUserReadsUnassignedClinicTreatmentInSameTenant()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedTreatmentReaderUserAsync(_factory.Services, hasher);
        var treatmentId = await IntegrationTestAuthHelper.SeedTreatmentInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueTreatmentReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/treatments/{treatmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Treatments.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return404_When_UserReadsForeignTenantTreatment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, _) =
            await IntegrationTestAuthHelper.SeedTreatmentReaderUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var foreignTreatmentId = await IntegrationTestAuthHelper.SeedTreatmentInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var token = await IssueTreatmentReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/treatments/{foreignTreatmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Treatments.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return200_When_TenantAdminReadsOtherClinicTreatment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var treatmentId = await IntegrationTestAuthHelper.SeedTreatmentInClinicAsync(
            _factory.Services,
            extraClinicId);

        var token = await IssueTreatmentReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/treatments/{treatmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return403_When_UserHasNoTreatmentsReadPermission()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (_, _, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var treatmentId = await IntegrationTestAuthHelper.SeedTreatmentInClinicAsync(
            _factory.Services,
            extraClinicId);

        var (plainEmail, plainPassword) =
            await IntegrationTestAuthHelper.SeedPlainTenantMemberAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, plainEmail, plainPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/treatments/{treatmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
