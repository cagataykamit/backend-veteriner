using System.Net;
using System.Net.Http.Headers;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.LabResults;

/// <summary>
/// GET /api/v1/lab-results/{id} lab result detay IDOR erişim kontrolü (FAZ IDOR-2D.2).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class LabResultDetailIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LabResultDetailIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<string> IssueLabResultReadTokenAsync(string email)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            new[] { PermissionCatalog.LabResults.Read });

    [Fact]
    public async Task GetById_Should_Return200_When_NonTenantWideUserReadsAssignedClinicLabResult()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedLabResultReaderUserAsync(_factory.Services, hasher);
        var labResultId = await IntegrationTestAuthHelper.SeedLabResultInClinicAsync(
            _factory.Services,
            assignedClinicId);

        var token = await IssueLabResultReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/lab-results/{labResultId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return404_When_NonTenantWideUserReadsUnassignedClinicLabResultInSameTenant()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedLabResultReaderUserAsync(_factory.Services, hasher);
        var labResultId = await IntegrationTestAuthHelper.SeedLabResultInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueLabResultReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/lab-results/{labResultId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("LabResults.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return404_When_UserReadsForeignTenantLabResult()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, _) =
            await IntegrationTestAuthHelper.SeedLabResultReaderUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var foreignLabResultId = await IntegrationTestAuthHelper.SeedLabResultInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var token = await IssueLabResultReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/lab-results/{foreignLabResultId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("LabResults.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return200_When_TenantAdminReadsOtherClinicLabResult()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var labResultId = await IntegrationTestAuthHelper.SeedLabResultInClinicAsync(
            _factory.Services,
            extraClinicId);

        var token = await IssueLabResultReadTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/v1/lab-results/{labResultId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Should_Return403_When_UserHasNoLabResultsReadPermission()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (_, _, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var labResultId = await IntegrationTestAuthHelper.SeedLabResultInClinicAsync(
            _factory.Services,
            extraClinicId);

        var (plainEmail, plainPassword) =
            await IntegrationTestAuthHelper.SeedPlainTenantMemberAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, plainEmail, plainPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.GetAsync($"/api/v1/lab-results/{labResultId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
