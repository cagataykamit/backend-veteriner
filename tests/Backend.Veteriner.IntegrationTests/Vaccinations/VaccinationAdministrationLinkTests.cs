using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Vaccinations;

/// <summary>
/// Aşı kaydına bağlı uygulama (ürün/parti/stok): gerçek SQL Server/LocalDB üzerinde atomiklik, requestId tekrarı,
/// iptal tutarlılığı, bağlı kayıtta kilitli güncelleme ve çift sayımın önlenmesi.
/// </summary>
[Collection("products-api")]
public sealed class VaccinationAdministrationLinkTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AllPermissions =
    {
        "Vaccinations.Create", "Vaccinations.Update", "Vaccinations.Read",
        "Administrations.Create", "Administrations.Read", "Administrations.Correct",
        "Products.Read", "Pets.Read",
    };

    private readonly CustomWebApplicationFactory _factory;

    public VaccinationAdministrationLinkTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_WithoutProduct_Should_Return201_BareGuid_And_Not_Touch_Stock()
    {
        var ctx = await SeedAsync(stock: 10m);

        var response = await PostAsync(ClientFor(ctx), ctx, includeProduct: false);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.String);
        (await GetStockAsync(ctx)).Should().Be(10m);
        (await AdministrationCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Post_WithProduct_Should_Write_Vaccination_And_Linked_Administration_And_Drop_Stock()
    {
        var ctx = await SeedAsync(stock: 10m, minimum: 8m);
        var client = ClientFor(ctx);

        var response = await PostAsync(client, ctx, quantity: 2m);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("created").GetBoolean().Should().BeTrue();
        var vaccinationId = json.GetProperty("id").GetGuid();
        var admin = json.GetProperty("administration");
        admin.GetProperty("vaccinationId").GetGuid().Should().Be(vaccinationId);
        admin.GetProperty("quantity").GetDecimal().Should().Be(2m);
        var stock = json.GetProperty("stock");
        stock.GetProperty("quantityOnHand").GetDecimal().Should().Be(8m);
        stock.GetProperty("isBelowMinimum").GetBoolean().Should().BeFalse();
        (await GetStockAsync(ctx)).Should().Be(8m);

        var detail = await (await client.GetAsync($"/api/v1/vaccinations/{vaccinationId:D}")).Content.ReadFromJsonAsync<JsonElement>();
        var linked = detail.GetProperty("administration");
        linked.GetProperty("administrationId").GetGuid().Should().Be(admin.GetProperty("id").GetGuid());
        linked.GetProperty("productId").GetGuid().Should().Be(ctx.ProductId);
        linked.GetProperty("quantity").GetDecimal().Should().Be(2m);

        var list = await (await client.GetAsync($"/api/v1/vaccinations?clinicId={ctx.ClinicId:D}&petId={ctx.PetId:D}")).Content.ReadFromJsonAsync<JsonElement>();
        list.GetProperty("items").EnumerateArray().Single()
            .GetProperty("administrationId").GetGuid().Should().Be(admin.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Post_WithProduct_DefaultQuantity_Should_Be_One()
    {
        var ctx = await SeedAsync(stock: 10m);

        var response = await PostAsync(ClientFor(ctx), ctx, quantity: null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await GetStockAsync(ctx)).Should().Be(9m);
    }

    [Fact]
    public async Task Post_WithProduct_InsufficientStock_Should_Return400_And_Leave_No_Vaccination()
    {
        var ctx = await SeedAsync(stock: 1m);

        var response = await PostAsync(ClientFor(ctx), ctx, quantity: 5m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("Administrations.InsufficientStock");
        (await GetStockAsync(ctx)).Should().Be(1m);
        (await VaccinationCountAsync(ctx)).Should().Be(0);
        (await AdministrationCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Post_WithProduct_NotApplied_Should_Return400()
    {
        var ctx = await SeedAsync(stock: 10m);

        var response = await PostAsync(ClientFor(ctx), ctx, quantity: 1m, status: VaccinationStatus.Scheduled);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("Vaccinations.ProductRequiresAppliedStatus");
        (await GetStockAsync(ctx)).Should().Be(10m);
    }

    [Fact]
    public async Task Post_WithProduct_Without_AdministrationsCreate_Should_Return403()
    {
        var ctx = await SeedAsync(stock: 10m, permissions: new[] { "Vaccinations.Create", "Vaccinations.Read" });

        var response = await PostAsync(ClientFor(ctx), ctx, quantity: 1m);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(response)).Should().Be("Vaccinations.AdministrationForbidden");
        (await VaccinationCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Post_SameRequestId_Should_Return200_And_Drop_Stock_Once()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();

        var first = await PostAsync(client, ctx, quantity: 3m, requestId: requestId);
        var second = await PostAsync(client, ctx, quantity: 3m, requestId: requestId);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstJson = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondJson = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondJson.GetProperty("created").GetBoolean().Should().BeFalse();
        secondJson.GetProperty("id").GetGuid().Should().Be(firstJson.GetProperty("id").GetGuid());
        (await GetStockAsync(ctx)).Should().Be(7m);
        (await VaccinationCountAsync(ctx)).Should().Be(1);

        var different = await PostAsync(client, ctx, quantity: 4m, requestId: requestId);
        different.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetStockAsync(ctx)).Should().Be(7m);
        (await VaccinationCountAsync(ctx)).Should().Be(1);
    }

    [Fact]
    public async Task Post_Concurrent_SameRequestId_Should_Never500_And_Create_One_Pair()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAsync(client, ctx, quantity: 2m, requestId: requestId)));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        (await GetStockAsync(ctx)).Should().Be(8m);
        (await VaccinationCountAsync(ctx)).Should().Be(1);
        (await AdministrationCountAsync(ctx)).Should().Be(1);
    }

    [Theory]
    [InlineData("StockReturned", 10)]
    [InlineData("WrittenOff", 8)]
    public async Task VoidAdministration_Should_Cancel_Linked_Vaccination(string outcome, int expectedStock)
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var created = await (await PostAsync(client, ctx, quantity: 2m)).Content.ReadFromJsonAsync<JsonElement>();
        var vaccinationId = created.GetProperty("id").GetGuid();
        var administrationId = created.GetProperty("administration").GetProperty("id").GetGuid();

        var voided = await client.PostAsJsonAsync(
            $"/api/v1/administrations/{administrationId:D}/void",
            new { reason = "Yanlış ürün girildi", stockOutcome = outcome });

        voided.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetStockAsync(ctx)).Should().Be(expectedStock);
        var detail = await (await client.GetAsync($"/api/v1/vaccinations/{vaccinationId:D}")).Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("status").GetInt32().Should().Be((int)VaccinationStatus.Cancelled);
        detail.GetProperty("appliedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);

        var again = await client.PostAsJsonAsync(
            $"/api/v1/administrations/{administrationId:D}/void",
            new { reason = "Tekrar iptal", stockOutcome = outcome });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Put_On_Linked_Vaccination_Should_Lock_Core_Fields_But_Allow_Notes()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var created = await (await PostAsync(client, ctx, quantity: 1m)).Content.ReadFromJsonAsync<JsonElement>();
        var vaccinationId = created.GetProperty("id").GetGuid();
        var appliedAt = created.GetProperty("administration").GetProperty("administeredAtUtc").GetDateTime();

        var cancel = await client.PutAsJsonAsync($"/api/v1/vaccinations/{vaccinationId:D}", UpdateBody(ctx, VaccinationStatus.Cancelled, null, "iptal"));
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(cancel)).Should().Be("Vaccinations.LinkedToAdministration");

        var notes = await client.PutAsJsonAsync($"/api/v1/vaccinations/{vaccinationId:D}", UpdateBody(ctx, VaccinationStatus.Applied, appliedAt, "Yeni not"));
        notes.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await (await client.GetAsync($"/api/v1/vaccinations/{vaccinationId:D}")).Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("notes").GetString().Should().Be("Yeni not");
    }

    [Fact]
    public async Task Timeline_Should_Show_Linked_Administration_As_Vaccination_Only()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var created = await (await PostAsync(client, ctx, quantity: 1m)).Content.ReadFromJsonAsync<JsonElement>();
        var vaccinationId = created.GetProperty("id").GetGuid();

        var timeline = await (await client.GetAsync($"/api/v1/pets/{ctx.PetId:D}/timeline")).Content.ReadFromJsonAsync<JsonElement>();

        var rows = timeline.GetProperty("items").EnumerateArray().ToList();
        rows.Should().ContainSingle(r => r.GetProperty("type").GetString() == "Vaccination"
                                         && r.GetProperty("sourceId").GetGuid() == vaccinationId);
        rows.Should().NotContain(r => r.GetProperty("type").GetString() == "Administration");
    }

    // ---- yardımcılar ----

    private HttpClient ClientFor(SeedCtx ctx)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        SeedCtx ctx,
        decimal? quantity = null,
        Guid? requestId = null,
        bool includeProduct = true,
        VaccinationStatus status = VaccinationStatus.Applied)
        => client.PostAsJsonAsync("/api/v1/vaccinations", new
        {
            clinicId = ctx.ClinicId,
            petId = ctx.PetId,
            vaccineDefinitionId = ctx.VaccineDefinitionId,
            status,
            appliedAtUtc = status == VaccinationStatus.Applied ? DateTime.UtcNow.AddMinutes(-5) : (DateTime?)null,
            dueAtUtc = status == VaccinationStatus.Scheduled ? DateTime.UtcNow.AddDays(7) : (DateTime?)null,
            productId = includeProduct ? ctx.ProductId : (Guid?)null,
            quantity = includeProduct ? quantity : null,
            requestId = includeProduct ? requestId : null,
        });

    private static object UpdateBody(SeedCtx ctx, VaccinationStatus status, DateTime? appliedAt, string notes)
        => new
        {
            clinicId = ctx.ClinicId,
            petId = ctx.PetId,
            vaccineDefinitionId = ctx.VaccineDefinitionId,
            status,
            appliedAtUtc = appliedAt,
            notes,
        };

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<decimal> GetStockAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProductStocks.AsNoTracking()
            .Where(s => s.ClinicId == ctx.ClinicId && s.ProductId == ctx.ProductId)
            .Select(s => s.QuantityOnHand)
            .SingleOrDefaultAsync();
    }

    private async Task<int> AdministrationCountAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Administrations.AsNoTracking().CountAsync(a => a.ProductId == ctx.ProductId);
    }

    private async Task<int> VaccinationCountAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Vaccinations.AsNoTracking().CountAsync(v => v.PetId == ctx.PetId);
    }

    private async Task<SeedCtx> SeedAsync(decimal stock, decimal minimum = 0m, string[]? permissions = null)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"vac-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Va-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var product = new Product(tenant.Id, $"vp-{Guid.NewGuid():N}"[..14], "Doz", 2m, "TRY", DateTime.UtcNow, sku: $"VSK-{Guid.NewGuid():N}"[..14]);
        db.Products.Add(product);
        var client = new Client(tenant.Id, $"VacClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110099");
        db.Clients.Add(client);
        var definition = VaccineDefinition.CreateGlobal($"V{Guid.NewGuid():N}"[..8], "Test karma aşı", DateTime.UtcNow);
        db.VaccineDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, $"VacPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        db.ProductStocks.Add(new ProductStock(tenant.Id, clinic.Id, product.Id, stock, minimum, DateTime.UtcNow));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, permissions ?? AllPermissions, includeClinicClaimInJwt: true);

        return new SeedCtx(tenant.Id, clinic.Id, product.Id, pet.Id, definition.Id, token);
    }

    private sealed record SeedCtx(Guid TenantId, Guid ClinicId, Guid ProductId, Guid PetId, Guid VaccineDefinitionId, string Token);
}
