using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Prescriptions;

/// <summary>
/// Reçete satırları (Aşama 5 Dilim 3) gerçek veritabanı üzerinden: tam yazma, kısmi PUT yorumu, geriye uyum ve stok bağımsızlığı.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class PrescriptionItemsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PrescriptionItemsIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed record Context(HttpClient Http, Guid PrescriptionId, Guid TenantId, Guid ClinicId, Guid PetId);

    private async Task<Context> ArrangeAsync()
    {
        var http = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, clinicId) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var prescriptionId = await IntegrationTestAuthHelper.SeedPrescriptionInClinicAsync(_factory.Services, clinicId);

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            new[]
            {
                PermissionCatalog.Prescriptions.Read,
                PermissionCatalog.Prescriptions.Update,
                PermissionCatalog.Prescriptions.Create,
            },
            clinicId);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var detail = await GetDetailAsync(http, prescriptionId);
        var root = detail.RootElement;
        return new Context(
            http,
            prescriptionId,
            root.GetProperty("tenantId").GetGuid(),
            root.GetProperty("clinicId").GetGuid(),
            root.GetProperty("petId").GetGuid());
    }

    private static async Task<JsonDocument> GetDetailAsync(HttpClient http, Guid id)
    {
        var response = await http.GetAsync($"/api/v1/prescriptions/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PutAsync(Context c, object? items, bool includeItems = true)
    {
        var body = new Dictionary<string, object?>
        {
            ["clinicId"] = c.ClinicId,
            ["petId"] = c.PetId,
            ["prescribedAtUtc"] = DateTime.UtcNow.AddHours(-1),
            ["title"] = "Güncel başlık",
            ["content"] = "Güncel içerik",
        };
        if (includeItems)
            body["items"] = items;

        return await c.Http.PutAsJsonAsync($"/api/v1/prescriptions/{c.PrescriptionId}", body);
    }

    private async Task<Guid> SeedProductAsync(Guid tenantId, string name, bool active = true)
    {
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product(tenantId, name, "Kutu", 10m, "TRY", DateTime.UtcNow);
        if (!active)
            product.Deactivate(DateTime.UtcNow);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private static async Task<List<JsonElement>> ItemsAsync(Context c)
    {
        using var detail = await GetDetailAsync(c.Http, c.PrescriptionId);
        return detail.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    [Fact]
    public async Task LegacyPrescription_Should_OpenWithEmptyItems_And_KeepFreeText()
    {
        var c = await ArrangeAsync();

        using var detail = await GetDetailAsync(c.Http, c.PrescriptionId);

        detail.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        detail.RootElement.GetProperty("content").GetString().Should().Be("Reçete içeriği");
    }

    [Fact]
    public async Task Put_Should_WriteReplaceKeepAndClearItems_AndNeverTouchStock()
    {
        var c = await ArrangeAsync();
        var productId = await SeedProductAsync(c.TenantId, "Katalog İlaç");

        // Dolu liste: serbest satır + ürüne bağlı satır (adı boş, ürün adı kopyalanır).
        (await PutAsync(c, new object[]
        {
            new { name = "Dış reçete ilacı", instructions = "Günde 2", quantity = 14 },
            new { productId, quantity = 1.5 },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var items = await ItemsAsync(c);
        items.Should().HaveCount(2);
        items[0].GetProperty("sortOrder").GetInt32().Should().Be(1);
        items[0].GetProperty("name").GetString().Should().Be("Dış reçete ilacı");
        items[0].GetProperty("productId").ValueKind.Should().Be(JsonValueKind.Null);
        items[1].GetProperty("name").GetString().Should().Be("Katalog İlaç");
        items[1].GetProperty("productId").GetGuid().Should().Be(productId);
        items[1].GetProperty("quantity").GetDecimal().Should().Be(1.5m);

        // items alanı yok (eski istemci): satırlara dokunulmaz.
        (await PutAsync(c, null, includeItems: false)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ItemsAsync(c)).Should().HaveCount(2);

        // Değiştir: eski satırlar silinir, yenisi gelir.
        (await PutAsync(c, new object[] { new { name = "Yeni ilaç" } })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var replaced = await ItemsAsync(c);
        replaced.Should().HaveCount(1);
        replaced[0].GetProperty("name").GetString().Should().Be("Yeni ilaç");

        // Boş dizi: hepsi silinir.
        (await PutAsync(c, Array.Empty<object>())).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ItemsAsync(c)).Should().BeEmpty();

        // Reçete hiçbir aşamada stok hareketi/stok satırı üretmez.
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.StockMovements.Any(m => m.ProductId == productId).Should().BeFalse();
        db.ProductStocks.Any(s => s.ProductId == productId).Should().BeFalse();
    }

    [Fact]
    public async Task Put_Should_Return400_When_ProductInactive_And_KeepExistingItems()
    {
        var c = await ArrangeAsync();
        var inactiveId = await SeedProductAsync(c.TenantId, "Pasif ilaç", active: false);
        (await PutAsync(c, new object[] { new { name = "Var olan" } })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await PutAsync(c, new object[] { new { productId = inactiveId } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Products.Inactive");
        (await ItemsAsync(c)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Put_Should_Return404_When_ProductMissing_And_400_When_NameMissingWithoutProduct()
    {
        var c = await ArrangeAsync();

        var missing = await PutAsync(c, new object[] { new { productId = Guid.NewGuid() } });
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(missing)).Should().Be("Products.NotFound");

        var noName = await PutAsync(c, new object[] { new { instructions = "adsız" } });
        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
