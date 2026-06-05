using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

[Collection("pilot-smoke-api")]
public sealed class AuthSmokeIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthSmokeIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_Should_ReturnTokens_When_CredentialsAreValid()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "admin@example.com",
            Password = "123456"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("resolvedTenantId").GetGuid().Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Login_Should_ReturnUnauthorized_WithProblemCode_When_CredentialsAreInvalid()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "admin@example.com",
            Password = "wrong-password"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response))
            .Should().Be("Auth.Unauthorized.InvalidCredentials");
    }

    [Fact]
    public async Task Refresh_Should_IssueNewTokens_When_RefreshTokenIsValid()
    {
        var login = await IntegrationTestAuthHelper.LoginAsync(
            _client,
            _factory.Services,
            "admin@example.com",
            "123456");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            RefreshToken = login.RefreshToken
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("refreshToken").GetString().Should().NotBe(login.RefreshToken);
    }

    [Fact]
    public async Task Refresh_Should_ReturnNotFound_WithProblemCode_When_RefreshTokenIsInvalid()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            RefreshToken = "invalid-refresh-token"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response))
            .Should().Be("Auth.Unauthorized.RefreshTokenNotFound");
    }

    [Fact]
    public async Task SelectClinic_Should_ReturnTokens_When_RefreshTokenAndClinicAreValid()
    {
        var login = await IntegrationTestAuthHelper.LoginAsync(
            _client,
            _factory.Services,
            "admin@example.com",
            "123456");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clinicId = db.Clinics
            .Where(c => c.TenantId == login.TenantId)
            .Select(c => c.Id)
            .First();

        var response = await _client.PostAsJsonAsync("/api/v1/auth/select-clinic", new
        {
            RefreshToken = login.RefreshToken,
            ClinicId = clinicId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("refreshToken").GetString().Should().NotBe(login.RefreshToken);
    }

    [Fact]
    public async Task SelectClinic_Should_ReturnBadRequest_WithProblemCode_When_RequestInvalid()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/select-clinic", new
        {
            RefreshToken = "",
            ClinicId = Guid.Empty
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response))
            .Should().Be("Auth.Validation.SelectClinicRequestInvalid");
    }

    [Fact]
    public async Task Logout_Should_ReturnSuccess_When_AccessTokenAndRefreshTokenAreValid()
    {
        var login = await IntegrationTestAuthHelper.LoginAsync(
            _client,
            _factory.Services,
            "admin@example.com",
            "123456");

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await _client.PostAsJsonAsync("/api/v1/auth/logout", new
        {
            RefreshToken = login.RefreshToken
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
