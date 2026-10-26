using System.Net;
using System.Text.Json;
using Backend.IntegrationTests.Auditing;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Backend.IntegrationTests.Security;

[Collection("audit-integration")]
public sealed class SecurityHeadersIntegrationTests
{
    private readonly AuditAuthFixture _fixture;

    public SecurityHeadersIntegrationTests(AuditAuthFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task Response_Should_CarryBaselineSecurityHeaders()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("no-referrer");
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Which.Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task Swagger_Should_NotBeServed_Outside_Development()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Swagger_Should_GenerateDocument_When_Enabled()
    {
        await using var swaggerEnabled = _fixture.Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })));
        using var client = swaggerEnabled.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("openapi").GetString().Should().StartWith("3.");
        root.GetProperty("paths").TryGetProperty("/api/v1/Auth/login", out _).Should().BeTrue();
        root.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).Should().BeTrue();
        var createPayment = root.GetProperty("components").GetProperty("schemas").GetProperty("CreatePaymentCommand");
        createPayment.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Should().Contain("allocations");
        createPayment.GetProperty("properties").GetProperty("petId").GetProperty("nullable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AdminPing_Should_RequireAuthentication()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/ping");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
