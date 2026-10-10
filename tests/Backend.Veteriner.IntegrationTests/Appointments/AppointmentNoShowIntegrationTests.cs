using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Appointments;

/// <summary>
/// CHECKIN-010 gelmedi (NoShow) uçtan uca: HTTP + gerçek SQL Server (LocalDB). İşaretleme/geri alma, tüketiciler
/// (Bugün, rapor, dashboard), geç gelen hasta, muayene reddi, PUT/POST durum bypass'ı ve izolasyon.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class AppointmentNoShowIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Reason = "Hasta aranmadi, gelmedi";

    private static readonly string[] AllPermissions =
    [
        PermissionCatalog.Appointments.Read,
        PermissionCatalog.Appointments.Create,
        PermissionCatalog.Appointments.Cancel,
        PermissionCatalog.Appointments.Complete,
        PermissionCatalog.Appointments.Reschedule,
        PermissionCatalog.Appointments.NoShow,
        PermissionCatalog.Visits.Read,
        PermissionCatalog.Visits.Create,
        PermissionCatalog.Examinations.Create,
        PermissionCatalog.Examinations.Read,
        PermissionCatalog.Dashboard.Read,
    ];

    private readonly CustomWebApplicationFactory _factory;

    public AppointmentNoShowIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ---- Yetki, işaretleme, geri alma ----

    [Fact]
    public async Task NoShow_Endpoints_Should_Require_NoShow_Permission()
    {
        var ctx = await SeedAsync(AllPermissions.Where(p => p != PermissionCatalog.Appointments.NoShow).ToArray());
        var id = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-5));

        (await ctx.PostAsync($"/api/v1/appointments/{id}/no-show")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ctx.PostAsync($"/api/v1/appointments/{id}/no-show/revert", new { reason = Reason }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Mark_Should_Reject_Future_Appointment()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(TimeSpan.FromHours(3));

        var response = await ctx.PostAsync($"/api/v1/appointments/{id}/no-show");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Appointments.NoShowNotYetDue");
        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task Mark_Should_Set_NoShow_Be_Idempotent_Emit_One_Event_And_Write_Audit()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-5));

        (await ctx.PostAsync($"/api/v1/appointments/{id}/no-show", new { reason = Reason }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ctx.PostAsync($"/api/v1/appointments/{id}/no-show"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.NoShow);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = await db.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.Type == AppointmentIntegrationEventTypes.Updated && m.Payload.Contains(id.ToString()));
        events.Should().Be(1, "ikinci istek idempotent: değişiklik ve olay yok");
        var log = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Appointment.NoShow" && a.TargetId == $"AppointmentId={id}" && a.Success)
            .ToListAsync();
        log.Should().NotBeEmpty();
        log.Should().Contain(a => a.RequestPayload!.Contains(Reason));
    }

    [Fact]
    public async Task Concurrent_Mark_Requests_Should_All_Succeed_Without_Server_Error()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-5));

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ctx.PostAsync($"/api/v1/appointments/{id}/no-show")));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.NoContent || r.StatusCode == HttpStatusCode.Conflict);
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().BeGreaterThanOrEqualTo(1);
        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.NoShow);
    }

    [Fact]
    public async Task NoShow_Appointment_Should_Be_Terminal_For_Cancel_Complete_And_Reschedule()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-5));
        await ctx.PostAsync($"/api/v1/appointments/{id}/no-show");

        foreach (var path in new[] { "cancel", "complete" })
        {
            var response = await ctx.PostAsync($"/api/v1/appointments/{id}/{path}");
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, path);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Appointments.InvalidStatusTransition");
        }

        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.NoShow);
    }

    [Fact]
    public async Task Mark_Should_Return_400_HasVisit_When_Patient_Already_Arrived_And_For_Terminal()
    {
        var ctx = await SeedAsync();
        var arrived = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(1));
        (await ctx.PostAsync("/api/v1/visits", new { appointmentId = arrived })).StatusCode.Should().Be(HttpStatusCode.Created);
        await ctx.SetScheduledAtAsync(arrived, DateTime.UtcNow.AddMinutes(-10));

        var hasVisit = await ctx.PostAsync($"/api/v1/appointments/{arrived}/no-show");
        hasVisit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(hasVisit)).Should().Be("Appointments.HasVisit");

        var cancelled = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-5), AppointmentStatus.Cancelled);
        var terminal = await ctx.PostAsync($"/api/v1/appointments/{cancelled}/no-show");
        terminal.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(terminal)).Should().Be("Appointments.InvalidStatusTransition");
    }

    [Fact]
    public async Task Revert_Should_Require_Reason_Return_To_Scheduled_Write_Audit_And_Reject_Non_NoShow()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-5));

        var notNoShow = await ctx.PostAsync($"/api/v1/appointments/{id}/no-show/revert", new { reason = Reason });
        notNoShow.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(notNoShow)).Should().Be("Appointments.InvalidStatusTransition");

        await ctx.PostAsync($"/api/v1/appointments/{id}/no-show");
        var noReason = await ctx.PostAsync($"/api/v1/appointments/{id}/no-show/revert", new { reason = "x" });
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noReason)).Should().Be("Appointments.Validation");
        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.NoShow);

        (await ctx.PostAsync($"/api/v1/appointments/{id}/no-show/revert", new { reason = "Yanlis isaretlendi" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.Scheduled);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
            a.Action == "Appointment.NoShowRevert" && a.TargetId == $"AppointmentId={id}" && a.Success)).Should().BeTrue();
        (await db.AuditLogs.AsNoTracking().AnyAsync(a =>
            a.Action == "Appointment.NoShowRevert" && a.TargetId == $"AppointmentId={id}" && !a.Success)).Should().BeTrue();
    }

    // ---- Geç gelen hasta, muayene ----

    [Fact]
    public async Task Late_Arrival_Should_Revert_NoShow_To_Scheduled_And_Allow_Normal_Flow()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-5));
        await ctx.PostAsync($"/api/v1/appointments/{id}/no-show");

        var arrival = await ctx.PostAsync("/api/v1/visits", new { appointmentId = id });

        arrival.StatusCode.Should().Be(HttpStatusCode.Created, await arrival.Content.ReadAsStringAsync());
        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.Scheduled);
        var visitId = (await arrival.Content.ReadFromJsonAsync<VisitCreateResultDto>())!.Visit.Id;

        var examination = await ctx.PostAsync("/api/v1/examinations", new
        {
            visitId,
            examinedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            visitReason = "Kontrol",
        });
        examination.StatusCode.Should().Be(HttpStatusCode.Created, await examination.Content.ReadAsStringAsync());
        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.Completed, "K2-A: muayene randevuyu tamamlar");
    }

    [Fact]
    public async Task Examination_On_NoShow_Appointment_Should_Be_Rejected()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-5));
        await ctx.PostAsync($"/api/v1/appointments/{id}/no-show");

        var response = await ctx.PostAsync("/api/v1/examinations", new
        {
            appointmentId = id,
            examinedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            visitReason = "Kontrol",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Examinations.AppointmentNoShow");
        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.NoShow);
    }

    // ---- Tüketiciler: Bugün, rapor, dashboard ----

    [Fact]
    public async Task Today_Should_List_NoShow_Appointment_Last_With_AppointmentStatus()
    {
        var ctx = await SeedAsync();
        var noShowId = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-5));
        await ctx.PostAsync($"/api/v1/appointments/{noShowId}/no-show");
        var walkinPet = await ctx.SeedPetAsync();
        (await ctx.PostAsync("/api/v1/visits", new { clinicId = ctx.ClinicId, petId = walkinPet }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var today = await ctx.Http.GetFromJsonAsync<TodayDto>($"/api/v1/visits/today?clinicId={ctx.ClinicId}");

        today!.Items.Should().HaveCount(2);
        today.Items[^1].AppointmentId.Should().Be(noShowId);
        today.Items[^1].AppointmentStatus.Should().Be(AppointmentStatus.NoShow);
        today.Items[^1].VisitId.Should().BeNull();
        today.Items[0].CareStatus.Should().Be(VisitCareStatus.Waiting);
    }

    [Fact]
    public async Task Report_And_Csv_Should_Count_NoShow_Separately_And_Keep_Total_Consistent()
    {
        var ctx = await SeedAsync();
        var noShowId = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-5));
        await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-4));
        await ctx.PostAsync($"/api/v1/appointments/{noShowId}/no-show");
        var from = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));
        var baseUrl = $"clinicId={ctx.ClinicId}&from={from}&to={to}";

        var report = await ctx.Http.GetFromJsonAsync<AppointmentReportResultDto>($"/api/v1/reports/appointments?{baseUrl}");
        report!.StatusCounts.NoShow.Should().Be(1);
        report.StatusCounts.Scheduled.Should().Be(1);
        report.TotalCount.Should().Be(2);

        var filtered = await ctx.Http.GetFromJsonAsync<AppointmentReportResultDto>(
            $"/api/v1/reports/appointments?{baseUrl}&status=3");
        filtered!.TotalCount.Should().Be(1);
        filtered.Items.Should().ContainSingle(i => i.AppointmentId == noShowId && i.Status == AppointmentStatus.NoShow);

        var csv = await ctx.Http.GetStringAsync($"/api/v1/reports/appointments/export?{baseUrl}");
        csv.Should().Contain("Gelmedi");
    }

    [Fact]
    public async Task Dashboard_Summary_Should_Report_NoShow_Today_Count()
    {
        var ctx = await SeedAsync();
        var before = (await ctx.Http.GetFromJsonAsync<DashboardSummaryDto>("/api/v1/dashboard/summary"))!;
        var id = await ctx.SeedAppointmentAsync(TimeSpan.FromMinutes(-5));
        await ctx.PostAsync($"/api/v1/appointments/{id}/no-show");

        var after = (await ctx.Http.GetFromJsonAsync<DashboardSummaryDto>("/api/v1/dashboard/summary"))!;

        after.NoShowTodayCount.Should().Be(before.NoShowTodayCount + 1);
    }

    // ---- Durum geçişi bypass'ı kapatıldı ----

    [Fact]
    public async Task Put_Should_Not_Change_Status_And_Create_Should_Only_Accept_Scheduled()
    {
        var ctx = await SeedAsync();
        var id = await ctx.SeedAppointmentAsync(TimeSpan.FromDays(2));

        foreach (var status in new[] { AppointmentStatus.Completed, AppointmentStatus.Cancelled, AppointmentStatus.NoShow })
        {
            var put = await ctx.Http.PutAsJsonAsync($"/api/v1/appointments/{id}", new
            {
                id,
                clinicId = ctx.ClinicId,
                petId = ctx.PetId,
                scheduledAtUtc = DateTime.UtcNow.AddDays(2),
                appointmentType = AppointmentType.Consultation,
                status,
            });
            put.StatusCode.Should().Be(HttpStatusCode.BadRequest, status.ToString());
            (await IntegrationTestProblemDetails.ReadCodeAsync(put)).Should().Be("Appointments.InvalidStatusTransition");
        }

        (await ctx.GetStatusAsync(id)).Should().Be(AppointmentStatus.Scheduled);

        var create = await ctx.PostAsync("/api/v1/appointments", new
        {
            clinicId = ctx.ClinicId,
            petId = ctx.PetId,
            scheduledAtUtc = DateTime.UtcNow.AddDays(3),
            appointmentType = AppointmentType.Consultation,
            status = AppointmentStatus.Completed,
        });
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(create)).Should().Be("Appointments.Validation");
    }

    // ---- İzolasyon ----

    [Fact]
    public async Task NoShow_Should_Respect_Tenant_Isolation_And_Clinic_Assignment()
    {
        var ctx = await SeedAsync();
        Guid otherTenantAppointment;
        Guid unassignedClinicAppointment;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.Select(s => s.Id).FirstAsync();

            var otherTenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..20]);
            db.Tenants.Add(otherTenant);
            await db.SaveChangesAsync();
            var otherClinic = new Clinic(otherTenant.Id, $"Foreign-{Guid.NewGuid():N}"[..14], "Ankara");
            var otherClient = new Client(otherTenant.Id, $"Foreign-{Guid.NewGuid():N}"[..14], "905551110068");
            db.AddRange(otherClinic, otherClient);
            await db.SaveChangesAsync();
            var otherPet = new Pet(otherTenant.Id, otherClient.Id, $"FPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(otherPet);
            await db.SaveChangesAsync();
            var foreign = new Appointment(otherTenant.Id, otherClinic.Id, otherPet.Id, DateTime.UtcNow.AddHours(-2), 30, AppointmentType.Other);

            var unassigned = new Clinic(ctx.TenantId, $"Other-{Guid.NewGuid():N}"[..14], "Bursa");
            db.Clinics.Add(unassigned);
            await db.SaveChangesAsync();
            var sameTenant = new Appointment(ctx.TenantId, unassigned.Id, ctx.PetId, DateTime.UtcNow.AddHours(-2), 30, AppointmentType.Other);
            db.Appointments.AddRange(foreign, sameTenant);
            await db.SaveChangesAsync();
            otherTenantAppointment = foreign.Id;
            unassignedClinicAppointment = sameTenant.Id;
        }

        var cross = await ctx.PostAsync($"/api/v1/appointments/{otherTenantAppointment}/no-show");
        cross.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(cross)).Should().Be("Appointments.NotFound");

        (await ctx.PostAsync($"/api/v1/appointments/{unassignedClinicAppointment}/no-show"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ctx.PostAsync($"/api/v1/appointments/{unassignedClinicAppointment}/no-show/revert", new { reason = Reason }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Kurulum ----

    private async Task<NoShowTestContext> SeedAsync(string[]? permissions = null)
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId;
        Guid clinicId;
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"NoShow-{Guid.NewGuid():N}"[..14], "Izmir");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"noshow-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"));
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id));
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            clinicId = clinic.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services, email, permissions ?? AllPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var ctx = new NoShowTestContext(_factory.Services, http, tenantId, clinicId);
        ctx.PetId = await ctx.SeedPetAsync();
        return ctx;
    }

    private sealed class NoShowTestContext
    {
        private readonly IServiceProvider _services;

        public NoShowTestContext(IServiceProvider services, HttpClient http, Guid tenantId, Guid clinicId)
        {
            _services = services;
            Http = http;
            TenantId = tenantId;
            ClinicId = clinicId;
        }

        public HttpClient Http { get; }
        public Guid TenantId { get; }
        public Guid ClinicId { get; }
        public Guid PetId { get; set; }

        public Task<HttpResponseMessage> PostAsync(string url, object? body = null)
            => body is null ? Http.PostAsync(url, null) : Http.PostAsJsonAsync(url, body);

        public async Task<AppointmentStatus> GetStatusAsync(Guid appointmentId)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId).Select(a => a.Status).SingleAsync();
        }

        public async Task SetScheduledAtAsync(Guid appointmentId, DateTime scheduledAtUtc)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var appointment = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
            typeof(Appointment).GetProperty(nameof(Appointment.ScheduledAtUtc))!.SetValue(appointment, scheduledAtUtc);
            await db.SaveChangesAsync();
        }

        /// <summary>Doğrudan veritabanına randevu yazar (zaman/slot doğrulaması atlanır; geçmiş saat mümkün), her biri için ayrı hayvan.</summary>
        public async Task<Guid> SeedAppointmentAsync(TimeSpan fromNow, AppointmentStatus? status = null)
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var petId = await SeedPetAsync();
            var appointment = new Appointment(
                TenantId, ClinicId, petId, DateTime.UtcNow.Add(fromNow), 30, AppointmentType.Consultation, status);
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            return appointment.Id;
        }

        public async Task<Guid> SeedPetAsync()
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(TenantId, $"NClient-{Guid.NewGuid():N}"[..14], "905551110079");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(TenantId, client.Id, $"NPet-{Guid.NewGuid():N}"[..12], speciesId);
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return pet.Id;
        }
    }
}
