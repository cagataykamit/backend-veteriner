using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// ADR-046 K3/K4: ticari iletideki ret bağlantısı. Sayfayı açmak (GET ya da önizleme) durumu değiştirmez; POST onayı geri alır ve ret
/// zamanını yazar. Bağlantı tek kullanımlık değildir, tekrar ret başarılıdır; geçersiz token 404.
/// </summary>
public sealed partial class OwnerPortalIntegrationTests
{
    private const string OptOutUrl = "/api/portal/v1/marketing-opt-out";

    private async Task<(Guid LinkId, string RawToken)> SeedOptOutLinkAsync()
    {
        var staff = await SeedStaffAsync();
        var session = await InviteAndAcceptAsync(staff);
        var rawToken = $"optout-{Guid.NewGuid():N}";
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = scope.ServiceProvider.GetRequiredService<ITokenHashService>();
        var link = await db.OwnerAccountClientLinks.SingleAsync(l => l.Id == session.LinkId);
        link.SetMarketingConsent(true, DateTime.UtcNow.AddDays(-10));
        db.MarketingOptOutLinks.Add(MarketingOptOutLink.Create(link.TenantId, link.ClinicId, link.Id, hash.ComputeSha256(rawToken), DateTime.UtcNow));
        await db.SaveChangesAsync();
        return (link.Id, rawToken);
    }

    private async Task<OwnerAccountClientLink> ReadLinkAsync(Guid linkId)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OwnerAccountClientLinks.AsNoTracking().SingleAsync(l => l.Id == linkId);
    }

    [Fact]
    public async Task OptOut_Page_Should_Not_Change_State_And_Post_Should_Withdraw_Consent()
    {
        var (linkId, token) = await SeedOptOutLinkAsync();

        // Bağlantıyı açmak (e-posta tarayıcısı dahil) onayı kaldırmaz: GET yok, önizleme değiştirmez.
        (await SendAsync(HttpMethod.Get, $"{OptOutUrl}/confirm?token={token}")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var preview = await SendAsync(HttpMethod.Post, $"{OptOutUrl}/preview", body: new { token });
        preview.StatusCode.Should().Be(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var page = await preview.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("clinicName").GetString().Should().NotBeNullOrWhiteSpace();
        page.GetProperty("isOptedOut").GetBoolean().Should().BeFalse();
        (await ReadLinkAsync(linkId)).MarketingConsentAtUtc.Should().NotBeNull();

        (await SendAsync(HttpMethod.Post, $"{OptOutUrl}/confirm", body: new { token })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var link = await ReadLinkAsync(linkId);
        link.MarketingConsentAtUtc.Should().BeNull();
        link.MarketingOptOutAtUtc.Should().NotBeNull();

        // Tek kullanımlık değil: tekrar ret başarılı, ret zamanı değişmez.
        (await SendAsync(HttpMethod.Post, $"{OptOutUrl}/confirm", body: new { token })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadLinkAsync(linkId)).MarketingOptOutAtUtc.Should().Be(link.MarketingOptOutAtUtc);
        (await (await SendAsync(HttpMethod.Post, $"{OptOutUrl}/preview", body: new { token })).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("isOptedOut").GetBoolean().Should().BeTrue();

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.MarketingOptOutLinks.AsNoTracking().SingleAsync(l => l.OwnerAccountClientLinkId == linkId)).UsedAtUtc.Should().NotBeNull();
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "Portal.MarketingOptOut" && a.TargetId == linkId.ToString("D")).ToListAsync();
        audits.Should().HaveCount(2).And.OnlyContain(a => a.Success && a.ActorUserId == null);
        audits.Should().OnlyContain(a => !(a.RequestPayload ?? string.Empty).Contains(token));
    }

    [Fact]
    public async Task OptOut_Should_Return_NotFound_For_Unknown_Token()
    {
        var response = await SendAsync(HttpMethod.Post, $"{OptOutUrl}/confirm", body: new { token = $"yok-{Guid.NewGuid():N}" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("MarketingOptOut.NotFound");
    }
}
