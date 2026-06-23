using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Vaccinations;

[Collection("products-api")]
public sealed class VaccinationsListOverdueEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public VaccinationsListOverdueEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetList_OnlyOverdueTrue_WithStatusZero_ReturnsOnlyOverdueScheduled()
    {
        var ctx = await SeedOverdueScenarioAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var url =
            $"/api/v1/vaccinations?page=1&pageSize=50&status=0&onlyOverdue=true&clinicId={ctx.ClinicId:D}";
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalItems").GetInt32().Should().Be(1);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("id").GetGuid().Should().Be(ctx.OverdueScheduledId);
        items[0].GetProperty("status").GetInt32().Should().Be((int)VaccinationStatus.Scheduled);
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
        json.GetProperty("totalItems").GetInt32().Should().Be(1);
        json.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(ctx.OverdueScheduledId);
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

    private sealed record OverdueSeedCtx(
        string Token,
        Guid ClinicId,
        Guid OverdueScheduledId,
        Guid FutureScheduledId,
        Guid NullDueScheduledId,
        Guid AppliedPastDueId,
        Guid CancelledPastDueId);

    private async Task<OverdueSeedCtx> SeedOverdueScenarioAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Vac-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var client = new Client(tenant.Id, "Müşteri", "905551112200", "vac@example.com");
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
            null);

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
            null);

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
            null);

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
            null);

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
            null);

        db.AddRange(tenant, clinic, client, pet, overdue, future, nullDue, appliedPast, cancelledPast);
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
            cancelledPast.Id);
    }
}
