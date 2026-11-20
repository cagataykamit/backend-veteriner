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

/// <summary>
/// Çalışma saati kısıtı (APPOINTMENT_CALENDAR_API_CONTRACT.md §9) HTTP üzerinden: kısıt açıkken kapalı güne randevu reddedilir; ayar
/// kapatılınca aynı randevu verilir ve günlük kapasite kapalı günde de pozitiftir.
/// </summary>
[Collection("products-api")]
public sealed class AppointmentWorkingHoursRestrictionIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AppointmentWorkingHoursRestrictionIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Disabling_Restriction_Allows_Appointment_On_Closed_Day_And_Counts_Capacity()
    {
        var (token, clinicId, petId) = await SeedAsync();
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Varsayılan haftada pazar kapalı; yerel 22:00.
        var today = OperationDayBounds.ToLocalDate(DateTime.UtcNow);
        var sunday = today.AddDays(2);
        while (sunday.DayOfWeek != DayOfWeek.Sunday)
            sunday = sunday.AddDays(1);
        var sundayStartUtc = OperationDayBounds.ForLocalDate(sunday).DayStartUtc;
        var body = new { ClinicId = clinicId, PetId = petId, ScheduledAtUtc = sundayStartUtc.AddHours(22), AppointmentType = AppointmentType.Consultation, DurationMinutes = 30 };

        var rejected = await http.PostAsJsonAsync("/api/v1/appointments", body);
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(rejected)).Should().Be("Appointments.ClinicClosed");

        var settings = await http.PutAsJsonAsync($"/api/v1/clinics/{clinicId:D}/appointment-settings", new
        {
            DefaultAppointmentDurationMinutes = 30,
            SlotIntervalMinutes = 15,
            AllowOverlappingAppointments = false,
            EnforceWorkingHours = false,
        });
        settings.StatusCode.Should().Be(HttpStatusCode.OK);
        (await settings.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enforceWorkingHours").GetBoolean().Should().BeFalse();

        var created = await http.PostAsJsonAsync("/api/v1/appointments", body);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var window = $"dateFromUtc={sundayStartUtc:O}&dateToUtc={sundayStartUtc.AddDays(1):O}&clinicId={clinicId:D}";
        var capacity = await http.GetFromJsonAsync<JsonElement>($"/api/v1/appointments/calendar/capacity?{window}");
        capacity.EnumerateArray().Single().GetProperty("capacity").GetInt32().Should().BePositive("kısıt kapalıyken kapalı gün yok");
    }

    private async Task<(string Token, Guid ClinicId, Guid PetId)> SeedAsync()
    {
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"ApptWh-{Guid.NewGuid():N}"[..16], DateTime.UtcNow);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var owner = new Client(tenant.Id, "Ayşe Yılmaz", DateTime.UtcNow, "905551112203", "calisma-saati@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, owner.Id, "Pamuk", speciesId);

        db.AddRange(tenant, clinic, owner, pet);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var token = await IntegrationTestAuthHelper.SeedScopedClinicUserAndIssueTokenAsync(
            db, jwt, hasher, tenant.Id, clinic.Id,
            [PermissionCatalog.Appointments.Create, PermissionCatalog.Appointments.Read, PermissionCatalog.Clinics.Update]);

        return (token, clinic.Id, pet.Id);
    }
}
