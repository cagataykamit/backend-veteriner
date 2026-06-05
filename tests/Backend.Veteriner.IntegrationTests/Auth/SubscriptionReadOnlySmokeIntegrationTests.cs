using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

[Collection("pilot-smoke-api")]
public sealed class SubscriptionReadOnlySmokeIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SubscriptionReadOnlySmokeIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task PostProducts_Should_Return403_SubscriptionsTenantReadOnly_For_ReadOnlyTenant()
    {
        var client = _factory.CreateClient();
        var jwt = _factory.Services.GetRequiredService<IJwtTokenService>();

        var token = await IntegrationTestAuthHelper.IssueReadOnlyTenantTokenAsync(
            _factory.Services,
            jwt,
            new[] { "Products.Create" });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            productCategoryId = (Guid?)null,
            name = "RO-Smoke",
            sku = $"SKU-{Guid.NewGuid():N}"[..14],
            barcode = "123",
            description = "d",
            unit = "Adet",
            unitPrice = 1,
            currency = "TRY"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response))
            .Should().Be("Subscriptions.TenantReadOnly");
    }
}
