using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Payments;

/// <summary>
/// Tahsilat–ücret ilişkilendirme uçtan uca: HTTP + gerçek SQL Server (LocalDB, izole test veritabanı).
/// Fazla ödeme reddi, toplam eşitliği, requestId (aynı/farklı içerik/eşzamanlı), iki kullanıcı çakışması,
/// DB CHECK kısıtları, tahsisli ödeme PUT kilidi, düzeltme yetkisi/gerekçe/audit, yetki ve izolasyon.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class PaymentAllocationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AdminPermissions =
    [
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Payments.Create,
        PermissionCatalog.Payments.Update,
        PermissionCatalog.Payments.Correct,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Charges.Create,
    ];

    private static readonly string[] ReceptionPermissions =
    [
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Payments.Create,
        PermissionCatalog.Payments.Update,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Charges.Create,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public PaymentAllocationIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ---- Kabul senaryosu ----

    [Fact]
    public async Task Charge_1000_Paid_400_Then_600_Should_Close_The_Charge_And_List_Allocations()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(1000m);

        var first = await ctx.PostPaymentAsync(ctx.Body(400m, chargeId));
        first.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ctx.GetChargeAsync(chargeId)).OutstandingAmount.Should().Be(600m);

        var second = await ctx.PostPaymentAsync(ctx.Body(600m, chargeId));
        second.Response.StatusCode.Should().Be(HttpStatusCode.Created);

        var charge = await ctx.GetChargeAsync(chargeId);
        charge.AllocatedAmount.Should().Be(1000m);
        charge.OutstandingAmount.Should().Be(0m);

        var payment = await ctx.Http.GetFromJsonAsync<PaymentDetailDto>($"/api/v1/payments/{second.Id}");
        payment!.AllocatedAmount.Should().Be(600m);
        payment.UnallocatedAmount.Should().Be(0m);

        var byPayment = await ctx.Http.GetFromJsonAsync<List<PaymentAllocationItemDto>>(
            $"/api/v1/payments/{second.Id}/allocations");
        byPayment.Should().ContainSingle(a => a.ChargeId == chargeId && a.Amount == 600m);

        var byCharge = await ctx.Http.GetFromJsonAsync<List<ChargeAllocationItemDto>>(
            $"/api/v1/charges/{chargeId}/allocations");
        byCharge!.Select(a => a.Amount).Should().BeEquivalentTo(new[] { 400m, 600m });

        await ctx.AssertCountersMatchAllocationsAsync();
    }

    [Fact]
    public async Task Payment_Can_Be_Split_Across_Two_Charges()
    {
        var ctx = await SeedAsync();
        var a = await ctx.SeedChargeAsync(300m);
        var b = await ctx.SeedChargeAsync(200m);

        var result = await ctx.PostPaymentRawAsync(ctx.Body(500m, (a, 300m), (b, 200m)));

        result.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ctx.GetChargeAsync(a)).OutstandingAmount.Should().Be(0m);
        (await ctx.GetChargeAsync(b)).OutstandingAmount.Should().Be(0m);
        await ctx.AssertCountersMatchAllocationsAsync();
    }

    [Fact]
    public async Task Payment_Without_Pet_Should_Show_Pet_Of_Allocated_Charge_In_Detail_And_Pet_Filter()
    {
        var ctx = await SeedAsync();
        var pet = await ctx.SeedPetAsync();
        var chargeId = await ctx.SeedChargeAsync(300m, petId: pet.Id);

        var created = await ctx.PostPaymentAsync(ctx.Body(300m, chargeId));

        created.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await ctx.Http.GetFromJsonAsync<PaymentDetailDto>($"/api/v1/payments/{created.Id}");
        detail!.PetId.Should().Be(pet.Id);
        detail.PetName.Should().Be(pet.Name);
        detail.PetCount.Should().Be(1);
        var listResponse = await ctx.Http.GetAsync($"/api/v1/payments?Page=1&PageSize=25&ClinicId={ctx.ClinicId}&PetId={pet.Id}");
        var list = await listResponse.Content.ReadAsStringAsync();
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, list);
        list.Should().Contain(created.Id.ToString());
    }

    [Fact]
    public async Task Payment_Across_Two_Pets_Should_Have_No_PetId_And_PetCount_2_In_Detail_And_List()
    {
        var ctx = await SeedAsync();
        var petA = await ctx.SeedPetAsync();
        var petB = await ctx.SeedPetAsync();
        var a = await ctx.SeedChargeAsync(100m, petId: petA.Id);
        var b = await ctx.SeedChargeAsync(100m, petId: petB.Id);

        var created = await ctx.PostPaymentAsync(ctx.Body(200m, (a, 100m), (b, 100m)));

        created.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await ctx.Http.GetFromJsonAsync<PaymentDetailDto>($"/api/v1/payments/{created.Id}");
        detail!.PetId.Should().BeNull();
        detail.PetCount.Should().Be(2);
        var list = await ctx.Http.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/v1/payments?Page=1&PageSize=25&ClinicId={ctx.ClinicId}");
        var item = list.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == created.Id);
        item.GetProperty("petCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Payment_Without_Any_Pet_Should_Have_PetCount_0()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(50m);

        var created = await ctx.PostPaymentAsync(ctx.Body(50m, chargeId));

        var detail = await ctx.Http.GetFromJsonAsync<PaymentDetailDto>($"/api/v1/payments/{created.Id}");
        detail!.PetId.Should().BeNull();
        detail.PetCount.Should().Be(0);
    }

    // ---- Fazla ödeme / toplam eşitliği / zorunlu alanlar ----

    [Fact]
    public async Task Overpayment_Should_Return_409_ExceedsOutstanding_And_Persist_Nothing()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(1000m);
        (await ctx.PostPaymentAsync(ctx.Body(400m, chargeId))).Response.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await ctx.PostPaymentRawAsync(ctx.Body(601m, chargeId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("PaymentAllocations.ExceedsOutstanding");
        (await ctx.GetChargeAsync(chargeId)).AllocatedAmount.Should().Be(400m);
        (await ctx.CountPaymentsAsync()).Should().Be(1);
        await ctx.AssertCountersMatchAllocationsAsync();
    }

    [Theory]
    [InlineData(500, 400)]
    [InlineData(500, 600)]
    public async Task Allocation_Sum_Different_From_Amount_Should_Return_400(decimal amount, decimal allocated)
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(1000m);

        var response = await ctx.PostPaymentRawAsync(ctx.Body(amount, (chargeId, allocated)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Payments.AllocationSumMismatch");
        (await ctx.CountPaymentsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Missing_RequestId_Or_Allocations_Should_Return_400()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(100m);

        var noRequestId = await ctx.Http.PostAsJsonAsync("/api/v1/payments", new
        {
            ctx.ClinicId, ctx.ClientId, Amount = 100m, Currency = "TRY", Method = PaymentMethod.Cash,
            PaidAtUtc = DateTime.UtcNow.AddMinutes(-1),
            Allocations = new[] { new { ChargeId = chargeId, Amount = 100m } },
        });
        var noAllocations = await ctx.Http.PostAsJsonAsync("/api/v1/payments", new
        {
            ctx.ClinicId, ctx.ClientId, Amount = 100m, Currency = "TRY", Method = PaymentMethod.Cash,
            PaidAtUtc = DateTime.UtcNow.AddMinutes(-1), RequestId = Guid.NewGuid(),
        });

        noRequestId.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noRequestId)).Should().Be("Payments.RequestIdRequired");
        noAllocations.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noAllocations)).Should().Be("Payments.AllocationsRequired");
        (await ctx.CountPaymentsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task New_Payment_In_Non_Try_Currency_Should_Return_400()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(100m);

        var response = await ctx.PostPaymentRawAsync(ctx.Body(100m, chargeId, currency: "USD"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Payments.Validation");
    }

    // ---- requestId: tekrar ----

    [Fact]
    public async Task Same_RequestId_And_Content_Should_Return_200_Replay_And_Create_One_Payment()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(1000m);
        var requestId = Guid.NewGuid();
        var paidAt = DateTime.UtcNow.AddMinutes(-2);

        var first = await ctx.PostPaymentAsync(ctx.Body(400m, chargeId, requestId: requestId, paidAtUtc: paidAt));
        var second = await ctx.PostPaymentAsync(ctx.Body(400m, chargeId, requestId: requestId, paidAtUtc: paidAt));

        first.Response.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Response.Headers.GetValues("Idempotency-Replayed").Should().ContainSingle("true");
        second.Id.Should().Be(first.Id);
        (await ctx.CountPaymentsAsync()).Should().Be(1);
        (await ctx.GetChargeAsync(chargeId)).AllocatedAmount.Should().Be(400m);
        await ctx.AssertCountersMatchAllocationsAsync();
    }

    [Fact]
    public async Task Same_RequestId_With_Different_Content_Should_Return_409_DuplicateRequestMismatch()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(1000m);
        var requestId = Guid.NewGuid();
        (await ctx.PostPaymentAsync(ctx.Body(400m, chargeId, requestId: requestId))).Response.StatusCode
            .Should().Be(HttpStatusCode.Created);

        var response = await ctx.PostPaymentRawAsync(ctx.Body(300m, chargeId, requestId: requestId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Payments.DuplicateRequestMismatch");
        (await ctx.CountPaymentsAsync()).Should().Be(1);
        (await ctx.GetChargeAsync(chargeId)).AllocatedAmount.Should().Be(400m);
    }

    [Fact]
    public async Task Failed_Request_Should_Not_Consume_RequestId()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(100m);
        var requestId = Guid.NewGuid();

        var tooMuch = await ctx.PostPaymentRawAsync(ctx.Body(150m, chargeId, requestId: requestId));
        var corrected = await ctx.PostPaymentAsync(ctx.Body(100m, chargeId, requestId: requestId));

        tooMuch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        corrected.Response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Concurrent_Requests_With_Same_RequestId_Should_Produce_Exactly_One_Payment()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(1000m);
        var requestId = Guid.NewGuid();
        var paidAt = DateTime.UtcNow.AddMinutes(-2);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
            ctx.PostPaymentAsync(ctx.Body(400m, chargeId, requestId: requestId, paidAtUtc: paidAt)))));

        results.Select(r => r.Response.StatusCode).Should()
            .OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        results.Count(r => r.Response.StatusCode == HttpStatusCode.Created).Should().Be(1);
        (await ctx.CountPaymentsAsync()).Should().Be(1);
        (await ctx.GetChargeAsync(chargeId)).AllocatedAmount.Should().Be(400m);
        await ctx.AssertCountersMatchAllocationsAsync();
    }

    // ---- İki kullanıcı çakışması ----

    [Fact]
    public async Task Two_Users_Paying_The_Same_Charge_Concurrently_Should_Never_Over_Allocate()
    {
        var ctx = await SeedAsync();
        var other = await ctx.NewHttpForAnotherUserAsync(AdminPermissions);
        var chargeId = await ctx.SeedChargeAsync(1000m);

        var results = await Task.WhenAll(
            Task.Run(() => ctx.PostPaymentRawAsync(ctx.Body(600m, chargeId))),
            Task.Run(() => other.PostAsJsonAsync("/api/v1/payments", ctx.Body(600m, chargeId))));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        var loser = results.Single(r => r.StatusCode != HttpStatusCode.Created);
        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(loser)).Should()
            .BeOneOf("Payments.ConcurrencyConflict", "PaymentAllocations.ExceedsOutstanding");
        (await ctx.GetChargeAsync(chargeId)).AllocatedAmount.Should().Be(600m);
        (await ctx.CountPaymentsAsync()).Should().Be(1);
        await ctx.AssertCountersMatchAllocationsAsync();
    }

    [Fact]
    public async Task Many_Concurrent_Payments_Should_Keep_Sum_Of_Allocations_Within_Charge_Amount()
    {
        var ctx = await SeedAsync();
        var other = await ctx.NewHttpForAnotherUserAsync(AdminPermissions);
        var chargeId = await ctx.SeedChargeAsync(500m);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() =>
            (i % 2 == 0 ? ctx.Http : other).PostAsJsonAsync("/api/v1/payments", ctx.Body(100m, chargeId)))));

        var created = results.Count(r => r.StatusCode == HttpStatusCode.Created);
        results.Where(r => r.StatusCode != HttpStatusCode.Created).Should()
            .OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        created.Should().BeInRange(1, 5);
        (await ctx.GetChargeAsync(chargeId)).AllocatedAmount.Should().Be(created * 100m);
        (await ctx.CountPaymentsAsync()).Should().Be(created);
        await ctx.AssertCountersMatchAllocationsAsync();
    }

    // ---- DB kısıtları ----

    [Fact]
    public async Task Database_Check_Constraints_Should_Reject_Over_Allocated_Counters_And_Non_Positive_Allocations()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(100m);
        var paymentId = (await ctx.PostPaymentAsync(ctx.Body(100m, chargeId))).Id;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var overCharge = async () => await db.Database.ExecuteSqlRawAsync(
            "UPDATE Charges SET AllocatedAmount = Amount + 1 WHERE Id = {0}", chargeId);
        var overPayment = async () => await db.Database.ExecuteSqlRawAsync(
            "UPDATE Payments SET AllocatedAmount = Amount + 1 WHERE Id = {0}", paymentId);
        var negative = async () => await db.Database.ExecuteSqlRawAsync(
            "UPDATE Charges SET AllocatedAmount = -1 WHERE Id = {0}", chargeId);
        var zeroAllocation = async () => await db.Database.ExecuteSqlRawAsync(
            "UPDATE PaymentAllocations SET Amount = 0 WHERE PaymentId = {0}", paymentId);

        await overCharge.Should().ThrowAsync<Exception>();
        await overPayment.Should().ThrowAsync<Exception>();
        await negative.Should().ThrowAsync<Exception>();
        await zeroAllocation.Should().ThrowAsync<Exception>();
    }

    // ---- Tutarlılık / izolasyon ----

    [Fact]
    public async Task Charge_Of_Another_Client_Should_Return_400_ChargeClientMismatch()
    {
        var ctx = await SeedAsync();
        var otherClientId = await ctx.SeedClientAsync();
        var foreignCharge = await ctx.SeedChargeAsync(100m, clientId: otherClientId);

        var response = await ctx.PostPaymentRawAsync(ctx.Body(100m, foreignCharge));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("PaymentAllocations.ChargeClientMismatch");
    }

    [Fact]
    public async Task Charge_In_Another_Clinic_Should_Return_400_ChargeClinicMismatch()
    {
        var ctx = await SeedAsync();
        var foreignClinicId = await ctx.SeedForeignClinicAsync();
        var foreignCharge = await ctx.SeedChargeAsync(100m, clinicId: foreignClinicId);

        var response = await ctx.PostPaymentRawAsync(ctx.Body(100m, foreignCharge));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("PaymentAllocations.ChargeClinicMismatch");
    }

    [Fact]
    public async Task Charge_Of_Another_Tenant_Or_Unknown_Should_Return_404()
    {
        var ctx = await SeedAsync();
        var foreignTenantCharge = await ctx.SeedForeignTenantChargeAsync();

        var foreign = await ctx.PostPaymentRawAsync(ctx.Body(100m, foreignTenantCharge));
        var unknown = await ctx.PostPaymentRawAsync(ctx.Body(100m, Guid.NewGuid()));

        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(foreign)).Should().Be("Charges.NotFound");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Payment_In_Unassigned_Clinic_Should_Return_403()
    {
        var ctx = await SeedAsync();
        var foreignClinicId = await ctx.SeedForeignClinicAsync();
        var foreignCharge = await ctx.SeedChargeAsync(100m, clinicId: foreignClinicId);

        var response = await ctx.PostPaymentRawAsync(ctx.Body(100m, foreignCharge, clinicId: foreignClinicId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Allocation_Lists_Of_Other_Tenant_Or_Unassigned_Clinic_Should_Return_404()
    {
        var ctx = await SeedAsync();
        var foreignClinicId = await ctx.SeedForeignClinicAsync();
        var foreignCharge = await ctx.SeedChargeAsync(100m, clinicId: foreignClinicId);
        var foreignTenantCharge = await ctx.SeedForeignTenantChargeAsync();

        (await ctx.Http.GetAsync($"/api/v1/charges/{foreignCharge}/allocations")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await ctx.Http.GetAsync($"/api/v1/charges/{foreignTenantCharge}/allocations")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await ctx.Http.GetAsync($"/api/v1/payments/{Guid.NewGuid()}/allocations")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Yetki ----

    [Fact]
    public async Task Create_Without_Create_Permission_Should_Return_403_And_Allocations_Need_Read_Permissions()
    {
        var readOnly = await SeedAsync([PermissionCatalog.Payments.Read]);
        var chargeId = await readOnly.SeedChargeAsync(100m);
        (await readOnly.PostPaymentRawAsync(readOnly.Body(100m, chargeId))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var noRead = await SeedAsync([PermissionCatalog.Payments.Create]);
        var chargeId2 = await noRead.SeedChargeAsync(100m);
        var paymentId = (await noRead.PostPaymentAsync(noRead.Body(100m, chargeId2))).Id;
        (await noRead.Http.GetAsync($"/api/v1/payments/{paymentId}/allocations")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await noRead.Http.GetAsync($"/api/v1/charges/{chargeId2}/allocations")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- PUT kilidi / düzeltme ----

    [Fact]
    public async Task Allocated_Payment_Correction_Fields_Are_Locked_Even_For_Correct_Permission_But_Ordinary_Fields_Update()
    {
        var ctx = await SeedAsync();
        var chargeId = await ctx.SeedChargeAsync(500m);
        var paymentId = (await ctx.PostPaymentAsync(ctx.Body(500m, chargeId))).Id;

        var amount = await ctx.PutAsync(paymentId, amount: 450m, reason: "Yanlis tutar girildi");
        var ordinary = await ctx.PutAsync(paymentId, amount: 500m, method: PaymentMethod.Card, notes: "Kart ile");

        amount.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(amount)).Should().Be("Payments.UpdateBlockedByAllocation");
        ordinary.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await ctx.Http.GetFromJsonAsync<PaymentDetailDto>($"/api/v1/payments/{paymentId}");
        detail!.Amount.Should().Be(500m);
        detail.Method.Should().Be(PaymentMethod.Card);
        detail.AllocatedAmount.Should().Be(500m);
    }

    [Fact]
    public async Task Legacy_Unallocated_Payment_Correction_Needs_Correct_Permission_And_Reason_And_Is_Audited()
    {
        var admin = await SeedAsync();
        var reception = await SeedAsync(ReceptionPermissions);
        var adminPayment = await admin.SeedLegacyPaymentAsync(80m);
        var receptionPayment = await reception.SeedLegacyPaymentAsync(80m);

        // Sekreter: aynı değerlerle (sıradan alan) güncelleyebilir, tutar düzeltemez.
        (await reception.PutAsync(receptionPayment, amount: 80m, notes: "Not")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        var forbidden = await reception.PutAsync(receptionPayment, amount: 90m, reason: "Yanlis tutar girildi");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(forbidden)).Should().Be("Payments.CorrectionForbidden");

        // Yönetici: gerekçesiz 400, gerekçeli 204 + audit.
        var noReason = await admin.PutAsync(adminPayment, amount: 90m);
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noReason)).Should().Be("Payments.Validation");
        var corrected = await admin.PutAsync(adminPayment, amount: 90m, reason: "Yanlis tutar girildi");
        corrected.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.Http.GetFromJsonAsync<PaymentDetailDto>($"/api/v1/payments/{adminPayment}"))!.Amount.Should().Be(90m);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Payment.Update" && a.TargetId == $"PaymentId={adminPayment}" && a.Success)
            .SingleAsync(l => l.RequestPayload!.Contains("Yanlis tutar girildi"));
        log.ActorUserId.Should().Be(admin.UserId);
        (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
            a.Action == "Payment.Update" && a.TargetId == $"PaymentId={receptionPayment}" && !a.Success)).Should().BeTrue();
    }

    [Fact]
    public async Task Legacy_Payment_Should_Stay_Unallocated_And_Keep_Totals()
    {
        var ctx = await SeedAsync();
        var legacy = await ctx.SeedLegacyPaymentAsync(120m);

        var detail = await ctx.Http.GetFromJsonAsync<PaymentDetailDto>($"/api/v1/payments/{legacy}");

        detail!.AllocatedAmount.Should().Be(0m);
        detail.UnallocatedAmount.Should().Be(120m);
        (await ctx.Http.GetFromJsonAsync<List<PaymentAllocationItemDto>>($"/api/v1/payments/{legacy}/allocations"))
            .Should().BeEmpty();
    }

    // ---- Kurulum ----

    private async Task<AllocationTestContext> SeedAsync(string[]? permissions = null)
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
            var clinic = new Clinic(tenant.Id, $"Alc-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            (userId, email) = await AllocationTestContext.SeedUserAsync(db, hasher, tenant.Id, clinic.Id);
            tenantId = tenant.Id;
            clinicId = clinic.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, permissions ?? AdminPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ctx = new AllocationTestContext(_factory.Services, hasher, _factory.CreateClient, http, tenantId, clinicId, userId);
        ctx.ClientId = await ctx.SeedClientAsync();
        return ctx;
    }

    private sealed class AllocationTestContext
    {
        private readonly IServiceProvider _services;
        private readonly IPasswordHasher _hasher;
        private readonly Func<HttpClient> _createClient;

        public AllocationTestContext(
            IServiceProvider services, IPasswordHasher hasher, Func<HttpClient> createClient, HttpClient http, Guid tenantId, Guid clinicId, Guid userId)
        {
            _services = services;
            _hasher = hasher;
            _createClient = createClient;
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

        public static async Task<(Guid UserId, string Email)> SeedUserAsync(
            AppDbContext db, IPasswordHasher hasher, Guid tenantId, Guid clinicId)
        {
            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            var email = $"alloc-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenantId, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinicId, DateTime.UtcNow));
            await db.SaveChangesAsync();
            return (user.Id, email);
        }

        public async Task<HttpClient> NewHttpForAnotherUserAsync(string[] permissions)
        {
            string email;
            await using (var scope = _services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (_, email) = await SeedUserAsync(db, _hasher, TenantId, ClinicId);
            }

            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_services, email, permissions);
            var http = _createClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        public object Body(
            decimal amount,
            Guid chargeId,
            Guid? requestId = null,
            string currency = "TRY",
            Guid? clinicId = null,
            DateTime? paidAtUtc = null)
            => Body(amount, [(chargeId, amount)], requestId, currency, clinicId, paidAtUtc);

        public object Body(decimal amount, params (Guid ChargeId, decimal Amount)[] lines)
            => Body(amount, lines, null, "TRY", null, null);

        private object Body(
            decimal amount,
            (Guid ChargeId, decimal Amount)[] lines,
            Guid? requestId,
            string currency,
            Guid? clinicId,
            DateTime? paidAtUtc)
            => new
            {
                ClinicId = clinicId ?? ClinicId,
                ClientId,
                Amount = amount,
                Currency = currency,
                Method = PaymentMethod.Cash,
                PaidAtUtc = paidAtUtc ?? DateTime.UtcNow.AddMinutes(-1),
                RequestId = requestId ?? Guid.NewGuid(),
                Allocations = lines.Select(l => new { l.ChargeId, l.Amount }).ToArray(),
            };

        public Task<HttpResponseMessage> PostPaymentRawAsync(object body) => Http.PostAsJsonAsync("/api/v1/payments", body);

        public async Task<(HttpResponseMessage Response, Guid Id)> PostPaymentAsync(object body)
        {
            var response = await PostPaymentRawAsync(body);
            var id = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<Guid>() : Guid.Empty;
            return (response, id);
        }

        public Task<HttpResponseMessage> PutAsync(
            Guid paymentId,
            decimal amount,
            string? reason = null,
            PaymentMethod method = PaymentMethod.Cash,
            string? notes = null)
            => Http.PutAsJsonAsync($"/api/v1/payments/{paymentId}", new
            {
                ClinicId,
                ClientId,
                Amount = amount,
                Currency = "TRY",
                Method = method,
                PaidAtUtc = DateTime.UtcNow.AddMinutes(-1),
                Notes = notes,
                Reason = reason,
            });

        public async Task<ChargeDto> GetChargeAsync(Guid chargeId)
            => (await Http.GetFromJsonAsync<ChargeDto>($"/api/v1/charges/{chargeId}"))!;

        public async Task<Guid> SeedClientAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(TenantId, $"AClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110077");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }

        public async Task<Guid> SeedChargeAsync(decimal amount, Guid? clientId = null, Guid? clinicId = null, Guid? petId = null)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var charge = Charge.Create(
                    TenantId, clinicId ?? ClinicId, clientId ?? ClientId, petId, null, null,
                    "Seed ucret", 1m, amount, UserId, DateTime.UtcNow, null)
                .Value!;
            db.Charges.Add(charge);
            await db.SaveChangesAsync();
            return charge.Id;
        }

        public async Task<(Guid Id, string Name)> SeedPetAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(TenantId, ClientId, $"APet-{Guid.NewGuid():N}"[..10], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return (pet.Id, pet.Name);
        }

        public async Task<Guid> SeedLegacyPaymentAsync(decimal amount)        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var payment = new Payment(
                TenantId, ClinicId, ClientId, null, null, null, amount, "TRY", PaymentMethod.Cash,
                DateTime.UtcNow.AddMinutes(-5), "Eski kayit");
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
            return payment.Id;
        }

        public async Task<Guid> SeedForeignClinicAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinic(TenantId, $"Other-{Guid.NewGuid():N}"[..14], "Bursa");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();
            return clinic.Id;
        }

        public async Task<Guid> SeedForeignTenantChargeAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"Foreign-{Guid.NewGuid():N}"[..16], DateTime.UtcNow);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            var clinic = new Clinic(tenant.Id, $"FClinic-{Guid.NewGuid():N}"[..14], "Ankara");
            var client = new Client(tenant.Id, $"FClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110098");
            db.Clinics.Add(clinic);
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var charge = Charge.Create(
                    tenant.Id, clinic.Id, client.Id, null, null, null, "Yabanci ucret", 1m, 100m,
                    UserId, DateTime.UtcNow, null)
                .Value!;
            db.Charges.Add(charge);
            await db.SaveChangesAsync();
            return charge.Id;
        }

        public async Task<int> CountPaymentsAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Payments.CountAsync(p => p.TenantId == TenantId && p.ClinicId == ClinicId);
        }

        /// <summary>Sayaç değişmezi: her ödeme/ücret için Σ PaymentAllocations.Amount == AllocatedAmount.</summary>
        public async Task AssertCountersMatchAllocationsAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var byPayment = await db.PaymentAllocations.AsNoTracking()
                .Where(a => a.TenantId == TenantId && a.ClinicId == ClinicId)
                .GroupBy(a => a.PaymentId)
                .Select(g => new { Id = g.Key, Sum = g.Sum(x => x.Amount) })
                .ToListAsync();
            foreach (var row in byPayment)
            {
                (await db.Payments.AsNoTracking().Where(p => p.Id == row.Id).Select(p => p.AllocatedAmount).SingleAsync())
                    .Should().Be(row.Sum);
            }

            var byCharge = await db.PaymentAllocations.AsNoTracking()
                .Where(a => a.TenantId == TenantId && a.ClinicId == ClinicId)
                .GroupBy(a => a.ChargeId)
                .Select(g => new { Id = g.Key, Sum = g.Sum(x => x.Amount) })
                .ToListAsync();
            foreach (var row in byCharge)
            {
                var charge = await db.Charges.AsNoTracking().SingleAsync(c => c.Id == row.Id);
                charge.AllocatedAmount.Should().Be(row.Sum);
                charge.AllocatedAmount.Should().BeLessThanOrEqualTo(charge.Amount);
            }
        }
    }
}
