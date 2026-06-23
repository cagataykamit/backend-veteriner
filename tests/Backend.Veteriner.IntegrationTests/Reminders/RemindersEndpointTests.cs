using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.IntegrationTests.Reminders;

[Collection("products-api")]
public sealed class RemindersEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RemindersEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetSettings_Should_Return401_When_TokenMissing()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/reminders/settings");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSettings_Should_Return403_When_MissingRemindersRead()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Permissions.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/reminders/settings");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSettings_Should_Return200_WithDefaults_When_RemindersRead_And_NoManage()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Reminders.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/reminders/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("appointmentRemindersEnabled").GetBoolean().Should().BeFalse();
        json.GetProperty("appointmentReminderHoursBefore").GetInt32().Should().Be(24);
        json.GetProperty("vaccinationRemindersEnabled").GetBoolean().Should().BeFalse();
        json.GetProperty("vaccinationReminderDaysBefore").GetInt32().Should().Be(3);
        json.GetProperty("emailChannelEnabled").GetBoolean().Should().BeTrue();
        json.GetProperty("updatedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetSettings_Should_Return200_For_ReadOnlyTenant()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Reminders.Read" }, readOnlyTenant: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/reminders/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PutSettings_Should_Return401_When_TokenMissing()
    {
        var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync("/api/v1/reminders/settings", ValidUpdateBody());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PutSettings_Should_Return403_When_MissingRemindersManage()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Reminders.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync("/api/v1/reminders/settings", ValidUpdateBody());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutSettings_Should_Return200_And_Persist_When_RemindersManage()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Reminders.Read", "Reminders.Manage" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            appointmentRemindersEnabled = true,
            appointmentReminderHoursBefore = 12,
            vaccinationRemindersEnabled = true,
            vaccinationReminderDaysBefore = 5,
            emailChannelEnabled = false
        };

        var put = await client.PutAsJsonAsync("/api/v1/reminders/settings", body);
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await put.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("appointmentRemindersEnabled").GetBoolean().Should().BeTrue();
        updated.GetProperty("appointmentReminderHoursBefore").GetInt32().Should().Be(12);
        updated.GetProperty("vaccinationRemindersEnabled").GetBoolean().Should().BeTrue();
        updated.GetProperty("vaccinationReminderDaysBefore").GetInt32().Should().Be(5);
        updated.GetProperty("emailChannelEnabled").GetBoolean().Should().BeFalse();
        updated.GetProperty("updatedAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var get = await client.GetAsync("/api/v1/reminders/settings");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var read = await get.Content.ReadFromJsonAsync<JsonElement>();
        read.GetProperty("appointmentReminderHoursBefore").GetInt32().Should().Be(12);
        read.GetProperty("vaccinationReminderDaysBefore").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task PutSettings_Should_Return400_ValidationFluentValidation_When_AppointmentHoursBeforeTooLow()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Reminders.Manage" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            appointmentRemindersEnabled = false,
            appointmentReminderHoursBefore = 0,
            vaccinationRemindersEnabled = false,
            vaccinationReminderDaysBefore = 3,
            emailChannelEnabled = true
        };
        var response = await client.PutAsJsonAsync("/api/v1/reminders/settings", body);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        ReadCodeFromProblemJson(doc).Should().Be("Validation.FluentValidation");
        doc.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task PutSettings_Should_Return400_ValidationFluentValidation_When_VaccinationDaysBeforeTooLow()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Reminders.Manage" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            appointmentRemindersEnabled = false,
            appointmentReminderHoursBefore = 24,
            vaccinationRemindersEnabled = false,
            vaccinationReminderDaysBefore = 0,
            emailChannelEnabled = true
        };
        var response = await client.PutAsJsonAsync("/api/v1/reminders/settings", body);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        ReadCodeFromProblemJson(doc).Should().Be("Validation.FluentValidation");
    }

    [Fact]
    public async Task PutSettings_Should_Return403_SubscriptionsTenantReadOnly_For_ReadOnlyTenant()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Reminders.Manage" }, readOnlyTenant: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync("/api/v1/reminders/settings", ValidUpdateBody());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(response)).Should().Be("Subscriptions.TenantReadOnly");
    }

    [Fact]
    public async Task GetLogs_Should_Return401_When_TokenMissing()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/reminders/logs?page=1&pageSize=20");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetLogs_Should_Return403_When_MissingRemindersRead()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Permissions.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/reminders/logs?page=1&pageSize=20");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetLogs_Should_Return200_Paged_When_RemindersRead()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantWithTenantWideRemindersReaderAsync();
        await SeedReminderLogsAsync(tenantId, clinicId: null, ReminderType.Appointment, ReminderDispatchStatus.Sent);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/v1/reminders/logs?page=1&pageSize=20");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("pageSize").GetInt32().Should().Be(20);
        json.GetProperty("totalItems").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        json.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetLogs_Should_Filter_By_ReminderType()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantWithTenantWideRemindersReaderAsync();
        await SeedReminderLogsAsync(tenantId, null, ReminderType.Appointment, ReminderDispatchStatus.Enqueued);
        await SeedReminderLogsAsync(tenantId, null, ReminderType.Vaccination, ReminderDispatchStatus.Enqueued);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/v1/reminders/logs?page=1&pageSize=50&reminderType=Vaccination");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalItems").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        json.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        foreach (var item in json.GetProperty("items").EnumerateArray())
            item.GetProperty("reminderType").GetInt32().Should().Be((int)ReminderType.Vaccination);
    }

    [Fact]
    public async Task GetLogs_Should_Filter_By_Status()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantWithTenantWideRemindersReaderAsync();
        await SeedReminderLogsAsync(tenantId, null, ReminderType.Appointment, ReminderDispatchStatus.Failed);
        await SeedReminderLogsAsync(tenantId, null, ReminderType.Appointment, ReminderDispatchStatus.Sent);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/v1/reminders/logs?page=1&pageSize=50&status=Failed");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalItems").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        json.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        foreach (var item in json.GetProperty("items").EnumerateArray())
            item.GetProperty("status").GetInt32().Should().Be((int)ReminderDispatchStatus.Failed);
    }

    [Fact]
    public async Task GetLogs_Should_Include_RecentLogs_When_DateRange_Wide()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantWithTenantWideRemindersReaderAsync();
        await SeedReminderLogsAsync(tenantId, null, ReminderType.Appointment, ReminderDispatchStatus.Skipped);

        var from = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-7).ToString("O"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("O"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync($"/api/v1/reminders/logs?page=1&pageSize=20&fromUtc={from}&toUtc={to}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalItems").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetLogs_Should_ReturnEmpty_When_DateRange_Excludes_Now()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantWithTenantWideRemindersReaderAsync();
        await SeedReminderLogsAsync(tenantId, null, ReminderType.Appointment, ReminderDispatchStatus.Pending);

        var from = Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("O"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddDays(2).ToString("O"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync($"/api/v1/reminders/logs?page=1&pageSize=20&fromUtc={from}&toUtc={to}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalItems").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetLogs_Should_Return400_ValidationFluentValidation_When_FromUtc_After_ToUtc()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Reminders.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var from = Uri.EscapeDataString(DateTime.UtcNow.ToString("O"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("O"));
        var response = await client.GetAsync($"/api/v1/reminders/logs?page=1&pageSize=20&fromUtc={from}&toUtc={to}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("Validation.FluentValidation");
    }

    [Fact]
    public async Task GetLogs_Should_Return404_ClinicsNotFound_When_ClinicId_Unknown_For_TenantWideAdmin()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantWithTenantWideRemindersReaderAsync();
        await SeedReminderLogsAsync(tenantId, null, ReminderType.Appointment, ReminderDispatchStatus.Enqueued);

        var foreignClinicId = Guid.NewGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync($"/api/v1/reminders/logs?page=1&pageSize=20&clinicId={foreignClinicId:D}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemCodeAsync(response)).Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task GetLogs_Should_Return403_ClinicsAccessDenied_When_ScopedReader_Without_UserClinic_Requests_ClinicId()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantWithScopedRemindersReaderWithoutClinicAssignmentAsync();
        await SeedReminderLogsAsync(tenantId, null, ReminderType.Appointment, ReminderDispatchStatus.Enqueued);

        var foreignClinicId = Guid.NewGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync($"/api/v1/reminders/logs?page=1&pageSize=20&clinicId={foreignClinicId:D}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(response)).Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task GetLogs_Should_Return403_ClinicsAccessDenied_When_ClinicAdmin_Requests_Unassigned_Clinic()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedClinicAdminTenantAsync();
        var clinicBId = ctx.ClinicBId;
        await SeedReminderLogsAsync(ctx.TenantId, clinicBId, ReminderType.Appointment, ReminderDispatchStatus.Enqueued);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var response = await client.GetAsync($"/api/v1/reminders/logs?page=1&pageSize=20&clinicId={clinicBId:D}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(response)).Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task GetLogs_Should_NotExpose_OtherTenant_Logs()
    {
        var client = _factory.CreateClient();
        var (tenantA, _) = await SeedTenantWithTenantWideRemindersReaderAsync();
        var logId = await SeedReminderLogsAsync(tenantA, null, ReminderType.Vaccination, ReminderDispatchStatus.Enqueued);

        var (_, tokenB) = await SeedTenantWithTenantWideRemindersReaderAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var response = await client.GetAsync("/api/v1/reminders/logs?page=1&pageSize=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in json.GetProperty("items").EnumerateArray())
            item.GetProperty("id").GetGuid().Should().NotBe(logId);
    }

    [Fact]
    public async Task GetLogs_ClinicAdmin_Should_Only_See_Assigned_Clinic_Logs_Without_ClinicId_Filter()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedClinicAdminTenantAsync();
        await SeedReminderLogsAsync(ctx.TenantId, ctx.ClinicAId, ReminderType.Appointment, ReminderDispatchStatus.Enqueued);
        await SeedReminderLogsAsync(ctx.TenantId, ctx.ClinicBId, ReminderType.Appointment, ReminderDispatchStatus.Enqueued);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var response = await client.GetAsync("/api/v1/reminders/logs?page=1&pageSize=50");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalItems").GetInt32().Should().Be(1);
        var item = json.GetProperty("items")[0];
        item.GetProperty("clinicId").GetGuid().Should().Be(ctx.ClinicAId);
    }

    [Fact]
    public async Task GetLogs_TenantWideAdmin_Should_See_All_Tenant_Logs_Without_ClinicId_Filter()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantWithTenantWideRemindersReaderAsync();
        Guid c1;
        Guid c2;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic1 = new Clinic(tenantId, "L1", "Istanbul");
            var clinic2 = new Clinic(tenantId, "L2", "Ankara");
            db.Clinics.AddRange(clinic1, clinic2);
            await db.SaveChangesAsync();
            c1 = clinic1.Id;
            c2 = clinic2.Id;
        }

        await SeedReminderLogsAsync(tenantId, c1, ReminderType.Appointment, ReminderDispatchStatus.Enqueued);
        await SeedReminderLogsAsync(tenantId, c2, ReminderType.Appointment, ReminderDispatchStatus.Enqueued);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/v1/reminders/logs?page=1&pageSize=50");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalItems").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetLogs_ScopedReader_Without_UserClinic_Should_ReturnEmpty_Without_ClinicId_Filter()
    {
        var client = _factory.CreateClient();
        var (tenantId, token) = await SeedTenantWithScopedRemindersReaderWithoutClinicAssignmentAsync();
        Guid c1;
        Guid c2;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic1 = new Clinic(tenantId, "L1", "Istanbul");
            var clinic2 = new Clinic(tenantId, "L2", "Ankara");
            db.Clinics.AddRange(clinic1, clinic2);
            await db.SaveChangesAsync();
            c1 = clinic1.Id;
            c2 = clinic2.Id;
        }

        await SeedReminderLogsAsync(tenantId, c1, ReminderType.Appointment, ReminderDispatchStatus.Enqueued);
        await SeedReminderLogsAsync(tenantId, c2, ReminderType.Appointment, ReminderDispatchStatus.Enqueued);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/v1/reminders/logs?page=1&pageSize=50");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalItems").GetInt32().Should().Be(0);
        json.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    private static object ValidUpdateBody() => new
    {
        appointmentRemindersEnabled = false,
        appointmentReminderHoursBefore = 24,
        vaccinationRemindersEnabled = false,
        vaccinationReminderDaysBefore = 3,
        emailChannelEnabled = true
    };

    private async Task<(Guid TenantId, string Token)> SeedTenantWithTenantWideRemindersReaderAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedTenantWideAdminAndIssueTokenAsync(
            db,
            jwt,
            hasher,
            tenant.Id,
            [PermissionCatalog.Reminders.Read]);

        return (tenant.Id, token);
    }

    private async Task<(Guid TenantId, string Token)> SeedTenantWithScopedRemindersReaderWithoutClinicAssignmentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
        await db.SaveChangesAsync();

        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);
        var token = await IntegrationTestAuthHelper.SeedRemindersReaderWithoutClinicAssignmentAndIssueTokenAsync(
            db,
            jwt,
            hasher,
            tenant.Id);

        return (tenant.Id, token);
    }

    private async Task<(Guid TenantId, Guid UserId, string AccessToken)> SeedTenantAndIssueTokenAsync(
        IReadOnlyCollection<string> permissions,
        bool readOnlyTenant = false,
        Guid? jwtUserId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);

        var now = DateTime.UtcNow;
        var subscription = readOnlyTenant
            ? TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now.AddDays(-40), 7)
            : TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14);
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var userId = jwtUserId ?? Guid.NewGuid();
        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();

        var (accessToken, _, _) = jwt.Create(userId, $"it-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return (tenant.Id, userId, accessToken);
    }

    private async Task<Guid> SeedReminderLogsAsync(
        Guid tenantId,
        Guid? clinicId,
        ReminderType reminderType,
        ReminderDispatchStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = new ReminderDispatchLog(
            tenantId,
            clinicId,
            reminderType,
            ReminderSourceEntityType.Appointment,
            Guid.NewGuid(),
            $"r-{Guid.NewGuid():N}@example.com",
            "Test Recipient",
            DateTime.UtcNow,
            DateTime.UtcNow,
            status,
            $"dedupe-it-{Guid.NewGuid():N}");
        db.ReminderDispatchLogs.Add(log);
        await db.SaveChangesAsync();
        return log.Id;
    }

    private sealed record ClinicAdminSeedContext(
        Guid TenantId,
        Guid UserId,
        Guid ClinicAId,
        Guid ClinicBId,
        string Token);

    private async Task<ClinicAdminSeedContext> SeedClinicAdminTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..18]);
        db.Tenants.Add(tenant);
        var now = DateTime.UtcNow;
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 14));

        var user = new User($"clinic-admin-{Guid.NewGuid():N}@example.com", hasher.Hash("P@ssw0rd!"));
        db.Users.Add(user);

        var clinicAdminClaim = await db.OperationClaims.AsNoTracking()
            .FirstAsync(c => c.Name == "ClinicAdmin");
        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, clinicAdminClaim.Id));

        var clinicA = new Clinic(tenant.Id, "CA", "Istanbul");
        var clinicB = new Clinic(tenant.Id, "CB", "Ankara");
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        db.UserClinics.Add(new UserClinic(user.Id, clinicA.Id));
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("permission", "Reminders.Read"),
            new(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (accessToken, _, _) = jwt.Create(user.Id, user.Email, Array.Empty<string>(), claims);

        return new ClinicAdminSeedContext(tenant.Id, user.Id, clinicA.Id, clinicB.Id, accessToken);
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return ReadCodeFromProblemJson(json);
    }

    private static string? ReadCodeFromProblemJson(JsonElement json)
    {
        if (json.TryGetProperty("code", out var code))
            return code.GetString();
        if (json.TryGetProperty("extensions", out var ext)
            && ext.TryGetProperty("code", out var extCode))
            return extCode.GetString();
        return null;
    }
}
