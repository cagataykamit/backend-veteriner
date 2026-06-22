using System.Net;
using System.Net.Http.Headers;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Hospitalizations;

/// <summary>
/// GET /api/v1/hospitalizations/{id} yatış detay IDOR erişim kontrolü (FAZ IDOR-2D.1).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class HospitalizationDetailIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HospitalizationDetailIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<string> IssueHospitalizationReadTokenAsync(string email)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            new[] { PermissionCatalog.Hospitalizations.Read });

    [Fact]
    public async Task GetById_Should_Return200_When_NonTenantWideUserReadsAssignedClinicHospitalization()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedHospitalizationReaderUserAsync(_factory.Services, hasher);
        var hospitalizationId = await IntegrationTestAuthHelper.SeedHospitalizationInClinicAsync(
            _factory.Services,
            assignedClinicId);

        var token = await IssueHospitalizationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/hospitalizations/{hospitalizationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return404_When_NonTenantWideUserReadsUnassignedClinicHospitalizationInSameTenant()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedHospitalizationReaderUserAsync(_factory.Services, hasher);
        var hospitalizationId = await IntegrationTestAuthHelper.SeedHospitalizationInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueHospitalizationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/hospitalizations/{hospitalizationId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Hospitalizations.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return404_When_UserReadsForeignTenantHospitalization()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, _) =
            await IntegrationTestAuthHelper.SeedHospitalizationReaderUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var foreignHospitalizationId = await IntegrationTestAuthHelper.SeedHospitalizationInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var token = await IssueHospitalizationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/hospitalizations/{foreignHospitalizationId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Hospitalizations.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return200_When_TenantAdminReadsOtherClinicHospitalization()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var hospitalizationId = await IntegrationTestAuthHelper.SeedHospitalizationInClinicAsync(
            _factory.Services,
            extraClinicId);

        var token = await IssueHospitalizationReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/hospitalizations/{hospitalizationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return403_When_UserHasNoHospitalizationsReadPermission()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (_, _, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var hospitalizationId = await IntegrationTestAuthHelper.SeedHospitalizationInClinicAsync(
            _factory.Services,
            extraClinicId);

        var (plainEmail, plainPassword) =
            await IntegrationTestAuthHelper.SeedPlainTenantMemberAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, plainEmail, plainPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/hospitalizations/{hospitalizationId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
