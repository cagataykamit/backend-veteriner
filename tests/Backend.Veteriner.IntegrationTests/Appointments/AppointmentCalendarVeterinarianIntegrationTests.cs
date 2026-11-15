using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.OnlineAppointments;
using Backend.Veteriner.Application.Auth;
using AwesomeAssertions;

namespace Backend.IntegrationTests.Appointments;

/// <summary>
/// Saat ızgaralı takvim (kullanıcı dostu denetim B1): klinik çakışması yalnız hekimsiz randevuda (hekimi atanmış randevuda hekim
/// bazında), takvim yanıtında hekim kimliği ve adı, hekim listesinin takvimi gören kullanıcıya açık olması.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AppointmentCalendarVeterinarianIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AppointmentCalendarVeterinarianIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Without_Clinic_Overlap_Different_Veterinarians_Share_A_Slot_But_Unassigned_Appointment_Conflicts()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        await ctx.SetAllowOverlapAsync(false);
        var vetA = await ctx.SeedClinicUserAsync("Veteriner", "Dr. Ayşe");
        var vetB = await ctx.SeedClinicUserAsync("Veteriner", "Dr. Mert");
        var (_, pet1) = await ctx.SeedClientWithPetAsync();
        var (_, pet2) = await ctx.SeedClientWithPetAsync();
        var (_, pet3) = await ctx.SeedClientWithPetAsync();
        var (_, pet4) = await ctx.SeedClientWithPetAsync();
        var at = OnlineAppointmentTestContext.NextIstanbul(DayOfWeek.Wednesday, 10);

        var first = await ctx.PostAppointmentAsync(pet1, at, veterinarianUserId: vetA);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());

        var otherVet = await ctx.PostAppointmentAsync(pet2, at, veterinarianUserId: vetB);
        otherVet.StatusCode.Should().Be(HttpStatusCode.Created, "aynı saatte farklı hekimin randevusu çakışma değildir");

        var sameVet = await ctx.PostAppointmentAsync(pet3, at, veterinarianUserId: vetA);
        sameVet.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(sameVet)).Should().Be("Appointments.VeterinarianTimeConflict");

        var unassigned = await ctx.PostAppointmentAsync(pet4, at);
        unassigned.StatusCode.Should().Be(HttpStatusCode.Conflict, "hekimsiz randevuda klinik kuralı aynen kalır");
        (await IntegrationTestProblemDetails.ReadCodeAsync(unassigned)).Should().Be("Appointments.ClinicTimeConflict");

        // Yeniden zamanlama da aynı kuralı kullanır: B hekiminin randevusu A'nınkiyle aynı saate taşınabilir.
        var otherId = await otherVet.Content.ReadFromJsonAsync<Guid>();
        var later = at.AddHours(2);
        (await ctx.Http.PostAsJsonAsync($"/api/v1/appointments/{otherId}/reschedule", new { scheduledAtUtc = later }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ctx.Http.PostAsJsonAsync($"/api/v1/appointments/{otherId}/reschedule", new { scheduledAtUtc = at }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Calendar_Should_Return_Veterinarian_Id_And_Name()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var vet = await ctx.SeedClinicUserAsync("Veteriner", "Dr. Selin");
        var (_, pet1) = await ctx.SeedClientWithPetAsync();
        var (_, pet2) = await ctx.SeedClientWithPetAsync();
        var at = OnlineAppointmentTestContext.NextIstanbul(DayOfWeek.Thursday, 11);
        (await ctx.PostAppointmentAsync(pet1, at, veterinarianUserId: vet)).EnsureSuccessStatusCode();
        (await ctx.PostAppointmentAsync(pet2, at.AddHours(1))).EnsureSuccessStatusCode();

        var from = Uri.EscapeDataString(at.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(at.AddHours(3).ToString("O"));
        var items = await ctx.Http.GetFromJsonAsync<JsonElement[]>(
            $"/api/v1/appointments/calendar?clinicId={ctx.ClinicId}&dateFromUtc={from}&dateToUtc={to}");

        items.Should().HaveCount(2);
        var withVet = items!.Single(i => i.GetProperty("petId").GetGuid() == pet1);
        withVet.GetProperty("veterinarianUserId").GetGuid().Should().Be(vet);
        withVet.GetProperty("veterinarianName").GetString().Should().Be("Dr. Selin");
        var withoutVet = items!.Single(i => i.GetProperty("petId").GetGuid() == pet2);
        withoutVet.GetProperty("veterinarianUserId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Veterinarians_Should_Be_Readable_With_Appointments_Read_Only()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var vet = await ctx.SeedClinicUserAsync("Veteriner", "Dr. Can");
        var reader = await ctx.ClientWithPermissionsAsync(_factory, PermissionCatalog.Appointments.Read);

        var response = await reader.GetAsync($"/api/v1/clinics/{ctx.ClinicId}/veterinarians");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement[]>())!.Select(v => v.GetProperty("userId").GetGuid()).Should().Contain(vet);

        var none = await ctx.ClientWithPermissionsAsync(_factory, PermissionCatalog.Clients.Read);
        (await none.GetAsync($"/api/v1/clinics/{ctx.ClinicId}/veterinarians")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
