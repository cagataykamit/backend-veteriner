using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// Örnek klinik temizliği (EXAMPLE_CLINIC_API_CONTRACT.md §10) uçtan uca (HTTP + LocalDB): durum sayıları, ücretli
/// abonelikte kalıcı silme, kullanıcının kendi kayıtlarının korunması, örnek hastaya eklenen kaydın silinmesi, kullanılmış
/// örnek ürünün pasife alınması, okuma modeli olayları, ikinci temizlikte 409, IDOR 403, 401/403 ve temizlikten sonra
/// yeniden yükleme.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ExampleClinicCleanupIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ExampleClinicCleanupIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Clear_Should_DeleteExampleData_KeepOwnRecords_And_DeactivateUsedProduct()
    {
        var (tenantId, clinicId, email) = await SignupWithExampleClinicAsync();
        var own = await SeedUserRecordsAsync(tenantId, clinicId, email);
        await ActivatePaidPlanAsync(tenantId); // ücretli abonelikte de açık (§10 karar)
        var token = await TokenAsync(email);

        var statusResponse = await SendAsync(HttpMethod.Get, token, tenantId);
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK, await statusResponse.Content.ReadAsStringAsync());
        var status = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        status.GetProperty("hasExampleData").GetBoolean().Should().BeTrue();
        var preview = status.GetProperty("counts");
        preview.GetProperty("clients").GetInt32().Should().Be(12);
        preview.GetProperty("pets").GetInt32().Should().Be(20);
        preview.GetProperty("products").GetInt32().Should().Be(9);
        preview.GetProperty("productsDeactivated").GetInt32().Should().Be(1);

        int examinationsBefore, appointmentsBefore, paymentsBefore;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            examinationsBefore = await db.Examinations.CountAsync(x => x.TenantId == tenantId);
            appointmentsBefore = await db.Appointments.CountAsync(x => x.TenantId == tenantId);
            paymentsBefore = await db.Payments.CountAsync(x => x.TenantId == tenantId);
        }

        preview.GetProperty("examinations").GetInt32().Should().Be(examinationsBefore, "kullanıcının örnek hastaya eklediği muayene dahil");

        var cleared = await SendAsync(HttpMethod.Delete, token, tenantId);
        cleared.StatusCode.Should().Be(HttpStatusCode.OK, await cleared.Content.ReadAsStringAsync());
        var counts = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        counts.GetProperty("clients").GetInt32().Should().Be(12);
        counts.GetProperty("pets").GetInt32().Should().Be(20);
        counts.GetProperty("examinations").GetInt32().Should().Be(examinationsBefore);
        counts.GetProperty("appointments").GetInt32().Should().Be(appointmentsBefore);
        counts.GetProperty("payments").GetInt32().Should().Be(paymentsBefore);
        counts.GetProperty("products").GetInt32().Should().Be(9);
        counts.GetProperty("productsDeactivated").GetInt32().Should().Be(1);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tag = ExampleClinicDataGenerator.TagPrefix;

            // Kullanıcının kendi müşterisi, hastası ve ücreti kalır; örnek küme sıfırlanır.
            (await db.Clients.Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync()).Should().Equal(own.ClientId);
            (await db.Pets.Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync()).Should().Equal(own.PetId);
            (await db.Charges.Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync()).Should().Equal(own.ChargeId);
            (await db.Examinations.AnyAsync(x => x.TenantId == tenantId)).Should().BeFalse();
            (await db.Appointments.AnyAsync(x => x.TenantId == tenantId)).Should().BeFalse();
            (await db.Vaccinations.AnyAsync(x => x.TenantId == tenantId)).Should().BeFalse();
            (await db.Treatments.AnyAsync(x => x.TenantId == tenantId)).Should().BeFalse();
            (await db.LabResults.AnyAsync(x => x.TenantId == tenantId)).Should().BeFalse();
            (await db.Payments.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId)).Should().BeFalse();
            (await db.PaymentAllocations.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId)).Should().BeFalse();

            // Kullanılmış örnek ürün pasif kalır, hareketleri ve stoku silinmez; diğer örnek ürünler stoklarıyla gider.
            var products = await db.Products.Where(x => x.TenantId == tenantId).ToListAsync();
            products.Should().ContainSingle().Which.Id.Should().Be(own.UsedProductId);
            products[0].IsActive.Should().BeFalse();
            (await db.StockMovements.Where(x => x.TenantId == tenantId).AllAsync(x => x.ProductId == own.UsedProductId)).Should().BeTrue();
            (await db.StockMovements.AnyAsync(x => x.TenantId == tenantId && (x.Notes == null || !x.Notes.StartsWith(tag)))).Should().BeTrue();
            (await db.ProductStocks.Where(x => x.TenantId == tenantId).AllAsync(x => x.ProductId == own.UsedProductId)).Should().BeTrue();

            // Okuma modelleri için silme olayları aynı işlemde yazıldı.
            var types = await db.OutboxMessages
                .Where(m => m.Payload.Contains(tenantId.ToString()))
                .Select(m => m.Type)
                .ToListAsync();
            types.Count(t => t == ClientIntegrationEventTypes.Deleted).Should().Be(12);
            types.Count(t => t == PetIntegrationEventTypes.Deleted).Should().Be(20);
            types.Count(t => t == AppointmentIntegrationEventTypes.Deleted).Should().Be(appointmentsBefore);
            types.Count(t => t == PaymentIntegrationEventTypes.Voided).Should().Be(paymentsBefore);
        }

        var after = await (await SendAsync(HttpMethod.Get, token, tenantId)).Content.ReadFromJsonAsync<JsonElement>();
        after.GetProperty("hasExampleData").GetBoolean().Should().BeFalse();
        after.GetProperty("counts").GetProperty("clients").GetInt32().Should().Be(0);

        var again = await SendAsync(HttpMethod.Delete, token, tenantId);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("Tenants.ExampleClinicNotLoaded");
    }

    [Fact]
    public async Task Clear_Then_Load_Should_Work_When_TenantIsEmpty_And_Trialing()
    {
        var (tenantId, _, email) = await SignupWithExampleClinicAsync();
        var token = await TokenAsync(email);

        (await SendAsync(HttpMethod.Delete, token, tenantId)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountClientsAsync(tenantId)).Should().Be(0);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var reloaded = await client.PostAsync($"/api/v1/tenants/{tenantId:D}/example-clinic", null);
        reloaded.StatusCode.Should().Be(HttpStatusCode.OK, await reloaded.Content.ReadAsStringAsync());
        (await CountClientsAsync(tenantId)).Should().Be(12);
    }

    [Fact]
    public async Task Clear_Should_Return409_When_TenantHasNoExampleData()
    {
        var (response, email) = await SignupAsync(startWithExampleClinic: false);
        var tenantId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        var token = await TokenAsync(email);

        var status = await (await SendAsync(HttpMethod.Get, token, tenantId)).Content.ReadFromJsonAsync<JsonElement>();
        status.GetProperty("hasExampleData").GetBoolean().Should().BeFalse();

        var cleared = await SendAsync(HttpMethod.Delete, token, tenantId);
        cleared.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(cleared)).Should().Be("Tenants.ExampleClinicNotLoaded");
    }

    [Fact]
    public async Task Clear_Should_Return403_For_OtherTenant_And_DeleteNothing()
    {
        var (victimTenantId, _, _) = await SignupWithExampleClinicAsync();
        var (attacker, attackerEmail) = await SignupAsync(startWithExampleClinic: false);
        attacker.EnsureSuccessStatusCode();
        var token = await TokenAsync(attackerEmail);

        var status = await SendAsync(HttpMethod.Get, token, victimTenantId);
        status.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var cleared = await SendAsync(HttpMethod.Delete, token, victimTenantId);
        cleared.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(cleared)).Should().Be("Tenants.AccessDenied");
        (await CountClientsAsync(victimTenantId)).Should().Be(12);
    }

    [Fact]
    public async Task Clear_Should_Require_Auth_And_SubscriptionsManage()
    {
        var (tenantId, _, email) = await SignupWithExampleClinicAsync();

        var anonymous = await _factory.CreateClient().DeleteAsync($"/api/v1/tenants/{tenantId:D}/example-clinic");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var readOnly = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.Subscriptions.Read]);
        (await SendAsync(HttpMethod.Get, readOnly, tenantId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, readOnly, tenantId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CountClientsAsync(tenantId)).Should().Be(12);
    }

    private sealed record UserRecords(Guid ClientId, Guid PetId, Guid ChargeId, Guid UsedProductId);

    /// <summary>
    /// Kullanıcının kendi müşterisi/hastası/ücreti (kalmalı), örnek hastaya eklediği işaretsiz muayene (silinmeli) ve bir
    /// örnek ürüne işaretsiz stok girişi (ürün kullanılmış sayılır, pasife alınmalı).
    /// </summary>
    private async Task<UserRecords> SeedUserRecordsAsync(Guid tenantId, Guid clinicId, string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var speciesId = await db.Species.Where(s => s.Code == "CAT").Select(s => s.Id).SingleAsync();
        var now = DateTime.UtcNow;

        var client = new Client(tenantId, "Gerçek Müşteri", phone: "05559998877", address: "Kendi adresi");
        var pet = new Pet(tenantId, client.Id, "Gerçek Kedi", speciesId);
        db.Clients.Add(client);
        db.Pets.Add(pet);
        var charge = Charge.Create(tenantId, clinicId, client.Id, pet.Id, null, null, "Kendi ücreti", 1m, 300m, userId, now, null).Value!;
        db.Charges.Add(charge);

        var examplePetId = await db.Pets.Where(p => p.TenantId == tenantId && p.Notes!.StartsWith(ExampleClinicDataGenerator.TagPrefix))
            .Select(p => p.Id).FirstAsync();
        db.Examinations.Add(new Examination(tenantId, clinicId, examplePetId, null, now, "Kullanıcının denemesi", null, null, null));

        var usedProductId = await db.Products.Where(p => p.TenantId == tenantId && p.Sku == "ORN-MAMA-KEDI-2KG").Select(p => p.Id).SingleAsync();
        db.StockMovements.Add(new StockMovement(
            tenantId, clinicId, usedProductId, StockMovementType.In, 3m, now,
            reason: "Kullanıcı girişi", referenceType: "GoodsReceipt", createdByUserId: userId));

        await db.SaveChangesAsync();
        return new UserRecords(client.Id, pet.Id, charge.Id, usedProductId);
    }

    private async Task ActivatePaidPlanAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
        subscription.ActivatePaidPlan(SubscriptionPlanCode.Basic, DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    private Task<string> TokenAsync(string email)
        => IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.Subscriptions.Manage]);

    private async Task<int> CountClientsAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Clients.CountAsync(c => c.TenantId == tenantId);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string token, Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(new HttpRequestMessage(method, $"/api/v1/tenants/{tenantId:D}/example-clinic"));
    }

    private async Task<(Guid TenantId, Guid ClinicId, string Email)> SignupWithExampleClinicAsync()
    {
        var (response, email) = await SignupAsync(startWithExampleClinic: true);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("tenantId").GetGuid(), body.GetProperty("clinicId").GetGuid(), email);
    }

    private async Task<(HttpResponseMessage Response, string Email)> SignupAsync(bool startWithExampleClinic)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"ornek-temizle-{suffix}@example.com";
        var documents = LegalDocumentCatalog.CurrentVersions
            .Select(kv => (object)new { documentType = kv.Key.ToString(), version = kv.Value })
            .ToArray();

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/public/owner-signup", new
        {
            planCode = "Basic",
            tenantName = $"Örnek Temizlik {suffix}",
            clinicName = "Örnek Veteriner Kliniği",
            clinicCity = "İstanbul",
            email,
            password = "Abc12345!",
            acceptedLegalDocuments = documents,
            startWithExampleClinic,
        });
        return (response, email);
    }
}
