using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Infrastructure.EDocuments.Parasut;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.IntegrationTests.EDocuments;

/// <summary>
/// Paraşüt adaptörü sahte HTTP sunucusuna karşı (devhub.parasut.com/openapi.json uçları): belirteç yenileme, cari ve satış
/// faturası, external_reference_key ile tekrar koruması, e-SMM (eşzamanlı), e-Arşiv (iş takibi), iptal, PDF ve hata sınıfları.
/// </summary>
public sealed class ParasutEDocumentProviderTests
{
    private const string Company = "777";
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeParasut _server = new();
    private readonly FixedTimeProvider _time = new(Now);

    private ParasutEDocumentProvider Provider()
    {
        var options = Options.Create(new ParasutOptions
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RedirectUri = "https://localhost:7173/api/e-documents/parasut/callback",
            IdentityBaseUrl = "https://id.test/oidc",
            ApiBaseUrl = "https://api.test/v4",
            Environment = EDocumentEnvironment.Test,
        });
        var client = new ParasutClient(new StubHttpClientFactory(_server), options, _time, NullLogger<ParasutClient>.Instance);
        return new ParasutEDocumentProvider(client);
    }

    [Fact]
    public void Disabled_Option_Should_Make_Provider_Unconfigured()
    {
        var off = new ParasutOptions
        {
            Enabled = false, ClientId = "test-client", ClientSecret = "test-secret", RedirectUri = "https://localhost:7173/api/e-documents/parasut/callback",
        };

        off.IsConfigured.Should().BeFalse("canlıda Paraşüt kapalı: listelenmez, bağlanılamaz");
        Provider().IsConfigured.Should().BeTrue("varsayılan açık");
    }

    private static string Credentials(DateTime expiresAt)
        => JsonSerializer.Serialize(new { AccessToken = "access-1", RefreshToken = "refresh-1", ExpiresAtUtc = expiresAt });

    private static EDocumentProviderAccount Account(DateTime? expiresAt = null)
        => new(Company, Credentials(expiresAt ?? Now.UtcDateTime.AddHours(1)));

    private static EDocumentSendRequest Request(EDocumentType type, string identity = "10000000146", bool unknown = false)
        => new(
            type,
            Guid.Parse("3b241101-e2bb-4255-8caf-4136c566a962"),
            new DateTime(2026, 11, 11, 9, 5, 0),
            new EDocumentSendBuyer(EDocumentBuyerKind.Individual, "Ayşe Yılmaz", identity, unknown, null, "İstanbul", "Kadıköy", null, null),
            [new EDocumentSendLine("Muayene", 1m, null, 1000m, 20m)],
            null,
            null,
            null,
            null);

    [Fact]
    public void AuthorizationUrl_Should_Use_Pkce_And_Registered_Redirect()
    {
        var url = new Uri(Provider().BuildAuthorizationUrl("state-x", "challenge-y"));
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);

        url.GetLeftPart(UriPartial.Path).Should().Be("https://id.test/oidc/auth");
        query["code_challenge_method"].Should().Be("S256");
        query["code_challenge"].Should().Be("challenge-y");
        query["state"].Should().Be("state-x");
        query["scope"].Should().Be("openid");
        query["redirect_uri"].Should().Be("https://localhost:7173/api/e-documents/parasut/callback");
    }

    [Fact]
    public async Task Issue_ESmm_Should_Create_Contact_Invoice_With_Reference_And_Return_Issued()
    {
        _server.On("GET", "/sales_invoices/find_by_external_reference_key", HttpStatusCode.NotFound, null);
        _server.On("GET", "/contacts", HttpStatusCode.OK, """{"data":[]}""");
        _server.On("POST", "/contacts", HttpStatusCode.Created, """{"data":{"id":"c-1","type":"contacts"}}""");
        _server.On("GET", "/products", HttpStatusCode.OK, """{"data":[]}""");
        _server.On("POST", "/products", HttpStatusCode.Created, """{"data":{"id":"p-1","type":"products"}}""");
        _server.On("POST", "/sales_invoices", HttpStatusCode.Created, """{"data":{"id":"inv-1","type":"sales_invoices"}}""");
        _server.On("GET", "/sales_invoices/inv-1", HttpStatusCode.OK,
            """{"data":{"id":"inv-1","type":"sales_invoices","attributes":{"net_total":"1200.0"},"relationships":{"active_e_document":{"data":null}}}}""");
        _server.On("POST", "/e_smms", HttpStatusCode.Created,
            """{"data":{"id":"smm-1","type":"e_smms","attributes":{"invoice_number":42,"uuid":"u-1"}}}""");

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.ESmm), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value!.State.Should().Be(ProviderIssueState.Issued);
        result.Value.ProviderInvoiceId.Should().Be("inv-1");
        result.Value.ProviderDocumentId.Should().Be("smm-1");
        result.Value.DocumentNumber.Should().Be("42");
        result.Value.Total.Should().Be(1200m);
        result.RefreshedCredentials.Should().BeNull();

        var invoice = _server.Body("POST", "/sales_invoices")!;
        invoice["data"]!["relationships"]!["external_reference_key"]!["data"]!["attributes"]!["key"]!.ToString()
            .Should().Be("3b241101-e2bb-4255-8caf-4136c566a962");
        invoice["data"]!["attributes"]!["currency"]!.ToString().Should().Be("TRL");
        var detail = invoice["data"]!["relationships"]!["details"]!["data"]![0]!["attributes"]!;
        detail["unit_price"]!.GetValue<decimal>().Should().Be(1000m);
        detail["vat_rate"]!.GetValue<decimal>().Should().Be(20m);
        invoice["data"]!["relationships"]!["details"]!["data"]![0]!["relationships"]!["product"]!["data"]!["id"]!.ToString().Should().Be("p-1");
        var product = _server.Body("POST", "/products")!["data"]!["attributes"]!;
        product["name"]!.ToString().Should().Be("Muayene");
        product["code"]!.ToString().Should().Be(ParasutEDocumentProvider.ProductCode("muayene"));
        _server.Body("POST", "/e_smms")!["data"]!["relationships"]!["sales_invoice"]!["data"]!["id"]!.ToString().Should().Be("inv-1");
        _server.Requests.Should().AllSatisfy(r => r.Authorization.Should().Be("Bearer access-1"));
    }

    [Fact]
    public async Task Issue_Retry_Should_Reuse_Invoice_And_Existing_Document_Without_Creating_Again()
    {
        _server.On("GET", "/sales_invoices/find_by_external_reference_key", HttpStatusCode.OK, """{"data":{"id":"inv-1"}}""");
        _server.On("GET", "/sales_invoices/inv-1", HttpStatusCode.OK,
            """
            {"data":{"id":"inv-1","attributes":{"net_total":1200,"invoice_no":"A-1"},"relationships":{"active_e_document":{"data":{"type":"e_archives","id":"arc-1"}}}},
             "included":[{"id":"arc-1","type":"e_archives","attributes":{"status":"sent","invoice_number":"GIB2026000000001","uuid":"u-9"}}]}
            """);

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.EArchive), CancellationToken.None);

        result.Value!.State.Should().Be(ProviderIssueState.Issued);
        result.Value.ProviderDocumentId.Should().Be("arc-1");
        result.Value.DocumentNumber.Should().Be("GIB2026000000001");
        _server.Requests.Should().NotContain(r => r.Method == "POST");
    }

    [Fact]
    public async Task Duplicate_Reference_422_Should_Use_Existing_Invoice_From_Meta()
    {
        _server.On("GET", "/sales_invoices/find_by_external_reference_key", HttpStatusCode.NotFound, null);
        _server.On("GET", "/contacts", HttpStatusCode.OK, """{"data":[{"id":"c-1"}]}""");
        _server.On("GET", "/products", HttpStatusCode.OK, """{"data":[{"id":"p-9"}]}""");
        _server.On("POST", "/sales_invoices", HttpStatusCode.UnprocessableEntity,
            """{"errors":[{"title":"Unprocessable","detail":"Bu key ile daha önce bir fatura oluşturulmuş.","meta":{"sales_invoice_id":"inv-7"}}]}""");
        _server.On("GET", "/sales_invoices/inv-7", HttpStatusCode.OK, """{"data":{"id":"inv-7","relationships":{"active_e_document":{"data":null}}}}""");
        _server.On("POST", "/e_archives", HttpStatusCode.Accepted, """{"data":{"id":"job-1","type":"trackable_jobs"}}""");

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.EArchive), CancellationToken.None);

        result.Value!.State.Should().Be(ProviderIssueState.Pending);
        result.Value.ProviderInvoiceId.Should().Be("inv-7");
        result.Value.ProviderJobId.Should().Be("job-1");
        _server.Requests.Should().NotContain(r => r.Method == "POST" && (r.Path.EndsWith("/contacts") || r.Path.EndsWith("/products")));
    }

    [Fact]
    public async Task Check_Should_Report_Job_Error_As_Rejection()
    {
        _server.On("GET", "/sales_invoices/inv-1", HttpStatusCode.OK, """{"data":{"id":"inv-1","relationships":{"active_e_document":{"data":null}}}}""");
        _server.On("GET", "/trackable_jobs/job-1", HttpStatusCode.OK, """{"data":{"id":"job-1","attributes":{"status":"error","errors":["Alıcı adresi zorunlu"]}}}""");

        var result = await Provider().CheckAsync(
            Account(), new EDocumentProviderRefs(EDocumentType.EArchive, Guid.NewGuid(), "inv-1", null, "job-1"), CancellationToken.None);

        result.Error!.Kind.Should().Be(EDocumentErrorKind.ProviderRejected);
        result.Error.Message.Should().Contain("Alıcı adresi zorunlu");
    }

    [Fact]
    public async Task Expired_Token_Should_Be_Refreshed_And_Returned()
    {
        _server.On("POST", "/token", HttpStatusCode.OK, """{"access_token":"access-2","refresh_token":"refresh-2","expires_in":7200}""");
        _server.On("GET", "/e_invoice_inboxes", HttpStatusCode.OK, """{"data":[{"attributes":{"e_invoice_address":"urn:mail:defaultpk@x.com"}}]}""");

        var result = await Provider().FindEInvoiceRecipientAsync(Account(Now.UtcDateTime.AddMinutes(-1)), "1234567890", CancellationToken.None);

        result.Value.Should().Be("urn:mail:defaultpk@x.com");
        result.RefreshedCredentials.Should().Contain("access-2").And.Contain("refresh-2");
        _server.Requests.Last().Authorization.Should().Be("Bearer access-2");
        _server.TokenForm!["grant_type"].Should().Be("refresh_token");
        _server.TokenForm["refresh_token"].Should().Be("refresh-1");
    }

    [Fact]
    public async Task Rejected_Refresh_Should_Be_Authorization_Error()
    {
        _server.On("POST", "/token", HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");

        var result = await Provider().VerifyAsync(Account(Now.UtcDateTime.AddMinutes(-1)), CancellationToken.None);

        result.Error!.Kind.Should().Be(EDocumentErrorKind.Authorization);
        result.Error.ProviderCode.Should().Be("invalid_grant");
    }

    [Theory]
    [InlineData(HttpStatusCode.PaymentRequired, EDocumentErrorKind.AccountSuspended)]
    [InlineData(HttpStatusCode.TooManyRequests, EDocumentErrorKind.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, EDocumentErrorKind.Transient)]
    [InlineData(HttpStatusCode.UnprocessableEntity, EDocumentErrorKind.InvalidData)]
    public async Task Errors_Should_Be_Classified(HttpStatusCode status, EDocumentErrorKind kind)
    {
        _server.On("GET", "/e_invoice_inboxes", status, """{"errors":[{"title":"x","detail":"ayrıntı"}]}""");

        var result = await Provider().FindEInvoiceRecipientAsync(Account(), "1234567890", CancellationToken.None);

        result.Error!.Kind.Should().Be(kind);
    }

    [Fact]
    public async Task Rate_Limit_Should_Be_Retried_Within_Request()
    {
        _server.OnSequence("GET", "/e_invoice_inboxes",
            (HttpStatusCode.TooManyRequests, """{"errors":[{"title":"Too Many Requests"}]}"""),
            (HttpStatusCode.OK, """{"data":[]}"""));

        var result = await Provider().FindEInvoiceRecipientAsync(Account(), "1234567890", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Should().BeNull();
        _server.Requests.Count(r => r.Path.EndsWith("/e_invoice_inboxes")).Should().Be(2);
    }

    [Fact]
    public async Task Cancel_Should_Cancel_Sales_Invoice()
    {
        _server.On("DELETE", "/sales_invoices/inv-1/cancel", HttpStatusCode.OK, """{"data":{"id":"inv-1","attributes":{"item_type":"cancelled"}}}""");

        var result = await Provider().CancelAsync(
            Account(), new EDocumentProviderRefs(EDocumentType.ESmm, Guid.NewGuid(), "inv-1", "smm-1", null), CancellationToken.None);

        result.Value!.State.Should().Be(ProviderCancelState.Cancelled);
    }

    [Fact]
    public async Task Pdf_Should_Be_Downloaded_From_Signed_Url_Or_Not_Ready_On_204()
    {
        _server.On("GET", "/e_smms/smm-1/pdf", HttpStatusCode.OK, """{"data":{"attributes":{"url":"https://files.test/doc.pdf"}}}""");
        _server.On("GET", "https://files.test/doc.pdf", HttpStatusCode.OK, "%PDF-1.4");
        _server.On("GET", "/e_archives/arc-1/pdf", HttpStatusCode.NoContent, null);

        var ready = await Provider().GetPdfAsync(
            Account(), new EDocumentProviderRefs(EDocumentType.ESmm, Guid.NewGuid(), "inv-1", "smm-1", null), CancellationToken.None);
        var notReady = await Provider().GetPdfAsync(
            Account(), new EDocumentProviderRefs(EDocumentType.EArchive, Guid.NewGuid(), "inv-1", "arc-1", null), CancellationToken.None);

        Encoding.ASCII.GetString(ready.Value!.Content!).Should().Be("%PDF-1.4");
        _server.Requests.Single(r => r.Path == "https://files.test/doc.pdf").Authorization.Should().BeNull();
        notReady.Value!.IsReady.Should().BeFalse();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Yol sonekine göre yanıt veren sahte Paraşüt; gelen istekleri kaydeder.</summary>
    private sealed class FakeParasut : HttpMessageHandler
    {
        private readonly List<(string Method, string Suffix, HttpStatusCode Status, string? Body)> _routes = [];

        public List<(string Method, string Path, string? Authorization, string? Body)> Requests { get; } = [];
        public Dictionary<string, string>? TokenForm { get; private set; }

        private readonly Dictionary<string, Queue<(HttpStatusCode Status, string? Body)>> _sequences = [];

        public void On(string method, string suffix, HttpStatusCode status, string? body) => _routes.Add((method, suffix, status, body));

        /// <summary>Aynı uca sırayla farklı yanıtlar (ör. önce 429, sonra 200).</summary>
        public void OnSequence(string method, string suffix, params (HttpStatusCode Status, string? Body)[] responses)
            => _sequences[$"{method} {suffix}"] = new Queue<(HttpStatusCode, string?)>(responses);

        public JsonNode? Body(string method, string suffix)
            => Requests.Where(r => r.Method == method && r.Path.EndsWith(suffix)).Select(r => r.Body is null ? null : JsonNode.Parse(r.Body)).LastOrDefault();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.Host == "files.test" ? request.RequestUri.ToString() : request.RequestUri.AbsolutePath;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            if (path.EndsWith("/token"))
                TokenForm = body!.Split('&').Select(p => p.Split('=')).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
            else
                Requests.Add((request.Method.Method, path, request.Headers.Authorization?.ToString(), body));

            var sequence = _sequences.FirstOrDefault(s => s.Key.StartsWith(request.Method.Method + " ") && path.EndsWith(s.Key[(request.Method.Method.Length + 1)..]) && s.Value.Count > 0);
            if (sequence.Value is { } queue)
            {
                var (status, text) = queue.Dequeue();
                var queued = new HttpResponseMessage(status);
                if (status == HttpStatusCode.TooManyRequests)
                    queued.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10));
                if (text is not null)
                    queued.Content = new StringContent(text, Encoding.UTF8, "application/json");
                return queued;
            }

            var route = _routes.LastOrDefault(r => r.Method == request.Method.Method && path.EndsWith(r.Suffix));
            if (route.Suffix is null)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            var response = new HttpResponseMessage(route.Status);
            if (route.Body is not null)
                response.Content = new StringContent(route.Body, Encoding.UTF8, "application/json");
            return response;
        }
    }
}
