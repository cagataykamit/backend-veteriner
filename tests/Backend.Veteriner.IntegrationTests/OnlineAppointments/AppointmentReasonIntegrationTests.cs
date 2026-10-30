using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using AwesomeAssertions;

namespace Backend.IntegrationTests.OnlineAppointments;

/// <summary>
/// APPT-005/006/007 (ADR-042 K2/K3): klinik randevu nedenleri (CRUD, ad benzersizliği, uygun hekim doğrulaması, yetki) ve
/// randevuda neden/hekim (süre önerisi, hekim çakışması, hekim olmayan kullanıcı reddi).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AppointmentReasonIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AppointmentReasonIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string ReasonsUrl(Guid clinicId) => $"/api/v1/clinics/{clinicId}/appointment-reasons";

    [Fact]
    public async Task Reasons_Crud_Should_Validate_Name_Veterinarians_And_Activation()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var vet = await ctx.SeedClinicUserAsync("Veteriner", "Dr. Ayşe");
        var secretary = await ctx.SeedClinicUserAsync("Sekreter", "Sekreter");

        var vaccine = await ctx.CreateReasonAsync("Aşı", 15, online: true, veterinarianUserIds: [vet], sortOrder: 2);
        var control = await ctx.CreateReasonAsync(" Kontrol ", 20, online: false, sortOrder: 1);
        control.GetProperty("name").GetString().Should().Be("Kontrol");
        vaccine.GetProperty("veterinarianUserIds").EnumerateArray().Select(v => v.GetGuid()).Should().Equal(vet);

        var duplicate = await ctx.Http.PostAsJsonAsync(ReasonsUrl(ctx.ClinicId), new { name = "aşı", defaultDurationMinutes = 10 });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(duplicate)).Should().Be("AppointmentReasons.DuplicateName");

        var nonVet = await ctx.Http.PostAsJsonAsync(ReasonsUrl(ctx.ClinicId), new
        {
            name = "Tıraş",
            defaultDurationMinutes = 30,
            veterinarianUserIds = new[] { secretary },
        });
        nonVet.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(nonVet)).Should().Be("AppointmentReasons.Validation");

        var badDuration = await ctx.Http.PostAsJsonAsync(ReasonsUrl(ctx.ClinicId), new { name = "Uzun", defaultDurationMinutes = 600 });
        badDuration.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var list = await ctx.Http.GetFromJsonAsync<JsonElement>(ReasonsUrl(ctx.ClinicId));
        list.EnumerateArray().Select(r => r.GetProperty("name").GetString()).Should().Equal("Kontrol", "Aşı");

        var vaccineId = vaccine.GetProperty("id").GetGuid();
        var update = await ctx.Http.PutAsJsonAsync($"{ReasonsUrl(ctx.ClinicId)}/{vaccineId}", new
        {
            name = "Karma aşı",
            defaultDurationMinutes = 25,
            isOnlineBookable = true,
            sortOrder = 0,
            veterinarianUserIds = Array.Empty<Guid>(),
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("defaultDurationMinutes").GetInt32().Should().Be(25);
        updated.GetProperty("veterinarianUserIds").GetArrayLength().Should().Be(0);

        var deactivate = await ctx.Http.PostAsync($"{ReasonsUrl(ctx.ClinicId)}/{vaccineId}/deactivate", null);
        (await deactivate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeFalse();

        // Pasif nedenin adı yeniden kullanılabilir; eski neden etkinleştirilirken ad çakışması 409.
        await ctx.CreateReasonAsync("Karma aşı", 25);
        var activate = await ctx.Http.PostAsync($"{ReasonsUrl(ctx.ClinicId)}/{vaccineId}/activate", null);
        activate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reasons_Should_Be_Readable_By_Scheduling_Roles_But_Writable_Only_With_Clinics_Update()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        await ctx.CreateReasonAsync("Muayene", 30);
        var scheduler = await ctx.ClientWithPermissionsAsync(_factory, PermissionCatalog.Appointments.Create);

        var list = await scheduler.GetAsync(ReasonsUrl(ctx.ClinicId));
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        var create = await scheduler.PostAsJsonAsync(ReasonsUrl(ctx.ClinicId), new { name = "X", defaultDurationMinutes = 15 });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreignClinic = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        (await ctx.Http.GetAsync(ReasonsUrl(foreignClinic))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Appointment_Should_Take_Reason_Duration_And_Reject_Veterinarian_Conflicts()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        await ctx.SetAllowOverlapAsync(true);
        var vet = await ctx.SeedClinicUserAsync("Veteriner", "Dr. Can");
        var secretary = await ctx.SeedClinicUserAsync("Sekreter", "Sekreter");
        var reason = await ctx.CreateReasonAsync("Aşı", 45);
        var reasonId = reason.GetProperty("id").GetGuid();
        var (_, pet1) = await ctx.SeedClientWithPetAsync();
        var (_, pet2) = await ctx.SeedClientWithPetAsync();
        var (_, pet3) = await ctx.SeedClientWithPetAsync();
        var at = OnlineAppointmentTestContext.NextIstanbul(DayOfWeek.Tuesday, 10);

        var first = await ctx.PostAppointmentAsync(pet1, at, reasonId, vet);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var firstId = await first.Content.ReadFromJsonAsync<Guid>();
        var detail = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/appointments/{firstId}");
        detail.GetProperty("durationMinutes").GetInt32().Should().Be(45, "süre verilmezse nedenin varsayılanı");
        detail.GetProperty("reasonId").GetGuid().Should().Be(reasonId);
        detail.GetProperty("veterinarianUserId").GetGuid().Should().Be(vet);

        // Aynı hekim, çakışan aralık → 409; hekimsiz randevu çakışma serbest (klinik üst üste binmeye izin veriyor).
        var conflict = await ctx.PostAppointmentAsync(pet2, at.AddMinutes(30), veterinarianUserId: vet);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(conflict)).Should().Be("Appointments.VeterinarianTimeConflict");
        (await ctx.PostAppointmentAsync(pet2, at.AddMinutes(30))).StatusCode.Should().Be(HttpStatusCode.Created);

        var notVet = await ctx.PostAppointmentAsync(pet3, at.AddHours(3), veterinarianUserId: secretary);
        notVet.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(notVet)).Should().Be("Appointments.Validation");

        var unknownReason = await ctx.PostAppointmentAsync(pet3, at.AddHours(3), reasonId: Guid.NewGuid());
        unknownReason.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
