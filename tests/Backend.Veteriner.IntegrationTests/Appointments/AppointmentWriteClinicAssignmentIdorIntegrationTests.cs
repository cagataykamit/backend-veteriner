using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Appointments;

/// <summary>Appointment write clinic assignment IDOR (IDOR-7A).</summary>
[Collection("pilot-smoke-api")]
public sealed class AppointmentWriteClinicAssignmentIdorIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AppointmentWriteClinicAssignmentIdorIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static readonly string[] AppointmentWritePermissions =
    [
        PermissionCatalog.Appointments.Create,
        PermissionCatalog.Appointments.Cancel,
        PermissionCatalog.Appointments.Complete,
        PermissionCatalog.Appointments.Reschedule,
    ];

    private async Task<string> IssueAppointmentWriteTokenAsync(string email)
        => await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            email,
            AppointmentWritePermissions);

    private static DateTime SlotAlignedUtcPlusDays(int days)
    {
        var date = DateTime.UtcNow.Date.AddDays(days);
        if (days >= 0)
        {
            while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                date = date.AddDays(1);
        }

        return date.AddHours(9);
    }

    [Fact]
    public async Task Create_Should_Return403_When_NonTenantWideUserCreatesInUnassignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, assignedClinicId, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedAppointmentWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, assignedClinicId);

        var token = await IssueAppointmentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new
        {
            ClinicId = unassignedClinicId,
            PetId = petId,
            ScheduledAtUtc = SlotAlignedUtcPlusDays(3),
            AppointmentType = AppointmentType.Consultation,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Create_Should_Return201_When_NonTenantWideUserCreatesInAssignedClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, assignedClinicId, _) =
            await IntegrationTestAuthHelper.SeedAppointmentWriterUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, assignedClinicId);

        var token = await IssueAppointmentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new
        {
            ClinicId = assignedClinicId,
            PetId = petId,
            ScheduledAtUtc = SlotAlignedUtcPlusDays(3),
            AppointmentType = AppointmentType.Consultation,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Lifecycle_Should_Return403_And_NotMutate_When_UnassignedClinicAppointment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, unassignedClinicId) =
            await IntegrationTestAuthHelper.SeedAppointmentWriterUserAsync(_factory.Services, hasher);
        var appointmentId = await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(
            _factory.Services,
            unassignedClinicId);

        var token = await IssueAppointmentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var petId = await GetAppointmentPetIdAsync(appointmentId);
        var scheduledAt = await GetAppointmentScheduledAtAsync(appointmentId);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/appointments/{appointmentId}", new
        {
            ClinicId = unassignedClinicId,
            PetId = petId,
            ScheduledAtUtc = scheduledAt,
            AppointmentType = AppointmentType.Vaccination,
            Status = AppointmentStatus.Scheduled,
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(updateResponse)).Should().Be("Clinics.AccessDenied");

        var cancelResponse = await client.PostAsJsonAsync($"/api/v1/appointments/{appointmentId}/cancel", new { Reason = "x" });
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var completeResponse = await client.PostAsync($"/api/v1/appointments/{appointmentId}/complete", null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var rescheduleResponse = await client.PostAsJsonAsync(
            $"/api/v1/appointments/{appointmentId}/reschedule",
            new { ScheduledAtUtc = SlotAlignedUtcPlusDays(5) });
        rescheduleResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        row.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task Create_Should_Return201_When_TenantAdminCreatesInOtherClinic()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, password, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, extraClinicId);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            ScheduledAtUtc = SlotAlignedUtcPlusDays(4),
            AppointmentType = AppointmentType.Consultation,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Cancel_Should_Return404_When_ForeignTenantAppointment()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (email, _, _, _) =
            await IntegrationTestAuthHelper.SeedAppointmentWriterUserAsync(_factory.Services, hasher);
        var foreignClinicId = await IntegrationTestAuthHelper.SeedClinicInForeignTenantAsync(_factory.Services);
        var foreignAppointmentId = await IntegrationTestAuthHelper.SeedAppointmentInClinicAsync(
            _factory.Services,
            foreignClinicId);

        var token = await IssueAppointmentWriteTokenAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync($"/api/v1/appointments/{foreignAppointmentId}/cancel", new { Reason = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Appointments.NotFound");
    }

    [Fact]
    public async Task Create_Should_Return403_When_UserHasNoAppointmentCreatePermission()
    {
        var client = _factory.CreateClient();
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        var (_, _, extraClinicId) =
            await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);
        var petId = await IntegrationTestAuthHelper.SeedPetInClinicAsync(_factory.Services, extraClinicId);

        var (plainEmail, plainPassword) =
            await IntegrationTestAuthHelper.SeedPlainTenantMemberAsync(_factory.Services, hasher);

        var login = await IntegrationTestAuthHelper.LoginAsync(client, _factory.Services, plainEmail, plainPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new
        {
            ClinicId = extraClinicId,
            PetId = petId,
            ScheduledAtUtc = SlotAlignedUtcPlusDays(3),
            AppointmentType = AppointmentType.Consultation,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> GetAppointmentPetIdAsync(Guid appointmentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId)
            .Select(a => a.PetId)
            .SingleAsync();
    }

    private async Task<DateTime> GetAppointmentScheduledAtAsync(Guid appointmentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId)
            .Select(a => a.ScheduledAtUtc)
            .SingleAsync();
    }
}
