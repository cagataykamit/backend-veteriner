using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.ChargeSuggestions;

/// <summary>
/// Ücret önerisi (ADR-018): gerçek SQL Server/LocalDB üzerinde uygulamadan kalem doğması, tek tıkla ücrete çevirme,
/// çift ücretin önlenmesi (requestId, eşzamanlılık), iptal kuralları, ücretsiz sayma, hekim okuma/yazma ayrımı ve hizmet kapıları.
/// </summary>
[Collection("products-api")]
public sealed class ChargeSuggestionsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private const decimal Price = 250m;

    private static readonly string[] ReceptionPermissions =
    {
        "Administrations.Create", "Administrations.Read", "Administrations.Correct",
        "Charges.Read", "Charges.Create", "Charges.Correct", "Examinations.Read", "Products.Read",
    };

    private static readonly string[] VeterinarianPermissions =
    {
        "Administrations.Create", "Administrations.Read", "Charges.Read", "Examinations.Read", "Products.Read",
    };

    private readonly CustomWebApplicationFactory _factory;

    public ChargeSuggestionsEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Administration_Should_Create_Pending_Item_Visible_To_Veterinarian_And_In_Examination_Summary()
    {
        var ctx = await SeedAsync();
        var vet = await ClientAsync(ctx, VeterinarianPermissions);
        var administrationId = await AdministerAsync(vet, ctx, 2m);

        var items = await PendingAsync(vet, ctx);

        var item = items.Should().ContainSingle().Subject;
        item.GetProperty("sourceType").GetString().Should().Be("Administration");
        item.GetProperty("sourceId").GetGuid().Should().Be(administrationId);
        item.GetProperty("examinationId").GetGuid().Should().Be(ctx.ExaminationId);
        item.GetProperty("clientId").GetGuid().Should().Be(ctx.ClientId);
        item.GetProperty("quantity").GetDecimal().Should().Be(2m);
        item.GetProperty("suggestedUnitPrice").GetDecimal().Should().Be(Price);
        item.GetProperty("status").GetString().Should().Be("Pending");
        (await PendingCountAsync(vet, ctx)).Should().Be(1);

        // Hekim okur ama ücret ekleyemez (ADR-012 K2).
        var convert = await vet.PostAsJsonAsync($"/api/v1/charge-suggestions/{Id(item)}/convert", new { });
        convert.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var waive = await vet.PostAsJsonAsync($"/api/v1/charge-suggestions/{Id(item)}/waive", new { reason = "Hekim ücretsiz saydı" });
        waive.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ChargeCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Convert_Should_Create_One_Charge_From_Prefilled_Values_And_Replay_By_RequestId()
    {
        var ctx = await SeedAsync();
        var client = await ClientAsync(ctx, ReceptionPermissions);
        await AdministerAsync(client, ctx, 2m);
        var id = Id((await PendingAsync(client, ctx)).Single());
        var requestId = Guid.NewGuid();

        var first = await client.PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/convert", new { requestId });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var charge = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("charge");
        charge.GetProperty("amount").GetDecimal().Should().Be(500m);
        charge.GetProperty("clientId").GetGuid().Should().Be(ctx.ClientId);
        charge.GetProperty("petId").GetGuid().Should().Be(ctx.PetId);
        charge.GetProperty("examinationId").GetGuid().Should().Be(ctx.ExaminationId);
        (await PendingAsync(client, ctx)).Should().BeEmpty();
        (await PendingCountAsync(client, ctx)).Should().Be(0);

        var replay = await client.PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/convert", new { requestId });
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayJson = await replay.Content.ReadFromJsonAsync<JsonElement>();
        replayJson.GetProperty("created").GetBoolean().Should().BeFalse();
        replayJson.GetProperty("charge").GetProperty("id").GetGuid().Should().Be(charge.GetProperty("id").GetGuid());

        var second = await client.PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/convert", new { requestId = Guid.NewGuid() });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(second)).Should().Be("ChargeSuggestions.AlreadyConverted");
        (await ChargeCountAsync(ctx)).Should().Be(1);
    }

    [Fact]
    public async Task Convert_Should_Use_Edited_Values()
    {
        var ctx = await SeedAsync();
        var client = await ClientAsync(ctx, ReceptionPermissions);
        await AdministerAsync(client, ctx, 2m);
        var id = Id((await PendingAsync(client, ctx)).Single());

        var response = await client.PostAsJsonAsync(
            $"/api/v1/charge-suggestions/{id}/convert", new { description = "Kuduz aşısı (indirimli)", quantity = 1m, unitPrice = 199.90m });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var charge = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("charge");
        charge.GetProperty("description").GetString().Should().Be("Kuduz aşısı (indirimli)");
        charge.GetProperty("amount").GetDecimal().Should().Be(199.90m);
    }

    [Fact]
    public async Task Concurrent_Converts_Should_Create_Exactly_One_Charge_Without_500()
    {
        var ctx = await SeedAsync();
        var client = await ClientAsync(ctx, ReceptionPermissions);
        await AdministerAsync(client, ctx, 1m);
        var id = Id((await PendingAsync(client, ctx)).Single());

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            client.PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/convert", new { requestId = Guid.NewGuid() })));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await ChargeCountAsync(ctx)).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_Same_RequestId_Should_Create_One_Charge()
    {
        var ctx = await SeedAsync();
        var client = await ClientAsync(ctx, ReceptionPermissions);
        await AdministerAsync(client, ctx, 1m);
        var id = Id((await PendingAsync(client, ctx)).Single());
        var requestId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/convert", new { requestId })));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        (await ChargeCountAsync(ctx)).Should().Be(1);
    }

    [Fact]
    public async Task Voided_Charge_Should_Not_Reopen_Item()
    {
        var ctx = await SeedAsync();
        var client = await ClientAsync(ctx, ReceptionPermissions);
        await AdministerAsync(client, ctx, 1m);
        var id = Id((await PendingAsync(client, ctx)).Single());
        var chargeId = (await (await client.PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/convert", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("charge").GetProperty("id").GetGuid();

        var voided = await client.PostAsJsonAsync($"/api/v1/charges/{chargeId}/void", new { reason = "Yanlış ücret kesildi" });

        voided.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PendingAsync(client, ctx)).Should().BeEmpty();
        var again = await client.PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/convert", new { });
        (await ReadProblemCodeAsync(again)).Should().Be("ChargeSuggestions.AlreadyConverted");
    }

    [Fact]
    public async Task Voided_Administration_Should_Drop_Pending_Item_But_Keep_Existing_Charge()
    {
        var ctx = await SeedAsync();
        var client = await ClientAsync(ctx, ReceptionPermissions);
        var dropped = await AdministerAsync(client, ctx, 1m);
        var charged = await AdministerAsync(client, ctx, 1m);
        var items = await PendingAsync(client, ctx);
        var droppedId = Id(items.Single(i => i.GetProperty("sourceId").GetGuid() == dropped));
        var chargedId = Id(items.Single(i => i.GetProperty("sourceId").GetGuid() == charged));
        (await client.PostAsJsonAsync($"/api/v1/charge-suggestions/{chargedId}/convert", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        foreach (var administrationId in new[] { dropped, charged })
        {
            (await client.PostAsJsonAsync($"/api/v1/administrations/{administrationId}/void", new { reason = "Yanlış ürün seçildi" }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await PendingAsync(client, ctx)).Should().BeEmpty();
        (await StatusAsync(droppedId)).Should().Be(ChargeSuggestionStatus.Cancelled);
        (await StatusAsync(chargedId)).Should().Be(ChargeSuggestionStatus.Converted);
        (await ChargeCountAsync(ctx)).Should().Be(1, "kesilmiş ücret kendiliğinden değişmez; düzeltme yöneticide");
        var convert = await client.PostAsJsonAsync($"/api/v1/charge-suggestions/{droppedId}/convert", new { });
        convert.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(convert)).Should().Be("ChargeSuggestions.SourceAlreadyVoided");
    }

    [Fact]
    public async Task Waive_Should_Require_Reason_Drop_Item_And_Block_Conversion()
    {
        var ctx = await SeedAsync();
        var client = await ClientAsync(ctx, ReceptionPermissions);
        await AdministerAsync(client, ctx, 1m);
        var id = Id((await PendingAsync(client, ctx)).Single());

        var tooShort = await client.PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/waive", new { reason = "yok" });
        tooShort.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(tooShort)).Should().Be("ChargeSuggestions.Validation");

        var waived = await client.PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/waive", new { reason = "Kontrol muayenesi ücretsiz" });
        waived.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await waived.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("status").GetString().Should().Be("Waived");
        dto.GetProperty("waiveReason").GetString().Should().Be("Kontrol muayenesi ücretsiz");

        (await PendingAsync(client, ctx)).Should().BeEmpty();
        var convert = await client.PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/convert", new { });
        (await ReadProblemCodeAsync(convert)).Should().Be("ChargeSuggestions.AlreadyWaived");
        var again = await client.PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/waive", new { reason = "Tekrar ücretsiz say" });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ChargeCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Free_Catalog_Product_Should_Not_Create_Item()
    {
        var ctx = await SeedAsync(price: 0m);
        var client = await ClientAsync(ctx, ReceptionPermissions);

        await AdministerAsync(client, ctx, 1m);

        (await PendingAsync(client, ctx)).Should().BeEmpty();
    }

    [Fact]
    public async Task Service_Product_Should_Be_Rejected_By_Stock_Surfaces()
    {
        var ctx = await SeedAsync(kind: ProductKind.Service, stock: null);
        var client = await ClientAsync(ctx, ReceptionPermissions);

        var response = await PostAdministrationAsync(client, ctx, 1m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("Products.NotStockTracked");
        (await PendingAsync(client, ctx)).Should().BeEmpty();
    }

    [Fact]
    public async Task List_Should_Require_Clinic_Scope_And_Not_Cross_Tenants()
    {
        var ctx = await SeedAsync();
        var client = await ClientAsync(ctx, ReceptionPermissions);
        await AdministerAsync(client, ctx, 1m);
        var id = Id((await PendingAsync(client, ctx)).Single());

        var noScope = await (await ClientAsync(ctx, ReceptionPermissions, includeClinicClaim: false)).GetAsync("/api/v1/charge-suggestions");
        noScope.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(noScope)).Should().Be("ChargeSuggestions.ClinicScopeRequired");

        var other = await SeedAsync();
        var foreign = await (await ClientAsync(other, ReceptionPermissions)).PostAsJsonAsync($"/api/v1/charge-suggestions/{id}/convert", new { });
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ChargeCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task List_By_ClientId_Should_Return_Items_Of_All_Client_Pets_In_One_Request()
    {
        var ctx = await SeedAsync();
        var secondPet = await AddPetWithExaminationAsync(ctx);
        var reception = await ClientAsync(ctx, ReceptionPermissions);
        await AdministerAsync(reception, ctx, 1m);
        await AdministerAsync(reception, secondPet, 2m);

        var response = await reception.GetAsync($"/api/v1/charge-suggestions?clinicId={ctx.ClinicId:D}&clientId={ctx.ClientId:D}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalItems").GetInt32().Should().Be(2);
        body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("petId").GetGuid())
            .Should().BeEquivalentTo(new[] { ctx.PetId, secondPet.PetId });

        // Süzgeç yoksa davranış değişmez; başka müşteri boş döner.
        var otherClientId = await AddClientAsync(ctx.TenantId);
        var other = await reception.GetAsync($"/api/v1/charge-suggestions?clinicId={ctx.ClinicId:D}&clientId={otherClientId:D}");
        (await other.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalItems").GetInt32().Should().Be(0);
        var all = await reception.GetAsync($"/api/v1/charge-suggestions?clinicId={ctx.ClinicId:D}");
        (await all.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalItems").GetInt32().Should().Be(2);
    }

    // ---- yardımcılar ----

    private async Task<SeedCtx> AddPetWithExaminationAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(ctx.TenantId, ctx.ClientId, $"CsPet2-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        var exam = new Examination(ctx.TenantId, ctx.ClinicId, pet.Id, appointmentId: null, DateTime.UtcNow.AddHours(-1), "Aşı", "Bulgu yok", null, null);
        db.Examinations.Add(exam);
        await db.SaveChangesAsync();
        return ctx with { PetId = pet.Id, ExaminationId = exam.Id };
    }

    private async Task<Guid> AddClientAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = new Client(tenantId, $"CsOther-{Guid.NewGuid():N}"[..14], "905551110078");
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> PostAdministrationAsync(HttpClient client, SeedCtx ctx, decimal quantity)
        => client.PostAsJsonAsync("/api/v1/administrations", new
        {
            clinicId = ctx.ClinicId,
            petId = ctx.PetId,
            productId = ctx.ProductId,
            quantity,
            examinationId = ctx.ExaminationId,
        });

    private static async Task<Guid> AdministerAsync(HttpClient client, SeedCtx ctx, decimal quantity)
    {
        var created = await PostAdministrationAsync(client, ctx, quantity);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration").GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> PendingAsync(HttpClient client, SeedCtx ctx)
    {
        var response = await client.GetAsync($"/api/v1/charge-suggestions?clinicId={ctx.ClinicId:D}&petId={ctx.PetId:D}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToList();
    }

    private static async Task<int?> PendingCountAsync(HttpClient client, SeedCtx ctx)
    {
        var response = await client.GetAsync($"/api/v1/examinations/{ctx.ExaminationId:D}/related-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var count = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pendingChargeCount");
        return count.ValueKind == JsonValueKind.Null ? null : count.GetInt32();
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<int> ChargeCountAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Charges.AsNoTracking().IgnoreQueryFilters().CountAsync(c => c.PetId == ctx.PetId);
    }

    private async Task<ChargeSuggestionStatus> StatusAsync(Guid suggestionId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ChargeSuggestions.AsNoTracking().Where(s => s.Id == suggestionId).Select(s => s.Status).SingleAsync();
    }

    private async Task<HttpClient> ClientAsync(SeedCtx ctx, string[] permissions, bool includeClinicClaim = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, ctx.TenantId, ctx.ClinicId, permissions, includeClinicClaimInJwt: includeClinicClaim);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<SeedCtx> SeedAsync(decimal price = Price, ProductKind kind = ProductKind.Other, decimal? stock = 20m)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = new Tenant($"cs-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Cs-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var product = new Product(tenant.Id, $"cp-{Guid.NewGuid():N}"[..14], "Doz", price, "TRY", sku: $"CSK-{Guid.NewGuid():N}"[..14], kind: kind);
        db.Products.Add(product);
        var owner = new Client(tenant.Id, $"CsClient-{Guid.NewGuid():N}"[..14], "905551110077");
        db.Clients.Add(owner);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, owner.Id, $"CsPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        if (stock is { } onHand)
            db.ProductStocks.Add(new ProductStock(tenant.Id, clinic.Id, product.Id, onHand, 0m));
        await db.SaveChangesAsync();

        var exam = new Examination(tenant.Id, clinic.Id, pet.Id, appointmentId: null, DateTime.UtcNow.AddHours(-1), "Aşı", "Bulgu yok", null, null);
        db.Examinations.Add(exam);
        await db.SaveChangesAsync();

        return new SeedCtx(tenant.Id, clinic.Id, product.Id, owner.Id, pet.Id, exam.Id);
    }

    private sealed record SeedCtx(Guid TenantId, Guid ClinicId, Guid ProductId, Guid ClientId, Guid PetId, Guid ExaminationId);
}
