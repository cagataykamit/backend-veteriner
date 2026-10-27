using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// Örnek Veteriner Kliniği (ADR-002, TRIAL-002/003) uçtan uca (HTTP + LocalDB): kayıtta <c>startWithExampleClinic</c>,
/// sonradan <c>POST /tenants/{tenantId}/example-clinic</c>, ikinci yüklemede 409, deneme dışı abonelikte 409, başka kiracıya
/// yükleme girişiminde 403 (IDOR) ve veri tutarlılığı (özet sayıları = veritabanı, ödeme tahsisleri, stok bakiyeleri, işaret).
/// Bkz. docs/EXAMPLE_CLINIC_API_CONTRACT.md.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ExampleClinicIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ExampleClinicIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task OwnerSignup_WithStartWithExampleClinic_Should_LoadConsistentData_And_SecondLoadConflicts()
    {
        var (response, email) = await SignupAsync(startWithExampleClinic: true);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = body.GetProperty("tenantId").GetGuid();
        var clinicId = body.GetProperty("clinicId").GetGuid();

        var summary = body.GetProperty("exampleClinic");
        summary.ValueKind.Should().Be(JsonValueKind.Object);
        summary.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        summary.GetProperty("clinicId").GetGuid().Should().Be(clinicId);
        summary.GetProperty("clients").GetInt32().Should().Be(12);
        summary.GetProperty("pets").GetInt32().Should().Be(20);
        summary.GetProperty("appointments").GetInt32().Should().BeInRange(25, 60);
        summary.GetProperty("examinations").GetInt32().Should().BeInRange(15, 45);
        summary.GetProperty("vaccinations").GetInt32().Should().BeGreaterThan(5);
        summary.GetProperty("treatments").GetInt32().Should().BeGreaterThan(0);
        summary.GetProperty("labResults").GetInt32().Should().Be(5);
        summary.GetProperty("charges").GetInt32().Should().BeGreaterThan(summary.GetProperty("examinations").GetInt32());
        summary.GetProperty("payments").GetInt32().Should().BeGreaterThan(0);
        summary.GetProperty("products").GetInt32().Should().Be(10);
        summary.GetProperty("stockMovements").GetInt32().Should().BeGreaterThan(10);

        await AssertDatabaseMatchesSummaryAsync(tenantId, clinicId, summary);

        // İkinci yükleme: kiracıda veri var → 409, hiçbir şey değişmez.
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.Subscriptions.Manage]);
        var again = await LoadAsync(token, tenantId);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("Tenants.ExampleClinicAlreadyLoaded");
        (await CountClientsAsync(tenantId)).Should().Be(12);
    }

    [Fact]
    public async Task OwnerSignup_WithoutFlag_Should_StayEmpty_Then_Endpoint_Loads_Once()
    {
        var (response, email) = await SignupAsync(startWithExampleClinic: false);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = body.GetProperty("tenantId").GetGuid();
        var clinicId = body.GetProperty("clinicId").GetGuid();
        body.GetProperty("exampleClinic").ValueKind.Should().Be(JsonValueKind.Null);
        (await CountClientsAsync(tenantId)).Should().Be(0);

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.Subscriptions.Manage]);
        var loaded = await LoadAsync(token, tenantId);
        loaded.StatusCode.Should().Be(HttpStatusCode.OK, await loaded.Content.ReadAsStringAsync());
        var summary = await loaded.Content.ReadFromJsonAsync<JsonElement>();
        summary.GetProperty("clinicId").GetGuid().Should().Be(clinicId);
        summary.GetProperty("clients").GetInt32().Should().Be(12);
        await AssertDatabaseMatchesSummaryAsync(tenantId, clinicId, summary);

        var again = await LoadAsync(token, tenantId);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("Tenants.ExampleClinicAlreadyLoaded");
    }

    [Fact]
    public async Task LoadExampleClinic_Should_Return403_For_OtherTenant_And_WriteNothing()
    {
        var (victim, _) = await SignupAsync(startWithExampleClinic: false);
        var victimTenantId = (await victim.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        var (attacker, attackerEmail) = await SignupAsync(startWithExampleClinic: false);
        attacker.EnsureSuccessStatusCode();

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, attackerEmail, [PermissionCatalog.Subscriptions.Manage]);
        var response = await LoadAsync(token, victimTenantId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Tenants.AccessDenied");
        (await CountClientsAsync(victimTenantId)).Should().Be(0);
    }

    [Fact]
    public async Task LoadExampleClinic_Should_Return409_When_SubscriptionIsNotTrialing()
    {
        var (response, email) = await SignupAsync(startWithExampleClinic: false);
        var tenantId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscription = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == tenantId);
            subscription.ActivatePaidPlan(SubscriptionPlanCode.Basic, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.Subscriptions.Manage]);
        var loaded = await LoadAsync(token, tenantId);

        loaded.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(loaded)).Should().Be("Tenants.ExampleClinicTrialRequired");
        (await CountClientsAsync(tenantId)).Should().Be(0);
    }

    [Fact]
    public async Task LoadExampleClinic_Should_Require_Auth_And_SubscriptionsManage()
    {
        var (response, email) = await SignupAsync(startWithExampleClinic: false);
        var tenantId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();

        var anonymous = await _factory.CreateClient().PostAsync($"/api/v1/tenants/{tenantId:D}/example-clinic", null);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var readOnly = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, [PermissionCatalog.Subscriptions.Read]);
        (await LoadAsync(readOnly, tenantId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CountClientsAsync(tenantId)).Should().Be(0);
    }

    private async Task AssertDatabaseMatchesSummaryAsync(Guid tenantId, Guid clinicId, JsonElement summary)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Clients.CountAsync(x => x.TenantId == tenantId)).Should().Be(summary.GetProperty("clients").GetInt32());
        (await db.Pets.CountAsync(x => x.TenantId == tenantId)).Should().Be(summary.GetProperty("pets").GetInt32());
        (await db.Appointments.CountAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId)).Should().Be(summary.GetProperty("appointments").GetInt32());
        (await db.Examinations.CountAsync(x => x.TenantId == tenantId && x.ClinicId == clinicId)).Should().Be(summary.GetProperty("examinations").GetInt32());
        (await db.Vaccinations.CountAsync(x => x.TenantId == tenantId)).Should().Be(summary.GetProperty("vaccinations").GetInt32());
        (await db.Treatments.CountAsync(x => x.TenantId == tenantId)).Should().Be(summary.GetProperty("treatments").GetInt32());
        (await db.LabResults.CountAsync(x => x.TenantId == tenantId)).Should().Be(summary.GetProperty("labResults").GetInt32());
        (await db.Charges.CountAsync(x => x.TenantId == tenantId)).Should().Be(summary.GetProperty("charges").GetInt32());
        (await db.Payments.CountAsync(x => x.TenantId == tenantId)).Should().Be(summary.GetProperty("payments").GetInt32());
        (await db.Products.CountAsync(x => x.TenantId == tenantId)).Should().Be(summary.GetProperty("products").GetInt32());
        (await db.StockMovements.CountAsync(x => x.TenantId == tenantId)).Should().Be(summary.GetProperty("stockMovements").GetInt32());

        // Tüm kayıtlar "ÖRNEK" işaretli (temizleme için): müşteride adres, diğerlerinde not/açıklama.
        var marker = ExampleClinicDataGenerator.Marker;
        (await db.Clients.Where(x => x.TenantId == tenantId).AllAsync(x => x.Address != null && x.Address.StartsWith(marker))).Should().BeTrue();
        (await db.Pets.Where(x => x.TenantId == tenantId).AllAsync(x => x.Notes != null && x.Notes.StartsWith(marker))).Should().BeTrue();
        (await db.Appointments.Where(x => x.TenantId == tenantId).AllAsync(x => x.Notes != null && x.Notes.StartsWith(marker))).Should().BeTrue();
        (await db.Charges.Where(x => x.TenantId == tenantId).AllAsync(x => x.Description.StartsWith(marker))).Should().BeTrue();
        (await db.Payments.Where(x => x.TenantId == tenantId).AllAsync(x => x.Notes != null && x.Notes.StartsWith(marker))).Should().BeTrue();

        // Takvim: son 30 gün + gelecek 7 gün; geçmişte tamamlanmış/iptal/gelmedi, gelecekte planlı.
        var now = DateTime.UtcNow;
        var appointments = await db.Appointments.Where(x => x.TenantId == tenantId).ToListAsync();
        appointments.Should().OnlyContain(a => a.ScheduledAtUtc > now.AddDays(-32) && a.ScheduledAtUtc < now.AddDays(9));
        appointments.Should().Contain(a => a.Status == AppointmentStatus.Scheduled && a.ScheduledAtUtc > now);
        appointments.Should().Contain(a => a.Status == AppointmentStatus.Completed);
        appointments.Should().Contain(a => a.Status == AppointmentStatus.Cancelled);
        appointments.Should().Contain(a => a.Status == AppointmentStatus.NoShow);
        appointments.Where(a => a.Status == AppointmentStatus.Scheduled).Should().OnlyContain(a => a.ScheduledAtUtc > now);
        // Yükleme günü hafta sonu da olsa Bugün boş kalmaz (ilk açılış değeri); günün İstanbul sınırları.
        var (todayStartUtc, todayEndUtc) = Backend.Veteriner.Application.Common.Time.OperationDayBounds.ForUtcNow(now);
        appointments.Count(a => a.ScheduledAtUtc >= todayStartUtc && a.ScheduledAtUtc < todayEndUtc)
            .Should().BeGreaterThanOrEqualTo(2);

        // Hastalar katalog ırkı (ad + kimlik) ve renk taşır; liste/kart boş görünmesin.
        var pets = await db.Pets.Where(x => x.TenantId == tenantId).ToListAsync();
        pets.Should().OnlyContain(p => p.Breed != null && p.ColorId != null);
        pets.Should().Contain(p => p.BreedId != null);

        // Klinik kayıtları: kesinleşmiş ve taslak muayene, uygulanmış/planlı/gecikmiş aşı, bekleyen lab.
        var examinations = await db.Examinations.Where(x => x.TenantId == tenantId).ToListAsync();
        examinations.Should().Contain(e => e.IsFinalized).And.Contain(e => !e.IsFinalized);
        var vaccinations = await db.Vaccinations.Where(x => x.TenantId == tenantId).ToListAsync();
        vaccinations.Should().Contain(v => v.Status == VaccinationStatus.Applied && v.ExaminationId != null);
        vaccinations.Should().Contain(v => v.Status == VaccinationStatus.Scheduled && v.DueAtUtc > now);
        vaccinations.Should().Contain(v => v.Status == VaccinationStatus.Scheduled && v.DueAtUtc < now, "bir gecikmiş aşı olmalı");
        var labs = await db.LabResults.Where(x => x.TenantId == tenantId).ToListAsync();
        labs.Should().Contain(l => l.Status == LabResultStatus.Requested).And.Contain(l => l.Status == LabResultStatus.Resulted);

        // Finans: tahsisler ödeme/ücret sayaçlarıyla tutarlı; kısmi ödeme ve açık borç var.
        var charges = await db.Charges.Where(x => x.TenantId == tenantId).ToListAsync();
        var payments = await db.Payments.Where(x => x.TenantId == tenantId).ToListAsync();
        var allocations = await db.PaymentAllocations.Where(x => x.TenantId == tenantId).ToListAsync();
        payments.Sum(p => p.AllocatedAmount).Should().Be(allocations.Sum(a => a.Amount));
        charges.Sum(c => c.AllocatedAmount).Should().Be(allocations.Sum(a => a.Amount));
        payments.Should().OnlyContain(p => p.AllocatedAmount == p.Amount);
        charges.Should().OnlyContain(c => c.AllocatedAmount <= c.Amount);
        charges.Should().Contain(c => c.AllocatedAmount == 0, "açık borç örneği olmalı");
        charges.Should().Contain(c => c.AllocatedAmount > 0 && c.AllocatedAmount < c.Amount, "kısmi ödeme örneği olmalı");

        // Stok: eldeki miktar = giriş - çıkış; aşılarda parti/SKT; parti kalanı hareketlerle tutarlı.
        var movements = await db.StockMovements.Where(x => x.TenantId == tenantId).ToListAsync();
        var stocks = await db.ProductStocks.Where(x => x.TenantId == tenantId).ToListAsync();
        foreach (var stock in stocks)
        {
            var expected = movements.Where(m => m.ProductId == stock.ProductId)
                .Sum(m => m.MovementType == StockMovementType.Out ? -m.Quantity : m.Quantity);
            stock.QuantityOnHand.Should().Be(expected);
        }

        var batches = await db.StockBatches.Where(x => x.TenantId == tenantId).ToListAsync();
        batches.Should().NotBeEmpty().And.OnlyContain(b => b.LotNumber != null && b.ExpiryDate != null);
        foreach (var batch in batches)
        {
            var expected = movements.Where(m => m.BatchId == batch.Id)
                .Sum(m => m.MovementType == StockMovementType.Out ? -m.Quantity : m.Quantity);
            batch.RemainingQuantity.Should().Be(expected);
        }

        movements.Should().Contain(m => m.MovementType == StockMovementType.Out && m.ReferenceType == "Vaccination");

        // Okuma modelleri için outbox olayları aynı işlemde yazıldı (Client/Pet/Appointment/Payment Created).
        var outboxTypes = await db.OutboxMessages
            .Where(m => m.Payload.Contains(tenantId.ToString()))
            .Select(m => m.Type)
            .ToListAsync();
        outboxTypes.Should().Contain(t => t.Contains("Client", StringComparison.OrdinalIgnoreCase))
            .And.Contain(t => t.Contains("Pet", StringComparison.OrdinalIgnoreCase))
            .And.Contain(t => t.Contains("Appointment", StringComparison.OrdinalIgnoreCase))
            .And.Contain(t => t.Contains("Payment", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<int> CountClientsAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Clients.CountAsync(c => c.TenantId == tenantId);
    }

    private async Task<HttpResponseMessage> LoadAsync(string token, Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.PostAsync($"/api/v1/tenants/{tenantId:D}/example-clinic", null);
    }

    private async Task<(HttpResponseMessage Response, string Email)> SignupAsync(bool startWithExampleClinic)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"ornek-klinik-{suffix}@example.com";
        var documents = LegalDocumentCatalog.CurrentVersions
            .Select(kv => (object)new { documentType = kv.Key.ToString(), version = kv.Value })
            .ToArray();

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/public/owner-signup", new
        {
            planCode = "Basic",
            tenantName = $"Örnek Kiracı {suffix}",
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
