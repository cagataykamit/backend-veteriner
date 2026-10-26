using System.Net;
using AwesomeAssertions;
using Backend.IntegrationTests.Auditing;

namespace Backend.IntegrationTests.Middleware;

/// <summary>
/// URL segmentiyle sürümleme sözleşmesi (Asp.Versioning): desteklenen sürüm başlıkta bildirilir, <c>v1</c> ve <c>v1.0</c>
/// aynı uca düşer; desteklenmeyen sürüm uç bulunamadı sayılır ve standart ProblemDetails 404 döner.
/// </summary>
[Collection("audit-integration")]
public sealed class ApiVersioningIntegrationTests
{
    private const string PublicEndpoint = "public/legal-documents";

    private readonly AuditAuthFixture _fixture;

    public ApiVersioningIntegrationTests(AuditAuthFixture fixture)
        => _fixture = fixture;

    [Theory]
    [InlineData("v1")]
    [InlineData("v1.0")]
    public async Task Supported_Version_Should_Be_Served_And_Reported(string version)
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync($"/api/{version}/{PublicEndpoint}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("api-supported-versions").Should().ContainSingle().Which.Should().Be("1.0");
    }

    [Fact]
    public async Task Unsupported_Version_Should_Return_NotFound_ProblemDetails()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync($"/api/v2/{PublicEndpoint}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"code\":\"Errors.NotFound\"");
    }
}
