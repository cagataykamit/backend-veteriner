using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.IntegrationTests.VaccinationSchedules;

/// <summary>
/// TIER-005 (docs/VACCINATION_SCHEDULE_API_CONTRACT.md): paket kilidi, takvim yazma, önerinin doğum tarihi ve uygulanmış dozdan
/// hesaplanması, onayın planlı aşı kaydı yazması (tümü ya da hiçbiri), çift onayın reddi ve başlangıç şablonu.
/// </summary>
public sealed class VaccinationScheduleEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] Permissions =
    [
        "VaccineDefinitions.Read", "VaccineDefinitions.Update", "Vaccinations.Read", "Vaccinations.Create", "Pets.Read",
    ];

    private readonly CustomWebApplicationFactory _factory;

    public VaccinationScheduleEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static DateOnly Today => OperationDayBounds.ToLocalDate(DateTime.UtcNow);

    [Fact]
    public async Task Basic_Should_Read_Schedule_But_Reject_Writing_And_Plan()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Basic);
        var client = ClientFor(ctx);

        var read = await client.GetAsync("/api/v1/vaccination-schedule/items");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength().Should().Be(0);

        var create = await client.PostAsJsonAsync("/api/v1/vaccination-schedule/items", ItemBody(ctx));
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await create.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Subscriptions.FeatureNotInPlan");
        problem.GetProperty("feature").GetString().Should().Be("VaccinationWizard");
        problem.GetProperty("requiredPlanCode").GetString().Should().Be("Pro");

        (await client.GetAsync($"/api/v1/vaccination-schedule/pets/{ctx.PetId}/plan")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync("/api/v1/vaccination-schedule/starter-template", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_Should_Return_Names_And_Reject_Duplicate_And_Invalid_Values()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        var client = ClientFor(ctx);

        var create = await client.PostAsJsonAsync("/api/v1/vaccination-schedule/items", ItemBody(ctx));
        create.StatusCode.Should().Be(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());
        var item = await create.Content.ReadFromJsonAsync<JsonElement>();
        item.GetProperty("speciesName").GetString().Should().Be("Köpek");
        item.GetProperty("vaccineName").GetString().Should().Be(ctx.VaccineName);
        item.GetProperty("doseCount").GetInt32().Should().Be(3);

        var duplicate = await client.PostAsJsonAsync("/api/v1/vaccination-schedule/items", ItemBody(ctx));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(duplicate)).Should().Be("VaccinationSchedules.DuplicateItem");

        var id = item.GetProperty("id").GetGuid();
        var invalid = await client.PutAsJsonAsync($"/api/v1/vaccination-schedule/items/{id}", ItemBody(ctx, doseCount: 2, doseIntervalWeeks: null));
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(invalid)).Should().Be(VaccinationScheduleItem.ValidationCode);

        var update = await client.PutAsJsonAsync($"/api/v1/vaccination-schedule/items/{id}", ItemBody(ctx, doseCount: 1, doseIntervalWeeks: null));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("doseIntervalWeeks").ValueKind.Should().Be(JsonValueKind.Null);

        (await client.DeleteAsync($"/api/v1/vaccination-schedule/items/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/v1/vaccination-schedule/items/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Plan_Should_Follow_Birth_Date_And_Apply_Should_Write_Scheduled_Vaccinations_Once()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro, birthDate: Today.AddDays(-7 * 7));
        var client = ClientFor(ctx);
        (await client.PostAsJsonAsync("/api/v1/vaccination-schedule/items", ItemBody(ctx))).EnsureSuccessStatusCode();

        var plan = await (await client.GetAsync($"/api/v1/vaccination-schedule/pets/{ctx.PetId}/plan")).Content.ReadFromJsonAsync<JsonElement>();
        plan.GetProperty("petName").GetString().Should().StartWith("Yavru-");
        var suggestions = plan.GetProperty("suggestions").EnumerateArray().ToList();
        suggestions.Should().HaveCount(4);
        suggestions[0].GetProperty("doseNumber").GetInt32().Should().Be(1);
        suggestions[0].GetProperty("isLate").GetBoolean().Should().BeTrue("6. hafta geçti; ilk doz yarına alınır");
        suggestions[0].GetProperty("dueDate").GetString().Should().Be(Today.AddDays(1).ToString("yyyy-MM-dd"));
        suggestions[1].GetProperty("dueDate").GetString().Should().Be(Today.AddDays(22).ToString("yyyy-MM-dd"));
        suggestions[3].GetProperty("doseNumber").ValueKind.Should().Be(JsonValueKind.Null);

        var items = suggestions.Take(2).Select(s => new { vaccineDefinitionId = ctx.VaccineDefinitionId, dueDate = s.GetProperty("dueDate").GetString(), notes = "Aşı takviminden" }).ToArray();
        var apply = await client.PostAsJsonAsync($"/api/v1/vaccination-schedule/pets/{ctx.PetId}/plan", new { clinicId = ctx.ClinicId, items });
        apply.StatusCode.Should().Be(HttpStatusCode.OK, await apply.Content.ReadAsStringAsync());
        (await apply.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetArrayLength().Should().Be(2);

        var rows = await VaccinationsAsync(ctx);
        rows.Should().HaveCount(2).And.OnlyContain(v => v.Status == VaccinationStatus.Scheduled && v.AppliedAtUtc == null && v.ClinicId == ctx.ClinicId);
        rows.Select(v => OperationDayBounds.ToLocalDate(v.DueAtUtc!.Value)).Should().BeEquivalentTo([Today.AddDays(1), Today.AddDays(22)]);

        var again = await client.PostAsJsonAsync($"/api/v1/vaccination-schedule/pets/{ctx.PetId}/plan", new { clinicId = ctx.ClinicId, items });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("VaccinationPlans.DuplicateScheduled");

        var after = await (await client.GetAsync($"/api/v1/vaccination-schedule/pets/{ctx.PetId}/plan")).Content.ReadFromJsonAsync<JsonElement>();
        after.GetProperty("suggestions").GetArrayLength().Should().Be(0);
        after.GetProperty("skipped")[0].GetProperty("reason").GetString().Should().Be("AlreadyScheduled");
    }

    [Fact]
    public async Task Plan_Should_Continue_From_Applied_Dose()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Premium, birthDate: Today.AddDays(-9 * 7));
        var client = ClientFor(ctx);
        (await client.PostAsJsonAsync("/api/v1/vaccination-schedule/items", ItemBody(ctx))).EnsureSuccessStatusCode();
        await AddAppliedAsync(ctx, OperationDayBounds.NoonUtcForLocalDate(Today.AddDays(-14)));

        var plan = await (await client.GetAsync($"/api/v1/vaccination-schedule/pets/{ctx.PetId}/plan")).Content.ReadFromJsonAsync<JsonElement>();

        var first = plan.GetProperty("suggestions")[0];
        first.GetProperty("doseNumber").GetInt32().Should().Be(2);
        first.GetProperty("appliedDoseCount").GetInt32().Should().Be(1);
        first.GetProperty("isLate").GetBoolean().Should().BeFalse();
        first.GetProperty("dueDate").GetString().Should().Be(Today.AddDays(7).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Apply_Should_Write_Nothing_When_One_Item_Fails_Or_Date_Is_Not_Future()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        var client = ClientFor(ctx);
        var tomorrow = Today.AddDays(1).ToString("yyyy-MM-dd");

        var past = await client.PostAsJsonAsync($"/api/v1/vaccination-schedule/pets/{ctx.PetId}/plan", new
        {
            clinicId = ctx.ClinicId,
            items = new[] { new { vaccineDefinitionId = ctx.VaccineDefinitionId, dueDate = Today.ToString("yyyy-MM-dd") } },
        });
        past.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(past)).Should().Be("Vaccinations.ScheduledDueAtMustNotBePast");

        var partial = await client.PostAsJsonAsync($"/api/v1/vaccination-schedule/pets/{ctx.PetId}/plan", new
        {
            clinicId = ctx.ClinicId,
            items = new[]
            {
                new { vaccineDefinitionId = ctx.VaccineDefinitionId, dueDate = tomorrow },
                new { vaccineDefinitionId = Guid.NewGuid(), dueDate = Today.AddDays(2).ToString("yyyy-MM-dd") },
            },
        });
        partial.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await VaccinationsAsync(ctx)).Should().BeEmpty("bir satır reddedilince önceki satır da geri alınır");
    }

    [Fact]
    public async Task Deceased_Pet_Should_Get_No_Plan()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pet = await db.Pets.SingleAsync(p => p.Id == ctx.PetId);
            pet.SetStatus(PetStatus.Deceased, Today, Today).IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        var response = await ClientFor(ctx).GetAsync($"/api/v1/vaccination-schedule/pets/{ctx.PetId}/plan");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Pets.Deceased");
    }

    [Fact]
    public async Task Starter_Template_Should_Add_Missing_Rows_Only_Once()
    {
        var ctx = await SeedAsync(SubscriptionPlanCode.Pro);
        var client = ClientFor(ctx);

        var first = await client.PostAsync("/api/v1/vaccination-schedule/starter-template", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var loaded = await first.Content.ReadFromJsonAsync<JsonElement>();
        var added = loaded.GetProperty("addedCount").GetInt32();
        added.Should().BeGreaterThan(0);
        var rows = loaded.GetProperty("schedule").GetProperty("items").EnumerateArray().ToList();
        rows.Should().HaveCount(added);
        rows.Select(r => r.GetProperty("speciesName").GetString()).Distinct().Should().BeEquivalentTo(["Kedi", "Köpek"]);

        var second = await (await client.PostAsync("/api/v1/vaccination-schedule/starter-template", null)).Content.ReadFromJsonAsync<JsonElement>();
        second.GetProperty("addedCount").GetInt32().Should().Be(0);
        second.GetProperty("schedule").GetProperty("items").GetArrayLength().Should().Be(added);
    }

    // ---- yardımcılar ----

    private static object ItemBody(SeedCtx ctx, int doseCount = 3, int? doseIntervalWeeks = 3)
        => new
        {
            speciesId = SpeciesSeedConstants.Dog,
            vaccineDefinitionId = ctx.VaccineDefinitionId,
            startAgeWeeks = 6,
            doseCount,
            doseIntervalWeeks,
            repeatIntervalMonths = 12,
        };

    private HttpClient ClientFor(SeedCtx ctx)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        return client;
    }

    private async Task<List<Vaccination>> VaccinationsAsync(SeedCtx ctx)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Vaccinations.AsNoTracking().Where(v => v.PetId == ctx.PetId).ToListAsync();
    }

    private async Task AddAppliedAsync(SeedCtx ctx, DateTime appliedAtUtc)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Vaccinations.Add(new Vaccination(
            ctx.TenantId, ctx.PetId, ctx.ClinicId, null, ctx.VaccineDefinitionId, ctx.VaccineName, VaccinationStatus.Applied,
            appliedAtUtc, null, null, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<SeedCtx> SeedAsync(SubscriptionPlanCode plan, DateOnly? birthDate = null)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTime.UtcNow;

        var tenant = new Tenant($"asi-{Guid.NewGuid():N}"[..18], now);
        var subscription = TenantSubscription.StartTrial(tenant.Id, plan, now, 14);
        subscription.ActivatePaidPlan(plan, now);
        var clinic = new Clinic(tenant.Id, $"As-{Guid.NewGuid():N}"[..14], "İzmir");
        var client = new Client(tenant.Id, $"AsiSahip-{Guid.NewGuid():N}"[..14], now, "905551110098");
        var vaccineName = $"Karma {Guid.NewGuid():N}"[..14];
        var definition = VaccineDefinition.CreateTenant(tenant.Id, $"K{Guid.NewGuid():N}"[..10], vaccineName, now, SpeciesSeedConstants.Dog);
        db.AddRange(tenant, subscription, clinic, client, definition);
        await db.SaveChangesAsync();

        var pet = new Pet(tenant.Id, client.Id, $"Yavru-{Guid.NewGuid():N}"[..12], SpeciesSeedConstants.Dog, birthDate: birthDate);
        db.Pets.Add(pet);
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id, Permissions, includeClinicClaimInJwt: true);

        return new SeedCtx(tenant.Id, clinic.Id, pet.Id, definition.Id, vaccineName, token);
    }

    private sealed record SeedCtx(Guid TenantId, Guid ClinicId, Guid PetId, Guid VaccineDefinitionId, string VaccineName, string Token);
}
