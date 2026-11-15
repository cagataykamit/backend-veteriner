using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// Turnstile ile korunan portal uçları (TRIAL_SIGNUP_ABUSE_API_CONTRACT.md §4): giriş kodu, parola sıfırlama kodu ve online randevu
/// e-posta kodu. Yalnız "insan-jetonu" geçen sahte doğrulayıcıyla sınanır; portal girişi ayarla kapatılabilir.
/// </summary>
public sealed partial class OwnerPortalIntegrationTests
{
    private const string HumanToken = "insan-jetonu";

    [Fact]
    public async Task Human_Verification_Settings_Should_Expose_Site_Key_And_Portal_Protection()
    {
        var body = await (await HumanCheckedClient(protectPortal: true).GetAsync("/api/portal/v1/human-verification"))
            .Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("turnstileSiteKey").GetString().Should().Be(StrictHumanVerifier.Key);
        body.GetProperty("portalSignInProtected").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/portal/v1/auth/code")]
    [InlineData("/api/portal/v1/auth/password-reset")]
    public async Task Portal_Code_Request_Should_Require_Human_Check_Unless_Disabled(string url)
    {
        var email = UniqueEmail();

        var rejected = await HumanCheckedClient(protectPortal: true).PostAsJsonAsync(url, new { email, turnstileToken = "bot" });
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(rejected)).Should().Be("HumanVerification.Failed");

        (await HumanCheckedClient(protectPortal: true).PostAsJsonAsync(url, new { email, turnstileToken = HumanToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await HumanCheckedClient(protectPortal: false).PostAsJsonAsync(url, new { email }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "mağaza uygulaması için ayarla kapatılabilir");
    }

    [Fact]
    public async Task Online_Booking_Code_Request_Should_Require_Human_Check_Even_When_Portal_Disabled()
    {
        var staff = await SeedStaffAsync(OnlineStaffPermissions);
        var code = await EnableBookingAsync(staff);
        var url = $"/api/portal/v1/booking/{code}/verification-code";
        var client = HumanCheckedClient(protectPortal: false);

        var rejected = await client.PostAsJsonAsync(url, new { email = UniqueEmail() });
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(rejected)).Should().Be("HumanVerification.Failed");

        (await client.PostAsJsonAsync(url, new { email = UniqueEmail(), turnstileToken = HumanToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private HttpClient HumanCheckedClient(bool protectPortal)
        => _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Turnstile:ProtectPortalSignIn"] = protectPortal ? "true" : "false",
            }));
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IHumanVerifier>();
                s.AddSingleton<IHumanVerifier>(new StrictHumanVerifier());
            });
        }).CreateClient();

    /// <summary>Yalnız <see cref="HumanToken"/> geçer.</summary>
    private sealed class StrictHumanVerifier : IHumanVerifier
    {
        public const string Key = "portal-site-anahtari";

        public bool IsConfigured => true;
        public string? SiteKey => Key;

        public Task<HumanVerificationResult> VerifyAsync(string? token, string? remoteIp, string action, CancellationToken ct)
            => Task.FromResult(token == HumanToken ? HumanVerificationResult.Passed : HumanVerificationResult.Failed);
    }
}
