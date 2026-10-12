using System.Net;
using Backend.IntegrationTests.Auditing;
using FluentAssertions;

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
    public async Task AdminPing_Should_RequireAuthentication()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/ping");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
