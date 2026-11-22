using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.LabResults;

/// <summary>
/// REF-001 Dilim 3 gerçek veritabanı üzerinden: değer satırlarıyla yazma, hesaplanan işaret, önceki değer ve eğilim,
/// kısmi PUT yorumu, parametre geçmişi ve değişiklik geçmişindeki "lines" alanı.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class LabResultLinesIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LabResultLinesIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

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

    private static async Task<Guid> CreateAsync(HttpClient client, Guid clinicId, Guid petId, DateTime resultDate, object[] lines, string? text = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/lab-results", new
        {
            ClinicId = clinicId, PetId = petId, TestName = "Biyokimya", ResultDateUtc = resultDate, ResultText = text, Lines = lines,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static Task<JsonElement> GetAsync(HttpClient client, Guid id)
        => client.GetFromJsonAsync<JsonElement>($"/api/v1/lab-results/{id}");

    [Fact]
    public async Task Lines_Should_Be_Flagged_And_Compared_With_Previous_Result_Of_Same_Unit()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        await CreateAsync(client, clinicId, petId, DateTime.UtcNow.AddDays(-5), [
            new { ParameterName = "Glikoz", Value = 110m, Unit = "mg/dL", ReferenceLow = 70m, ReferenceHigh = 120m },
            new { ParameterName = "Üre", Value = 9m, Unit = "mmol/L" },
        ]);
        var id = await CreateAsync(client, clinicId, petId, DateTime.UtcNow.AddHours(-1), [
            new { ParameterName = "GLİKOZ", Value = 140.5m, Unit = "mg/dL", ReferenceLow = 70m, ReferenceHigh = 120m },
            new { ParameterName = "Üre", Value = 40m, Unit = "mg/dL" },
        ]);

        var detail = await GetAsync(client, id);

        detail.GetProperty("status").GetString().Should().Be("Resulted");
        detail.GetProperty("resultText").ValueKind.Should().Be(JsonValueKind.Null);
        var lines = detail.GetProperty("lines").EnumerateArray().ToList();
        lines.Should().HaveCount(2);
        lines[0].GetProperty("value").GetDecimal().Should().Be(140.5m);
        lines[0].GetProperty("flag").GetString().Should().Be("High");
        lines[0].GetProperty("previous").GetProperty("value").GetDecimal().Should().Be(110m);
        lines[0].GetProperty("previous").GetProperty("flag").GetString().Should().Be("Normal");
        lines[0].GetProperty("trend").GetString().Should().Be("Up");
        // Birimi farklı önceki değer karşılaştırılmaz; aralık yoksa işaret yok.
        lines[1].GetProperty("flag").ValueKind.Should().Be(JsonValueKind.Null);
        lines[1].GetProperty("previous").ValueKind.Should().Be(JsonValueKind.Null);
        lines[1].GetProperty("trend").ValueKind.Should().Be(JsonValueKind.Null);

        var history = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/lab-results/parameter-history?clinicId={clinicId}&petId={petId}&parameter=glikoz");
        history.EnumerateArray().Select(p => p.GetProperty("value").GetDecimal()).Should().Equal(140.5m, 110m);
        history[0].GetProperty("labResultId").GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task Put_Should_Keep_Lines_When_Omitted_Replace_When_Given_And_Record_History()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var resultDate = DateTime.UtcNow.AddHours(-2);
        var id = await CreateAsync(client, clinicId, petId, resultDate, [new { ParameterName = "ALT", Value = 45m, Unit = "U/L" }], "Rapor ekte");

        object Body(string? version, object[]? lines) => new
        {
            ClinicId = clinicId, PetId = petId, TestName = "Biyokimya", ResultDateUtc = resultDate, ResultText = "Rapor ekte",
            RowVersion = version, Lines = lines,
        };

        var keep = await client.PutAsJsonAsync($"/api/v1/lab-results/{id}", Body((await GetAsync(client, id)).GetProperty("rowVersion").GetString(), null));
        keep.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync(client, id)).GetProperty("lines").GetArrayLength().Should().Be(1);

        var replace = await client.PutAsJsonAsync($"/api/v1/lab-results/{id}", Body(
            (await GetAsync(client, id)).GetProperty("rowVersion").GetString(),
            [
                new { ParameterName = "ALT", Value = 150m, Unit = "U/L", ReferenceLow = 10m, ReferenceHigh = 100m },
                new { ParameterName = "AST", Value = 20m, Unit = "U/L", ReferenceLow = 10m, ReferenceHigh = 50m },
            ]));
        replace.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await GetAsync(client, id);
        var lines = detail.GetProperty("lines").EnumerateArray().ToList();
        lines.Select(l => l.GetProperty("parameterName").GetString()).Should().Equal("ALT", "AST");
        lines.Select(l => l.GetProperty("flag").GetString()).Should().Equal("High", "Normal");
        detail.GetProperty("resultText").GetString().Should().Be("Rapor ekte");

        var changes = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/lab-results/{id}/history"))[0]
            .GetProperty("changes").EnumerateArray().ToList();
        var lineChange = changes.Should().ContainSingle(c => c.GetProperty("field").GetString() == "lines").Subject;
        lineChange.GetProperty("oldValue").GetString().Should().Be("ALT: 45 U/L");
        lineChange.GetProperty("newValue").GetString().Should().Be("ALT: 150 U/L · referans 10–100\nAST: 20 U/L · referans 10–50");
    }

    [Fact]
    public async Task Invalid_Lines_Should_Return_400()
    {
        var (client, clinicId, petId) = await AdminClientAsync();

        var duplicate = await client.PostAsJsonAsync("/api/v1/lab-results", new
        {
            ClinicId = clinicId, PetId = petId, TestName = "Biyokimya", ResultDateUtc = DateTime.UtcNow.AddHours(-1),
            Lines = new[] { new { ParameterName = "ALT", Value = 1m }, new { ParameterName = "alt", Value = 2m } },
        });
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(duplicate)).Should().Be("LabResults.Validation");

        var reversedRange = await client.PostAsJsonAsync("/api/v1/lab-results", new
        {
            ClinicId = clinicId, PetId = petId, TestName = "Biyokimya", ResultDateUtc = DateTime.UtcNow.AddHours(-1),
            Lines = new[] { new { ParameterName = "ALT", Value = 1m, ReferenceLow = (decimal?)100m, ReferenceHigh = (decimal?)10m } },
        });
        reversedRange.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noDate = await client.PostAsJsonAsync("/api/v1/lab-results", new
        {
            ClinicId = clinicId, PetId = petId, TestName = "Biyokimya", Lines = new[] { new { ParameterName = "ALT", Value = 1m } },
        });
        noDate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
