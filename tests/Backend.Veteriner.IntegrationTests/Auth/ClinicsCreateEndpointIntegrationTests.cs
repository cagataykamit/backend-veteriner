using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

[Collection("pilot-smoke-api")]
public sealed class ClinicsCreateEndpointIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ClinicsCreateEndpointIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_Should_CreateClinic_When_TenantAdminHasClinicsCreate()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var clinicName = $"Smoke-{Guid.NewGuid():N}"[..16];
        var response = await client.PostAsJsonAsync("/api/v1/clinics", new
        {
            name = clinicName,
            city = "Bursa"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task District_Should_RoundTrip_Through_CreateUpdateAndDetail()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var clinicName = $"District-{Guid.NewGuid():N}"[..16];
        var created = await client.PostAsJsonAsync("/api/v1/clinics", new
        {
            name = clinicName,
            city = "İstanbul",
            district = "  Kadıköy "
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var clinicId = await created.Content.ReadFromJsonAsync<Guid>();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/clinics/{clinicId}");
        detail.GetProperty("district").GetString().Should().Be("Kadıköy");

        var updated = await client.PutAsJsonAsync($"/api/v1/clinics/{clinicId}", new
        {
            name = clinicName,
            city = "İstanbul",
            district = "Üsküdar"
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("district").GetString().Should().Be("Üsküdar");

        var tooLong = await client.PutAsJsonAsync($"/api/v1/clinics/{clinicId}", new
        {
            name = clinicName,
            city = "İstanbul",
            district = new string('a', 101)
        });
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Should_ReturnForbidden_When_ClinicAdminMissingClinicsCreate()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, _, _) =
            await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/clinics", new
        {
            name = $"Denied-{Guid.NewGuid():N}"[..16],
            city = "Antalya"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
