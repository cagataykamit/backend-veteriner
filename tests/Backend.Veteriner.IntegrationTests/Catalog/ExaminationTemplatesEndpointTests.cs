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
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Catalog;

/// <summary>
/// Muayene şablonu (ADR-020 S4): gerçek SQL Server/LocalDB üzerinde şablon yönetimi (sistem salt okunur, RowVersion/409,
/// kiracı ayrımı) ve muayeneye uygulama (metin birleştirme, stok düşümü ve ücret önerisi mevcut ADR-018 akışıyla, tek transaction).
/// </summary>
[Collection("products-api")]
public sealed class ExaminationTemplatesEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/v1/examination-templates";
    private const decimal DrugPrice = 45m;
    private const decimal ServicePrice = 300m;

    private static readonly string[] VeterinarianPermissions =
    [
        "ExaminationTemplates.Read", "ExaminationTemplates.Create", "ExaminationTemplates.Update", "ExaminationTemplates.Apply",
        "Examinations.Read", "Examinations.Update", "Administrations.Create", "Administrations.Read", "Products.Read",
    ];

    private static readonly string[] ManagerPermissions = [.. VeterinarianPermissions, "ExaminationTemplates.Deactivate"];

    private readonly CustomWebApplicationFactory _factory;

    public ExaminationTemplatesEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ExaminationTemplateSeeder.SeedAsync(db).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Endpoints_Should_Require_Permissions()
    {
        var ctx = await SeedAsync();
        (await _factory.CreateClient().GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var reader = await ClientAsync(ctx, ["ExaminationTemplates.Read", "Examinations.Read"]);
        (await reader.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync(Url, new { name = "Deneme", visitReason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var system = await SystemTemplateAsync(reader, "Rutin muayene");
        var apply = await reader.PostAsJsonAsync(
            $"/api/v1/examinations/{ctx.ExaminationId}/apply-template",
            new { templateId = Id(system), rowVersion = await ExamRowVersionAsync(ctx) });
        apply.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Uygulama izni tek başına yetmez: muayene güncelleme ve uygulama kaydı izni de gerekir.
        var applyOnly = await ClientAsync(ctx, ["ExaminationTemplates.Read", "ExaminationTemplates.Apply"]);
        var applyOnlyResponse = await applyOnly.PostAsJsonAsync(
            $"/api/v1/examinations/{ctx.ExaminationId}/apply-template",
            new { templateId = Id(system), rowVersion = await ExamRowVersionAsync(ctx) });
        applyOnlyResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task System_Templates_Should_Be_Listed_Read_Only_And_Block_Same_Name()
    {
        var ctx = await SeedAsync();
        var client = await ClientAsync(ctx, ManagerPermissions);

        var system = await SystemTemplateAsync(client, "Rutin muayene");
        system.GetProperty("isSystem").GetBoolean().Should().BeTrue();
        system.GetProperty("lines").GetArrayLength().Should().Be(0);

        var update = await client.PutAsJsonAsync($"{Url}/{Id(system)}", new
        {
            name = "Rutin muayene X", visitReason = "x", lines = Array.Empty<object>(), rowVersion = system.GetProperty("rowVersion").GetString(),
        });
        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(update)).Should().Be("ExaminationTemplates.SystemTemplateReadOnly");

        var deactivate = await client.PostAsync($"{Url}/{Id(system)}/deactivate", null);
        (await ProblemCodeAsync(deactivate)).Should().Be("ExaminationTemplates.SystemTemplateReadOnly");

        var sameName = await client.PostAsJsonAsync(Url, new { name = "RUTİN MUAYENE", visitReason = "x" });
        sameName.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(sameName)).Should().Be("ExaminationTemplates.DuplicateName");
    }

    [Fact]
    public async Task Create_Update_Conflict_Deactivate_Activate_Flow_Should_Work()
    {
        var ctx = await SeedAsync();
        var client = await ClientAsync(ctx, ManagerPermissions);

        var create = await client.PostAsJsonAsync(Url, new
        {
            name = "  Kulak   paketi ",
            visitReason = "Kulak kaşıntısı",
            findings = "Otoskopi: ",
            lines = new object[]
            {
                new { productId = ctx.ServiceId, quantity = 1m },
                new { productId = ctx.DrugId, quantity = 2m },
            },
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = Id(created);
        created.GetProperty("name").GetString().Should().Be("Kulak paketi");
        var lines = created.GetProperty("lines").EnumerateArray().ToList();
        lines.Select(l => l.GetProperty("productId").GetGuid()).Should().Equal(ctx.ServiceId, ctx.DrugId);
        lines[0].GetProperty("productKind").GetString().Should().Be("Service");
        lines[1].GetProperty("currentUnitPrice").GetDecimal().Should().Be(DrugPrice);
        var firstVersion = created.GetProperty("rowVersion").GetString();

        var search = await client.GetFromJsonAsync<JsonElement>($"{Url}?search=KULAK");
        search.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).Should().Contain("Kulak paketi");

        var update = await client.PutAsJsonAsync($"{Url}/{id}", new
        {
            name = "Kulak paketi",
            visitReason = "Kulak kaşıntısı",
            plan = "Kontrol: 7 gün",
            lines = new object[] { new { productId = ctx.DrugId, quantity = 1.5m } },
            rowVersion = firstVersion,
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("findings").ValueKind.Should().Be(JsonValueKind.Null);
        updated.GetProperty("lines").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("quantity").GetDecimal().Should().Be(1.5m);
        updated.GetProperty("rowVersion").GetString().Should().NotBe(firstVersion);

        var stale = await client.PutAsJsonAsync($"{Url}/{id}", new
        {
            name = "Kulak paketi", visitReason = "x", lines = Array.Empty<object>(), rowVersion = firstVersion,
        });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(stale)).Should().Be("ExaminationTemplates.ConcurrencyConflict");

        var noLines = await client.PutAsJsonAsync($"{Url}/{id}", new { name = "Kulak paketi", visitReason = "x", rowVersion = firstVersion });
        noLines.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.PostAsync($"{Url}/{id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var active = await client.GetFromJsonAsync<JsonElement>($"{Url}?search=kulak%20paketi");
        active.GetProperty("items").GetArrayLength().Should().Be(0);
        var all = await client.GetFromJsonAsync<JsonElement>($"{Url}?search=kulak%20paketi&includeInactive=true");
        all.GetProperty("items")[0].GetProperty("isActive").GetBoolean().Should().BeFalse();

        (await client.PostAsync($"{Url}/{id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>($"{Url}/{id}")).GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_Reject_Vaccine_Line_And_Other_Tenant_Product()
    {
        var ctx = await SeedAsync();
        var other = await SeedAsync();
        var client = await ClientAsync(ctx, ManagerPermissions);

        var vaccine = await client.PostAsJsonAsync(Url, new
        {
            name = "Aşı paketi", lines = new object[] { new { productId = ctx.VaccineId, quantity = 1m } },
        });
        vaccine.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var vaccineProblem = await vaccine.Content.ReadFromJsonAsync<JsonElement>();
        vaccineProblem.GetProperty("code").GetString().Should().Be("ExaminationTemplates.VaccineNotAllowed");
        vaccineProblem.GetProperty("productId").GetGuid().Should().Be(ctx.VaccineId);

        var foreign = await client.PostAsJsonAsync(Url, new
        {
            name = "Yabancı paket", lines = new object[] { new { productId = other.DrugId, quantity = 1m } },
        });
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(foreign)).Should().Be("Products.NotFound");
    }

    [Fact]
    public async Task Tenant_Templates_Should_Be_Isolated()
    {
        var ctx = await SeedAsync();
        var client = await ClientAsync(ctx, ManagerPermissions);
        var id = await CreateTemplateAsync(client, ctx, drugQuantity: 1m);

        var other = await SeedAsync();
        var otherClient = await ClientAsync(other, ManagerPermissions);
        (await otherClient.GetAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherClient.PostAsync($"{Url}/{id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var apply = await otherClient.PostAsJsonAsync(
            $"/api/v1/examinations/{other.ExaminationId}/apply-template",
            new { templateId = id, rowVersion = await ExamRowVersionAsync(other) });
        apply.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCodeAsync(apply)).Should().Be("ExaminationTemplates.NotFound");
    }

    [Fact]
    public async Task Apply_Should_Merge_Text_Write_Administration_And_Charge_Items_Through_Existing_Flow()
    {
        var ctx = await SeedAsync();
        var vet = await ClientAsync(ctx, VeterinarianPermissions);
        var templateId = await CreateTemplateAsync(vet, ctx, drugQuantity: 2m);
        var firstVersion = await ExamRowVersionAsync(ctx);

        var apply = await vet.PostAsJsonAsync(
            $"/api/v1/examinations/{ctx.ExaminationId}/apply-template", new { templateId, rowVersion = firstVersion });

        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await apply.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("updatedFields").EnumerateArray().Select(f => f.GetString()).Should().Equal("visitReason", "findings", "plan");
        result.GetProperty("administrations").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("productId").GetGuid().Should().Be(ctx.DrugId);
        var service = result.GetProperty("services").EnumerateArray().Should().ContainSingle().Subject;
        service.GetProperty("productId").GetGuid().Should().Be(ctx.ServiceId);
        service.GetProperty("chargeSuggestionId").ValueKind.Should().Be(JsonValueKind.String);
        var secondVersion = result.GetProperty("rowVersion").GetString();
        secondVersion.Should().NotBe(firstVersion);

        var exam = await ExaminationAsync(ctx);
        exam.VisitReason.Should().Be("Aşı, Kulak kaşıntısı");
        exam.Findings.Should().Be("Bulgu yok\nOtoskopi:");
        exam.Plan.Should().Be("Kontrol: 7 gün");
        (await StockAsync(ctx)).Should().Be(18m);

        var suggestions = await SuggestionsAsync(ctx);
        suggestions.Should().HaveCount(2);
        suggestions.Should().ContainSingle(s => s.SourceType == ChargeSuggestion.AdministrationSource && s.SuggestedUnitPrice == DrugPrice);
        suggestions.Should().ContainSingle(s => s.SourceType == ChargeSuggestion.ExaminationServiceSource
                                               && s.SuggestedUnitPrice == ServicePrice && s.ExaminationId == ctx.ExaminationId);

        // Aynı sürümle ikinci istek (çift tıklama) reddedilir; stok değişmez.
        var replay = await vet.PostAsJsonAsync(
            $"/api/v1/examinations/{ctx.ExaminationId}/apply-template", new { templateId, rowVersion = firstVersion });
        replay.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(replay)).Should().Be("Examinations.ConcurrencyConflict");
        (await StockAsync(ctx)).Should().Be(18m);

        // Güncel sürümle bilinçli ikinci uygulama: metin çoğalmaz, satırlar yeniden yazılır.
        var again = await vet.PostAsJsonAsync(
            $"/api/v1/examinations/{ctx.ExaminationId}/apply-template", new { templateId, rowVersion = secondVersion });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("updatedFields").GetArrayLength().Should().Be(0);
        (await ExaminationAsync(ctx)).VisitReason.Should().Be("Aşı, Kulak kaşıntısı");
        (await StockAsync(ctx)).Should().Be(16m);
    }

    [Fact]
    public async Task Apply_Should_Roll_Back_Everything_When_A_Line_Fails()
    {
        var ctx = await SeedAsync(drugStock: 1m);
        var vet = await ClientAsync(ctx, VeterinarianPermissions);
        var templateId = await CreateTemplateAsync(vet, ctx, drugQuantity: 5m);

        var apply = await vet.PostAsJsonAsync(
            $"/api/v1/examinations/{ctx.ExaminationId}/apply-template",
            new { templateId, rowVersion = await ExamRowVersionAsync(ctx) });

        apply.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await apply.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Administrations.InsufficientStock");
        problem.GetProperty("productId").GetGuid().Should().Be(ctx.DrugId);

        var exam = await ExaminationAsync(ctx);
        exam.VisitReason.Should().Be("Aşı");
        exam.Findings.Should().Be("Bulgu yok");
        (await StockAsync(ctx)).Should().Be(1m);
        (await SuggestionsAsync(ctx)).Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_Should_Reject_Finalized_Examination()
    {
        var ctx = await SeedAsync(finalized: true);
        var vet = await ClientAsync(ctx, VeterinarianPermissions);
        var system = await SystemTemplateAsync(vet, "Kontrol muayenesi");

        var apply = await vet.PostAsJsonAsync(
            $"/api/v1/examinations/{ctx.ExaminationId}/apply-template",
            new { templateId = Id(system), rowVersion = await ExamRowVersionAsync(ctx) });

        apply.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(apply)).Should().Be("Examinations.Finalized");
        (await ExaminationAsync(ctx)).VisitReason.Should().Be("Aşı");
    }

    [Fact]
    public async Task Seeder_Should_Be_Idempotent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.ExaminationTemplates.CountAsync(t => t.TenantId == null);

        await ExaminationTemplateSeeder.SeedAsync(db);

        (await db.ExaminationTemplates.CountAsync(t => t.TenantId == null)).Should().Be(before);
        before.Should().Be(ExaminationTemplateSeeder.Templates.Count);
    }

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();

    private static async Task<Guid> CreateTemplateAsync(HttpClient client, SeedCtx ctx, decimal drugQuantity)
    {
        var create = await client.PostAsJsonAsync(Url, new
        {
            name = $"Kulak paketi {Guid.NewGuid():N}"[..20],
            visitReason = "Kulak kaşıntısı",
            findings = "Otoskopi: ",
            plan = "Kontrol: 7 gün",
            lines = new object[]
            {
                new { productId = ctx.ServiceId, quantity = 1m },
                new { productId = ctx.DrugId, quantity = drugQuantity },
            },
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        return Id(await create.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static async Task<JsonElement> SystemTemplateAsync(HttpClient client, string name)
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"{Url}?search={Uri.EscapeDataString(name)}");
        return page.GetProperty("items").EnumerateArray().First(i => i.GetProperty("name").GetString() == name);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<Examination> ExaminationAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Examinations.AsNoTracking().SingleAsync(e => e.Id == ctx.ExaminationId);
    }

    private async Task<string> ExamRowVersionAsync(SeedCtx ctx)
        => Convert.ToBase64String((await ExaminationAsync(ctx)).RowVersion);

    private async Task<decimal> StockAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProductStocks.AsNoTracking()
            .Where(s => s.ClinicId == ctx.ClinicId && s.ProductId == ctx.DrugId)
            .Select(s => s.QuantityOnHand)
            .SingleAsync();
    }

    private async Task<List<ChargeSuggestion>> SuggestionsAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ChargeSuggestions.AsNoTracking().Where(s => s.PetId == ctx.PetId).ToListAsync();
    }

    private async Task<HttpClient> ClientAsync(SeedCtx ctx, string[] permissions)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, ctx.TenantId, ctx.ClinicId, permissions, includeClinicClaimInJwt: true);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<SeedCtx> SeedAsync(decimal drugStock = 20m, bool finalized = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = new Tenant($"et-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Et-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var drug = new Product(tenant.Id, $"Ilac-{Guid.NewGuid():N}"[..14], "Doz", DrugPrice, "TRY", kind: ProductKind.Medicine);
        var service = new Product(tenant.Id, $"Hizmet-{Guid.NewGuid():N}"[..14], "Adet", ServicePrice, "TRY", kind: ProductKind.Service);
        var vaccine = new Product(tenant.Id, $"Asi-{Guid.NewGuid():N}"[..14], "Doz", 100m, "TRY", kind: ProductKind.Vaccine);
        db.Products.AddRange(drug, service, vaccine);
        var owner = new Client(tenant.Id, $"EtClient-{Guid.NewGuid():N}"[..14], "905551110078");
        db.Clients.Add(owner);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, owner.Id, $"EtPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        db.ProductStocks.Add(new ProductStock(tenant.Id, clinic.Id, drug.Id, drugStock, 0m));
        await db.SaveChangesAsync();

        var exam = new Examination(tenant.Id, clinic.Id, pet.Id, appointmentId: null, DateTime.UtcNow.AddHours(-1), "Aşı", "Bulgu yok", null, null);
        if (finalized)
            exam.Finalize(Guid.NewGuid(), DateTime.UtcNow);
        db.Examinations.Add(exam);
        await db.SaveChangesAsync();

        return new SeedCtx(tenant.Id, clinic.Id, drug.Id, service.Id, vaccine.Id, pet.Id, exam.Id);
    }

    private sealed record SeedCtx(
        Guid TenantId, Guid ClinicId, Guid DrugId, Guid ServiceId, Guid VaccineId, Guid PetId, Guid ExaminationId);
}
