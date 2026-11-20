using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Visits;

/// <summary>"Hesabı kapat" okuması (docs/VISIT_CHECKOUT_API_CONTRACT.md): HTTP + gerçek SQL Server. Her test kendi kliniğini kurar.</summary>
[Collection("pilot-smoke-api")]
public sealed class VisitCheckoutSummaryIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] Permissions =
    [
        PermissionCatalog.Visits.Read,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Charges.Read,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public VisitCheckoutSummaryIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Summary_Should_Split_Pending_Items_Visit_Charges_And_Other_Open_Charges()
    {
        var s = await SeedAsync(Permissions);
        var now = DateTime.UtcNow;
        Guid visitId, direct, viaExamination, oldOpen;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var oldVisit = NewVisit(s, now.AddDays(-20), VisitCareStatus.Completed);
            var visit = NewVisit(s, now.AddMinutes(-30), VisitCareStatus.InProgress);
            db.Visits.AddRange(oldVisit, visit);
            var exam = new Examination(s.TenantId, s.ClinicId, s.PetId, null, now.AddMinutes(-20), "Kontrol", null, null, null, now, visitId: visit.Id);
            var service = new Product(s.TenantId, $"Pansuman {Guid.NewGuid():N}"[..20], "Adet", 120m, "TRY", now, kind: ProductKind.Service);
            db.Examinations.Add(exam);
            db.Products.Add(service);
            await db.SaveChangesAsync();

            db.ChargeSuggestions.Add(ChargeSuggestion.ForExaminationService(exam, service, 2, now).Value!);
            var directCharge = NewCharge(s, visit.Id, null, 600m, now.AddMinutes(-10));
            directCharge.ApplyAllocation(100m, now).IsSuccess.Should().BeTrue();
            var examCharge = NewCharge(s, null, exam.Id, 200m, now.AddMinutes(-5));
            var oldCharge = NewCharge(s, oldVisit.Id, null, 150m, now.AddDays(-20));
            var oldPaid = NewCharge(s, oldVisit.Id, null, 80m, now.AddDays(-20));
            oldPaid.ApplyAllocation(80m, now).IsSuccess.Should().BeTrue();
            db.Charges.AddRange(directCharge, examCharge, oldCharge, oldPaid);
            await db.SaveChangesAsync();

            (visitId, direct, viaExamination, oldOpen) = (visit.Id, directCharge.Id, examCharge.Id, oldCharge.Id);
        }

        var summary = await s.GetSummaryAsync(visitId);

        summary.CareStatus.Should().Be(VisitCareStatus.InProgress);
        summary.PetId.Should().Be(s.PetId);
        summary.ClientId.Should().Be(s.ClientId);
        summary.ClientPhone.Should().Be("905551110099");
        summary.SpeciesName.Should().NotBeNullOrWhiteSpace();
        summary.Currency.Should().Be("TRY");

        var charges = summary.Charges!;
        charges.ChargeIndicator.Should().Be(VisitChargeIndicator.PartiallyPaid);
        charges.PendingItems.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            SourceType = "ExaminationService",
            Quantity = 2m,
            SuggestedUnitPrice = (decimal?)120m,
        });
        charges.VisitCharges.Select(c => (c.ChargeId, c.OutstandingAmount)).Should().Equal((direct, 500m), (viaExamination, 200m));
        charges.OtherOpenCharges.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            ChargeId = oldOpen,
            OutstandingAmount = 150m,
            PetId = (Guid?)s.PetId,
        });
        charges.CreditBalance.Should().Be(0m);
    }

    [Fact]
    public async Task Summary_Without_Charges_Read_Should_Return_Header_And_Null_Charges()
    {
        var s = await SeedAsync([PermissionCatalog.Visits.Read]);
        var visitId = await s.AddVisitAsync(s.ClinicId);

        var summary = await s.GetSummaryAsync(visitId);

        summary.VisitId.Should().Be(visitId);
        summary.ClientName.Should().NotBeNullOrWhiteSpace();
        summary.Charges.Should().BeNull();
    }

    [Fact]
    public async Task Summary_Of_Visit_In_Unassigned_Clinic_Should_Be_NotFound()
    {
        var s = await SeedAsync(Permissions);
        Guid otherClinicId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinic(s.TenantId, $"Other-{Guid.NewGuid():N}"[..14], "Bursa");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();
            otherClinicId = clinic.Id;
        }

        var visitId = await s.AddVisitAsync(otherClinicId);

        var response = await s.Http.GetAsync($"/api/v1/visits/{visitId}/checkout-summary");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    private static Visit NewVisit(Seeded s, DateTime arrivedAtUtc, VisitCareStatus status, Guid? clinicId = null)
    {
        var visit = new Visit(s.TenantId, clinicId ?? s.ClinicId, s.PetId, null, null, s.UserId, arrivedAtUtc);
        if (status != VisitCareStatus.Waiting)
        {
            visit.Start(arrivedAtUtc.AddMinutes(1)).IsSuccess.Should().BeTrue();
            if (status == VisitCareStatus.Completed)
                visit.Complete(arrivedAtUtc.AddMinutes(2)).IsSuccess.Should().BeTrue();
        }

        return visit;
    }

    private static Charge NewCharge(Seeded s, Guid? visitId, Guid? examinationId, decimal amount, DateTime createdAtUtc)
    {
        var charge = Charge.Create(
            s.TenantId, s.ClinicId, s.ClientId, s.PetId, visitId, examinationId, "Test ucreti", 1m, amount, s.UserId, createdAtUtc, null);
        charge.IsSuccess.Should().BeTrue(charge.IsSuccess ? "" : charge.Error.Message);
        return charge.Value!;
    }

    private async Task<Seeded> SeedAsync(IReadOnlyCollection<string> permissions)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Seeded seeded;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Chk-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            var email = $"checkout-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);

            var client = new Client(tenant.Id, $"CClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110099");
            db.Clients.Add(client);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));
            var speciesId = await db.Species.OrderBy(x => x.DisplayOrder).Select(x => x.Id).FirstAsync();
            var pet = new Pet(tenant.Id, client.Id, $"CPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();

            seeded = new Seeded(tenant.Id, clinic.Id, user.Id, client.Id, pet.Id, email);
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, seeded.Email, permissions);
        seeded.Http = _factory.CreateClient();
        seeded.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        seeded.Services = _factory.Services;
        return seeded;
    }

    private sealed record Seeded(Guid TenantId, Guid ClinicId, Guid UserId, Guid ClientId, Guid PetId, string Email)
    {
        public HttpClient Http { get; set; } = default!;
        public IServiceProvider Services { get; set; } = default!;

        public async Task<Guid> AddVisitAsync(Guid clinicId)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var visit = NewVisit(this, DateTime.UtcNow.AddMinutes(-10), VisitCareStatus.Waiting, clinicId);
            db.Visits.Add(visit);
            await db.SaveChangesAsync();
            return visit.Id;
        }

        public async Task<VisitCheckoutSummaryDto> GetSummaryAsync(Guid visitId)
        {
            var response = await Http.GetAsync($"/api/v1/visits/{visitId}/checkout-summary");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<VisitCheckoutSummaryDto>())!;
        }
    }
}
