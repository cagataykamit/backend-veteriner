using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Appointments;

/// <summary>
/// Randevu iptalini geri alma uçtan uca (docs/APPOINTMENT_CANCEL_UNDO_API_CONTRACT.md): iptal yanıtındaki süre, geri alma, not
/// satırının kalkması, denetim, süre dolumu, bu arada saate giren randevu ve yetki. Bekleme listesi etkileşimi
/// <c>WaitlistIntegrationTests</c>'te.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AppointmentCancelUndoIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] AllPermissions =
    [
        PermissionCatalog.Appointments.Read,
        PermissionCatalog.Appointments.Create,
        PermissionCatalog.Appointments.Cancel,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public AppointmentCancelUndoIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Cancel_Should_Return_Undo_Window_And_Revert_Should_Restore_Scheduled_Without_Cancel_Note_With_Audit()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(ctx.PetId, TimeSpan.FromHours(3), "Aşı kartı getirilecek");

        var cancel = await ctx.Http.PostAsJsonAsync($"/api/v1/appointments/{id}/cancel", new { reason = "Sahibi aradı" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        (await cancel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("undoWindowSeconds").GetInt32().Should().Be(10);

        var revert = await ctx.Http.PostAsync($"/api/v1/appointments/{id}/cancel/revert", null);
        revert.StatusCode.Should().Be(HttpStatusCode.NoContent, await revert.Content.ReadAsStringAsync());

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
        row.Status.Should().Be(AppointmentStatus.Scheduled);
        row.Notes.Should().Be("Aşı kartı getirilecek");

        var actions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TargetId == $"AppointmentId={id}" && a.Success)
            .Select(a => a.Action)
            .ToListAsync();
        actions.Should().Contain(["Appointment.Cancel", "Appointment.CancelRevert"]);
    }

    [Fact]
    public async Task Revert_Should_Reject_Not_Cancelled_Appointment()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(ctx.PetId, TimeSpan.FromHours(3));

        var revert = await ctx.Http.PostAsync($"/api/v1/appointments/{id}/cancel/revert", null);

        revert.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(revert)).Should().Be("Appointments.InvalidStatusTransition");
    }

    [Fact]
    public async Task Revert_After_Limit_Should_Return_Expired_And_Keep_Cancelled()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(ctx.PetId, TimeSpan.FromHours(3));
        (await ctx.Http.PostAsJsonAsync($"/api/v1/appointments/{id}/cancel", new { })).EnsureSuccessStatusCode();
        await ctx.SetChangedAtAsync(id, DateTime.UtcNow - Appointment.CancellationUndoLimit - TimeSpan.FromSeconds(1));

        var revert = await ctx.Http.PostAsync($"/api/v1/appointments/{id}/cancel/revert", null);

        revert.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(revert)).Should().Be("Appointments.CancellationUndoExpired");
        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Revert_Should_Conflict_When_Slot_Was_Booked_Meanwhile()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(ctx.PetId, TimeSpan.FromHours(3));
        (await ctx.Http.PostAsJsonAsync($"/api/v1/appointments/{id}/cancel", new { })).EnsureSuccessStatusCode();
        await ctx.SeedAppointmentAsync(ctx.PetId, TimeSpan.FromHours(3));

        var revert = await ctx.Http.PostAsync($"/api/v1/appointments/{id}/cancel/revert", null);

        // Klinik varsayılanı üst üste binmeye izin vermez; sıra klinik → hayvan → hekim (AppointmentSlotRules.EnsureNoConflictAsync).
        revert.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(revert)).Should().Be("Appointments.ClinicTimeConflict");
        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Revert_Should_Require_Cancel_Permission()
    {
        var ctx = await SeedAsync([PermissionCatalog.Appointments.Read]);
        var id = await ctx.SeedAppointmentAsync(ctx.PetId, TimeSpan.FromHours(3), status: AppointmentStatus.Cancelled);

        (await ctx.Http.PostAsync($"/api/v1/appointments/{id}/cancel/revert", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Kurulum ----

    private async Task<UndoTestContext> SeedAsync(string[]? permissions = null)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId;
        Guid clinicId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Undo-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"undo-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            clinicId = clinic.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, permissions ?? AllPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ctx = new UndoTestContext(_factory.Services, http, tenantId, clinicId);
        ctx.PetId = await ctx.SeedPetAsync();
        return ctx;
    }

    private sealed class UndoTestContext(IServiceProvider services, HttpClient http, Guid tenantId, Guid clinicId)
    {
        private readonly DateTime _slot = DateTime.UtcNow.Date.AddDays(2).AddHours(10);

        public HttpClient Http { get; } = http;
        public Guid PetId { get; set; }

        public async Task<AppointmentStatus> GetStatusAsync(Guid appointmentId)
        {
            await using var scope = services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId).Select(a => a.Status).SingleAsync();
        }

        public async Task SetChangedAtAsync(Guid appointmentId, DateTime changedAtUtc)
        {
            await using var scope = services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Appointments.Where(a => a.Id == appointmentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.ChangedAtUtc, changedAtUtc));
        }

        /// <summary>Doğrudan veritabanına randevu yazar (slot doğrulaması atlanır); aynı <paramref name="offset"/> aynı saati verir.</summary>
        public async Task<Guid> SeedAppointmentAsync(Guid petId, TimeSpan offset, string? notes = null, AppointmentStatus? status = null)
        {
            await using var scope = services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appointment = new Appointment(tenantId, clinicId, petId, _slot.Add(offset), 30, AppointmentType.Consultation, status, notes);
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            return appointment.Id;
        }

        public async Task<Guid> SeedPetAsync()
        {
            await using var scope = services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(tenantId, $"UClient-{Guid.NewGuid():N}"[..14], DateTime.UtcNow, "905551110078");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(tenantId, client.Id, $"UPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return pet.Id;
        }
    }
}
