using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Appointments;

/// <summary>Takvim filtreleri (APPOINTMENT_CALENDAR_API_CONTRACT.md §6) HTTP üzerinden: tekrarlı sorgu parametreleri ve sunucuda süzme.</summary>
[Collection("products-api")]
public sealed class AppointmentCalendarFilterEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AppointmentCalendarFilterEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Calendar_Filters_By_Veterinarian_Types_And_States()
    {
        var ctx = await SeedAsync();
        var http = Client(ctx.Token);

        async Task<List<Guid>> Ids(string filter)
        {
            var response = await http.GetAsync($"/api/v1/appointments/calendar?{ctx.Window}&{filter}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        }

        (await Ids("")).Should().HaveCount(4);
        (await Ids($"veterinarianUserId={ctx.VetId:D}")).Should().BeEquivalentTo([ctx.VetVaccinationTomorrow, ctx.VetCheckupCompleted]);
        (await Ids("withoutVeterinarian=true")).Should().BeEquivalentTo([ctx.UnassignedSurgeryTomorrow, ctx.UnassignedCancelled]);
        (await Ids("appointmentTypes=Vaccination&appointmentTypes=Surgery")).Should().BeEquivalentTo([ctx.VetVaccinationTomorrow, ctx.UnassignedSurgeryTomorrow]);
        (await Ids("states=Upcoming")).Should().BeEquivalentTo([ctx.VetVaccinationTomorrow, ctx.UnassignedSurgeryTomorrow]);
        (await Ids("states=Completed&states=Overdue")).Should().BeEquivalentTo([ctx.VetCheckupCompleted]);
    }

    [Fact]
    public async Task Calendar_Summary_Counts_Per_Day_With_Same_Filters()
    {
        var ctx = await SeedAsync();
        var http = Client(ctx.Token);

        var all = await http.GetFromJsonAsync<JsonElement>($"/api/v1/appointments/calendar/summary?{ctx.Window}");
        var days = all.EnumerateArray().ToList();

        // Dün: tamamlanan 1; yarın: planlı 2 (iptal sayılmaz).
        days.Sum(d => d.GetProperty("total").GetInt32()).Should().Be(3);
        days.Sum(d => d.GetProperty("completed").GetInt32()).Should().Be(1);
        days.Sum(d => d.GetProperty("upcoming").GetInt32()).Should().Be(2);

        var vetOnly = await http.GetFromJsonAsync<JsonElement>($"/api/v1/appointments/calendar/summary?{ctx.Window}&veterinarianUserId={ctx.VetId:D}&states=Upcoming");
        vetOnly.EnumerateArray().Single().GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Calendar_Rejects_Veterinarian_And_WithoutVeterinarian_Together()
    {
        var ctx = await SeedAsync();

        var response = await Client(ctx.Token).GetAsync($"/api/v1/appointments/calendar?{ctx.Window}&veterinarianUserId={ctx.VetId:D}&withoutVeterinarian=true");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VaccineCalendar_Accepts_States_Filter()
    {
        var ctx = await SeedAsync(PermissionCatalog.Vaccinations.Read);

        var response = await Client(ctx.Token).GetAsync($"/api/v1/vaccinations/calendar?{ctx.Window}&states=Overdue&states=DueToday");
        var summary = await Client(ctx.Token).GetAsync($"/api/v1/vaccinations/calendar/summary?{ctx.Window}&states=Overdue");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        summary.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient Client(string token)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private sealed record SeedCtx(
        string Token,
        string Window,
        Guid VetId,
        Guid VetVaccinationTomorrow,
        Guid VetCheckupCompleted,
        Guid UnassignedSurgeryTomorrow,
        Guid UnassignedCancelled);

    private async Task<SeedCtx> SeedAsync(string permission = PermissionCatalog.Appointments.Read)
    {
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"ApptFlt-{Guid.NewGuid():N}"[..16], DateTime.UtcNow);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var owner = new Client(tenant.Id, "Ayşe Yılmaz", DateTime.UtcNow, "905551112202", "takvim-filtre@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, owner.Id, "Pamuk", speciesId);
        var vetId = Guid.NewGuid();

        var today = OperationDayBounds.ToLocalDate(DateTime.UtcNow);
        var tomorrowNoon = OperationDayBounds.NoonUtcForLocalDate(today.AddDays(1));
        var yesterdayNoon = OperationDayBounds.NoonUtcForLocalDate(today.AddDays(-1));

        var vetVaccination = new Appointment(tenant.Id, clinic.Id, pet.Id, tomorrowNoon, 30, AppointmentType.Vaccination, veterinarianUserId: vetId);
        var vetCheckupCompleted = new Appointment(tenant.Id, clinic.Id, pet.Id, yesterdayNoon, 30, AppointmentType.Checkup, AppointmentStatus.Completed, veterinarianUserId: vetId);
        var surgery = new Appointment(tenant.Id, clinic.Id, pet.Id, tomorrowNoon.AddHours(2), 30, AppointmentType.Surgery);
        var cancelled = new Appointment(tenant.Id, clinic.Id, pet.Id, tomorrowNoon.AddHours(3), 30, AppointmentType.Checkup, AppointmentStatus.Cancelled);

        db.AddRange(tenant, clinic, owner, pet, vetVaccination, vetCheckupCompleted, surgery, cancelled);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var token = await IntegrationTestAuthHelper.SeedScopedListReaderAndIssueTokenAsync(db, jwt, hasher, tenant.Id, clinic.Id, permission);

        var from = OperationDayBounds.ForLocalDate(today.AddDays(-3)).DayStartUtc;
        var to = OperationDayBounds.ForLocalDate(today.AddDays(4)).DayStartUtc;
        var window = $"dateFromUtc={from:O}&dateToUtc={to:O}&clinicId={clinic.Id:D}";

        return new SeedCtx(token, window, vetId, vetVaccination.Id, vetCheckupCompleted.Id, surgery.Id, cancelled.Id);
    }
}
