using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Veteriner.IntegrationTests.Reports;

[Collection("products-api")]
public sealed class ReportsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ReportsEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static readonly DateTime TrMay12FromUtc = new(2026, 5, 11, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TrMay12ToUtc = new(2026, 5, 12, 20, 59, 59, 999, DateTimeKind.Utc);

    [Fact]
    public async Task GetPayments_Should_Return401_When_TokenMissing()
    {
        var client = _factory.CreateClient();
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc);
        var response = await client.GetAsync($"/api/v1/reports/payments{Query(from, to)}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPayments_Should_Return403_When_MissingPaymentsRead()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Permissions.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc);
        var response = await client.GetAsync($"/api/v1/reports/payments{Query(from, to)}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPayments_Should_Return200_And_Filter_By_Utc_Window_For_Turkey_May12()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedTenantWithPaymentsForTrMay12WindowAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);

        var q = Query(TrMay12FromUtc, TrMay12ToUtc);
        var response = await client.GetAsync($"/api/v1/reports/payments{q}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalCount").GetInt32().Should().Be(1);
        var items = json.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("paymentId").GetGuid().Should().Be(ctx.InsidePaymentId);
    }

    [Fact]
    public async Task GetPayments_Should_NotExpose_OtherTenant_Payments()
    {
        var client = _factory.CreateClient();
        var a = await SeedTenantWithSinglePaymentAsync(new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc));
        var b = await SeedTenantWithSinglePaymentAsync(new DateTime(2026, 4, 1, 14, 0, 0, DateTimeKind.Utc));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", b.Token);
        var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc);
        var response = await client.GetAsync($"/api/v1/reports/payments{Query(from, to)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in json.GetProperty("items").EnumerateArray())
            item.GetProperty("paymentId").GetGuid().Should().NotBe(a.PaymentId);
    }

    [Fact]
    public async Task GetPayments_Should_Return404_ClinicsNotFound_When_ClinicId_Unknown()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedTenantWithSinglePaymentAsync(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);
        var foreign = Guid.NewGuid();
        var response = await client.GetAsync($"/api/v1/reports/payments{Query(from, to)}&clinicId={foreign:D}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemCodeAsync(response)).Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task GetPayments_Should_Return400_PaymentsReportDateRangeInvalid_When_FromAfterTo()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Payments.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync(
            "/api/v1/reports/payments" + Query(new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("Payments.ReportDateRangeInvalid");
    }

    [Fact]
    public async Task GetPayments_Should_Return400_PaymentsReportRangeTooLong_When_SpanExceedsMax()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Payments.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var response = await client.GetAsync($"/api/v1/reports/payments{Query(from, to)}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("Payments.ReportRangeTooLong");
    }

    [Fact]
    public async Task GetPayments_Should_Return403_ContextClinicConflict_When_JwtClinicDiffersFromQuery()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedTenantTwoClinicsAsync();
        var claims = new List<Claim>
        {
            new("permission", "Payments.Read"),
            new(VeterinerClaims.TenantId, ctx.TenantId.ToString("D")),
            new(VeterinerClaims.ClinicId, ctx.ClinicAId.ToString("D"))
        };
        var token = await IssueTokenWithClaimsAsync(ctx.UserId, claims);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var from = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 2, 28, 23, 59, 59, DateTimeKind.Utc);
        var response = await client.GetAsync(
            $"/api/v1/reports/payments{Query(from, to)}&clinicId={ctx.ClinicBId:D}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(response)).Should().Be("Context.ClinicConflict");
    }

    [Fact]
    public async Task GetPayments_Should_Return403_ClinicsAccessDenied_When_ClinicAdmin_Requests_Unassigned_Clinic()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedClinicAdminWithTwoClinicsAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc);
        var response = await client.GetAsync(
            $"/api/v1/reports/payments{Query(from, to)}&clinicId={ctx.ClinicBId:D}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(response)).Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task GetPaymentsExport_Should_Return200_CsvUtf8Bom_And_ReadOnlyTenant()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedTenantWithSinglePaymentAsync(new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc), readOnlyTenant: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc);
        var response = await client.GetAsync($"/api/v1/reports/payments/export{Query(from, to)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().StartWith("text/csv");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(3);
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPaymentsExportXlsx_Should_Return200_For_ReadOnlyTenant()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedTenantWithSinglePaymentAsync(new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc), readOnlyTenant: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var from = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc);
        var response = await client.GetAsync($"/api/v1/reports/payments/export-xlsx{Query(from, to)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(4);
        Encoding.ASCII.GetString(bytes.AsSpan(0, 4)).Should().Be("PK\u0003\u0004");
    }

    [Fact]
    public async Task GetAppointments_Should_Return401_403_200_And_StatusFilter()
    {
        var client = _factory.CreateClient();
        var q = Query(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc));
        (await client.GetAsync($"/api/v1/reports/appointments{q}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var (_, _, noPerm) = await SeedTenantAndIssueTokenAsync(new[] { "Payments.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", noPerm);
        (await client.GetAsync($"/api/v1/reports/appointments{q}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ctx = await SeedTenantWithAppointmentsAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var ok = await client.GetAsync($"/api/v1/reports/appointments{q}&status=0");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ok.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetAppointmentsExport_Should_Return200_CsvWithBom()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedTenantWithAppointmentsAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var from = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc);
        var response = await client.GetAsync($"/api/v1/reports/appointments/export{Query(from, to)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);
    }

    [Fact]
    public async Task GetAppointmentsExportXlsx_Should_Return200()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedTenantWithAppointmentsAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var from = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc);
        var response = await client.GetAsync($"/api/v1/reports/appointments/export-xlsx{Query(from, to)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    [Fact]
    public async Task GetExaminations_Should_Return401_403_200_And_Search()
    {
        var client = _factory.CreateClient();
        var q = Query(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        (await client.GetAsync($"/api/v1/reports/examinations{q}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var (_, _, noPerm) = await SeedTenantAndIssueTokenAsync(new[] { "Payments.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", noPerm);
        (await client.GetAsync($"/api/v1/reports/examinations{q}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ctx = await SeedTenantWithExaminationAsync("UniqueExamSearchToken123");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var ok = await client.GetAsync($"/api/v1/reports/examinations{q}&search=UniqueExamSearchToken123");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ok.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetExaminationsExport_And_Xlsx_Should_Return200()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedTenantWithExaminationAsync("X");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var from = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var csv = await client.GetAsync($"/api/v1/reports/examinations/export{Query(from, to)}");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        (await csv.Content.ReadAsByteArrayAsync()).Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });
        var xlsx = await client.GetAsync($"/api/v1/reports/examinations/export-xlsx{Query(from, to)}");
        xlsx.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetVaccinations_Should_Return200_And_Filter_Applied_By_AppliedAtUtc()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedTenantWithVaccinationsAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var ok = await client.GetAsync(
            $"/api/v1/reports/vaccinations{Query(TrMay12FromUtc, TrMay12ToUtc)}&status=Applied");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ok.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalCount").GetInt32().Should().Be(1);
        json.GetProperty("items")[0].GetProperty("vaccinationId").GetGuid().Should().Be(ctx.AppliedInsideId);
    }

    [Fact]
    public async Task GetVaccinations_Should_Return200_For_Scheduled_On_DueAxis()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedTenantWithVaccinationsAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var ok = await client.GetAsync(
            $"/api/v1/reports/vaccinations{Query(TrMay12FromUtc, TrMay12ToUtc)}&status=Scheduled");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ok.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalCount").GetInt32().Should().Be(1);
        json.GetProperty("items")[0].GetProperty("vaccinationId").GetGuid().Should().Be(ctx.ScheduledInsideId);
    }

    [Fact]
    public async Task GetVaccinationsExport_And_Xlsx_Should_Return200()
    {
        var client = _factory.CreateClient();
        var ctx = await SeedTenantWithVaccinationsAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Token);
        var csv = await client.GetAsync($"/api/v1/reports/vaccinations/export{Query(TrMay12FromUtc, TrMay12ToUtc)}");
        csv.StatusCode.Should().Be(HttpStatusCode.OK);
        (await csv.Content.ReadAsByteArrayAsync()).Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });
        var xlsx = await client.GetAsync($"/api/v1/reports/vaccinations/export-xlsx{Query(TrMay12FromUtc, TrMay12ToUtc)}");
        xlsx.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAppointments_Should_Return400_AppointmentsReportDateRangeInvalid_When_FromAfterTo()
    {
        var client = _factory.CreateClient();
        var (_, _, token) = await SeedTenantAndIssueTokenAsync(new[] { "Appointments.Read" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync(
            "/api/v1/reports/appointments" + Query(new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be("Appointments.ReportDateRangeInvalid");
    }

    private static string Query(DateTime fromUtc, DateTime toUtc)
        => $"?from={Uri.EscapeDataString(fromUtc.ToString("o"))}&to={Uri.EscapeDataString(toUtc.ToString("o"))}";

    private async Task<(Guid TenantId, Guid UserId, string Token)> SeedTenantAndIssueTokenAsync(
        IReadOnlyCollection<string> permissions,
        bool readOnlyTenant = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Rep-{Guid.NewGuid():N}"[..16]);
        db.Tenants.Add(tenant);
        var now = DateTime.UtcNow;
        var subscription = readOnlyTenant
            ? TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now.AddDays(-40), 7)
            : TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 400);
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var claims = permissions
            .Select(p => new Claim("permission", p))
            .Append(new Claim(VeterinerClaims.TenantId, tenant.Id.ToString("D")))
            .ToList();

        var (accessToken, _, _) = jwt.Create(userId, $"rep-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return (tenant.Id, userId, accessToken);
    }

    private async Task<string> IssueTokenWithClaimsAsync(Guid userId, List<Claim> claims)
    {
        using var scope = _factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (accessToken, _, _) = jwt.Create(userId, $"rep-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return accessToken;
    }

    private sealed record PaymentWindowSeed(
        Guid TenantId,
        string Token,
        Guid InsidePaymentId,
        Guid OutsideEarlyPaymentId,
        Guid OutsideLatePaymentId);

    private async Task<PaymentWindowSeed> SeedTenantWithPaymentsForTrMay12WindowAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Rep-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var client = new Client(tenant.Id, "Ali", "905551112233", "ali@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, "Pamuk", speciesId);

        var inside = new Payment(tenant.Id, clinic.Id, client.Id, pet.Id, null, null, 10m, "TRY", PaymentMethod.Cash,
            new DateTime(2026, 5, 12, 15, 0, 0, DateTimeKind.Utc), "in");
        var early = new Payment(tenant.Id, clinic.Id, client.Id, pet.Id, null, null, 11m, "TRY", PaymentMethod.Cash,
            new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc), "early");
        var late = new Payment(tenant.Id, clinic.Id, client.Id, pet.Id, null, null, 12m, "TRY", PaymentMethod.Cash,
            new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc), "late");

        db.AddRange(tenant, clinic, client, pet, inside, early, late);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("permission", "Payments.Read"),
            new(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(Guid.NewGuid(), $"rep-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return new PaymentWindowSeed(tenant.Id, token, inside.Id, early.Id, late.Id);
    }

    private sealed record SinglePaymentSeed(Guid TenantId, string Token, Guid PaymentId);

    private async Task<SinglePaymentSeed> SeedTenantWithSinglePaymentAsync(DateTime paidAtUtc, bool readOnlyTenant = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Rep-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var client = new Client(tenant.Id, "Veli", "905551112244", "veli@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, "Kedi", speciesId);
        var payment = new Payment(tenant.Id, clinic.Id, client.Id, pet.Id, null, null, 20m, "TRY", PaymentMethod.Cash, paidAtUtc, null);

        db.AddRange(tenant, clinic, client, pet, payment);
        var now = DateTime.UtcNow;
        db.TenantSubscriptions.Add(
            readOnlyTenant
                ? TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now.AddDays(-40), 7)
                : TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, now, 400));
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("permission", "Payments.Read"),
            new(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(Guid.NewGuid(), $"rep-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return new SinglePaymentSeed(tenant.Id, token, payment.Id);
    }

    private sealed record TwoClinicSeed(Guid TenantId, Guid UserId, Guid ClinicAId, Guid ClinicBId);

    private async Task<TwoClinicSeed> SeedTenantTwoClinicsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"Rep-{Guid.NewGuid():N}"[..16]);
        var ca = new Clinic(tenant.Id, "CA", "Istanbul");
        var cb = new Clinic(tenant.Id, "CB", "Ankara");
        var user = new User($"u-{Guid.NewGuid():N}@example.com", hasher.Hash("x"));
        db.AddRange(tenant, ca, cb, user);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        return new TwoClinicSeed(tenant.Id, user.Id, ca.Id, cb.Id);
    }

    private sealed record ClinicAdminPaymentsCtx(Guid TenantId, Guid ClinicAId, Guid ClinicBId, string Token);

    private async Task<ClinicAdminPaymentsCtx> SeedClinicAdminWithTwoClinicsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var tenant = new Tenant($"Rep-{Guid.NewGuid():N}"[..16]);
        var ca = new Clinic(tenant.Id, "CA", "Istanbul");
        var cb = new Clinic(tenant.Id, "CB", "Ankara");
        var user = new User($"ca-{Guid.NewGuid():N}@example.com", hasher.Hash("x"));
        db.AddRange(tenant, ca, cb, user);
        var clinicAdminClaim = await db.OperationClaims.AsNoTracking().FirstAsync(c => c.Name == "ClinicAdmin");
        db.UserOperationClaims.Add(new UserOperationClaim(user.Id, clinicAdminClaim.Id));
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();
        db.UserClinics.Add(new UserClinic(user.Id, ca.Id));
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("permission", "Payments.Read"),
            new(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(user.Id, user.Email, Array.Empty<string>(), claims);
        return new ClinicAdminPaymentsCtx(tenant.Id, ca.Id, cb.Id, token);
    }

    private sealed record AppointmentSeed(string Token);

    private async Task<AppointmentSeed> SeedTenantWithAppointmentsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Rep-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var client = new Client(tenant.Id, "Müşteri", "905551112255", "m@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, "Pet1", speciesId);
        var inWindow = new Appointment(tenant.Id, clinic.Id, pet.Id, new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc), 30,
            AppointmentType.Checkup, AppointmentStatus.Scheduled);
        var outWindow = new Appointment(tenant.Id, clinic.Id, pet.Id, new DateTime(2027, 1, 1, 10, 0, 0, DateTimeKind.Utc), 30,
            AppointmentType.Checkup, AppointmentStatus.Scheduled);

        db.AddRange(tenant, clinic, client, pet, inWindow, outWindow);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("permission", "Appointments.Read"),
            new(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(Guid.NewGuid(), $"rep-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return new AppointmentSeed(token);
    }

    private sealed record ExaminationSeed(string Token);

    private async Task<ExaminationSeed> SeedTenantWithExaminationAsync(string visitReasonToken)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Rep-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var client = new Client(tenant.Id, "Müşteri", "905551112266", "e@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, "Pet2", speciesId);
        var exam = new Examination(
            tenant.Id,
            clinic.Id,
            pet.Id,
            null,
            new DateTime(2026, 12, 10, 11, 0, 0, DateTimeKind.Utc),
            visitReasonToken,
            "Bulgu",
            null,
            null);

        db.AddRange(tenant, clinic, client, pet, exam);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("permission", "Examinations.Read"),
            new(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(Guid.NewGuid(), $"rep-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return new ExaminationSeed(token);
    }

    private sealed record VaccinationSeed(string Token, Guid AppliedInsideId, Guid ScheduledInsideId);

    private async Task<VaccinationSeed> SeedTenantWithVaccinationsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var tenant = new Tenant($"Rep-{Guid.NewGuid():N}"[..16]);
        var clinic = new Clinic(tenant.Id, "K1", "Istanbul");
        var client = new Client(tenant.Id, "Müşteri", "905551112277", "v@example.com");
        var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, "Pet3", speciesId);

        var defs = await db.VaccineDefinitions.AsNoTracking()
            .Where(v => v.TenantId == null && (v.Code == "RABIES" || v.Code == "MIXED" || v.Code == "LYME"))
            .ToDictionaryAsync(v => v.Code, v => new { v.Id, v.Name });

        var appliedInside = new Vaccination(
            tenant.Id,
            pet.Id,
            clinic.Id,
            null,
            defs["RABIES"].Id,
            defs["RABIES"].Name,
            VaccinationStatus.Applied,
            new DateTime(2026, 5, 12, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            null);

        var scheduledInside = new Vaccination(
            tenant.Id,
            pet.Id,
            clinic.Id,
            null,
            defs["MIXED"].Id,
            defs["MIXED"].Name,
            VaccinationStatus.Scheduled,
            null,
            new DateTime(2026, 5, 12, 9, 0, 0, DateTimeKind.Utc),
            null);

        var appliedOutside = new Vaccination(
            tenant.Id,
            pet.Id,
            clinic.Id,
            null,
            defs["LYME"].Id,
            defs["LYME"].Name,
            VaccinationStatus.Applied,
            new DateTime(2026, 5, 13, 14, 0, 0, DateTimeKind.Utc),
            null,
            null);

        db.AddRange(tenant, clinic, client, pet, appliedInside, scheduledInside, appliedOutside);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 400));
        await db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new("permission", "Vaccinations.Read"),
            new(VeterinerClaims.TenantId, tenant.Id.ToString("D"))
        };
        var (token, _, _) = jwt.Create(Guid.NewGuid(), $"rep-{Guid.NewGuid():N}@example.com", Array.Empty<string>(), claims);
        return new VaccinationSeed(token, appliedInside.Id, scheduledInside.Id);
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (json.TryGetProperty("code", out var code))
            return code.GetString();
        if (json.TryGetProperty("extensions", out var ext)
            && ext.TryGetProperty("code", out var extCode))
            return extCode.GetString();
        return null;
    }
}
