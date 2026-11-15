using System.Net;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Security.Turnstile;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Security;

/// <summary>Turnstile sunucu doğrulaması (siteverify yanıtı sahte): gönderilen form, success/action/hostname ve hata eşlemesi.</summary>
public sealed class TurnstileHumanVerifierTests
{
    private const string Action = HumanVerificationActions.SignupEmailCode;

    [Fact]
    public async Task Should_Pass_When_Success_With_Matching_Action_And_Subdomain_And_Post_Form()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"success":true,"action":"signup-email-code","hostname":"panel.vetinity.com","error-codes":[]}""");

        var result = await Create(handler, "vetinity.com").VerifyAsync("jeton", "203.0.113.5", Action, CancellationToken.None);

        result.Should().Be(HumanVerificationResult.Passed);
        handler.LastBody.Should().Contain("secret=gizli").And.Contain("response=jeton").And.Contain("remoteip=203.0.113.5")
            .And.Contain("idempotency_key=");
        handler.LastContentType.Should().Be("application/x-www-form-urlencoded");
    }

    [Theory]
    [InlineData("""{"success":false,"error-codes":["timeout-or-duplicate"]}""", HumanVerificationResult.Failed)]
    [InlineData("""{"success":true,"action":"baska-eylem","hostname":"www.vetinity.com"}""", HumanVerificationResult.Failed)]
    [InlineData("""{"success":true,"action":"signup-email-code","hostname":"vetinity.com.saldirgan.example"}""", HumanVerificationResult.Failed)]
    [InlineData("""{"success":false,"error-codes":["invalid-input-secret"]}""", HumanVerificationResult.Unavailable)]
    public async Task Should_Map_Provider_Response(string json, HumanVerificationResult expected)
        => (await Create(new StubHandler(HttpStatusCode.OK, json), "vetinity.com").VerifyAsync("jeton", null, Action, CancellationToken.None))
            .Should().Be(expected);

    [Fact]
    public async Task Should_Fail_Without_Token_And_Report_Unavailable_On_Provider_Error()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"success":true}""");
        (await Create(handler, null).VerifyAsync(" ", null, Action, CancellationToken.None)).Should().Be(HumanVerificationResult.Failed);
        handler.Calls.Should().Be(0, "jetonsuz istek sağlayıcıya gitmez");

        (await Create(new StubHandler(HttpStatusCode.BadGateway, ""), null).VerifyAsync("jeton", null, Action, CancellationToken.None))
            .Should().Be(HumanVerificationResult.Unavailable);
    }

    private static TurnstileHumanVerifier Create(StubHandler handler, string? expectedHostname)
        => new(
            new StubFactory(handler),
            Options.Create(new TurnstileOptions { SiteKey = "site", SecretKey = "gizli", ExpectedHostname = expectedHostname }),
            NullLogger<TurnstileHumanVerifier>.Instance);

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            LastContentType = request.Content?.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
