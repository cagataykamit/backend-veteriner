using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.DrugGuide;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.DrugGuide;

/// <summary>
/// İlaç rehberi (REF-001 Dilim 1–2) gerçek veritabanı üzerinden. Katalog testte elle yazılmış örnek kayıtlarla kurulur
/// (Bakanlık verisi değildir); adlar her testte benzersiz etiket taşır.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class DrugGuideEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DrugGuideEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed record Arranged(HttpClient Http, Guid ClinicId, Guid PetId, Guid PetSpeciesId, string Tag, Guid IngredientId, Guid DogProductId, Guid OtherProductId);

    private async Task<Arranged> ArrangeAsync(params string[] permissions)
    {
        var http = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, clinicId) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, permissions, clinicId);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var petSpeciesId = await db.Pets.Where(p => p.Id == petId).Select(p => p.SpeciesId).SingleAsync();
        var otherSpeciesId = await db.Species.Where(s => s.Id != petSpeciesId).Select(s => s.Id).FirstAsync();

        var ingredient = new ActiveIngredient($"Örnekmaddesi {tag}");
        var second = new ActiveIngredient($"İkincimadde {tag}");
        db.AddRange(ingredient, second);
        var forPet = new LicensedProduct($"Örnek Tablet {tag}", $"Örnek İlaç {tag} A.Ş.", "R-1", "https://ornek.gov.tr/oz.pdf", "TEST",
            [ingredient.Id, second.Id], [new LicensedProductTargetSpeciesDraft("Evcil", petSpeciesId)], DateTime.UtcNow);
        var forOther = new LicensedProduct($"Başka Şurup {tag}", $"Deneme Sağlık {tag}", null, null, "TEST",
            [ingredient.Id], [new LicensedProductTargetSpeciesDraft("Diğer", otherSpeciesId)], DateTime.UtcNow);
        db.AddRange(forPet, forOther);
        await db.SaveChangesAsync();

        return new Arranged(http, clinicId, petId, petSpeciesId, tag, ingredient.Id, forPet.Id, forOther.Id);
    }

    private static readonly string[] AllPermissions =
        [PermissionCatalog.DrugGuide.Read, PermissionCatalog.Products.Read, PermissionCatalog.Products.Update];

    [Fact]
    public async Task Search_Should_Match_By_Product_Ingredient_And_Holder_Ignoring_Turkish_Case()
    {
        var a = await ArrangeAsync(AllPermissions);

        async Task<List<string>> NamesAsync(string query)
        {
            var page = await a.Http.GetFromJsonAsync<JsonElement>($"/api/v1/drug-guide/products?{query}");
            return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();
        }

        (await NamesAsync($"search=ornek tablet {a.Tag}&field=Product")).Should().Equal($"Örnek Tablet {a.Tag}");
        (await NamesAsync($"search=ORNEKMADDESI {a.Tag}&field=Ingredient")).Should().HaveCount(2);
        (await NamesAsync($"search=deneme saglik {a.Tag}&field=Holder")).Should().Equal($"Başka Şurup {a.Tag}");
        (await NamesAsync($"search=ikincimadde {a.Tag}")).Should().Equal($"Örnek Tablet {a.Tag}");
        (await NamesAsync($"activeIngredientId={a.IngredientId}")).Should().HaveCount(2);

        var detail = await a.Http.GetFromJsonAsync<JsonElement>($"/api/v1/drug-guide/products/{a.DogProductId}");
        detail.GetProperty("activeIngredients").EnumerateArray().Select(i => i.GetProperty("name").GetString())
            .Should().Equal($"Örnekmaddesi {a.Tag}", $"İkincimadde {a.Tag}");
        detail.GetProperty("licenseHolder").GetString().Should().Be($"Örnek İlaç {a.Tag} A.Ş.");
        detail.GetProperty("spcUrl").GetString().Should().Be("https://ornek.gov.tr/oz.pdf");
        detail.GetProperty("isListedInSource").GetBoolean().Should().BeTrue();

        var ingredients = await a.Http.GetFromJsonAsync<JsonElement>($"/api/v1/drug-guide/active-ingredients?search=ornekmaddesi {a.Tag}");
        var row = ingredients.EnumerateArray().Should().ContainSingle().Subject;
        row.GetProperty("productCount").GetInt32().Should().Be(2);

        var summary = await a.Http.GetFromJsonAsync<JsonElement>("/api/v1/drug-guide/summary");
        summary.GetProperty("productCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        summary.GetProperty("lastSyncedAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        (await a.Http.GetAsync($"/api/v1/drug-guide/products/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Linked_Product_Should_Show_Ingredients_Species_Note_And_Stock_By_Ingredient()
    {
        var a = await ArrangeAsync(AllPermissions);
        Guid cardForPet, cardForOther;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantId = await db.Clinics.Where(c => c.Id == a.ClinicId).Select(c => c.TenantId).SingleAsync();
            var p1 = new Product(tenantId, $"Klinik tablet {a.Tag}", "Kutu", 10m, "TRY", DateTime.UtcNow);
            var p2 = new Product(tenantId, $"Klinik şurup {a.Tag}", "Şişe", 20m, "TRY", DateTime.UtcNow);
            db.Products.AddRange(p1, p2);
            db.ProductStocks.Add(new ProductStock(tenantId, a.ClinicId, p1.Id, 7m, 0m, DateTime.UtcNow));
            await db.SaveChangesAsync();
            (cardForPet, cardForOther) = (p1.Id, p2.Id);
        }

        (await a.Http.PutAsJsonAsync($"/api/v1/products/{cardForPet}/licensed-product", new { LicensedProductId = a.DogProductId }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await a.Http.PutAsJsonAsync($"/api/v1/products/{cardForOther}/licensed-product", new { LicensedProductId = a.OtherProductId }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await a.Http.PutAsJsonAsync($"/api/v1/products/{cardForOther}/licensed-product", new { LicensedProductId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var card = await a.Http.GetFromJsonAsync<JsonElement>($"/api/v1/products/{cardForPet}");
        card.GetProperty("licensedProductId").GetGuid().Should().Be(a.DogProductId);

        var info = await a.Http.GetFromJsonAsync<JsonElement>(
            $"/api/v1/drug-guide/product-info?productIds={cardForPet}&productIds={cardForOther}&petId={a.PetId}");
        var byProduct = info.EnumerateArray().ToDictionary(i => i.GetProperty("productId").GetGuid());
        byProduct[cardForPet].GetProperty("notLicensedForPetSpecies").GetBoolean().Should().BeFalse();
        byProduct[cardForOther].GetProperty("notLicensedForPetSpecies").GetBoolean().Should().BeTrue();
        byProduct[cardForOther].GetProperty("petSpeciesName").GetString().Should().NotBeNullOrEmpty();
        byProduct[cardForPet].GetProperty("activeIngredients").GetArrayLength().Should().Be(2);

        var stock = await a.Http.GetFromJsonAsync<JsonElement>($"/api/v1/drug-guide/active-ingredients/{a.IngredientId}/stock-products");
        var rows = stock.EnumerateArray().ToDictionary(r => r.GetProperty("productId").GetGuid());
        rows.Should().HaveCount(2);
        rows[cardForPet].GetProperty("quantityOnHand").GetDecimal().Should().Be(7m);
        rows[cardForOther].GetProperty("quantityOnHand").GetDecimal().Should().Be(0m);

        // Bağ kaldırılınca ürün rehber bilgisinde görünmez.
        (await a.Http.PutAsJsonAsync($"/api/v1/products/{cardForOther}/licensed-product", new { LicensedProductId = (Guid?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await a.Http.GetFromJsonAsync<JsonElement>($"/api/v1/drug-guide/product-info?productIds={cardForOther}");
        after.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Endpoints_Should_Require_DrugGuide_Read_And_Products_Read_For_Stock()
    {
        var noGuide = await ArrangeAsync(PermissionCatalog.Products.Read);
        (await noGuide.Http.GetAsync("/api/v1/drug-guide/products")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var guideOnly = await ArrangeAsync(PermissionCatalog.DrugGuide.Read);
        (await guideOnly.Http.GetAsync("/api/v1/drug-guide/products")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await guideOnly.Http.GetAsync($"/api/v1/drug-guide/active-ingredients/{guideOnly.IngredientId}/stock-products"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
