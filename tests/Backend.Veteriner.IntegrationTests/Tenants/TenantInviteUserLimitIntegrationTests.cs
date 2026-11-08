using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Billing;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// Paket kullanıcı sınırı (WEB-007: Basic 3, Pro 5, Premium 15): davet oluşturma üye + bekleyen daveti, kabul üye sayısını
/// sayar. Sınırı aşan üyeler kalır; paket düşürme üye + bekleyen davet ve aktif klinik sayısına bakar (ADR-035 2026-11-08, seçenek A).
/// </summary>
public sealed class TenantInviteUserLimitIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TenantInviteUserLimitIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private sealed record Seeded(Guid TenantId, Guid ClinicId, Guid ClaimId, string OwnerEmail);

    private async Task<Seeded> SeedTenantAsync(SubscriptionPlanCode plan, int memberCount)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTime.UtcNow;

        var tenant = new Tenant($"Sınır Kiracı {Guid.NewGuid():N}"[..24], now);
        var clinic = new Clinic(tenant.Id, "Sınır Klinik", "İzmir");
        db.Tenants.Add(tenant);
        db.Clinics.Add(clinic);
        db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, plan, now, 14));
        var claim = await db.OperationClaims.SingleAsync(c => c.Name == "Veteriner");

        var ownerEmail = string.Empty;
        for (var i = 0; i < memberCount; i++)
        {
            var email = $"limit-member-{Guid.NewGuid():N}@example.com";
            ownerEmail = i == 0 ? email : ownerEmail;
            var user = new User(email, hasher.Hash("123456"), now);
            db.Users.Add(user);
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, now));
        }

        await db.SaveChangesAsync();
        return new Seeded(tenant.Id, clinic.Id, claim.Id, ownerEmail);
    }

    private async Task<HttpResponseMessage> CreateInviteAsync(Seeded s, string? email = null)
    {
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, s.OwnerEmail, [PermissionCatalog.Tenants.InviteCreate]);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tenants/{s.TenantId}/invites")
        {
            Content = JsonContent.Create(new
            {
                email = email ?? $"limit-invitee-{Guid.NewGuid():N}@example.com",
                clinicId = s.ClinicId,
                operationClaimId = s.ClaimId,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<int> CountInvitesAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.TenantInvites.AsNoTracking().CountAsync(i => i.TenantId == tenantId);
    }

    [Fact]
    public async Task CreateInvite_Should_Reject_On_Basic_When_Limit_Is_Reached()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Basic, memberCount: 3);

        var response = await CreateInviteAsync(seeded);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Subscriptions.UserLimitExceeded");
        (await CountInvitesAsync(seeded.TenantId)).Should().Be(0);
    }

    [Fact]
    public async Task CreateInvite_Should_Count_Pending_Invites_Against_Pro_Limit()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Pro, memberCount: 4);

        var first = await CreateInviteAsync(seeded);
        var second = await CreateInviteAsync(seeded);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(second)).Should().Be("Subscriptions.UserLimitExceeded");
        (await CountInvitesAsync(seeded.TenantId)).Should().Be(1);
    }

    [Fact]
    public async Task CreateInvite_Should_Allow_Up_To_Basic_Limit()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Basic, memberCount: 2);

        (await CreateInviteAsync(seeded)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreateInviteAsync(seeded)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateInvite_Should_Allow_Up_To_Premium_Limit()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Premium, memberCount: 14);

        (await CreateInviteAsync(seeded)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreateInviteAsync(seeded)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SignupAndAccept_Should_Reject_When_Tenant_Is_Full_And_Keep_Invite_Pending()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Basic, memberCount: 3);
        var rawToken = $"limit-invite-{Guid.NewGuid():N}";
        var email = $"limit-invitee-{Guid.NewGuid():N}@example.com";

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenHash = scope.ServiceProvider.GetRequiredService<ITokenHashService>();
            var now = DateTime.UtcNow;
            db.TenantInvites.Add(TenantInvite.CreatePending(
                seeded.TenantId, seeded.ClinicId, email, tokenHash.ComputeSha256(rawToken), seeded.ClaimId, now.AddDays(3), now));
            await db.SaveChangesAsync();
        }

        var documents = LegalDocumentCatalog.CurrentVersions
            .Select(kv => (object)new { documentType = kv.Key.ToString(), version = kv.Value })
            .ToArray();
        var response = await _client.PostAsJsonAsync($"/api/v1/public/invites/{rawToken}/signup-and-accept",
            new { password = "Abc12345!", acceptedLegalDocuments = documents });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Subscriptions.UserLimitExceeded");

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.UserTenants.AsNoTracking().CountAsync(ut => ut.TenantId == seeded.TenantId)).Should().Be(3);
            (await db.Users.AsNoTracking().AnyAsync(u => u.Email == email)).Should().BeFalse();
            (await db.TenantInvites.AsNoTracking().SingleAsync(i => i.Email == email)).Status.Should().Be(TenantInviteStatus.Pending);
        }
    }

    [Fact]
    public async Task Members_Over_New_Limit_Should_Stay_And_Only_New_Invites_Are_Rejected()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Basic, memberCount: 5);

        var response = await CreateInviteAsync(seeded);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Subscriptions.UserLimitExceeded");

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.UserTenants.AsNoTracking().CountAsync(ut => ut.TenantId == seeded.TenantId)).Should().Be(5);
    }

    private async Task<HttpResponseMessage> ScheduleDowngradeAsync(Seeded s, string target)
    {
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, s.OwnerEmail, [PermissionCatalog.Subscriptions.Manage]);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tenants/{s.TenantId}/subscription-plan-change/downgrade")
        {
            Content = JsonContent.Create(new { targetPlanCode = target }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<int> CountPlanChangesAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ScheduledSubscriptionPlanChanges.AsNoTracking().CountAsync(c => c.TenantId == tenantId);
    }

    /// <summary>Seçenek A (ADR-035 2026-11-08): üye sayısı hedef pakete sığmıyorsa düşürme reddedilir; üyeler silinmez.</summary>
    [Fact]
    public async Task ScheduleDowngrade_Should_Reject_When_Members_Exceed_Target_Limit()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Pro, memberCount: 4);

        var response = await ScheduleDowngradeAsync(seeded, "Basic");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("Subscriptions.PlanLimitsExceeded");
        json.GetProperty("targetPlanCode").GetString().Should().Be("Basic");
        json.GetProperty("maxActiveClinics").GetInt32().Should().Be(1);
        json.GetProperty("maxUsers").GetInt32().Should().Be(3);
        json.GetProperty("activeClinicCount").GetInt32().Should().Be(1);
        json.GetProperty("memberCount").GetInt32().Should().Be(4);
        json.GetProperty("pendingInviteCount").GetInt32().Should().Be(0);
        json.GetProperty("excessClinicCount").GetInt32().Should().Be(0);
        json.GetProperty("excessUserCount").GetInt32().Should().Be(1);
        (await CountPlanChangesAsync(seeded.TenantId)).Should().Be(0);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.UserTenants.AsNoTracking().CountAsync(ut => ut.TenantId == seeded.TenantId)).Should().Be(4);
    }

    [Fact]
    public async Task ScheduleDowngrade_Should_Count_Pending_Invites_And_Excess_Clinics()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Premium, memberCount: 3);
        (await CreateInviteAsync(seeded)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Clinics.Add(new Clinic(seeded.TenantId, "İkinci Şube", "Bursa"));
            await db.SaveChangesAsync();
        }

        var response = await ScheduleDowngradeAsync(seeded, "Basic");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("activeClinicCount").GetInt32().Should().Be(2);
        json.GetProperty("pendingInviteCount").GetInt32().Should().Be(1);
        json.GetProperty("excessClinicCount").GetInt32().Should().Be(1);
        json.GetProperty("excessUserCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ScheduleDowngrade_Should_Accept_When_Usage_Fits_Target()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Pro, memberCount: 3);

        var response = await ScheduleDowngradeAsync(seeded, "Basic");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ScheduledSubscriptionPlanChanges.AsNoTracking().SingleAsync(c => c.TenantId == seeded.TenantId)).TargetPlanCode
            .Should().Be(SubscriptionPlanCode.Basic);
    }

    /// <summary>
    /// Dönem sonu yeniden kontrol (çalışma varsayımı): planlandıktan sonra sınır aşıldıysa düşürme uygulanmaz, kiracı mevcut pakette
    /// kalır, abonelik özeti nedeni bugünkü sayılarla döner.
    /// </summary>
    [Fact]
    public async Task Due_Downgrade_Should_Be_Rejected_When_Usage_Grew_After_Scheduling()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Pro, memberCount: 3);
        (await ScheduleDowngradeAsync(seeded, "Basic")).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Clinics.Add(new Clinic(seeded.TenantId, "Sonradan Şube", "Bursa"));
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ScheduledSubscriptionPlanChanges SET EffectiveAtUtc = {DateTime.UtcNow.AddMinutes(-1)} WHERE TenantId = {seeded.TenantId}");
            await db.SaveChangesAsync();
        }

        await ProcessDuePlanChangesAsync();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ScheduledSubscriptionPlanChanges.AsNoTracking().SingleAsync(c => c.TenantId == seeded.TenantId)).Status
                .Should().Be(SubscriptionPlanChangeStatus.Rejected);
            (await db.TenantSubscriptions.AsNoTracking().SingleAsync(s => s.TenantId == seeded.TenantId)).PlanCode
                .Should().Be(SubscriptionPlanCode.Pro);
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, seeded.OwnerEmail, [PermissionCatalog.Subscriptions.Read]);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tenants/{seeded.TenantId}/subscription-summary");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var summary = await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();
        summary.GetProperty("pendingPlanChange").ValueKind.Should().Be(JsonValueKind.Null);
        var rejected = summary.GetProperty("rejectedPlanChange");
        rejected.GetProperty("targetPlanCode").GetString().Should().Be("Basic");
        rejected.GetProperty("fit").GetProperty("excessClinicCount").GetInt32().Should().Be(1);
        rejected.GetProperty("fit").GetProperty("excessUserCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Due_Downgrade_Should_Apply_When_Usage_Still_Fits()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Pro, memberCount: 2);
        (await ScheduleDowngradeAsync(seeded, "Basic")).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ScheduledSubscriptionPlanChanges SET EffectiveAtUtc = {DateTime.UtcNow.AddMinutes(-1)} WHERE TenantId = {seeded.TenantId}");
        }

        await ProcessDuePlanChangesAsync();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ScheduledSubscriptionPlanChanges.AsNoTracking().SingleAsync(c => c.TenantId == seeded.TenantId)).Status
                .Should().Be(SubscriptionPlanChangeStatus.Applied);
            var sub = await db.TenantSubscriptions.AsNoTracking().SingleAsync(s => s.TenantId == seeded.TenantId);
            sub.PlanCode.Should().Be(SubscriptionPlanCode.Basic);
            sub.Status.Should().Be(TenantSubscriptionStatus.Trialing, "düşürme denemeyi ücretli döneme çevirmez");
            sub.ActivatedAtUtc.Should().BeNull();
        }
    }

    /// <summary>
    /// Otomatik yenileme yok (BILL-001): dönem sonunda düşürme yalnız paketi değiştirir. Yeni dönem ödemeyle başlar; ödeme yoksa
    /// abonelik salt okunur kalır.
    /// </summary>
    [Fact]
    public async Task Due_Downgrade_At_Paid_Period_End_Should_Not_Start_Unpaid_Period()
    {
        var seeded = await SeedTenantAsync(SubscriptionPlanCode.Pro, memberCount: 2);
        var paidAt = DateTime.UtcNow.AddMonths(-1).AddMinutes(-5);
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sub = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == seeded.TenantId);
            sub.ActivatePaidPlan(SubscriptionPlanCode.Pro, BillingPeriodKind.Monthly, paidAt);
            db.ScheduledSubscriptionPlanChanges.Add(ScheduledSubscriptionPlanChange.CreateScheduledDowngrade(
                seeded.TenantId, SubscriptionPlanCode.Pro, SubscriptionPlanCode.Basic, Guid.NewGuid(),
                paidAt.AddDays(10), sub.PaidPeriodEndUtc!.Value, reason: null));
            await db.SaveChangesAsync();
        }

        await ProcessDuePlanChangesAsync();

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ScheduledSubscriptionPlanChanges.AsNoTracking().SingleAsync(c => c.TenantId == seeded.TenantId)).Status
                .Should().Be(SubscriptionPlanChangeStatus.Applied);
            var sub = await db.TenantSubscriptions.AsNoTracking().SingleAsync(s => s.TenantId == seeded.TenantId);
            sub.PlanCode.Should().Be(SubscriptionPlanCode.Basic);
            sub.ActivatedAtUtc.Should().BeCloseTo(paidAt, TimeSpan.FromMilliseconds(10), "ödeme alınmadan yeni dönem başlamaz");
            sub.PaidPeriodEndUtc.Should().BeBefore(DateTime.UtcNow);
            TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, DateTime.UtcNow).Should().Be(TenantSubscriptionStatus.ReadOnly);
            (await db.BillingCheckoutSessions.AsNoTracking().AnyAsync(c => c.TenantId == seeded.TenantId)).Should().BeFalse();
        }
    }

    private Task ProcessDuePlanChangesAsync()
        => _factory.Services.GetServices<IHostedService>().OfType<ScheduledPlanChangeProcessorHostedService>().Single()
            .ProcessDueOnceAsync(CancellationToken.None);
}