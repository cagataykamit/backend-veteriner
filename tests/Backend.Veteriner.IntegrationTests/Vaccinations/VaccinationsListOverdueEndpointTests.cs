using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Vaccinations;

[Collection("products-api")]
public sealed class VaccinationsListOverdueEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public VaccinationsListOverdueEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetList_OnlyOverdueTrue_WithStatusZero_ReturnsOverdueScheduledAndOpenNextDue()
    {
        var ctx = await SeedOverdueScenarioAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var url =
            $"/api/v1/vaccinations?page=1&pageSize=50&status=0&onlyOverdue=true&clinicId={ctx.ClinicId:D}";
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Uygulanmış aşının karşılanmamış sonraki tarihi de gecikmiştir; aynı aşı sonra uygulandıysa (FulfilledPast) sayılmaz (VaccinationDueRule).
        json.GetProperty("totalItems").GetInt32().Should().Be(2);
        var items = json.GetProperty("items").EnumerateArray().ToList();
        items.Select(e => e.GetProperty("id").GetGuid()).Should().BeEquivalentTo([ctx.OverdueScheduledId, ctx.AppliedPastDueId]);
        items.Single(e => e.GetProperty("id").GetGuid() == ctx.AppliedPastDueId).GetProperty("status").GetInt32().Should().Be((int)VaccinationStatus.Applied);
    }

    [Fact]
    public async Task GetList_OnlyOverdueFalse_WithStatusZero_ReturnsAllScheduledVarieties()
    {
        var ctx = await SeedOverdueScenarioAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var url =
            $"/api/v1/vaccinations?page=1&pageSize=50&status=0&onlyOverdue=false&clinicId={ctx.ClinicId:D}";
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalItems").GetInt32().Should().Be(3);
        var ids = json.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToHashSet();
        ids.Should().Contain(ctx.OverdueScheduledId);
        ids.Should().Contain(ctx.FutureScheduledId);
        ids.Should().Contain(ctx.NullDueScheduledId);
        ids.Should().NotContain(ctx.AppliedPastDueId);
        ids.Should().NotContain(ctx.CancelledPastDueId);
    }

    [Fact]
    public async Task GetList_OnlyOverdueTrue_WithStatusApplied_StillReturnsOverdueScheduledPreset()
    {
        var ctx = await SeedOverdueScenarioAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var url =
            $"/api/v1/vaccinations?page=1&pageSize=50&status=1&onlyOverdue=true&clinicId={ctx.ClinicId:D}";
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalItems").GetInt32().Should().Be(2);
        json.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo([ctx.OverdueScheduledId, ctx.AppliedPastDueId]);
    }

    [Fact]
    public async Task GetList_OnlyOverdueTrue_DoesNotIncludeOtherTenantRows()
    {
        var a = await SeedOverdueScenarioAsync();
        var b = await SeedOverdueScenarioAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.Token);

        var url =
            $"/api/v1/vaccinations?page=1&pageSize=50&onlyOverdue=true&clinicId={a.ClinicId:D}";
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in json.GetProperty("items").EnumerateArray())
            item.GetProperty("id").GetGuid().Should().NotBe(b.OverdueScheduledId);
    }

    [Fact]
    public async Task GetList_OnlyReceivingCareTrue_ExcludesDeceasedPetScheduled_DefaultStillReturnsIt()
    {
        var ctx = await SeedOverdueScenarioAsync(withDeceasedPetScheduled: true);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var filtered = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/vaccinations?page=1&pageSize=50&status=0&onlyReceivingCare=true&clinicId={ctx.ClinicId:D}");
        var filteredIds = filtered.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        filtered.GetProperty("totalItems").GetInt32().Should().Be(3);
        filteredIds.Should().NotContain(ctx.DeceasedPetScheduledId!.Value);
        filteredIds.Should().Contain(ctx.FutureScheduledId);

        var unfiltered = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/vaccinations?page=1&pageSize=50&status=0&clinicId={ctx.ClinicId:D}");
        unfiltered.GetProperty("totalItems").GetInt32().Should().Be(4);
        unfiltered.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid())
            .Should().Contain(ctx.DeceasedPetScheduledId.Value);
    }

    [Fact]
    public async Task GetList_OnlyOverdueTrue_Excludes_Due_Older_Than_One_Calendar_Month_But_Status_List_Keeps_It()
    {
        var ctx = await SeedOverdueScenarioAsync(withTooOldScheduled: true);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var overdue = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/vaccinations?page=1&pageSize=50&onlyOverdue=true&clinicId={ctx.ClinicId:D}");
        var scheduled = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/vaccinations?page=1&pageSize=50&status=0&clinicId={ctx.ClinicId:D}");

        overdue.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo([ctx.OverdueScheduledId, ctx.AppliedPastDueId]);
        scheduled.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid())
            .Should().Contain(ctx.TooOldScheduledId!.Value);
    }

    private sealed record OverdueSeedCtx(
        string Token,
        Guid ClinicId,
        Guid OverdueScheduledId,
        Guid FutureScheduledId,
        Guid NullDueScheduledId,
        Guid AppliedPastDueId,
        Guid CancelledPastDueId,
        Guid? DeceasedPetScheduledId = null,
        Guid? TooOldScheduledId = null);

    private async Task<OverdueSeedCtx> SeedOverdueScenarioAsync(bool withDeceasedPetScheduled = false, bool withTooOldScheduled = false)
    {
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Vac-{Guid.NewGuid():N}"[..16], DateTime.UtcNow);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var client = new Client(tenant.Id, "Müşteri", DateTime.UtcNow, "905551112200", "vac@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, "PetVac", speciesId);

        var catalogVaccine = await db.VaccineDefinitions.AsNoTracking()
            .Where(v => v.TenantId == null && v.Code == "RABIES")
            .Select(v => new { v.Id, v.Name })
            .FirstAsync();

        var now = DateTime.UtcNow;
        var overdue = new Vaccination(
            tenant.Id,
            pet.Id,
            clinic.Id,
            null,
            catalogVaccine.Id,
            "OverdueCase",
            VaccinationStatus.Scheduled,
            null,
            now.AddDays(-10),
            null,
            DateTime.UtcNow);

        var future = new Vaccination(
            tenant.Id,
            pet.Id,
            clinic.Id,
            null,
            catalogVaccine.Id,
            "FutureCase",
            VaccinationStatus.Scheduled,
            null,
            now.AddDays(30),
            null,
            DateTime.UtcNow);

        var nullDue = new Vaccination(
            tenant.Id,
            pet.Id,
            clinic.Id,
            null,
            catalogVaccine.Id,
            "NullDueCase",
            VaccinationStatus.Scheduled,
            null,
            null,
            null,
            DateTime.UtcNow);

        var appliedPast = new Vaccination(
            tenant.Id,
            pet.Id,
            clinic.Id,
            null,
            catalogVaccine.Id,
            "AppliedPastCase",
            VaccinationStatus.Applied,
            now.AddDays(-20),
            now.AddDays(-5),
            null,
            DateTime.UtcNow);

        // AppliedPastCase aynı aşının sonraki dozu olduğundan bu kaydın sonraki tarihi karşılanmıştır.
        var fulfilledPast = new Vaccination(
            tenant.Id, pet.Id, clinic.Id, null, catalogVaccine.Id, "FulfilledPastCase", VaccinationStatus.Applied,
            now.AddDays(-60), now.AddDays(-7), null, DateTime.UtcNow);

        var cancelledPast = new Vaccination(
            tenant.Id,
            pet.Id,
            clinic.Id,
            null,
            catalogVaccine.Id,
            "CancelledPastCase",
            VaccinationStatus.Cancelled,
            null,
            now.AddDays(-8),
            null,
            DateTime.UtcNow);

        db.AddRange(tenant, clinic, client, pet, overdue, future, nullDue, appliedPast, fulfilledPast, cancelledPast);

        Guid? deceasedPetScheduledId = null;
        if (withDeceasedPetScheduled)
        {
            var deceasedPet = new Pet(tenant.Id, client.Id, "PetVacDeceased", speciesId);
            var today = DateOnly.FromDateTime(now);
            deceasedPet.SetStatus(PetStatus.Deceased, today, today).IsSuccess.Should().BeTrue();
            var deceasedScheduled = new Vaccination(
                tenant.Id,
                deceasedPet.Id,
                clinic.Id,
                null,
                catalogVaccine.Id,
                "DeceasedPetFutureCase",
                VaccinationStatus.Scheduled,
                null,
                now.AddDays(5),
                null,
                DateTime.UtcNow);
            db.AddRange(deceasedPet, deceasedScheduled);
            deceasedPetScheduledId = deceasedScheduled.Id;
        }

        Guid? tooOldScheduledId = null;
        if (withTooOldScheduled)
        {
            // Vadesi gecikmiş penceresinin (son 1 takvim ayı) başından bir gün önce (İstanbul öğlesi).
            var today = OperationDayBounds.ToLocalDate(now);
            var tooOld = new Vaccination(
                tenant.Id, pet.Id, clinic.Id, null, catalogVaccine.Id, "TooOldCase", VaccinationStatus.Scheduled, null,
                OperationDayBounds.NoonUtcForLocalDate(today.AddMonths(-1).AddDays(-1)), null, DateTime.UtcNow);
            db.Add(tooOld);
            tooOldScheduledId = tooOld.Id;
        }

        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var token = await IntegrationTestAuthHelper.SeedScopedListReaderAndIssueTokenAsync(
            db,
            jwt,
            hasher,
            tenant.Id,
            clinic.Id,
            PermissionCatalog.Vaccinations.Read);

        return new OverdueSeedCtx(
            token,
            clinic.Id,
            overdue.Id,
            future.Id,
            nullDue.Id,
            appliedPast.Id,
            cancelledPast.Id,
            deceasedPetScheduledId,
            tooOldScheduledId);
    }
}
