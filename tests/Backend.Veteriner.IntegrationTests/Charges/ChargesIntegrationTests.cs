using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Charges;

/// <summary>
/// Charge uçtan uca: HTTP + gerçek SQL Server (LocalDB, izole test veritabanı). Yetki, tenant/klinik izolasyonu,
/// doğrulama, ClientId zorunluluğu, Visit/Muayene tutarlılığı, RequestId tekrar koruması ve audit.
/// Her test kendi kliniğini, kullanıcısını ve müşterisini kurar.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ChargesIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] FullPermissions =
    [
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Charges.Create,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public ChargesIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ---- Oluşturma / okuma ----

    [Fact]
    public async Task Create_Without_Visit_And_Examination_Should_Return_201_With_Computed_Amount_And_Be_Readable()
    {
        var ctx = await SeedAsync();

        var post = await ctx.PostChargeAsync(ctx.Body(quantity: 2m, unitPrice: 125.50m));

        post.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        post.Response.Headers.Location.Should().NotBeNull();
        post.Result!.Created.Should().BeTrue();
        var charge = post.Result.Charge;
        charge.ClientId.Should().Be(ctx.ClientId);
        charge.VisitId.Should().BeNull();
        charge.ExaminationId.Should().BeNull();
        charge.Amount.Should().Be(251.00m);
        charge.Currency.Should().Be("TRY");
        charge.CreatedByUserId.Should().Be(ctx.UserId);

        var get = await ctx.Http.GetAsync($"/api/v1/charges/{charge.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        (await get.Content.ReadFromJsonAsync<ChargeDto>())!.Id.Should().Be(charge.Id);

        var list = await ctx.Http.GetFromJsonAsync<PagedResult<ChargeDto>>(
            $"/api/v1/charges?clinicId={ctx.ClinicId}&clientId={ctx.ClientId}");
        list!.Items.Select(i => i.Id).Should().Contain(charge.Id);
        list.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Create_Without_ClientId_Should_Return_400_Validation()
    {
        var ctx = await SeedAsync();

        var response = await ctx.Http.PostAsJsonAsync("/api/v1/charges", new
        {
            clinicId = ctx.ClinicId,
            description = "Aşı",
            quantity = 1,
            unitPrice = 100,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.Validation");
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 0)]
    [InlineData(1, 10.005)]
    public async Task Create_With_Invalid_Numbers_Should_Return_400_Validation(double quantity, double unitPrice)
    {
        var ctx = await SeedAsync();

        var response = await ctx.PostRawAsync(ctx.Body(quantity: (decimal)quantity, unitPrice: (decimal)unitPrice));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.Validation");
    }

    [Fact]
    public async Task Create_With_Blank_Description_Should_Return_400_Validation()
    {
        var ctx = await SeedAsync();

        var response = await ctx.PostRawAsync(ctx.Body(description: "   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.Validation");
    }

    // ---- Tekrar koruması ----

    [Fact]
    public async Task Create_Twice_With_Same_RequestId_Should_Return_201_Then_200_And_Create_One_Row()
    {
        var ctx = await SeedAsync();
        var requestId = Guid.NewGuid();

        var first = await ctx.PostChargeAsync(ctx.Body(requestId: requestId));
        var second = await ctx.PostChargeAsync(ctx.Body(requestId: requestId));

        first.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Result!.Created.Should().BeFalse();
        second.Result.Charge.Id.Should().Be(first.Result!.Charge.Id);
        (await ctx.CountChargesAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_Creates_With_Same_RequestId_Should_Produce_Exactly_One_Charge()
    {
        var ctx = await SeedAsync();
        var requestId = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Task.Run(() => ctx.PostChargeAsync(ctx.Body(requestId: requestId)))));

        results.Should().OnlyContain(r => r.Response.IsSuccessStatusCode);
        results.Count(r => r.Response.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Select(r => r.Result!.Charge.Id).Distinct().Should().HaveCount(1);
        (await ctx.CountChargesAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RequestId_Reused_For_Another_Client_Should_Return_400()
    {
        var ctx = await SeedAsync();
        var requestId = Guid.NewGuid();
        (await ctx.PostChargeAsync(ctx.Body(requestId: requestId))).Response.StatusCode.Should().Be(HttpStatusCode.Created);
        var otherClient = await ctx.SeedClientAsync();

        var response = await ctx.PostRawAsync(ctx.Body(clientId: otherClient.ClientId, requestId: requestId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.Validation");
        (await ctx.CountChargesAsync()).Should().Be(1);
    }

    // ---- Hayvan / Visit / Muayene tutarlılığı ----

    [Fact]
    public async Task Create_With_Pet_Of_Another_Client_Should_Return_400_PetClientMismatch()
    {
        var ctx = await SeedAsync();
        var other = await ctx.SeedClientAsync();

        var response = await ctx.PostRawAsync(ctx.Body(petId: other.PetId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.PetClientMismatch");
    }

    [Fact]
    public async Task Create_With_Visit_Should_Link_And_Derive_Pet()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.SeedVisitAsync(ctx.PetId);

        var post = await ctx.PostChargeAsync(ctx.Body(visitId: visitId));

        post.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        post.Result!.Charge.VisitId.Should().Be(visitId);
        post.Result.Charge.PetId.Should().Be(ctx.PetId);
        post.Result.Charge.PetName.Should().NotBeNullOrEmpty();

        var byVisit = await ctx.Http.GetFromJsonAsync<PagedResult<ChargeDto>>(
            $"/api/v1/charges?clinicId={ctx.ClinicId}&visitId={visitId}");
        byVisit!.Items.Should().ContainSingle(i => i.Id == post.Result.Charge.Id);
    }

    [Fact]
    public async Task Create_With_Visit_Of_Another_Client_Should_Return_400_VisitClientMismatch()
    {
        var ctx = await SeedAsync();
        var other = await ctx.SeedClientAsync();
        var foreignVisitId = await ctx.SeedVisitAsync(other.PetId);

        var response = await ctx.PostRawAsync(ctx.Body(visitId: foreignVisitId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.VisitClientMismatch");
    }

    [Fact]
    public async Task Create_With_Visit_And_Different_Pet_Should_Return_400_VisitPetMismatch()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.SeedVisitAsync(ctx.PetId);
        var secondPetId = await ctx.SeedPetAsync(ctx.ClientId);

        var response = await ctx.PostRawAsync(ctx.Body(visitId: visitId, petId: secondPetId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.VisitPetMismatch");
    }

    [Fact]
    public async Task Create_With_Voided_Visit_Should_Return_400_VisitVoided()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.SeedVisitAsync(ctx.PetId, voided: true);

        var response = await ctx.PostRawAsync(ctx.Body(visitId: visitId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.VisitVoided");
    }

    [Fact]
    public async Task Create_With_Visit_In_Another_Clinic_Should_Return_400_VisitClinicMismatch()
    {
        var ctx = await SeedAsync();
        var foreignClinicId = await ctx.SeedForeignClinicAsync();
        var visitId = await ctx.SeedVisitAsync(ctx.PetId, clinicId: foreignClinicId);

        var response = await ctx.PostRawAsync(ctx.Body(visitId: visitId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.VisitClinicMismatch");
    }

    [Fact]
    public async Task Create_With_Unknown_Visit_Should_Return_404()
    {
        var ctx = await SeedAsync();

        var response = await ctx.PostRawAsync(ctx.Body(visitId: Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Visits.NotFound");
    }

    [Fact]
    public async Task Create_With_Examination_Without_Visit_Should_Link_And_Derive_Pet()
    {
        var ctx = await SeedAsync();
        var examinationId = await ctx.SeedExaminationAsync(ctx.PetId, visitId: null);

        var post = await ctx.PostChargeAsync(ctx.Body(examinationId: examinationId));

        post.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        post.Result!.Charge.ExaminationId.Should().Be(examinationId);
        post.Result.Charge.PetId.Should().Be(ctx.PetId);
        post.Result.Charge.VisitId.Should().BeNull();
    }

    [Fact]
    public async Task Create_With_Visit_And_Its_Own_Examination_Should_Succeed()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.SeedVisitAsync(ctx.PetId);
        var examinationId = await ctx.SeedExaminationAsync(ctx.PetId, visitId);

        var post = await ctx.PostChargeAsync(ctx.Body(visitId: visitId, examinationId: examinationId));

        post.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        post.Result!.Charge.VisitId.Should().Be(visitId);
        post.Result.Charge.ExaminationId.Should().Be(examinationId);
    }

    [Fact]
    public async Task Create_With_Examination_Bound_To_Another_Visit_Should_Return_400_ExaminationVisitMismatch()
    {
        var ctx = await SeedAsync();
        var visitId = await ctx.SeedVisitAsync(ctx.PetId);
        var examinationId = await ctx.SeedExaminationAsync(ctx.PetId, visitId: Guid.NewGuid());

        var response = await ctx.PostRawAsync(ctx.Body(visitId: visitId, examinationId: examinationId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.ExaminationVisitMismatch");
    }

    [Fact]
    public async Task Create_With_Examination_Of_Another_Client_Should_Return_400_ExaminationClientMismatch()
    {
        var ctx = await SeedAsync();
        var other = await ctx.SeedClientAsync();
        var examinationId = await ctx.SeedExaminationAsync(other.PetId, visitId: null);

        var response = await ctx.PostRawAsync(ctx.Body(examinationId: examinationId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.ExaminationClientMismatch");
    }

    [Fact]
    public async Task Create_With_Examination_And_Different_Pet_Should_Return_400_ExaminationPetMismatch()
    {
        var ctx = await SeedAsync();
        var secondPetId = await ctx.SeedPetAsync(ctx.ClientId);
        var examinationId = await ctx.SeedExaminationAsync(ctx.PetId, visitId: null);

        var response = await ctx.PostRawAsync(ctx.Body(examinationId: examinationId, petId: secondPetId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.ExaminationPetMismatch");
    }

    [Fact]
    public async Task Create_With_Examination_In_Another_Clinic_Should_Return_400_ExaminationClinicMismatch()
    {
        var ctx = await SeedAsync();
        var foreignClinicId = await ctx.SeedForeignClinicAsync();
        var examinationId = await ctx.SeedExaminationAsync(ctx.PetId, visitId: null, clinicId: foreignClinicId);

        var response = await ctx.PostRawAsync(ctx.Body(examinationId: examinationId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.ExaminationClinicMismatch");
    }

    // ---- Tenant / klinik izolasyonu ----

    [Fact]
    public async Task Create_For_Client_Of_Another_Tenant_Should_Return_404()
    {
        var ctx = await SeedAsync();
        var foreignClientId = await ctx.SeedForeignTenantClientAsync();

        var response = await ctx.PostRawAsync(ctx.Body(clientId: foreignClientId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clients.NotFound");
    }

    [Fact]
    public async Task Get_Charge_Of_Another_Tenant_Should_Return_404()
    {
        var ctx = await SeedAsync();
        var foreignChargeId = await ctx.SeedForeignTenantChargeAsync();

        var response = await ctx.Http.GetAsync($"/api/v1/charges/{foreignChargeId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_In_Unassigned_Clinic_Should_Return_403()
    {
        var ctx = await SeedAsync();
        var foreignClinicId = await ctx.SeedForeignClinicAsync();

        var response = await ctx.PostRawAsync(ctx.Body(clinicId: foreignClinicId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Get_Charge_In_Unassigned_Clinic_Should_Return_404_And_List_Should_Return_403()
    {
        var ctx = await SeedAsync();
        var foreignClinicId = await ctx.SeedForeignClinicAsync();
        var chargeId = await ctx.SeedChargeAsync(foreignClinicId);

        var get = await ctx.Http.GetAsync($"/api/v1/charges/{chargeId}");
        var list = await ctx.Http.GetAsync($"/api/v1/charges?clinicId={foreignClinicId}");

        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_Without_Clinic_Scope_Should_Return_400_ClinicScopeRequired()
    {
        var ctx = await SeedAsync();

        var response = await ctx.Http.GetAsync("/api/v1/charges");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Charges.ClinicScopeRequired");
    }

    [Fact]
    public async Task List_Should_Page_And_Order_By_CreatedAt_Descending()
    {
        var ctx = await SeedAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await ctx.PostChargeAsync(ctx.Body(description: $"Satir {i}"))).Result!.Charge.Id);
            await Task.Delay(20);
        }

        var page1 = await ctx.Http.GetFromJsonAsync<PagedResult<ChargeDto>>(
            $"/api/v1/charges?clinicId={ctx.ClinicId}&page=1&pageSize=2");
        var page2 = await ctx.Http.GetFromJsonAsync<PagedResult<ChargeDto>>(
            $"/api/v1/charges?clinicId={ctx.ClinicId}&page=2&pageSize=2");

        page1!.TotalItems.Should().Be(3);
        page1.Items.Select(i => i.Id).Should().Equal(ids[2], ids[1]);
        page2!.Items.Select(i => i.Id).Should().Equal(ids[0]);
    }

    // ---- Yetki ----

    [Fact]
    public async Task Create_Without_Create_Permission_Should_Return_403()
    {
        var ctx = await SeedAsync(permissions: [PermissionCatalog.Charges.Read]);

        var response = await ctx.PostRawAsync(ctx.Body());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ctx.CountChargesAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Read_Without_Read_Permission_Should_Return_403()
    {
        var ctx = await SeedAsync(permissions: [PermissionCatalog.Charges.Create]);
        var chargeId = await ctx.SeedChargeAsync(ctx.ClinicId);

        (await ctx.Http.GetAsync($"/api/v1/charges/{chargeId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ctx.Http.GetAsync($"/api/v1/charges?clinicId={ctx.ClinicId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Audit ----

    [Fact]
    public async Task Create_Should_Write_Audit_For_Success_And_Failure()
    {
        var ctx = await SeedAsync();
        var ok = await ctx.PostChargeAsync(ctx.Body());
        var bad = await ctx.PostRawAsync(ctx.Body(quantity: 0m));

        ok.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Charge.Create" && a.TargetId == $"ClientId={ctx.ClientId}")
            .ToListAsync();
        logs.Should().Contain(l => l.Success && l.ActorUserId == ctx.UserId);
        logs.Should().Contain(l => !l.Success);
    }

    // ---- Kurulum ----

    private async Task<ChargeTestContext> SeedAsync(string[]? permissions = null)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId;
        Guid clinicId;
        Guid userId;
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Chg-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"charge-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            clinicId = clinic.Id;
            userId = user.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, permissions ?? FullPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ctx = new ChargeTestContext(_factory.Services, http, tenantId, clinicId, userId);
        var seeded = await ctx.SeedClientAsync();
        ctx.ClientId = seeded.ClientId;
        ctx.PetId = seeded.PetId;
        return ctx;
    }

    private sealed class ChargeTestContext
    {
        private readonly IServiceProvider _services;

        public ChargeTestContext(IServiceProvider services, HttpClient http, Guid tenantId, Guid clinicId, Guid userId)
        {
            _services = services;
            Http = http;
            TenantId = tenantId;
            ClinicId = clinicId;
            UserId = userId;
        }

        public HttpClient Http { get; }
        public Guid TenantId { get; }
        public Guid ClinicId { get; }
        public Guid UserId { get; }
        public Guid ClientId { get; set; }
        public Guid PetId { get; set; }

        public object Body(
            Guid? clinicId = null,
            Guid? clientId = null,
            Guid? petId = null,
            Guid? visitId = null,
            Guid? examinationId = null,
            string description = "Genel muayene",
            decimal quantity = 1m,
            decimal unitPrice = 750m,
            Guid? requestId = null)
            => new
            {
                clinicId = clinicId ?? ClinicId,
                clientId = clientId ?? ClientId,
                petId,
                visitId,
                examinationId,
                description,
                quantity,
                unitPrice,
                requestId,
            };

        public Task<HttpResponseMessage> PostRawAsync(object body) => Http.PostAsJsonAsync("/api/v1/charges", body);

        public async Task<(HttpResponseMessage Response, ChargeCreateResultDto? Result)> PostChargeAsync(object body)
        {
            var response = await PostRawAsync(body);
            var result = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<ChargeCreateResultDto>()
                : null;
            return (response, result);
        }

        public async Task<int> CountChargesAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Charges.CountAsync(c => c.TenantId == TenantId && c.ClinicId == ClinicId);
        }

        public async Task<(Guid ClientId, Guid PetId)> SeedClientAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(TenantId, $"CClient-{Guid.NewGuid():N}"[..14], "905551110088");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(TenantId, client.Id, $"CPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return (client.Id, pet.Id);
        }

        public async Task<Guid> SeedPetAsync(Guid clientId)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(TenantId, clientId, $"CPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return pet.Id;
        }

        public async Task<Guid> SeedVisitAsync(Guid petId, Guid? clinicId = null, bool voided = false)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = new Visit(TenantId, clinicId ?? ClinicId, petId, null, null, UserId, DateTime.UtcNow);
            if (voided)
                visit.MarkAsMistaken("Yanlis hasta secildi", DateTime.UtcNow).IsSuccess.Should().BeTrue();
            db.Visits.Add(visit);
            await db.SaveChangesAsync();
            return visit.Id;
        }

        public async Task<Guid> SeedExaminationAsync(Guid petId, Guid? visitId, Guid? clinicId = null)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var examination = new Examination(
                TenantId, clinicId ?? ClinicId, petId, null, DateTime.UtcNow.AddMinutes(-5),
                "Kontrol", "Bulgu yok", null, null, visitId: visitId);
            db.Examinations.Add(examination);
            await db.SaveChangesAsync();
            return examination.Id;
        }

        /// <summary>Kullanıcıya atanmamış, aynı kiracıda ikinci klinik.</summary>
        public async Task<Guid> SeedForeignClinicAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinic(TenantId, $"Other-{Guid.NewGuid():N}"[..14], "Bursa");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();
            return clinic.Id;
        }

        public async Task<Guid> SeedChargeAsync(Guid clinicId)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var charge = Charge.Create(
                TenantId, clinicId, ClientId, null, null, null, "Seed ucret", 1m, 100m, UserId, DateTime.UtcNow, null)
                .Value!;
            db.Charges.Add(charge);
            await db.SaveChangesAsync();
            return charge.Id;
        }

        public async Task<Guid> SeedForeignTenantClientAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"Foreign-{Guid.NewGuid():N}"[..16]);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            var client = new Client(tenant.Id, $"FClient-{Guid.NewGuid():N}"[..14], "905551110099");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }

        public async Task<Guid> SeedForeignTenantChargeAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"Foreign-{Guid.NewGuid():N}"[..16]);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            var clinic = new Clinic(tenant.Id, $"FClinic-{Guid.NewGuid():N}"[..14], "Ankara");
            db.Clinics.Add(clinic);
            var client = new Client(tenant.Id, $"FClient-{Guid.NewGuid():N}"[..14], "905551110098");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var charge = Charge.Create(
                tenant.Id, clinic.Id, client.Id, null, null, null, "Yabanci ucret", 1m, 100m, UserId, DateTime.UtcNow, null)
                .Value!;
            db.Charges.Add(charge);
            await db.SaveChangesAsync();
            return charge.Id;
        }
    }
}
