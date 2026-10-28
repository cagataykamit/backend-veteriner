using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// Hasta sahibi hesap silme (ADR-041 seçenek C): kodla onay, anonimleştirme, iki kiracıdaki bağların kapanması, oturum ve
/// kodların silinmesi, kabul ve klinik kayıtlarının kalması, silinen hesapla girişin genel hatayla reddi, personel tarafında
/// gerekçe ve aynı e-postayla yeni davetin yeni hesap açması.
/// </summary>
public sealed partial class OwnerPortalIntegrationTests
{
    private const string DeletionCodeInvalid = "Portal.Account.DeletionCodeInvalid";

    private async Task<string> RequestDeletionCodeAsync(string bearer)
    {
        var before = _secrets.CodeCount;
        (await SendAsync(HttpMethod.Post, "/api/portal/v1/me/deletion-code", bearer)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _secrets.CodeCount.Should().Be(before + 1);
        return _secrets.LastCode!;
    }

    private Task<HttpResponseMessage> DeleteAccountAsync(string bearer, string oneTimeCode)
        => SendAsync(HttpMethod.Delete, "/api/portal/v1/me", bearer, new { oneTimeCode });

    [Fact]
    public async Task Account_Deletion_Should_Anonymize_Close_All_Links_And_Keep_Clinic_Records()
    {
        var staff = await SeedStaffAsync();
        var portal = await InviteAndAcceptAsync(staff);

        Guid accountId, secondLinkId, secondClientId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            accountId = await db.OwnerAccounts
                .Where(a => a.Email == OwnerAccount.NormalizeEmail(portal.Email))
                .Select(a => a.Id)
                .SingleAsync();

            var otherTenant = new Tenant($"PortalSil-{Guid.NewGuid():N}"[..20], now);
            var otherClinic = new Clinic(otherTenant.Id, $"Diğer-{Guid.NewGuid():N}"[..12], "İzmir");
            var otherClient = new Client(otherTenant.Id, "İkinci Klinik Kaydı", now, email: portal.Email);
            var link = OwnerAccountClientLink.Create(accountId, otherTenant.Id, otherClinic.Id, otherClient.Id, Guid.NewGuid(), now);
            db.AddRange(otherTenant, otherClinic, otherClient, link);
            await db.SaveChangesAsync();
            secondLinkId = link.Id;
            secondClientId = otherClient.Id;
        }

        var code = await RequestDeletionCodeAsync(portal.AccessToken);
        (await SendAsync(HttpMethod.Post, "/api/portal/v1/me/deletion-code", portal.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "bekleme süresinde de yanıt aynı");

        var wrong = await DeleteAccountAsync(portal.AccessToken, code == "000000" ? "111111" : "000000");
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(wrong)).Should().Be(DeletionCodeInvalid);

        int emailsBefore;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            emailsBefore = await db.OutboxMessages.CountAsync(m => m.Type == OutboxMessageTypes.Email && m.Payload.Contains(portal.Email));
        }

        (await DeleteAccountAsync(portal.AccessToken, code)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = await db.OwnerAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
            account.Status.Should().Be(OwnerAccountStatus.Deleted);
            account.Email.Should().BeNull();
            account.HasPassword.Should().BeFalse();
            account.DisplayName.Should().Be(OwnerAccount.DeletedDisplayName);
            account.DeletedAtUtc.Should().NotBeNull();

            var links = await db.OwnerAccountClientLinks.AsNoTracking().Where(l => l.OwnerAccountId == accountId).ToListAsync();
            links.Select(l => l.Id).Should().BeEquivalentTo([portal.LinkId, secondLinkId]);
            links.Should().AllSatisfy(l =>
            {
                l.Status.Should().Be(OwnerAccountClientLinkStatus.Closed);
                l.CloseReason.Should().Be(OwnerAccountClientLinkCloseReason.OwnerAccountDeleted);
                l.ClosedByUserId.Should().BeNull();
            });

            (await db.OwnerPortalCodes.CountAsync(c => c.OwnerAccountId == accountId)).Should().Be(0);
            (await db.OwnerPortalRefreshTokens.CountAsync(t => t.OwnerAccountId == accountId)).Should().Be(0);
            (await db.LegalAcceptances.CountAsync(a => a.OwnerAccountId == accountId)).Should().BeGreaterThan(0, "kabul kayıtları ispat için kalır");
            (await db.Clients.CountAsync(c => c.Id == portal.ClientId || c.Id == secondClientId)).Should().Be(2, "klinik kayıtlarına dokunulmaz");
            (await db.OutboxMessages.CountAsync(m => m.Type == OutboxMessageTypes.Email && m.Payload.Contains(portal.Email)))
                .Should().Be(emailsBefore + 1, "eski adrese tek onay e-postası gider");
        }

        var me = await SendAsync(HttpMethod.Get, "/api/portal/v1/me", portal.AccessToken);
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "kalan erişim jetonu hesabı bulamaz");
        (await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{secondLinkId}/pets", portal.AccessToken))
            .StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/refresh", body: new { refreshToken = portal.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var login = await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/login", body: new { email = portal.Email, password = Password });
        (await IntegrationTestProblemDetails.ReadCodeAsync(login)).Should().Be(InvalidCredentials, "silinen hesap yokmuş gibi yanıtlanır");
        var codesBefore = _secrets.CodeCount;
        (await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/code", body: new { email = portal.Email }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        _secrets.CodeCount.Should().Be(codesBefore, "silinen hesaba kod gitmez");

        var access = await (await SendAsync(HttpMethod.Get, $"/api/v1/clients/{portal.ClientId}/portal-access", staff.Token))
            .Content.ReadFromJsonAsync<JsonElement>();
        var staffLink = access.GetProperty("links").EnumerateArray().Single();
        staffLink.GetProperty("status").GetString().Should().Be("Closed");
        staffLink.GetProperty("closeReason").GetString().Should().Be("OwnerAccountDeleted");
        staffLink.GetProperty("accountEmail").ValueKind.Should().Be(JsonValueKind.Null);
        access.GetProperty("canInvite").GetBoolean().Should().BeTrue("klinik yeniden davet edebilir");
    }

    [Fact]
    public async Task New_Invite_After_Deletion_Should_Create_New_Account_With_Same_Email()
    {
        var staff = await SeedStaffAsync();
        var client = await SeedClientAsync(staff.TenantId);
        var first = await InviteAndAcceptAsync(staff, client: client);
        var code = await RequestDeletionCodeAsync(first.AccessToken);
        (await DeleteAccountAsync(first.AccessToken, code)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await InviteAndAcceptAsync(staff, client: client);

        second.LinkId.Should().NotBe(first.LinkId);
        var me = await (await SendAsync(HttpMethod.Get, "/api/portal/v1/me", second.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("email").GetString().Should().Be(OwnerAccount.NormalizeEmail(client.Email));
        me.GetProperty("links").EnumerateArray().Select(l => l.GetProperty("linkId").GetGuid()).Should().Equal(second.LinkId);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var links = await db.OwnerAccountClientLinks.AsNoTracking().Where(l => l.ClientId == client.Id).ToListAsync();
        links.Should().HaveCount(2);
        links.Select(l => l.OwnerAccountId).Distinct().Should().HaveCount(2, "eski bağ eski (silinmiş) hesapta kalır");
        links.Single(l => l.Id == first.LinkId).Status.Should().Be(OwnerAccountClientLinkStatus.Closed);
    }
}
