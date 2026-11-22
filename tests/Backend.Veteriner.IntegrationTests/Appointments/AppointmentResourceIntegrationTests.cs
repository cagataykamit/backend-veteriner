using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.OnlineAppointments;
using Backend.Veteriner.Application.Auth;
using AwesomeAssertions;

namespace Backend.IntegrationTests.Appointments;

/// <summary>
/// Oda ve cihaz (ADR-053; APPOINTMENT_RESOURCES_API_CONTRACT.md): tanım uçları, randevuda kaynak, kapasite aşımında 409 (klinik üst üste
/// binme ayarından bağımsız), pasif kaynak ve takvim yanıtında kaynak adları.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AppointmentResourceIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AppointmentResourceIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Definitions_Should_List_Rooms_Before_Equipment_And_Enforce_Name_Type_And_Permissions()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var device = await CreateResourceAsync(ctx, "Ultrason", "Equipment");
        var room = await CreateResourceAsync(ctx, "Ameliyathane", "Room", capacity: 1, sortOrder: 5);

        var list = await ctx.Http.GetFromJsonAsync<JsonElement[]>(ResourcesUrl(ctx));
        list!.Select(r => r.GetProperty("name").GetString()).Should().Equal("Ameliyathane", "Ultrason");
        list[0].GetProperty("type").GetString().Should().Be("Room");
        list[0].GetProperty("capacity").GetInt32().Should().Be(1);

        var duplicate = await ctx.Http.PostAsJsonAsync(ResourcesUrl(ctx), new { name = " ultrason ", type = "Equipment", capacity = 1 });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(duplicate)).Should().Be("AppointmentResources.DuplicateName");

        var typeChange = await ctx.Http.PutAsJsonAsync($"{ResourcesUrl(ctx)}/{device}", new { name = "Ultrason", type = "Room", capacity = 1 });
        typeChange.StatusCode.Should().Be(HttpStatusCode.BadRequest, "kullanımdaki cihaz oda alanına geçemez");

        var badCapacity = await ctx.Http.PutAsJsonAsync($"{ResourcesUrl(ctx)}/{room}", new { name = "Ameliyathane", type = "Room", capacity = 0 });
        badCapacity.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await ctx.Http.PostAsync($"{ResourcesUrl(ctx)}/{device}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ctx.Http.PostAsJsonAsync(ResourcesUrl(ctx), new { name = "Ultrason", type = "Equipment", capacity = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Created, "pasif kaynağın adı yeniden kullanılabilir");

        var scheduler = await ctx.ClientWithPermissionsAsync(_factory, PermissionCatalog.Appointments.Create);
        (await scheduler.GetAsync(ResourcesUrl(ctx))).StatusCode.Should().Be(HttpStatusCode.OK, "randevu formu kaynakları okur");
        (await scheduler.PostAsJsonAsync(ResourcesUrl(ctx), new { name = "Röntgen", type = "Equipment", capacity = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "tanım yazma Clinics.Update ister");

        var outsider = await ctx.ClientWithPermissionsAsync(_factory, PermissionCatalog.Clients.Read);
        (await outsider.GetAsync(ResourcesUrl(ctx))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Same_Resource_At_Same_Time_Should_Conflict_Even_With_Other_Veterinarian_And_Clinic_Overlap_Allowed()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        await ctx.SetAllowOverlapAsync(true);
        var vetA = await ctx.SeedClinicUserAsync("Veteriner", "Dr. Ayşe");
        var vetB = await ctx.SeedClinicUserAsync("Veteriner", "Dr. Mert");
        var surgery = await CreateResourceAsync(ctx, "Ameliyathane", "Room");
        var ultrasound = await CreateResourceAsync(ctx, "Ultrason", "Equipment");
        var (_, pet1) = await ctx.SeedClientWithPetAsync();
        var (_, pet2) = await ctx.SeedClientWithPetAsync();
        var (_, pet3) = await ctx.SeedClientWithPetAsync();
        var at = OnlineAppointmentTestContext.NextIstanbul(DayOfWeek.Wednesday, 10);

        var first = await PostAsync(ctx, pet1, at, vetA, room: surgery, equipment: ultrasound);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());

        var sameRoom = await PostAsync(ctx, pet2, at.AddMinutes(15), vetB, room: surgery);
        sameRoom.StatusCode.Should().Be(HttpStatusCode.Conflict, "fiziksel oda iki yerde olamaz; klinik ayarı bu kuralı açmaz");
        (await IntegrationTestProblemDetails.ReadCodeAsync(sameRoom)).Should().Be("Appointments.ResourceTimeConflict");

        var sameDevice = await PostAsync(ctx, pet2, at, vetB, equipment: ultrasound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(sameDevice)).Should().Be("Appointments.ResourceTimeConflict");

        (await PostAsync(ctx, pet2, at.AddMinutes(30), vetB, room: surgery)).StatusCode
            .Should().Be(HttpStatusCode.Created, "önceki randevu bitince oda boşalır");
        (await PostAsync(ctx, pet3, at, null)).StatusCode.Should().Be(HttpStatusCode.Created, "kaynaksız randevu etkilenmez");

        var wrongField = await PostAsync(ctx, pet3, at.AddHours(3), vetA, room: ultrasound);
        wrongField.StatusCode.Should().Be(HttpStatusCode.BadRequest, "oda alanında cihaz seçilemez");

        // Yeniden zamanlama aynı kuralı kullanır: ikinci randevu ilkinin saatine taşınamaz.
        var secondId = await GetIdAsync(ctx, pet2, at.AddMinutes(30));
        var moved = await ctx.Http.PostAsJsonAsync($"/api/v1/appointments/{secondId}/reschedule", new { scheduledAtUtc = at });
        (await IntegrationTestProblemDetails.ReadCodeAsync(moved)).Should().Be("Appointments.ResourceTimeConflict");
    }

    [Fact]
    public async Task Capacity_Should_Allow_N_Concurrent_Appointments_And_Inactive_Resource_Should_Stay_On_Existing_Appointment()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var serum = await CreateResourceAsync(ctx, "Serum odası", "Room", capacity: 2);
        var (_, pet1) = await ctx.SeedClientWithPetAsync();
        var (_, pet2) = await ctx.SeedClientWithPetAsync();
        var (_, pet3) = await ctx.SeedClientWithPetAsync();
        var vets = new[]
        {
            await ctx.SeedClinicUserAsync("Veteriner", "Dr. A"),
            await ctx.SeedClinicUserAsync("Veteriner", "Dr. B"),
            await ctx.SeedClinicUserAsync("Veteriner", "Dr. C"),
        };
        var at = OnlineAppointmentTestContext.NextIstanbul(DayOfWeek.Thursday, 11);

        (await PostAsync(ctx, pet1, at, vets[0], room: serum)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostAsync(ctx, pet2, at.AddMinutes(15), vets[1], room: serum)).StatusCode.Should().Be(HttpStatusCode.Created);
        var third = await PostAsync(ctx, pet3, at.AddMinutes(15), vets[2], room: serum);
        (await IntegrationTestProblemDetails.ReadCodeAsync(third)).Should().Be("Appointments.ResourceTimeConflict");

        (await ctx.Http.PostAsync($"{ResourcesUrl(ctx)}/{serum}/deactivate", null)).EnsureSuccessStatusCode();
        var inactive = await PostAsync(ctx, pet3, at.AddHours(3), vets[2], room: serum);
        inactive.StatusCode.Should().Be(HttpStatusCode.BadRequest, "pasif kaynak yeni randevuda seçilemez");

        var firstId = await GetIdAsync(ctx, pet1, at);
        var keep = await ctx.Http.PutAsJsonAsync($"/api/v1/appointments/{firstId}", new
        {
            id = firstId,
            clinicId = ctx.ClinicId,
            petId = pet1,
            scheduledAtUtc = at,
            appointmentType = 0,
            status = 0,
            notes = "not eklendi",
            veterinarianUserId = vets[0],
            roomResourceId = serum,
        });
        keep.StatusCode.Should().Be(HttpStatusCode.NoContent, await keep.Content.ReadAsStringAsync());

        var detail = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/appointments/{firstId}");
        detail.GetProperty("roomResourceId").GetGuid().Should().Be(serum);
        detail.GetProperty("equipmentResourceId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Calendar_Should_Return_Resource_Ids_And_Names()
    {
        var ctx = await OnlineAppointmentTestContext.CreateAsync(_factory);
        var room = await CreateResourceAsync(ctx, "Muayene 1", "Room");
        var device = await CreateResourceAsync(ctx, "Röntgen", "Equipment");
        var (_, pet1) = await ctx.SeedClientWithPetAsync();
        var (_, pet2) = await ctx.SeedClientWithPetAsync();
        var at = OnlineAppointmentTestContext.NextIstanbul(DayOfWeek.Friday, 9);
        (await PostAsync(ctx, pet1, at, null, room: room, equipment: device)).EnsureSuccessStatusCode();
        (await PostAsync(ctx, pet2, at.AddHours(1), null)).EnsureSuccessStatusCode();

        var from = Uri.EscapeDataString(at.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(at.AddHours(3).ToString("O"));
        var items = await ctx.Http.GetFromJsonAsync<JsonElement[]>(
            $"/api/v1/appointments/calendar?clinicId={ctx.ClinicId}&dateFromUtc={from}&dateToUtc={to}");

        var withResources = items!.Single(i => i.GetProperty("petId").GetGuid() == pet1);
        withResources.GetProperty("roomResourceId").GetGuid().Should().Be(room);
        withResources.GetProperty("roomResourceName").GetString().Should().Be("Muayene 1");
        withResources.GetProperty("equipmentResourceId").GetGuid().Should().Be(device);
        withResources.GetProperty("equipmentResourceName").GetString().Should().Be("Röntgen");
        var without = items!.Single(i => i.GetProperty("petId").GetGuid() == pet2);
        without.GetProperty("roomResourceId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static string ResourcesUrl(OnlineAppointmentTestContext ctx) => $"/api/v1/clinics/{ctx.ClinicId}/appointment-resources";

    private static async Task<Guid> CreateResourceAsync(
        OnlineAppointmentTestContext ctx,
        string name,
        string type,
        int capacity = 1,
        int sortOrder = 0)
    {
        var response = await ctx.Http.PostAsJsonAsync(ResourcesUrl(ctx), new { name, type, capacity, sortOrder });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostAsync(
        OnlineAppointmentTestContext ctx,
        Guid petId,
        DateTime at,
        Guid? veterinarianUserId,
        Guid? room = null,
        Guid? equipment = null)
        => ctx.Http.PostAsJsonAsync("/api/v1/appointments", new
        {
            clinicId = ctx.ClinicId,
            petId,
            scheduledAtUtc = at,
            appointmentType = 0,
            veterinarianUserId,
            roomResourceId = room,
            equipmentResourceId = equipment,
        });

    private static async Task<Guid> GetIdAsync(OnlineAppointmentTestContext ctx, Guid petId, DateTime at)
    {
        var from = Uri.EscapeDataString(at.AddMinutes(-1).ToString("O"));
        var to = Uri.EscapeDataString(at.AddMinutes(1).ToString("O"));
        var items = await ctx.Http.GetFromJsonAsync<JsonElement[]>(
            $"/api/v1/appointments/calendar?clinicId={ctx.ClinicId}&dateFromUtc={from}&dateToUtc={to}");
        return items!.Single(i => i.GetProperty("petId").GetGuid() == petId).GetProperty("id").GetGuid();
    }
}
