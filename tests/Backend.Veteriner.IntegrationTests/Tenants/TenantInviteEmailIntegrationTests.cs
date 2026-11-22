using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Tenants;

/// <summary>Personel daveti oluşturma ve "Yeniden gönder" davetliye katılım bağlantısını e-postayla (outbox) gönderir.</summary>
public sealed class TenantInviteEmailIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TenantInviteEmailIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_And_Resend_Should_Email_Join_Link_To_Invitee()
    {
        Guid tenantId, clinicId, claimId;
        var ownerEmail = $"invite-owner-{Guid.NewGuid():N}@example.com";
        var inviteeEmail = $"invite-target-{Guid.NewGuid():N}@example.com";
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var now = DateTime.UtcNow;
            var tenant = new Tenant($"Davet Kiracı {Guid.NewGuid():N}"[..24], now);
            var clinic = new Clinic(tenant.Id, "Davet Klinik", "İzmir");
            var owner = new User(ownerEmail, hasher.Hash("123456"), now);
            db.AddRange(tenant, clinic, owner);
            db.UserTenants.Add(new UserTenant(owner.Id, tenant.Id, now));
            db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Pro, now, 14));
            await db.SaveChangesAsync();
            (tenantId, clinicId) = (tenant.Id, clinic.Id);
            claimId = (await db.OperationClaims.SingleAsync(c => c.Name == "Veteriner")).Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, ownerEmail, [PermissionCatalog.Tenants.InviteCreate]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var created = await _client.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/invites", new { email = inviteeEmail, clinicId, operationClaimId = claimId });
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdJson = await created.Content.ReadFromJsonAsync<JsonElement>();
        var inviteId = createdJson.GetProperty("inviteId").GetGuid();
        var firstToken = createdJson.GetProperty("token").GetString()!;

        var resent = await _client.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/invites/{inviteId}/resend", new { });
        resent.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondToken = (await resent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        await using var verify = _factory.Services.CreateSystemScope();
        var payloads = await verify.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Type == OutboxMessageTypes.Email && m.Payload.Contains(inviteeEmail))
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => m.Payload)
            .ToListAsync();
        payloads.Should().HaveCount(2);
        payloads[0].Should().Contain($"/join/{firstToken}").And.Contain("Davet Klinik");
        payloads[1].Should().Contain($"/join/{secondToken}");
    }
}
