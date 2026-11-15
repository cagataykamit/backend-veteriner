using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// TRIAL-001 deneme kaydı kötüye kullanım önlemleri uçtan uca (HTTP + LocalDB): ayarlar, Turnstile, e-posta kodu, tek e-posta ve
/// tek vergi numarası kuralı, bekleme, jetonsuz kayıt, Cloudflare'e ulaşılamazken 503 ve denetimde maskeli vergi numarası. E-posta ve
/// Turnstile yakalayan sahtelerle sınanır. Bkz. docs/TRIAL_SIGNUP_ABUSE_API_CONTRACT.md.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed partial class TrialSignupAbuseIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string SettingsUrl = "/api/v1/public/owner-signup/settings";
    private const string CodeUrl = "/api/v1/public/owner-signup/email-code";
    private const string VerifyUrl = "/api/v1/public/owner-signup/email-code/verify";
    private const string SignupUrl = "/api/v1/public/owner-signup";
    private const string HumanToken = "insan-jetonu";

    private readonly CapturingEmailSender _email = new();
    private readonly WebApplicationFactory<global::Program> _factory;

    public TrialSignupAbuseIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(_email);
            s.RemoveAll<IHumanVerifier>();
            s.AddSingleton<IHumanVerifier>(new FakeHumanVerifier());
        }));
    }

    [Fact]
    public async Task Settings_Should_Expose_Tax_Requirement_And_Site_Key()
    {
        var body = await (await _factory.CreateClient().GetAsync(SettingsUrl)).Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("taxIdentityNumberRequired").GetBoolean().Should().BeTrue();
        body.GetProperty("turnstileSiteKey").GetString().Should().Be(FakeHumanVerifier.Key);
    }

    [Fact]
    public async Task Code_Verify_Signup_Should_Create_Trial_With_Confirmed_Email_Tax_Profile_And_Masked_Audit()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        var tax = IntegrationTestTrialSignup.NewTaxNumber();

        var sent = await client.PostAsJsonAsync(CodeUrl, new { email, turnstileToken = HumanToken });
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiresInSeconds").GetInt32().Should().Be(600);

        var code = _email.LastCodeFor(email);
        var bad = await client.PostAsJsonAsync(VerifyUrl, new { email, oneTimeCode = code == "000000" ? "111111" : "000000" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(bad)).Should().Be("SignupEmail.CodeInvalid");

        var token = await VerifyAsync(client, email, code);
        var signup = await SignupAsync(client, email, token, tax);
        signup.StatusCode.Should().Be(HttpStatusCode.OK, await signup.Content.ReadAsStringAsync());
        var tenantId = (await signup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(u => u.Email == email)).EmailConfirmed.Should().BeTrue();
        var identity = await db.TrialSignupIdentities.SingleAsync(i => i.TrialTenantId == tenantId);
        identity.TaxIdentityNumber.Should().Be(tax);
        (await db.TenantBillingProfiles.SingleAsync(p => p.TenantId == tenantId)).TaxNumber.Should().Be(tax);

        var audit = await db.AuditLogs.Where(a => a.Action == "Public.OwnerSignup" && a.RequestPayload!.Contains(email)).SingleAsync();
        audit.RequestPayload.Should().NotContain(tax).And.NotContain(token).And.Contain($"{tax[..3]}*****{tax[^2..]}");
    }

    [Fact]
    public async Task Failed_Human_Check_Should_Return_400_Without_Email()
    {
        var email = NewEmail();

        var response = await _factory.CreateClient().PostAsJsonAsync(CodeUrl, new { email, turnstileToken = "bot" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("HumanVerification.Failed");
        _email.CountFor(email).Should().Be(0);
    }

    [Fact]
    public async Task Unreachable_Human_Verifier_Should_Return_503()
    {
        var unreachable = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IHumanVerifier>();
            s.AddSingleton<IHumanVerifier>(new UnreachableHumanVerifier());
        }));

        var response = await unreachable.CreateClient().PostAsJsonAsync(CodeUrl, new { email = NewEmail(), turnstileToken = HumanToken });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("HumanVerification.Unavailable");
    }

    [Fact]
    public async Task Same_Email_Or_Tax_Number_Should_Not_Open_Second_Trial()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        var tax = IntegrationTestTrialSignup.NewTaxNumber();
        (await SignupAsync(client, email, await RequestAndVerifyAsync(client, email), tax)).StatusCode.Should().Be(HttpStatusCode.OK);

        var alias = email.Replace("@", "+ikinci@");
        var again = await client.PostAsJsonAsync(CodeUrl, new { email = alias, turnstileToken = HumanToken });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("SignupIdentity.DuplicateEmail");
        _email.CountFor(alias).Should().Be(0);

        var other = NewEmail();
        var sameTax = await SignupAsync(client, other, await RequestAndVerifyAsync(client, other), tax);
        sameTax.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(sameTax)).Should().Be("SignupIdentity.DuplicateTaxIdentity");

        await using var scope = _factory.Services.CreateSystemScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.Email == other)).Should().BeFalse();
    }

    [Fact]
    public async Task Resend_Within_Cooldown_Should_Return_429_With_RetryAfter()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        (await client.PostAsJsonAsync(CodeUrl, new { email, turnstileToken = HumanToken })).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await client.PostAsJsonAsync(CodeUrl, new { email, turnstileToken = HumanToken });

        again.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var body = await again.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("SignupEmail.ResendTooSoon");
        body.GetProperty("retryAfterSeconds").GetInt32().Should().BeInRange(1, 60);
        _email.CountFor(email).Should().Be(1);
    }

    [Fact]
    public async Task Signup_Without_Valid_Email_Verification_Or_Tax_Number_Should_Fail()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();

        (await SignupAsync(client, email, null, IntegrationTestTrialSignup.NewTaxNumber())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var forged = await SignupAsync(client, email, "uydurma", IntegrationTestTrialSignup.NewTaxNumber());
        (await IntegrationTestProblemDetails.ReadCodeAsync(forged)).Should().Be("SignupEmail.VerificationRequired");

        var token = await RequestAndVerifyAsync(client, email);
        var noTax = await SignupAsync(client, email, token, null);
        noTax.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(noTax)).Should().Be("SignupIdentity.Validation.TaxIdentityRequired");
        (await SignupAsync(client, email, token, "1234567891")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Reddedilen kayıt jetonu tüketmez: düzeltilen formla aynı jeton geçer.
        (await SignupAsync(client, email, token, IntegrationTestTrialSignup.NewTaxNumber())).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<string> RequestAndVerifyAsync(HttpClient client, string email)
    {
        (await client.PostAsJsonAsync(CodeUrl, new { email, turnstileToken = HumanToken })).StatusCode.Should().Be(HttpStatusCode.OK);
        return await VerifyAsync(client, email, _email.LastCodeFor(email));
    }

    private static async Task<string> VerifyAsync(HttpClient client, string email, string code)
    {
        var verified = await client.PostAsJsonAsync(VerifyUrl, new { email, oneTimeCode = code });
        verified.StatusCode.Should().Be(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync());
        return (await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("emailVerificationToken").GetString()!;
    }

    private static Task<HttpResponseMessage> SignupAsync(HttpClient client, string email, string? token, string? tax)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        return client.PostAsJsonAsync(SignupUrl, new
        {
            planCode = "Basic",
            tenantName = $"Deneme Kiracı {suffix}",
            clinicName = "Deneme Klinik",
            clinicCity = "Ankara",
            email,
            password = "Abc12345!",
            emailVerificationToken = token,
            taxIdentityNumber = tax,
            acceptedLegalDocuments = LegalDocumentCatalog.CurrentVersions
                .Select(kv => new { documentType = kv.Key.ToString(), version = kv.Value })
                .ToArray(),
        });
    }

    private static string NewEmail() => $"deneme-{Guid.NewGuid():N}@example.com";

    /// <summary>Yalnız <see cref="HumanToken"/> geçer; eylem adı da doğru olmalı.</summary>
    private sealed class FakeHumanVerifier : IHumanVerifier
    {
        public const string Key = "test-site-anahtari";

        public bool IsConfigured => true;
        public string? SiteKey => Key;

        public Task<HumanVerificationResult> VerifyAsync(string? token, string? remoteIp, string action, CancellationToken ct)
            => Task.FromResult(token == HumanToken && action == HumanVerificationActions.SignupEmailCode
                ? HumanVerificationResult.Passed
                : HumanVerificationResult.Failed);
    }

    /// <summary>Cloudflare'e ulaşılamıyor (zaman aşımı, yanlış gizli anahtar).</summary>
    private sealed class UnreachableHumanVerifier : IHumanVerifier
    {
        public bool IsConfigured => true;
        public string? SiteKey => FakeHumanVerifier.Key;

        public Task<HumanVerificationResult> VerifyAsync(string? token, string? remoteIp, string action, CancellationToken ct)
            => Task.FromResult(HumanVerificationResult.Unavailable);
    }

    private sealed record SentEmail(string To, string Body);

    /// <summary>Gönderilen e-postaları yakalar; kod metinden okunur.</summary>
    private sealed partial class CapturingEmailSender : IEmailSender
    {
        private readonly ConcurrentQueue<SentEmail> _sent = new();

        public Task SendAsync(string to, string subject, string body, CancellationToken ct = default, bool isHtml = false)
        {
            _sent.Enqueue(new SentEmail(to, body));
            return Task.CompletedTask;
        }

        public int CountFor(string email) => _sent.Count(m => m.To == email);

        public string LastCodeFor(string email) => CodePattern().Match(_sent.Last(m => m.To == email).Body).Value;

        [GeneratedRegex(@"\b\d{6}\b")]
        private static partial Regex CodePattern();
    }
}
