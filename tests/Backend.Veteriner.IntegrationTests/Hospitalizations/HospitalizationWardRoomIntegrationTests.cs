using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Hospitalizations;

/// <summary>
/// HOSP-002: klinik bazlı bölüm/oda tanımları (CRUD, benzersizlik, aktif/pasif, policy) ve yatışa oda ataması
/// (konum metni türetme, pasif oda 409, başka kiracı/klinik odası 404, clearRoom).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class HospitalizationWardRoomIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HospitalizationWardRoomIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, Guid ClinicId, Guid PetId)> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, clinicId) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return (client, clinicId, petId);
    }

    private static string WardsUrl(Guid clinicId) => $"/api/v1/clinics/{clinicId}/hospitalization-wards";

    private static async Task<Guid> CreateWardAsync(HttpClient client, Guid clinicId, string name, int sortOrder = 0)
    {
        var response = await client.PostAsJsonAsync(WardsUrl(clinicId), new { Name = name, SortOrder = sortOrder });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> CreateRoomAsync(HttpClient client, Guid clinicId, Guid wardId, string name, int sortOrder = 0)
    {
        var response = await client.PostAsJsonAsync($"{WardsUrl(clinicId)}/{wardId}/rooms", new { Name = name, SortOrder = sortOrder });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<JsonElement> ListWardsAsync(HttpClient client, Guid clinicId)
        => await client.GetFromJsonAsync<JsonElement>(WardsUrl(clinicId));

    private static JsonElement FindWard(JsonElement wards, Guid wardId)
        => wards.EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == wardId);

    private static object HospitalizationBody(Guid clinicId, Guid petId, Guid? roomId = null, string? location = null)
        => new
        {
            ClinicId = clinicId,
            PetId = petId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-1),
            Reason = "Gözlem",
            Location = location,
            RoomId = roomId,
        };

    private static async Task<Guid> CreateHospitalizationAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/hospitalizations", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<JsonElement> GetHospitalizationAsync(HttpClient client, Guid id)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/hospitalizations/{id}");

    /// <summary>Başka kiracının kliniğinde aktif bölüm + oda (veritabanına doğrudan).</summary>
    private async Task<Guid> SeedRoomInForeignTenantAsync()
    {
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clinic = await db.Clinics.SingleAsync(c => c.Id == foreignClinicId);
        var ward = HospitalizationWard.Create(clinic.TenantId, clinic.Id, "Yabancı bölüm", 0, DateTime.UtcNow);
        var room = HospitalizationRoom.Create(ward, "Yabancı oda", 0, DateTime.UtcNow);
        db.HospitalizationWards.Add(ward);
        db.HospitalizationRooms.Add(room);
        await db.SaveChangesAsync();
        return room.Id;
    }

    // ---------------- Tanımlar ----------------

    [Fact]
    public async Task Wards_Crud_Should_Create_List_Rename_Deactivate_And_Reject_Duplicates()
    {
        var (client, clinicId, _) = await AdminClientAsync();

        var icuId = await CreateWardAsync(client, clinicId, "Yoğun bakım", sortOrder: 2);
        var catsId = await CreateWardAsync(client, clinicId, " Kedi servisi ", sortOrder: 1);
        var cage2Id = await CreateRoomAsync(client, clinicId, icuId, "Kafes 2", sortOrder: 2);
        var cage1Id = await CreateRoomAsync(client, clinicId, icuId, "Kafes 1", sortOrder: 1);

        // Aktif bölümler arasında ad case-insensitive benzersiz.
        var duplicateWard = await client.PostAsJsonAsync(WardsUrl(clinicId), new { Name = " yoğun Bakım ", SortOrder = 0 });
        duplicateWard.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(duplicateWard)).Should().Be("HospitalizationWards.DuplicateName");

        var duplicateRoom = await client.PostAsJsonAsync($"{WardsUrl(clinicId)}/{icuId}/rooms", new { Name = "kafes 1", SortOrder = 0 });
        duplicateRoom.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(duplicateRoom)).Should().Be("HospitalizationRooms.DuplicateName");

        // Aynı ad başka bölümde serbest.
        await CreateRoomAsync(client, clinicId, catsId, "Kafes 1");

        var wards = await ListWardsAsync(client, clinicId);
        var ordered = wards.EnumerateArray().Where(w =>
            w.GetProperty("id").GetGuid() == icuId || w.GetProperty("id").GetGuid() == catsId).ToList();
        ordered.Select(w => w.GetProperty("name").GetString()).Should().Equal("Kedi servisi", "Yoğun bakım");
        var icu = FindWard(wards, icuId);
        icu.GetProperty("clinicId").GetGuid().Should().Be(clinicId);
        icu.GetProperty("isActive").GetBoolean().Should().BeTrue();
        icu.GetProperty("rooms").EnumerateArray().Select(r => r.GetProperty("name").GetString()).Should().Equal("Kafes 1", "Kafes 2");
        icu.GetProperty("rooms").EnumerateArray().Should().OnlyContain(r => r.GetProperty("wardId").GetGuid() == icuId);

        // Yeniden adlandırma; RouteIdMismatch.
        var rename = await client.PutAsJsonAsync($"{WardsUrl(clinicId)}/{icuId}", new { Name = "Yoğun Bakım Ünitesi", SortOrder = 3 });
        rename.StatusCode.Should().Be(HttpStatusCode.OK);
        var renamed = await rename.Content.ReadFromJsonAsync<JsonElement>();
        renamed.GetProperty("name").GetString().Should().Be("Yoğun Bakım Ünitesi");
        renamed.GetProperty("rooms").GetArrayLength().Should().Be(2);

        var mismatch = await client.PutAsJsonAsync($"{WardsUrl(clinicId)}/{icuId}", new { Id = catsId, Name = "X", SortOrder = 0 });
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(mismatch)).Should().Be("HospitalizationWards.RouteIdMismatch");

        // Oda pasife alınır; liste pasifleri de döner; aynı adla yeni aktif oda açılabilir; eskisi etkinleştirilemez.
        var deactivate = await client.PostAsync($"{WardsUrl(clinicId)}/{icuId}/rooms/{cage1Id}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deactivate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeFalse();

        var afterDeactivate = FindWard(await ListWardsAsync(client, clinicId), icuId).GetProperty("rooms").EnumerateArray().ToList();
        afterDeactivate.Single(r => r.GetProperty("id").GetGuid() == cage1Id).GetProperty("isActive").GetBoolean().Should().BeFalse();
        afterDeactivate.Single(r => r.GetProperty("id").GetGuid() == cage2Id).GetProperty("isActive").GetBoolean().Should().BeTrue();

        await CreateRoomAsync(client, clinicId, icuId, "Kafes 1");
        var reactivate = await client.PostAsync($"{WardsUrl(clinicId)}/{icuId}/rooms/{cage1Id}/activate", null);
        reactivate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(reactivate)).Should().Be("HospitalizationRooms.DuplicateName");

        // Bölüm pasife alma idempotent.
        (await client.PostAsync($"{WardsUrl(clinicId)}/{catsId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await client.PostAsync($"{WardsUrl(clinicId)}/{catsId}/deactivate", null);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Wards_Should_Return404_For_Clinic_Of_Another_Tenant_And_For_Room_Outside_Ward()
    {
        var (client, clinicId, _) = await AdminClientAsync();
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var wardA = await CreateWardAsync(client, clinicId, "A");
        var wardB = await CreateWardAsync(client, clinicId, "B");
        var roomInA = await CreateRoomAsync(client, clinicId, wardA, "Oda 1");

        var foreignList = await client.GetAsync(WardsUrl(foreignClinicId));
        foreignList.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(foreignList)).Should().Be("Clinics.NotFound");

        var foreignCreate = await client.PostAsJsonAsync(WardsUrl(foreignClinicId), new { Name = "X", SortOrder = 0 });
        foreignCreate.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var wrongWard = await client.PutAsJsonAsync($"{WardsUrl(clinicId)}/{wardB}/rooms/{roomInA}", new { Name = "Y", SortOrder = 0 });
        wrongWard.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(wrongWard)).Should().Be("HospitalizationRooms.NotFound");
    }

    [Fact]
    public async Task Wards_Read_Policy_Should_Allow_Hospitalizations_Read_Only_In_Assigned_Clinic_And_Deny_Write()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, _, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedClinicAdminUserAsync(_factory.Services, hasher);
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, [PermissionCatalog.Hospitalizations.Read]);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await client.GetAsync(WardsUrl(assignedClinicId))).StatusCode.Should().Be(HttpStatusCode.OK);

        var unassigned = await client.GetAsync(WardsUrl(unassignedClinicId));
        unassigned.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(unassigned)).Should().Be("Clinics.AccessDenied");

        var write = await client.PostAsJsonAsync(WardsUrl(assignedClinicId), new { Name = "X", SortOrder = 0 });
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------- Yatışa oda ataması ----------------

    [Fact]
    public async Task Create_With_Room_Should_Derive_Location_And_Expose_Room_And_Ward_Names()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var wardId = await CreateWardAsync(client, clinicId, "Yoğun bakım");
        var roomId = await CreateRoomAsync(client, clinicId, wardId, "Kafes 2");

        var id = await CreateHospitalizationAsync(client, HospitalizationBody(clinicId, petId, roomId, location: "Serbest metin"));

        var detail = await GetHospitalizationAsync(client, id);
        detail.GetProperty("roomId").GetGuid().Should().Be(roomId);
        detail.GetProperty("roomName").GetString().Should().Be("Kafes 2");
        detail.GetProperty("wardName").GetString().Should().Be("Yoğun bakım");
        detail.GetProperty("location").GetString().Should().Be("Yoğun bakım / Kafes 2");

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/hospitalizations?clinicId={clinicId}&petId={petId}");
        var item = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        item.GetProperty("roomId").GetGuid().Should().Be(roomId);
        item.GetProperty("roomName").GetString().Should().Be("Kafes 2");
        item.GetProperty("wardName").GetString().Should().Be("Yoğun bakım");
        item.GetProperty("location").GetString().Should().Be("Yoğun bakım / Kafes 2");
    }

    [Fact]
    public async Task Create_Without_Room_Should_Keep_Legacy_Free_Text_And_Null_Room_Fields()
    {
        var (client, clinicId, petId) = await AdminClientAsync();

        var id = await CreateHospitalizationAsync(client, HospitalizationBody(clinicId, petId, location: "Kafes 3"));

        var detail = await GetHospitalizationAsync(client, id);
        detail.GetProperty("roomId").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("roomName").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("wardName").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("location").GetString().Should().Be("Kafes 3");
    }

    [Fact]
    public async Task Create_Should_Return409_RoomInactive_For_Passive_Room_Or_Passive_Ward()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var wardId = await CreateWardAsync(client, clinicId, "Bölüm");
        var passiveRoomId = await CreateRoomAsync(client, clinicId, wardId, "Pasif oda");
        (await client.PostAsync($"{WardsUrl(clinicId)}/{wardId}/rooms/{passiveRoomId}/deactivate", null)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/v1/hospitalizations", HospitalizationBody(clinicId, petId, passiveRoomId));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Hospitalizations.RoomInactive");

        var passiveWardId = await CreateWardAsync(client, clinicId, "Pasif bölüm");
        var roomInPassiveWard = await CreateRoomAsync(client, clinicId, passiveWardId, "Aktif oda");
        (await client.PostAsync($"{WardsUrl(clinicId)}/{passiveWardId}/deactivate", null)).EnsureSuccessStatusCode();

        var wardResponse = await client.PostAsJsonAsync("/api/v1/hospitalizations", HospitalizationBody(clinicId, petId, roomInPassiveWard));
        wardResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(wardResponse)).Should().Be("Hospitalizations.RoomInactive");
    }

    [Fact]
    public async Task Create_Should_Return404_RoomNotFound_For_Room_Of_Another_Tenant_Or_Another_Clinic()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var foreignRoomId = await SeedRoomInForeignTenantAsync();

        var foreign = await client.PostAsJsonAsync("/api/v1/hospitalizations", HospitalizationBody(clinicId, petId, foreignRoomId));
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(foreign)).Should().Be("Hospitalizations.RoomNotFound");

        // Aynı kiracının başka kliniği: klinik eşleşmesi zorunlu.
        var (_, otherClinicId, _) = await AdminClientAsync();
        var otherWardId = await CreateWardAsync(client, otherClinicId, "Diğer klinik bölümü");
        var otherRoomId = await CreateRoomAsync(client, otherClinicId, otherWardId, "Oda");

        var otherClinic = await client.PostAsJsonAsync("/api/v1/hospitalizations", HospitalizationBody(clinicId, petId, otherRoomId));
        otherClinic.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(otherClinic)).Should().Be("Hospitalizations.RoomNotFound");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Hospitalizations.AnyAsync(h => h.PetId == petId)).Should().BeFalse("hata durumunda kayıt yazılmamalı");
    }

    [Fact]
    public async Task Put_Should_Change_Room_Keep_Room_When_Omitted_Keep_Passive_Current_Room_And_Clear_On_Request()
    {
        var (client, clinicId, petId) = await AdminClientAsync();
        var wardId = await CreateWardAsync(client, clinicId, "Yoğun bakım");
        var room1 = await CreateRoomAsync(client, clinicId, wardId, "Kafes 1");
        var room2 = await CreateRoomAsync(client, clinicId, wardId, "Kafes 2");
        var id = await CreateHospitalizationAsync(client, HospitalizationBody(clinicId, petId, room1));

        object Put(Guid? roomId = null, bool clearRoom = false, string? location = null) => new
        {
            ClinicId = clinicId,
            PetId = petId,
            AdmittedAtUtc = DateTime.UtcNow.AddHours(-1),
            Reason = "Güncel",
            RoomId = roomId,
            ClearRoom = clearRoom,
            Location = location,
        };

        // Eski ekran: roomId göndermez → oda korunur.
        (await client.PutAsJsonAsync($"/api/v1/hospitalizations/{id}", Put())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetHospitalizationAsync(client, id)).GetProperty("roomId").GetGuid().Should().Be(room1);

        // Oda değişir, konum yeniden türetilir.
        (await client.PutAsJsonAsync($"/api/v1/hospitalizations/{id}", Put(roomId: room2))).StatusCode.Should().Be(HttpStatusCode.OK);
        var moved = await GetHospitalizationAsync(client, id);
        moved.GetProperty("roomId").GetGuid().Should().Be(room2);
        moved.GetProperty("location").GetString().Should().Be("Yoğun bakım / Kafes 2");

        // Mevcut oda pasifleşir: aynı oda yeniden gönderilebilir, başka yatışa atanamaz.
        (await client.PostAsync($"{WardsUrl(clinicId)}/{wardId}/rooms/{room2}/deactivate", null)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/v1/hospitalizations/{id}", Put(roomId: room2))).StatusCode.Should().Be(HttpStatusCode.OK);
        var kept = await GetHospitalizationAsync(client, id);
        kept.GetProperty("roomId").GetGuid().Should().Be(room2);
        kept.GetProperty("roomName").GetString().Should().Be("Kafes 2");

        var otherPetId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, clinicId);
        var otherStay = await client.PostAsJsonAsync("/api/v1/hospitalizations", HospitalizationBody(clinicId, otherPetId, room2));
        otherStay.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // roomId + clearRoom birlikte → 400.
        var both = await client.PutAsJsonAsync($"/api/v1/hospitalizations/{id}", Put(roomId: room1, clearRoom: true));
        both.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // clearRoom: bağ kalkar, konum metni kalır.
        (await client.PutAsJsonAsync($"/api/v1/hospitalizations/{id}", Put(clearRoom: true))).StatusCode.Should().Be(HttpStatusCode.OK);
        var cleared = await GetHospitalizationAsync(client, id);
        cleared.GetProperty("roomId").ValueKind.Should().Be(JsonValueKind.Null);
        cleared.GetProperty("roomName").ValueKind.Should().Be(JsonValueKind.Null);
        cleared.GetProperty("location").GetString().Should().Be("Yoğun bakım / Kafes 2");

        // clearRoom + location "" : konum da temizlenir.
        (await client.PutAsJsonAsync($"/api/v1/hospitalizations/{id}", Put(roomId: room1))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/v1/hospitalizations/{id}", Put(clearRoom: true, location: ""))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetHospitalizationAsync(client, id)).GetProperty("location").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
