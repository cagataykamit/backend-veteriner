using System.Net;
using System.Net.Http.Json;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auditing;

/// <summary>
/// Correlation: inbound header → middleware Items/response → ClientContext → audit persistence.
/// </summary>
[Collection("audit-integration")]
public sealed class CorrelationChainIntegrationTests
{
    private readonly AuditAuthFixture _fixture;

    public CorrelationChainIntegrationTests(AuditAuthFixture fixture)
        => _fixture = fixture;

    private HttpClient Client => _fixture.Client;

    [Fact]
    public async Task HealthRequest_Should_EchoInboundCorrelationId_On_ResponseHeader_When_HeaderProvided()
    {
        using var client = _fixture.Factory.CreateClient();
        var expected = $"it-cid-in-{Guid.NewGuid():N}";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(Correlation.HeaderName, expected);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(Correlation.HeaderName, out var outbound).Should().BeTrue();
        outbound.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public async Task HealthRequest_Should_ReturnGeneratedCorrelationId_On_ResponseHeader_When_HeaderMissing()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(Correlation.HeaderName, out var values).Should().BeTrue();
        var cid = values!.Single();
        cid.Should().MatchRegex("^[a-fA-F0-9]{32}$", "middleware Guid.NewGuid().ToString(\"N\") formatı");
    }

}
