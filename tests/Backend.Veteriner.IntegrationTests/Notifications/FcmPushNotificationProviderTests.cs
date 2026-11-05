using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Infrastructure.Notifications;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.IntegrationTests.Notifications;

/// <summary>
/// MOBILE-003: FCM HTTP v1 sağlayıcısı sahte HTTP ile (ağ yok): hizmet hesabı okuma, OAuth jetonunun önbelleklenmesi, ileti gövdesi ve
/// hata kodlarının teslim sonucuna çevrilmesi. Gerçek Firebase'e karşı doğrulanmadı (anahtar yok).
/// </summary>
public sealed class FcmPushNotificationProviderTests
{
    private const string ProjectId = "vetinity-test";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly PushMessage Message = new(OwnerNotificationType.DataRequestAnswered, "Merkez: veri talebiniz yanıtlandı", "Ayrıntılar için portalı açın.");

    private static string ServiceAccountJson()
    {
        using var rsa = RSA.Create(2048);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = ProjectId,
            ["client_email"] = "push@vetinity-test.iam.gserviceaccount.com",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["token_uri"] = "https://oauth2.example.test/token",
        });
    }

    private static (FcmPushNotificationProvider Provider, FakeHandler Handler, FakeTimeProvider Clock) Create()
    {
        var handler = new FakeHandler();
        var clock = new FakeTimeProvider(Now.UtcDateTime);
        var provider = new FcmPushNotificationProvider(
            new FakeHttpClientFactory(handler),
            FcmServiceAccount.Parse(ServiceAccountJson()),
            clock,
            NullLogger<FcmPushNotificationProvider>.Instance);
        return (provider, handler, clock);
    }

    [Fact]
    public void Service_Account_Is_Optional_But_Invalid_Configuration_Fails_Fast()
    {
        FcmServiceAccount.Load(new ConfigurationBuilder().Build()).Should().BeNull("anahtar yoksa push kapalı (no-op)");

        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Push:Fcm:ServiceAccountJson"] = ServiceAccountJson(), ["Push:Fcm:ProjectId"] = "override" })
            .Build();
        FcmServiceAccount.Load(configured)!.ProjectId.Should().Be("override");

        var act = () => FcmServiceAccount.Parse("{\"project_id\":\"p\",\"client_email\":\"e\",\"private_key\":\"not-a-key\"}");
        act.Should().Throw<InvalidOperationException>().Which.Message.Should().NotContain("not-a-key", "hata iletisi gizli değer taşımaz");
        FluentActions.Invoking(() => FcmServiceAccount.Parse("{}")).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => FcmServiceAccount.Parse("bozuk")).Should().Throw<InvalidOperationException>();

        var missingFile = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Push:Fcm:ServiceAccountFile"] = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json") })
            .Build();
        FluentActions.Invoking(() => FcmServiceAccount.Load(missingFile)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Sends_Message_With_Cached_Access_Token_And_Signed_Assertion()
    {
        var (provider, handler, clock) = Create();
        handler.Respond(HttpStatusCode.OK, "{}");

        (await provider.SendAsync(OwnerPushPlatform.Android, "device-1", Message, CancellationToken.None)).Should().Be(PushDeliveryOutcome.Delivered);
        (await provider.SendAsync(OwnerPushPlatform.Ios, "device-2", Message, CancellationToken.None)).Should().Be(PushDeliveryOutcome.Delivered);

        handler.TokenRequests.Should().ContainSingle("erişim jetonu süresi dolana dek yeniden kullanılır");
        var form = handler.TokenRequests[0];
        form.Should().Contain("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer");
        var assertion = Uri.UnescapeDataString(form.Split("assertion=")[1]);
        var claims = JsonDocument.Parse(Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(assertion.Split('.')[1]))).RootElement;
        claims.GetProperty("scope").GetString().Should().Be("https://www.googleapis.com/auth/firebase.messaging");
        claims.GetProperty("aud").GetString().Should().Be("https://oauth2.example.test/token");
        claims.GetProperty("iat").GetInt64().Should().Be(Now.ToUnixTimeSeconds());

        handler.SendRequests.Should().HaveCount(2);
        var (uri, auth, body) = handler.SendRequests[0];
        uri.Should().Be($"https://fcm.googleapis.com/v1/projects/{ProjectId}/messages:send");
        auth.Should().Be("Bearer access-1");
        var message = JsonDocument.Parse(body).RootElement.GetProperty("message");
        message.GetProperty("token").GetString().Should().Be("device-1");
        message.GetProperty("notification").GetProperty("title").GetString().Should().Be(Message.Title);
        message.GetProperty("data").GetProperty("type").GetString().Should().Be("DataRequestAnswered");

        clock.Advance(TimeSpan.FromMinutes(56));
        await provider.SendAsync(OwnerPushPlatform.Web, "device-3", Message, CancellationToken.None);
        handler.TokenRequests.Should().HaveCount(2, "süresinden 5 dk önce yenilenir");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{\"error\":{\"code\":404,\"status\":\"NOT_FOUND\",\"details\":[{\"@type\":\"type.googleapis.com/google.firebase.fcm.v1.FcmError\",\"errorCode\":\"UNREGISTERED\"}]}}", PushDeliveryOutcome.InvalidToken)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"code\":400,\"status\":\"INVALID_ARGUMENT\"}}", PushDeliveryOutcome.InvalidToken)]
    [InlineData(HttpStatusCode.Forbidden, "{\"error\":{\"code\":403,\"details\":[{\"errorCode\":\"SENDER_ID_MISMATCH\"}]}}", PushDeliveryOutcome.InvalidToken)]
    [InlineData(HttpStatusCode.NotFound, "", PushDeliveryOutcome.InvalidToken)]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":429,\"details\":[{\"errorCode\":\"QUOTA_EXCEEDED\"}]}}", PushDeliveryOutcome.Failed)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"code\":503,\"status\":\"UNAVAILABLE\"}}", PushDeliveryOutcome.Failed)]
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":401,\"details\":[{\"errorCode\":\"THIRD_PARTY_AUTH_ERROR\"}]}}", PushDeliveryOutcome.Failed)]
    public async Task Maps_Fcm_Errors_To_Delivery_Outcome(HttpStatusCode status, string body, PushDeliveryOutcome expected)
    {
        var (provider, handler, _) = Create();
        handler.Respond(status, body);

        (await provider.SendAsync(OwnerPushPlatform.Android, "device-1", Message, CancellationToken.None)).Should().Be(expected);
    }

    [Fact]
    public async Task Unauthorized_Drops_Cached_Token_And_Token_Endpoint_Failure_Is_Transient()
    {
        var (provider, handler, _) = Create();
        handler.Respond(HttpStatusCode.Unauthorized, "{}");
        await provider.SendAsync(OwnerPushPlatform.Android, "device-1", Message, CancellationToken.None);
        handler.Respond(HttpStatusCode.OK, "{}");
        await provider.SendAsync(OwnerPushPlatform.Android, "device-1", Message, CancellationToken.None);
        handler.TokenRequests.Should().HaveCount(2, "401 sonrası erişim jetonu yeniden alınır");

        var (failing, failingHandler, _) = Create();
        failingHandler.TokenStatus = HttpStatusCode.BadRequest;
        (await failing.SendAsync(OwnerPushPlatform.Android, "device-1", Message, CancellationToken.None)).Should().Be(PushDeliveryOutcome.Failed);
        failingHandler.SendRequests.Should().BeEmpty();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private HttpStatusCode _sendStatus = HttpStatusCode.OK;
        private string _sendBody = "{}";
        private int _tokenCount;

        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
        public List<string> TokenRequests { get; } = [];
        public List<(string Uri, string? Auth, string Body)> SendRequests { get; } = [];

        public void Respond(HttpStatusCode status, string body)
        {
            _sendStatus = status;
            _sendBody = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri!.Host == "oauth2.example.test")
            {
                TokenRequests.Add(content);
                _tokenCount++;
                return new HttpResponseMessage(TokenStatus)
                {
                    Content = new StringContent($"{{\"access_token\":\"access-{_tokenCount}\",\"expires_in\":3600}}", Encoding.UTF8, "application/json"),
                };
            }

            SendRequests.Add((request.RequestUri.ToString(), request.Headers.Authorization?.ToString(), content));
            return new HttpResponseMessage(_sendStatus) { Content = new StringContent(_sendBody, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
