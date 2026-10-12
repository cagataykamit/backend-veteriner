using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.LabResults;

/// <summary>Lab istem/durum akışı: istem → sonuç girişi → iptal, RowVersion eşzamanlılığı (Aşama 6 Dilim 1).</summary>
[Collection("pilot-smoke-api")]
public sealed class LabResultStatusFlowIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LabResultStatusFlowIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, Guid ClinicId, Guid PetId)> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, clinicId) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return (client, clinicId, petId);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid id)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/lab-results/{id}");

    [Fact]
    public async Task Requested_Should_Be_Resulted_Via_Put_And_Stale_RowVersion_Should_Conflict()
    {
        var (client, clinicId, petId) = await AdminClientAsync();

        var create = await client.PostAsJsonAsync("/api/v1/lab-results", new { ClinicId = clinicId, PetId = petId, TestName = "Kan sayımı" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var requested = await GetAsync(client, id);
        requested.GetProperty("status").GetString().Should().Be("Requested");
        requested.GetProperty("resultText").ValueKind.Should().Be(JsonValueKind.Null);
        requested.GetProperty("awaitingInterpretation").GetBoolean().Should().BeFalse();
        var firstVersion = requested.GetProperty("rowVersion").GetString();

        // İki sekme: ikisi de aynı sürümü görmüş; ilk yazan kazanır, ikinci 409 alır.
        object Body(string text, string? version) => new
        {
            ClinicId = clinicId,
            PetId = petId,
            TestName = "Kan sayımı",
            ResultDateUtc = DateTime.UtcNow.AddHours(-1),
            ResultText = text,
            RowVersion = version,
        };

        var first = await client.PutAsJsonAsync($"/api/v1/lab-results/{id}", Body("Sekme 1", firstVersion));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var written = await first.Content.ReadFromJsonAsync<JsonElement>();
        written.GetProperty("rowVersion").GetString().Should().NotBe(firstVersion);

        var second = await client.PutAsJsonAsync($"/api/v1/lab-results/{id}", Body("Sekme 2", firstVersion));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(second)).Should().Be("LabResults.ConcurrencyConflict");

        var after = await GetAsync(client, id);
        after.GetProperty("status").GetString().Should().Be("Resulted");
        after.GetProperty("resultText").GetString().Should().Be("Sekme 1");
        after.GetProperty("awaitingInterpretation").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Put_Should_Return400_When_RowVersionMissing()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/lab-results", new { ClinicId = clinicId, PetId = petId, TestName = "CBC" });
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var response = await client.PutAsJsonAsync($"/api/v1/lab-results/{id}", new { ClinicId = clinicId, PetId = petId, TestName = "CBC" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("LabResults.Validation");
    }

    [Fact]
    public async Task Cancel_Should_Hide_From_Default_List_Block_Put_And_Reject_Second_Cancel()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var create = await client.PostAsJsonAsync("/api/v1/lab-results", new { ClinicId = clinicId, PetId = petId, TestName = "İdrar" });
        var id = await create.Content.ReadFromJsonAsync<Guid>();
        var version = (await GetAsync(client, id)).GetProperty("rowVersion").GetString();

        var cancel = await client.PostAsJsonAsync($"/api/v1/lab-results/{id}/cancel", new { Reason = "Yanlış hasta için istendi", RowVersion = version });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelledVersion = (await cancel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rowVersion").GetString();

        var detail = await GetAsync(client, id);
        detail.GetProperty("status").GetString().Should().Be("Cancelled");
        detail.GetProperty("cancelReason").GetString().Should().Be("Yanlış hasta için istendi");

        var defaultList = await client.GetFromJsonAsync<JsonElement>($"/api/v1/lab-results?clinicId={clinicId}&petId={petId}");
        defaultList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(id);
        var cancelledList = await client.GetFromJsonAsync<JsonElement>($"/api/v1/lab-results?clinicId={clinicId}&petId={petId}&status=Cancelled");
        cancelledList.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);

        var put = await client.PutAsJsonAsync($"/api/v1/lab-results/{id}", new { ClinicId = clinicId, PetId = petId, TestName = "İdrar", RowVersion = cancelledVersion });
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(put)).Should().Be("LabResults.AlreadyCancelled");

        var again = await client.PostAsJsonAsync($"/api/v1/lab-results/{id}/cancel", new { Reason = "İkinci iptal denemesi", RowVersion = cancelledVersion });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("LabResults.AlreadyCancelled");
    }

    [Fact]
    public async Task Post_Should_Return400_When_OnlyResultTextGiven()
    {
        var (client, clinicId, petId) = await AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/lab-results", new { ClinicId = clinicId, PetId = petId, TestName = "CBC", ResultText = "Normal" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("LabResults.Validation");
    }
}
