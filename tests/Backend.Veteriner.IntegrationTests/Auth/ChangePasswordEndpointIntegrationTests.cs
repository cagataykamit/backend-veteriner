using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

public sealed class ChangePasswordEndpointIntegrationTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private const string InitialPassword = "Current1!Pass";
    private const string UpdatedPassword = "Updated2@Pass";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ChangePasswordEndpointIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ChangePassword_Should_ReturnUnauthorized_When_TokenMissing()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/change-password",
            new
            {
                currentPassword = InitialPassword,
                newPassword = UpdatedPassword,
                confirmPassword = UpdatedPassword
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_Should_ReturnOk_When_CurrentPasswordIsValid()
    {
        var email = await SeedTenantUserAsync(InitialPassword);
        var accessToken = await LoginAndGetAccessTokenAsync(email, InitialPassword);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/change-password",
            new
            {
                currentPassword = InitialPassword,
                newPassword = UpdatedPassword,
                confirmPassword = UpdatedPassword
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_Should_ReturnBadRequest_When_CurrentPasswordIsInvalid()
    {
        var email = await SeedTenantUserAsync(InitialPassword);
        var accessToken = await LoginAndGetAccessTokenAsync(email, InitialPassword);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/change-password",
            new
            {
                currentPassword = "WrongPass1!",
                newPassword = UpdatedPassword,
                confirmPassword = UpdatedPassword
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Auth.ChangePassword.InvalidCurrentPassword");
    }

    [Fact]
    public async Task ChangePassword_Should_BlockOldLogin_And_AllowNewLogin_AfterSuccessfulChange()
    {
        var email = await SeedTenantUserAsync(InitialPassword);
        var accessToken = await LoginAndGetAccessTokenAsync(email, InitialPassword);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var changeResponse = await _client.PostAsJsonAsync(
            "/api/v1/me/change-password",
            new
            {
                currentPassword = InitialPassword,
                newPassword = UpdatedPassword,
                confirmPassword = UpdatedPassword
            });
        changeResponse.EnsureSuccessStatusCode();

        var oldLoginResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Email = email, Password = InitialPassword });
        oldLoginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newLoginResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Email = email, Password = UpdatedPassword });
        newLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<string> SeedTenantUserAsync(string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
        var email = $"change-password-{Guid.NewGuid():N}@example.com";
        var user = new User(email, hasher.Hash(password));

        db.Users.Add(user);
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
        await db.SaveChangesAsync();

        return email;
    }

    private async Task<string> LoginAndGetAccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Email = email, Password = password });
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("accessToken").GetString()!;
    }
}
