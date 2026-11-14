using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Sms;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Tenants;

/// <summary>
/// TRIAL-001 telefon doğrulaması uçtan uca (HTTP + LocalDB): kod gönder → doğrula → kayıt; numara başına tek deneme, bekleme,
/// hatalı kod, jetonsuz kayıt, sağlayıcı yokken 503 ve denetimde maskeli numara. SMS, gönderilen metni yakalayan sahte
/// sağlayıcıyla sınanır. Bkz. docs/TRIAL_PHONE_VERIFICATION_API_CONTRACT.md.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed partial class SignupPhoneVerificationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string CodeUrl = "/api/v1/public/owner-signup/phone-code";
    private const string VerifyUrl = "/api/v1/public/owner-signup/phone-code/verify";
    private const string SignupUrl = "/api/v1/public/owner-signup";

    private readonly CapturingSmsSender _sms = new();
    private readonly WebApplicationFactory<global::Program> _factory;

    public SignupPhoneVerificationIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ISmsSender>();
            s.AddSingleton<ISmsSender>(_sms);
        }));
    }

    [Fact]
    public async Task Code_Verify_Signup_Should_Create_Account_With_Verified_Phone_And_Masked_Audit()
    {
        var client = _factory.CreateClient();
        var phone = IntegrationTestSignupPhone.NewPhone();
        var typed = $"0{phone[2..5]} {phone[5..8]} {phone[8..10]} {phone[10..]}";

        var sent = await client.PostAsJsonAsync(CodeUrl, new { mobilePhone = typed });
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        var sentBody = await sent.Content.ReadFromJsonAsync<JsonElement>();
        sentBody.GetProperty("expiresInSeconds").GetInt32().Should().Be(300);
        sentBody.GetProperty("resendAfterSeconds").GetInt32().Should().Be(60);

        var code = _sms.LastCodeFor(phone);
        var wrong = code == "000000" ? "111111" : "000000";
        var bad = await client.PostAsJsonAsync(VerifyUrl, new { mobilePhone = phone, oneTimeCode = wrong });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(bad)).Should().Be("SignupPhone.CodeInvalid");

        var verified = await client.PostAsJsonAsync(VerifyUrl, new { mobilePhone = typed, oneTimeCode = code });
        verified.StatusCode.Should().Be(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync());
        var token = (await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("phoneVerificationToken").GetString();

        var (signup, email) = await SignupAsync(client, phone, token);
        signup.StatusCode.Should().Be(HttpStatusCode.OK, await signup.Content.ReadAsStringAsync());
        var tenantId = (await signup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.PhoneNumber.Should().Be(phone);
        user.PhoneVerifiedAtUtc.Should().NotBeNull();
        var row = await db.SignupPhoneVerifications.SingleAsync(v => v.Phone == phone && v.UsedAtUtc != null);
        row.UsedByTenantId.Should().Be(tenantId);

        var masked = $"{phone[..5]}*****{phone[^2..]}";
        var audits = await db.AuditLogs
            .Where(a => a.Action.StartsWith("Public.OwnerSignup") && a.TargetId == masked)
            .ToListAsync();
        audits.Select(a => a.Action).Should().Contain(["Public.OwnerSignup.PhoneCodeRequest", "Public.OwnerSignup.PhoneVerify", "Public.OwnerSignup"]);
        audits.Should().AllSatisfy(a =>
        {
            (a.RequestPayload ?? "").Should().NotContain(phone);
            (a.RequestPayload ?? "").Should().NotContain($"\"{code}\"");
            (a.RequestPayload ?? "").Should().NotContain(token!);
        });
    }

    [Fact]
    public async Task Same_Phone_Should_Not_Open_Second_Trial()
    {
        var client = _factory.CreateClient();
        var phone = IntegrationTestSignupPhone.NewPhone();
        var token = await VerifyAsync(client, phone);
        (await SignupAsync(client, phone, token)).Response.StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await client.PostAsJsonAsync(CodeUrl, new { mobilePhone = phone });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("SignupPhone.DuplicateTrial");
        _sms.CountFor(phone).Should().Be(1, "kullanılmış numaraya yeni SMS gönderilmez");

        var reuse = (await SignupAsync(client, phone, token)).Response;
        reuse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(reuse)).Should().Be("SignupPhone.DuplicateTrial");
    }

    [Fact]
    public async Task Resend_Within_Cooldown_Should_Return_429_With_RetryAfter()
    {
        var client = _factory.CreateClient();
        var phone = IntegrationTestSignupPhone.NewPhone();
        (await client.PostAsJsonAsync(CodeUrl, new { mobilePhone = phone })).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await client.PostAsJsonAsync(CodeUrl, new { mobilePhone = phone });
        again.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var body = await again.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("SignupPhone.ResendTooSoon");
        body.GetProperty("retryAfterSeconds").GetInt32().Should().BeInRange(1, 60);
        _sms.CountFor(phone).Should().Be(1);
    }

    [Fact]
    public async Task Signup_Without_Valid_Verification_Should_Fail_And_Create_Nothing()
    {
        var client = _factory.CreateClient();
        var phone = IntegrationTestSignupPhone.NewPhone();

        var (missing, missingEmail) = await SignupAsync(client, phone, token: null);
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (forged, forgedEmail) = await SignupAsync(client, phone, token: "uydurma-jeton");
        forged.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IntegrationTestProblemDetails.ReadCodeAsync(forged)).Should().Be("SignupPhone.VerificationRequired");

        // Başka numaranın jetonu bu numarayla kullanılamaz.
        var otherToken = await VerifyAsync(client, IntegrationTestSignupPhone.NewPhone());
        var (crossed, crossedEmail) = await SignupAsync(client, phone, otherToken);
        crossed.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.AnyAsync(u => u.Email == missingEmail || u.Email == forgedEmail || u.Email == crossedEmail))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_Phone_Should_Return_400_Without_Sms()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(CodeUrl, new { mobilePhone = "0212 123 45 67" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _sms.Sent.Should().NotContain(m => m.Phone.StartsWith("90212"));
    }

    [Fact]
    public async Task Unconfigured_Provider_Should_Return_503()
    {
        var unconfigured = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ISmsSender>();
            s.AddScoped<ISmsSender, UnconfiguredSmsSender>();
        }));

        var response = await unconfigured.CreateClient()
            .PostAsJsonAsync(CodeUrl, new { mobilePhone = IntegrationTestSignupPhone.NewPhone() });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be("Sms.NotConfigured");
    }

    private async Task<string> VerifyAsync(HttpClient client, string phone)
    {
        (await client.PostAsJsonAsync(CodeUrl, new { mobilePhone = phone })).StatusCode.Should().Be(HttpStatusCode.OK);
        var verified = await client.PostAsJsonAsync(VerifyUrl, new { mobilePhone = phone, oneTimeCode = _sms.LastCodeFor(phone) });
        verified.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("phoneVerificationToken").GetString()!;
    }

    private static async Task<(HttpResponseMessage Response, string Email)> SignupAsync(HttpClient client, string phone, string? token)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"telefon-{suffix}@example.com";
        var response = await client.PostAsJsonAsync(SignupUrl, new
        {
            planCode = "Basic",
            tenantName = $"Telefon Kiracı {suffix}",
            clinicName = "Telefon Klinik",
            clinicCity = "Ankara",
            email,
            password = "Abc12345!",
            mobilePhone = phone,
            phoneVerificationToken = token,
            acceptedLegalDocuments = LegalDocumentCatalog.CurrentVersions
                .Select(kv => new { documentType = kv.Key.ToString(), version = kv.Value })
                .ToArray(),
        });
        return (response, email);
    }

    private sealed record SentSms(string Phone, string Text);

    /// <summary>Gönderilen SMS'leri yakalar; kod metinden okunur.</summary>
    private sealed partial class CapturingSmsSender : ISmsSender
    {
        private readonly ConcurrentQueue<SentSms> _sent = new();

        public IEnumerable<SentSms> Sent => _sent;

        public bool IsConfigured => true;

        public Task<SmsSendResult> SendOneTimeCodeAsync(string normalizedPhone, string text, CancellationToken ct)
        {
            _sent.Enqueue(new SentSms(normalizedPhone, text));
            return Task.FromResult(SmsSendResult.Success("test-job"));
        }

        public int CountFor(string phone) => _sent.Count(m => m.Phone == phone);

        public string LastCodeFor(string phone)
            => CodePattern().Match(_sent.Last(m => m.Phone == phone).Text).Value;

        [GeneratedRegex(@"\d{6}")]
        private static partial Regex CodePattern();
    }
}
