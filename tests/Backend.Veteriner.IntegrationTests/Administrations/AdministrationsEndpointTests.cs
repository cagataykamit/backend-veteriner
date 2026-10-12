using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Administrations;

/// <summary>
/// Uygulama kaydı (Aşama 5 Dilim 2): gerçek SQL Server/LocalDB üzerinde stok düşümü + kayıt atomikliği,
/// requestId tekrarı/yarışı, yetersiz stok geri alma, kesinleşmiş muayene kilidi ve iptal ile stok iadesi.
/// </summary>
[Collection("products-api")]
public sealed class AdministrationsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AllPermissions =
    {
        "Administrations.Create", "Administrations.Read", "Administrations.Correct", "Products.Read",
    };

    private readonly CustomWebApplicationFactory _factory;

    public AdministrationsEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_Should_Return401_When_TokenMissing()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/administrations", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_Should_Return403_When_OnlyRead()
    {
        var ctx = await SeedAsync(stock: 10m, permissions: new[] { "Administrations.Read", "Products.Read" });
        var client = ClientFor(ctx);

        var response = await PostAsync(client, ctx, 1m);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetStockAsync(ctx)).Should().Be(10m);
    }

    [Fact]
    public async Task Post_Should_Drop_Stock_Write_OutMovement_With_Source_And_Report_BelowMinimum()
    {
        var ctx = await SeedAsync(stock: 10m, minimum: 8m);
        var client = ClientFor(ctx);

        var response = await PostAsync(client, ctx, 3m);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("created").GetBoolean().Should().BeTrue();
        var admin = json.GetProperty("administration");
        admin.GetProperty("status").GetString().Should().Be("Active");
        admin.GetProperty("quantity").GetDecimal().Should().Be(3m);
        admin.GetProperty("unit").GetString().Should().Be("Adet");
        var stock = json.GetProperty("stock");
        stock.GetProperty("quantityOnHand").GetDecimal().Should().Be(7m);
        stock.GetProperty("minimumStockLevel").GetDecimal().Should().Be(8m);
        stock.GetProperty("isBelowMinimum").GetBoolean().Should().BeTrue();

        var administrationId = admin.GetProperty("id").GetGuid();
        (await GetStockAsync(ctx)).Should().Be(7m);
        var movement = (await MovementsForAsync(administrationId)).Should().ContainSingle().Subject;
        movement.MovementType.Should().Be(StockMovementType.Out);
        movement.Quantity.Should().Be(3m);
        movement.ReferenceType.Should().Be("Administration");
        movement.ProductId.Should().Be(ctx.ProductId);
        movement.ClinicId.Should().Be(ctx.ClinicId);
    }

    [Fact]
    public async Task Post_AboveMinimum_Should_Not_Flag_Low_Stock()
    {
        var ctx = await SeedAsync(stock: 10m, minimum: 2m);

        var response = await PostAsync(ClientFor(ctx), ctx, 3m);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var stock = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stock");
        stock.GetProperty("isBelowMinimum").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Post_SameRequestId_Should_Return200_And_Drop_Stock_Once()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();

        var first = await PostAsync(client, ctx, 3m, requestId: requestId);
        var second = await PostAsync(client, ctx, 3m, requestId: requestId);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondJson = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondJson.GetProperty("created").GetBoolean().Should().BeFalse();
        secondJson.GetProperty("stock").GetProperty("quantityOnHand").GetDecimal().Should().Be(7m);
        (await GetStockAsync(ctx)).Should().Be(7m);

        var different = await PostAsync(client, ctx, 4m, requestId: requestId);
        different.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(different)).Should().Be("Administrations.DuplicateRequestId");
        (await GetStockAsync(ctx)).Should().Be(7m);
    }

    [Fact]
    public async Task Post_Concurrent_SameRequestId_Should_Never500_And_Drop_Stock_Once()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostAsync(client, ctx, 2m, requestId: requestId)));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        (await GetStockAsync(ctx)).Should().Be(8m);
        (await AdministrationCountAsync(ctx)).Should().Be(1);
        (await OutMovementCountAsync(ctx)).Should().Be(1);
    }

    [Fact]
    public async Task Post_InsufficientStock_Should_Return400_And_Leave_No_Record()
    {
        var ctx = await SeedAsync(stock: 2m);

        var response = await PostAsync(ClientFor(ctx), ctx, 5m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("Administrations.InsufficientStock");
        (await GetStockAsync(ctx)).Should().Be(2m);
        (await AdministrationCountAsync(ctx)).Should().Be(0);
        (await OutMovementCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Post_WithoutStockRow_Should_Return400_InsufficientStock_And_Leave_No_Record()
    {
        var ctx = await SeedAsync(stock: null);

        var response = await PostAsync(ClientFor(ctx), ctx, 1m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("Administrations.InsufficientStock");
        (await AdministrationCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Post_Should_Reject_Invalid_Quantity_And_Inactive_Product_Without_Side_Effects()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);

        var zero = await PostAsync(client, ctx, 0m);
        zero.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(zero)).Should().Be("Administrations.Validation");

        var fourDecimals = await PostAsync(client, ctx, 1.0001m);
        fourDecimals.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await DeactivateProductAsync(ctx.ProductId);
        var inactive = await PostAsync(client, ctx, 1m);
        inactive.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(inactive)).Should().Be("Products.Inactive");

        (await GetStockAsync(ctx)).Should().Be(10m);
        (await AdministrationCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Post_Should_Reject_FinalizedExamination_And_Inherit_Examination_From_Treatment()
    {
        var ctx = await SeedAsync(stock: 10m, withExamination: true, withTreatment: true);
        await FinalizeExaminationAsync(ctx.ExaminationId!.Value);
        var client = ClientFor(ctx);

        var direct = await PostAsync(client, ctx, 1m, examinationId: ctx.ExaminationId);
        direct.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(direct)).Should().Be("Examinations.Finalized");

        var viaTreatment = await PostAsync(client, ctx, 1m, treatmentId: ctx.TreatmentId);
        viaTreatment.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(viaTreatment)).Should().Be("Examinations.Finalized");

        (await GetStockAsync(ctx)).Should().Be(10m);
        (await AdministrationCountAsync(ctx)).Should().Be(0);
    }

    [Fact]
    public async Task Post_Should_Link_Treatment_And_Examination_On_Open_Examination()
    {
        var ctx = await SeedAsync(stock: 10m, withExamination: true, withTreatment: true);

        var response = await PostAsync(ClientFor(ctx), ctx, 1m, treatmentId: ctx.TreatmentId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var admin = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration");
        admin.GetProperty("treatmentId").GetGuid().Should().Be(ctx.TreatmentId!.Value);
        admin.GetProperty("examinationId").GetGuid().Should().Be(ctx.ExaminationId!.Value);
    }

    [Fact]
    public async Task Post_Should_Reject_Treatment_Of_Another_Pet()
    {
        var ctx = await SeedAsync(stock: 10m, withExamination: true, withTreatment: true);
        var other = await SeedPetAsync(ctx.TenantId);

        var response = await ClientFor(ctx).PostAsJsonAsync("/api/v1/administrations", new
        {
            clinicId = ctx.ClinicId,
            petId = other,
            productId = ctx.ProductId,
            quantity = 1m,
            treatmentId = ctx.TreatmentId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("Administrations.TreatmentPetMismatch");
        (await GetStockAsync(ctx)).Should().Be(10m);
    }

    [Fact]
    public async Task Void_Should_Restore_Stock_Once_And_Reject_Second_Void_And_RequestId_Reuse()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var requestId = Guid.NewGuid();
        var created = await PostAsync(client, ctx, 4m, requestId: requestId);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration").GetProperty("id").GetGuid();
        (await GetStockAsync(ctx)).Should().Be(6m);

        var tooShort = await client.PostAsJsonAsync($"/api/v1/administrations/{id}/void", new { reason = "abc" });
        tooShort.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetStockAsync(ctx)).Should().Be(6m);

        var voided = await client.PostAsJsonAsync($"/api/v1/administrations/{id}/void", new { reason = "Yanlış ürün seçildi" });

        voided.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await voided.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("status").GetString().Should().Be("Voided");
        dto.GetProperty("voidReason").GetString().Should().Be("Yanlış ürün seçildi");
        (await GetStockAsync(ctx)).Should().Be(10m);
        var movements = await MovementsForAsync(id);
        movements.Select(m => m.MovementType).Should().BeEquivalentTo(new[] { StockMovementType.Out, StockMovementType.Reversal });
        movements.Single(m => m.MovementType == StockMovementType.Reversal).ReversesMovementId
            .Should().Be(movements.Single(m => m.MovementType == StockMovementType.Out).Id);

        var again = await client.PostAsJsonAsync($"/api/v1/administrations/{id}/void", new { reason = "Tekrar iptal denemesi" });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(again)).Should().Be("Administrations.AlreadyVoided");
        (await GetStockAsync(ctx)).Should().Be(10m);

        var reuse = await PostAsync(client, ctx, 4m, requestId: requestId);
        reuse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemCodeAsync(reuse)).Should().Be("Administrations.RequestIdVoided");
        (await GetStockAsync(ctx)).Should().Be(10m);
    }

    [Fact]
    public async Task Void_Concurrent_Should_Restore_Stock_Exactly_Once_Without_500()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var created = await PostAsync(client, ctx, 4m);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration").GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync($"/api/v1/administrations/{id}/void", new { reason = "Yanlış ürün seçildi" })));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        (await GetStockAsync(ctx)).Should().Be(10m);
        (await MovementsForAsync(id)).Count(m => m.MovementType == StockMovementType.Reversal).Should().Be(1);
    }

    [Fact]
    public async Task Void_Should_Be_Allowed_On_Finalized_Examination()
    {
        var ctx = await SeedAsync(stock: 10m, withExamination: true);
        var client = ClientFor(ctx);
        var created = await PostAsync(client, ctx, 2m, examinationId: ctx.ExaminationId);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration").GetProperty("id").GetGuid();
        await FinalizeExaminationAsync(ctx.ExaminationId!.Value);

        var voided = await client.PostAsJsonAsync($"/api/v1/administrations/{id}/void", new { reason = "Kesinleşmiş kayıt düzeltmesi" });

        voided.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetStockAsync(ctx)).Should().Be(10m);
    }

    [Fact]
    public async Task Void_Should_Return403_Without_Correct_And_404_For_Unknown_Id()
    {
        var ctx = await SeedAsync(stock: 10m);
        var created = await PostAsync(ClientFor(ctx), ctx, 1m);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration").GetProperty("id").GetGuid();

        var noCorrect = await SeedUserClientAsync(ctx, new[] { "Administrations.Create", "Administrations.Read" });
        var forbidden = await noCorrect.PostAsJsonAsync($"/api/v1/administrations/{id}/void", new { reason = "Yetkisiz iptal denemesi" });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var missing = await ClientFor(ctx).PostAsJsonAsync($"/api/v1/administrations/{Guid.NewGuid()}/void", new { reason = "Olmayan kayıt iptali" });
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemCodeAsync(missing)).Should().Be("Administrations.NotFound");
        (await GetStockAsync(ctx)).Should().Be(9m);
    }

    [Fact]
    public async Task Get_And_List_Should_Hide_Voided_By_Default_And_Not_Cross_Tenants()
    {
        var ctx = await SeedAsync(stock: 10m);
        var client = ClientFor(ctx);
        var keep = await PostAsync(client, ctx, 1m);
        var drop = await PostAsync(client, ctx, 1m);
        var keepId = (await keep.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration").GetProperty("id").GetGuid();
        var dropId = (await drop.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("administration").GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/v1/administrations/{dropId}/void", new { reason = "Yanlış ürün seçildi" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await client.GetAsync($"/api/v1/administrations?clinicId={ctx.ClinicId:D}&petId={ctx.PetId:D}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid()).Should().Equal(keepId);

        var all = await client.GetAsync($"/api/v1/administrations?clinicId={ctx.ClinicId:D}&includeVoided=true");
        (await all.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength().Should().Be(2);

        var noScope = await (await SeedUserClientAsync(ctx, AllPermissions, includeClinicClaim: false)).GetAsync("/api/v1/administrations");
        noScope.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(noScope)).Should().Be("Administrations.ClinicScopeRequired");

        (await client.GetAsync($"/api/v1/administrations/{dropId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var other = await SeedAsync(stock: 5m);
        var foreign = await ClientFor(other).GetAsync($"/api/v1/administrations/{keepId}");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
        decimal quantity,
        Guid? requestId = null,
        Guid? examinationId = null,
        Guid? treatmentId = null)
        => client.PostAsJsonAsync("/api/v1/administrations", new
        {
            clinicId = ctx.ClinicId,
            petId = ctx.PetId,
            productId = ctx.ProductId,
            quantity,
            requestId,
            examinationId,
            treatmentId,
        });

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<decimal> GetStockAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProductStocks.AsNoTracking()
            .Where(s => s.ClinicId == ctx.ClinicId && s.ProductId == ctx.ProductId)
            .Select(s => s.QuantityOnHand)
            .SingleOrDefaultAsync();
    }

    private async Task<int> AdministrationCountAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Administrations.AsNoTracking().CountAsync(a => a.ProductId == ctx.ProductId);
    }

    private async Task<int> OutMovementCountAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StockMovements.AsNoTracking()
            .CountAsync(m => m.ProductId == ctx.ProductId && m.MovementType == StockMovementType.Out);
    }

    private async Task<List<StockMovement>> MovementsForAsync(Guid administrationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StockMovements.AsNoTracking()
            .Where(m => m.ReferenceId == administrationId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync();
    }

    private async Task DeactivateProductAsync(Guid productId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = await db.Products.SingleAsync(p => p.Id == productId);
        product.Deactivate();
        await db.SaveChangesAsync();
    }

    private async Task FinalizeExaminationAsync(Guid examinationId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exam = await db.Examinations.SingleAsync(e => e.Id == examinationId);
        exam.Finalize(Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedPetAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = new Client(tenantId, $"AdmClient-{Guid.NewGuid():N}"[..14], "905551110088");
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenantId, client.Id, $"AdmPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();
        return pet.Id;
    }

    private async Task<HttpClient> SeedUserClientAsync(SeedCtx ctx, string[] permissions, bool includeClinicClaim = true)
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

    private async Task<SeedCtx> SeedAsync(
        decimal? stock,
        decimal minimum = 0m,
        string[]? permissions = null,
        bool withExamination = false,
        bool withTreatment = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"adm-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        var clinic = new Clinic(tenant.Id, $"Ad-{Guid.NewGuid():N}"[..14], "Istanbul");
        db.Clinics.Add(clinic);
        var product = new Product(tenant.Id, $"ap-{Guid.NewGuid():N}"[..14], "Adet", 2m, "TRY", sku: $"ASK-{Guid.NewGuid():N}"[..14]);
        db.Products.Add(product);
        var client = new Client(tenant.Id, $"AdmClient-{Guid.NewGuid():N}"[..14], "905551110088");
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, $"AdmPet-{Guid.NewGuid():N}"[..12], speciesId);
        db.Pets.Add(pet);
        if (stock is { } onHand)
            db.ProductStocks.Add(new ProductStock(tenant.Id, clinic.Id, product.Id, onHand, minimum));
        await db.SaveChangesAsync();

        Guid? examinationId = null;
        Guid? treatmentId = null;
        if (withExamination)
        {
            var examinedAt = DateTime.UtcNow.AddHours(-3);
            var exam = new Examination(tenant.Id, clinic.Id, pet.Id, appointmentId: null, examinedAt, "Kontrol", "Bulgu", null, null);
            db.Examinations.Add(exam);
            await db.SaveChangesAsync();
            examinationId = exam.Id;

            if (withTreatment)
            {
                var treatment = new Treatment(tenant.Id, clinic.Id, pet.Id, exam.Id, examinedAt.AddMinutes(15), "Tedavi", "Aciklama", null, null);
                db.Treatments.Add(treatment);
                await db.SaveChangesAsync();
                treatmentId = treatment.Id;
            }
        }

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, permissions ?? AllPermissions, includeClinicClaimInJwt: true);

        return new SeedCtx(tenant.Id, clinic.Id, product.Id, pet.Id, examinationId, treatmentId, token);
    }

    private sealed record SeedCtx(
        Guid TenantId,
        Guid ClinicId,
        Guid ProductId,
        Guid PetId,
        Guid? ExaminationId,
        Guid? TreatmentId,
        string Token);
}
