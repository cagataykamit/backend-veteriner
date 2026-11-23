using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Clients;

/// <summary>
/// ADR-054 uçtan uca: müşteri/hasta yumuşak silme, liste/arama dışlaması, bağlı kayıtların okunması, engeller, geri
/// yükleme, portal bağı ve izin. HTTP + gerçek SQL Server (LocalDB).
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class ClientPetSoftDeleteIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] ManagerPermissions =
    [
        PermissionCatalog.Clients.Read,
        PermissionCatalog.Clients.Create,
        PermissionCatalog.Clients.Update,
        PermissionCatalog.Clients.Delete,
        PermissionCatalog.Pets.Read,
        PermissionCatalog.Pets.Create,
        PermissionCatalog.Pets.Update,
        PermissionCatalog.Pets.Delete,
        PermissionCatalog.Visits.Read,
        PermissionCatalog.Visits.Create,
        PermissionCatalog.Payments.Read,
        PermissionCatalog.Payments.Create,
        PermissionCatalog.Charges.Read,
        PermissionCatalog.Vaccinations.Read,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public ClientPetSoftDeleteIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Delete_Client_Should_Hide_Client_And_Pets_Keep_History_And_Restore()
    {
        var ctx = await SeedAsync();
        var petId = await ctx.CreatePetAsync("SilPamuk");
        var chargeId = await ctx.SeedChargeAsync(250m, petId);
        (await ctx.PayAsync(250m, chargeId)).StatusCode.Should().Be(HttpStatusCode.Created);
        await ctx.SeedAppliedVaccinationAsync(petId);

        var delete = await ctx.Http.DeleteAsync($"/api/v1/clients/{ctx.ClientId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent, await delete.Content.ReadAsStringAsync());

        // Liste ve arama: müşteri ve hastası yok.
        (await ctx.SearchClientIdsAsync(ctx.ClientName)).Should().NotContain(ctx.ClientId);
        (await ctx.SearchPetIdsAsync("SilPamuk")).Should().NotContain(petId);
        (await ctx.ListPetIdsOfClientAsync()).Should().BeEmpty();

        // Kart açılır, silindiği görünür.
        var clientCard = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{ctx.ClientId}");
        clientCard.GetProperty("deletedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        var petCard = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/pets/{petId}");
        petCard.GetProperty("deletedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        petCard.GetProperty("deletedWithClient").GetBoolean().Should().BeTrue();

        // Bağlı ödeme ve aşı okunur, adlar görünür.
        var payments = await GetJsonAsync(ctx.Http, $"/api/v1/payments?clinicId={ctx.ClinicId}&clientId={ctx.ClientId}&pageSize=10");
        var payment = payments.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        payment.GetProperty("clientName").GetString().Should().Be(ctx.ClientName);
        payment.GetProperty("petName").GetString().Should().Be("SilPamuk");
        var vaccinations = await GetJsonAsync(ctx.Http, $"/api/v1/vaccinations?clinicId={ctx.ClinicId}&petId={petId}&pageSize=10");
        var vaccination = vaccinations.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        vaccination.GetProperty("petName").GetString().Should().Be("SilPamuk");
        vaccination.GetProperty("clientName").GetString().Should().Be(ctx.ClientName);

        // Silinenler listeleri.
        var deletedClients = await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/deleted?search={Uri.EscapeDataString(ctx.ClientName)}");
        var deletedClient = deletedClients.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        deletedClient.GetProperty("id").GetGuid().Should().Be(ctx.ClientId);
        deletedClient.GetProperty("deletedByUserId").GetGuid().Should().Be(ctx.UserId);
        var deletedPets = await ctx.Http.GetFromJsonAsync<JsonElement>("/api/v1/pets/deleted?search=SilPamuk&pageSize=50");
        var deletedPet = deletedPets.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == petId);
        deletedPet.GetProperty("clientIsDeleted").GetBoolean().Should().BeTrue();
        deletedPet.GetProperty("deletedWithClient").GetBoolean().Should().BeTrue();

        // Silinen kayda yeni iş yazılmaz.
        await ExpectConflictAsync(
            ctx.Http.PutAsJsonAsync($"/api/v1/clients/{ctx.ClientId}", new { fullName = ctx.ClientName, phone = ctx.Phone }), "Clients.Deleted");
        await ExpectConflictAsync(
            ctx.Http.PostAsJsonAsync("/api/v1/pets", new { clientId = ctx.ClientId, name = "Yeni", speciesId = ctx.SpeciesId }), "Clients.Deleted");
        await ExpectConflictAsync(
            ctx.Http.PostAsJsonAsync("/api/v1/visits", new { clinicId = ctx.ClinicId, petId }), "Pets.Deleted");

        // Hasta tek başına geri yüklenemez; müşteri geri yüklenince birlikte gelir.
        await ExpectConflictAsync(ctx.Http.PostAsync($"/api/v1/pets/{petId}/restore", null), "Pets.ClientDeleted");
        var restore = await ctx.Http.PostAsync($"/api/v1/clients/{ctx.ClientId}/restore", null);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent, await restore.Content.ReadAsStringAsync());

        (await ctx.SearchClientIdsAsync(ctx.ClientName)).Should().Contain(ctx.ClientId);
        (await ctx.ListPetIdsOfClientAsync()).Should().Equal(petId);
        (await ctx.Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{ctx.ClientId}"))
            .GetProperty("deletedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
        (await ctx.Http.PostAsJsonAsync("/api/v1/visits", new { clinicId = ctx.ClinicId, petId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Delete_Client_Should_Be_Blocked_By_Open_Account_Future_Appointment_And_Active_Hospitalization()
    {
        var ctx = await SeedAsync();
        var petId = await ctx.CreatePetAsync("EngelPet");
        await ctx.SeedChargeAsync(100m, petId);
        await ctx.SeedFutureAppointmentAsync(petId);
        await ctx.SeedActiveHospitalizationAsync(petId);

        var response = await ctx.Http.DeleteAsync($"/api/v1/clients/{ctx.ClientId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Clients.DeleteBlocked");
        problem.GetProperty("reasons").EnumerateArray().Select(r => r.GetString())
            .Should().Equal(RecordDeletionBlockers.OpenAccount, RecordDeletionBlockers.ActiveHospitalization, RecordDeletionBlockers.FutureAppointments);
        problem.GetProperty("futureAppointmentCount").GetInt32().Should().Be(1);
        problem.GetProperty("detail").GetString().Should().StartWith("Müşteri silinemez");

        (await ctx.SearchClientIdsAsync(ctx.ClientName)).Should().Contain(ctx.ClientId);
    }

    [Fact]
    public async Task Delete_Pet_Should_Be_Blocked_By_Future_Appointment_But_Not_By_Client_Account()
    {
        var ctx = await SeedAsync();
        var blockedPet = await ctx.CreatePetAsync("RandevuluPet");
        var freePet = await ctx.CreatePetAsync("SerbestPet");
        await ctx.SeedChargeAsync(100m, freePet);
        await ctx.SeedFutureAppointmentAsync(blockedPet);

        var blocked = await ctx.Http.DeleteAsync($"/api/v1/pets/{blockedPet}");
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Pets.DeleteBlocked");
        problem.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).Should().Equal(RecordDeletionBlockers.FutureAppointments);

        var deleted = await ctx.Http.DeleteAsync($"/api/v1/pets/{freePet}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        (await ctx.Http.DeleteAsync($"/api/v1/pets/{freePet}")).StatusCode.Should().Be(HttpStatusCode.NoContent, "tekrar silme işlem yapmadan başarı döner");
        (await ctx.ListPetIdsOfClientAsync()).Should().Equal(blockedPet);

        // Tek başına silinen hasta, müşteri silinip geri yüklenince silinmiş kalır; kendi ucuyla geri gelir.
        var restore = await ctx.Http.PostAsync($"/api/v1/pets/{freePet}/restore", null);
        restore.StatusCode.Should().Be(HttpStatusCode.NoContent, await restore.Content.ReadAsStringAsync());
        (await ctx.ListPetIdsOfClientAsync()).Should().BeEquivalentTo([blockedPet, freePet]);
    }

    [Fact]
    public async Task Deleted_Client_Should_Not_Block_New_Client_With_Same_Details_And_Restore_Should_Conflict()
    {
        var ctx = await SeedAsync();
        (await ctx.Http.DeleteAsync($"/api/v1/clients/{ctx.ClientId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var create = await ctx.Http.PostAsJsonAsync("/api/v1/clients", new { fullName = ctx.ClientName, phone = ctx.Phone, email = ctx.Email });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var newId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var restore = await ctx.Http.PostAsync($"/api/v1/clients/{ctx.ClientId}/restore", null);
        restore.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await restore.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("Clients.RestoreConflict");
        problem.GetProperty("conflictingClientId").GetGuid().Should().Be(newId);
    }

    [Fact]
    public async Task Delete_Client_Should_Close_Portal_Link_And_Restore_Should_Keep_It_Closed()
    {
        var ctx = await SeedAsync();
        var linkId = await ctx.SeedPortalLinkAsync();

        (await ctx.Http.DeleteAsync($"/api/v1/clients/{ctx.ClientId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ctx.Http.PostAsync($"/api/v1/clients/{ctx.ClientId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.OwnerAccountClientLinks.AsNoTracking().SingleAsync(l => l.Id == linkId);
        link.Status.Should().Be(OwnerAccountClientLinkStatus.Closed, "geri yükleme bağı açmaz (ADR-054 K9)");
        link.CloseReason.Should().Be(OwnerAccountClientLinkCloseReason.ClientDeleted);
        link.ClosedByUserId.Should().Be(ctx.UserId);
    }

    [Fact]
    public async Task Delete_Restore_And_Deleted_List_Should_Require_Delete_Permission()
    {
        var ctx = await SeedAsync();
        var petId = await ctx.CreatePetAsync("YetkiPet");
        var withoutDelete = await ctx.NewHttpAsync(ManagerPermissions
            .Where(p => p != PermissionCatalog.Clients.Delete && p != PermissionCatalog.Pets.Delete)
            .ToArray());

        (await withoutDelete.DeleteAsync($"/api/v1/clients/{ctx.ClientId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await withoutDelete.DeleteAsync($"/api/v1/pets/{petId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await withoutDelete.PostAsync($"/api/v1/clients/{ctx.ClientId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await withoutDelete.GetAsync("/api/v1/clients/deleted")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await withoutDelete.GetAsync("/api/v1/pets/deleted")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await ctx.SearchClientIdsAsync(ctx.ClientName)).Should().Contain(ctx.ClientId);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient http, string url)
    {
        var response = await http.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task ExpectConflictAsync(Task<HttpResponseMessage> request, string code)
    {
        var response = await request;
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be(code);
    }

    // ---- Kurulum ----

    private async Task<DeletionTestContext> SeedAsync()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var phone = $"90555{Random.Shared.Next(1000000, 9999999)}";
        var email = $"sil-{suffix}@example.com";
        Guid tenantId, clinicId, clientId, speciesId, userId;
        string userEmail;
        string clientName = $"Silme Sahibi {suffix}";
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Sil-{suffix}", "Izmir");
            var client = new Client(tenant.Id, clientName, DateTime.UtcNow, phone, email);
            db.AddRange(clinic, client);
            await db.SaveChangesAsync();

            (userId, userEmail) = await SeedUserAsync(db, hasher, tenant.Id, clinic.Id);
            tenantId = tenant.Id;
            clinicId = clinic.Id;
            clientId = client.Id;
            speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        }

        var http = await NewHttpAsync(userEmail, ManagerPermissions);
        return new DeletionTestContext(this, http, tenantId, clinicId, clientId, speciesId, userId, clientName, phone, email);
    }

    private static async Task<(Guid UserId, string Email)> SeedUserAsync(AppDbContext db, IPasswordHasher hasher, Guid tenantId, Guid clinicId)
    {
        var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
        var email = $"sil-user-{Guid.NewGuid():N}@example.com";
        var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
        db.UserTenants.Add(new UserTenant(user.Id, tenantId, DateTime.UtcNow));
        db.UserClinics.Add(new UserClinic(user.Id, clinicId, DateTime.UtcNow));
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    private async Task<HttpClient> NewHttpAsync(string userEmail, string[] permissions)
    {
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, userEmail, permissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private sealed class DeletionTestContext(
        ClientPetSoftDeleteIntegrationTests owner,
        HttpClient http,
        Guid tenantId,
        Guid clinicId,
        Guid clientId,
        Guid speciesId,
        Guid userId,
        string clientName,
        string phone,
        string email)
    {
        public HttpClient Http { get; } = http;
        public Guid TenantId { get; } = tenantId;
        public Guid ClinicId { get; } = clinicId;
        public Guid ClientId { get; } = clientId;
        public Guid SpeciesId { get; } = speciesId;
        public Guid UserId { get; } = userId;
        public string ClientName { get; } = clientName;
        public string Phone { get; } = phone;
        public string Email { get; } = email;

        private IServiceProvider Services => owner._factory.Services;

        public async Task<HttpClient> NewHttpAsync(string[] permissions)
        {
            string userEmail;
            await using (var scope = Services.CreateSystemScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (_, userEmail) = await SeedUserAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher>(), TenantId, ClinicId);
            }

            return await owner.NewHttpAsync(userEmail, permissions);
        }

        public async Task<Guid> CreatePetAsync(string name)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/pets", new { clientId = ClientId, name, speciesId = SpeciesId });
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<Guid>();
        }

        public async Task<List<Guid>> SearchClientIdsAsync(string search)
        {
            var page = await Http.GetFromJsonAsync<JsonElement>($"/api/v1/clients?pageSize=50&search={Uri.EscapeDataString(search)}");
            return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        }

        public async Task<List<Guid>> SearchPetIdsAsync(string search)
        {
            var page = await Http.GetFromJsonAsync<JsonElement>($"/api/v1/pets?pageSize=50&search={Uri.EscapeDataString(search)}");
            return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        }

        public async Task<List<Guid>> ListPetIdsOfClientAsync()
        {
            var page = await Http.GetFromJsonAsync<JsonElement>($"/api/v1/pets?clientId={ClientId}&pageSize=50");
            return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        }

        public Task<HttpResponseMessage> PayAsync(decimal amount, Guid chargeId)
            => Http.PostAsJsonAsync("/api/v1/payments", new
            {
                ClinicId,
                ClientId,
                Amount = amount,
                Currency = "TRY",
                Method = PaymentMethod.Cash,
                PaidAtUtc = DateTime.UtcNow.AddMinutes(-1),
                RequestId = Guid.NewGuid(),
                Allocations = new[] { new { ChargeId = chargeId, Amount = amount } },
            });

        public Task<Guid> SeedChargeAsync(decimal amount, Guid petId)
            => SeedAsync(db =>
            {
                var charge = Charge.Create(TenantId, ClinicId, ClientId, petId, null, null, "Silme testi", 1m, amount, UserId, DateTime.UtcNow, null).Value!;
                db.Charges.Add(charge);
                return charge.Id;
            });

        public Task<Guid> SeedFutureAppointmentAsync(Guid petId)
            => SeedAsync(db =>
            {
                var appointment = new Appointment(TenantId, ClinicId, petId, DateTime.UtcNow.AddDays(3), 30, AppointmentType.Checkup, AppointmentStatus.Scheduled);
                db.Appointments.Add(appointment);
                return appointment.Id;
            });

        public Task<Guid> SeedActiveHospitalizationAsync(Guid petId)
            => SeedAsync(db =>
            {
                var hospitalization = new Hospitalization(
                    TenantId, ClinicId, petId, null, DateTime.UtcNow.AddHours(-2), null, "Silme testi", null, DateTime.UtcNow);
                db.Hospitalizations.Add(hospitalization);
                return hospitalization.Id;
            });

        public async Task SeedAppliedVaccinationAsync(Guid petId)
        {
            await SeedAsync(async db =>
            {
                var definition = await db.VaccineDefinitions.AsNoTracking().FirstAsync(v => v.TenantId == null);
                var vaccination = new Vaccination(
                    TenantId, petId, ClinicId, null, definition.Id, definition.Name, VaccinationStatus.Applied,
                    DateTime.UtcNow.AddDays(-1), null, null, DateTime.UtcNow);
                db.Vaccinations.Add(vaccination);
                return vaccination.Id;
            });
        }

        public Task<Guid> SeedPortalLinkAsync()
            => SeedAsync(db =>
            {
                var account = OwnerAccount.Create($"portal-{Guid.NewGuid():N}@example.com", "Portal Sahibi", DateTime.UtcNow);
                var link = OwnerAccountClientLink.Create(account.Id, TenantId, ClinicId, ClientId, UserId, DateTime.UtcNow);
                db.AddRange(account, link);
                return link.Id;
            });

        private Task<Guid> SeedAsync(Func<AppDbContext, Guid> add) => SeedAsync(db => Task.FromResult(add(db)));

        private async Task<Guid> SeedAsync(Func<AppDbContext, Task<Guid>> add)
        {
            await using var scope = Services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = await add(db);
            await db.SaveChangesAsync();
            return id;
        }
    }
}
