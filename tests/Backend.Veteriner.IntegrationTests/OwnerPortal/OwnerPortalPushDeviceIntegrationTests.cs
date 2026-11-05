using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// MOBILE-003: push cihaz jetonu kaydı/iptali (idempotent, jeton yenileme, hesaplar arası geçiş), tercih kapanınca, tüm
/// cihazlardan çıkışta ve hesap silmede jetonların silinmesi, denetim kaydında jetonun maskelenmesi.
/// </summary>
public sealed partial class OwnerPortalIntegrationTests
{
    private const string DevicesUrl = "/api/portal/v1/me/push-devices";
    private const string PreferencesUrl = "/api/portal/v1/me/notification-preferences";

    private static string NewDeviceToken() => $"fcm-{Guid.NewGuid():N}:{Guid.NewGuid():N}";

    private Task<HttpResponseMessage> RegisterDeviceAsync(string bearer, string token, string platform = "Android", string? previousToken = null)
        => SendAsync(HttpMethod.Put, DevicesUrl, bearer, new { platform, token, previousToken });

    private async Task<PortalSession> PortalWithPushEnabledAsync()
    {
        var staff = await SeedStaffAsync();
        var portal = await InviteAndAcceptAsync(staff);
        (await SendAsync(HttpMethod.Put, PreferencesUrl, portal.AccessToken, new { pushEnabled = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        return portal;
    }

    private async Task<List<OwnerPushDevice>> DevicesOfAsync(string email)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = OwnerAccount.NormalizeEmail(email);
        var accountId = await db.OwnerAccounts.Where(a => a.Email == normalized).Select(a => a.Id).SingleAsync();
        return await db.OwnerPushDevices.AsNoTracking().Where(d => d.OwnerAccountId == accountId).ToListAsync();
    }

    private async Task<int> DeviceCountAsync(string bearer)
        => (await (await SendAsync(HttpMethod.Get, PreferencesUrl, bearer)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("push").GetProperty("deviceCount").GetInt32();

    [Fact]
    public async Task Push_Device_Registration_Requires_Push_Preference_And_Is_Idempotent()
    {
        var staff = await SeedStaffAsync();
        var portal = await InviteAndAcceptAsync(staff);
        var token = NewDeviceToken();

        var disabled = await RegisterDeviceAsync(portal.AccessToken, token);
        disabled.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(disabled)).Should().Be("Portal.Push.Disabled");

        (await SendAsync(HttpMethod.Put, PreferencesUrl, portal.AccessToken, new { pushEnabled = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RegisterDeviceAsync(portal.AccessToken, "", "Android")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, DevicesUrl, portal.AccessToken, new { token })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "platform zorunlu");

        (await RegisterDeviceAsync(portal.AccessToken, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var first = (await DevicesOfAsync(portal.Email)).Single();

        (await RegisterDeviceAsync(portal.AccessToken, $"  {token}  ", "Ios")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var again = (await DevicesOfAsync(portal.Email)).Single();
        again.Id.Should().Be(first.Id, "aynı jeton yeni satır açmaz");
        again.Platform.Should().Be(OwnerPushPlatform.Ios);
        again.LastSeenAtUtc.Should().BeOnOrAfter(first.LastSeenAtUtc);
        (await DeviceCountAsync(portal.AccessToken)).Should().Be(1);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "Portal.PushDevices.Register" && a.Success)
            .OrderByDescending(a => a.OccurredAtUtc)
            .FirstAsync();
        audit.RequestPayload.Should().NotContain(token, "cihaz jetonu denetim kaydına yazılmaz");
    }

    [Fact]
    public async Task Push_Device_Token_Refresh_Replaces_Previous_And_Token_Moves_Between_Accounts()
    {
        var portal = await PortalWithPushEnabledAsync();
        var oldToken = NewDeviceToken();
        var newToken = NewDeviceToken();

        (await RegisterDeviceAsync(portal.AccessToken, oldToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RegisterDeviceAsync(portal.AccessToken, newToken, previousToken: oldToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DevicesOfAsync(portal.Email)).Select(d => d.Token).Should().BeEquivalentTo([newToken]);

        // Aynı cihazda başka hesap oturum açıp kaydeder: jeton ona geçer, önceki hesabın cihazı kalmaz.
        var other = await PortalWithPushEnabledAsync();
        (await RegisterDeviceAsync(other.AccessToken, newToken, "Web")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DevicesOfAsync(portal.Email)).Should().BeEmpty();
        (await DevicesOfAsync(other.Email)).Single().Platform.Should().Be(OwnerPushPlatform.Web);

        // Başkasının jetonu iptal edilemez (yanıt ayırt etmez).
        (await SendAsync(HttpMethod.Post, $"{DevicesUrl}/revoke", portal.AccessToken, new { token = newToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DevicesOfAsync(other.Email)).Should().ContainSingle();

        (await SendAsync(HttpMethod.Post, $"{DevicesUrl}/revoke", other.AccessToken, new { token = newToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DevicesOfAsync(other.Email)).Should().BeEmpty();
        (await SendAsync(HttpMethod.Post, $"{DevicesUrl}/revoke", other.AccessToken, new { token = newToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "iptal idempotent");
    }

    [Fact]
    public async Task Push_Devices_Are_Capped_Per_Account_Oldest_Dropped()
    {
        var portal = await PortalWithPushEnabledAsync();
        var tokens = Enumerable.Range(0, OwnerPushDevice.MaxDevicesPerAccount + 1).Select(_ => NewDeviceToken()).ToList();
        foreach (var token in tokens)
            (await RegisterDeviceAsync(portal.AccessToken, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = (await DevicesOfAsync(portal.Email)).Select(d => d.Token).ToList();
        stored.Should().HaveCount(OwnerPushDevice.MaxDevicesPerAccount);
        stored.Should().NotContain(tokens[0], "en uzun süredir görülmeyen düşer");
        stored.Should().Contain(tokens[^1]);
    }

    [Fact]
    public async Task Push_Devices_Are_Removed_When_Preference_Is_Turned_Off_And_On_Logout_All()
    {
        var portal = await PortalWithPushEnabledAsync();
        (await RegisterDeviceAsync(portal.AccessToken, NewDeviceToken())).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var off = await (await SendAsync(HttpMethod.Put, PreferencesUrl, portal.AccessToken, new { pushEnabled = false }))
            .Content.ReadFromJsonAsync<JsonElement>();
        off.GetProperty("push").GetProperty("deviceCount").GetInt32().Should().Be(0);
        (await DevicesOfAsync(portal.Email)).Should().BeEmpty("tercih kapanınca jeton tutulmaz");

        (await SendAsync(HttpMethod.Put, PreferencesUrl, portal.AccessToken, new { pushEnabled = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RegisterDeviceAsync(portal.AccessToken, NewDeviceToken(), "Ios")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DeviceCountAsync(portal.AccessToken)).Should().Be(1);

        (await SendAsync(HttpMethod.Post, "/api/portal/v1/auth/logout-all", portal.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DevicesOfAsync(portal.Email)).Should().BeEmpty("tüm cihazlardan çıkış push'u da keser");
    }

    [Fact]
    public async Task Push_Devices_Are_Removed_On_Account_Deletion()
    {
        var portal = await PortalWithPushEnabledAsync();
        (await RegisterDeviceAsync(portal.AccessToken, NewDeviceToken())).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Guid accountId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            accountId = await db.OwnerAccounts.Where(a => a.Email == OwnerAccount.NormalizeEmail(portal.Email)).Select(a => a.Id).SingleAsync();
        }

        var code = await RequestDeletionCodeAsync(portal.AccessToken);
        (await DeleteAccountAsync(portal.AccessToken, code)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.OwnerPushDevices.CountAsync(d => d.OwnerAccountId == accountId)).Should().Be(0, "ADR-031 K4: hesap silinince cihaz jetonları silinir");
        }
    }
}
