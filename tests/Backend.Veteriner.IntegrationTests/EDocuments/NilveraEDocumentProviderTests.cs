using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.EDocuments.Automation;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Infrastructure.EDocuments.Nilvera;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.IntegrationTests.EDocuments;

/// <summary>
/// Nilvera adaptörü sahte HTTP sunucusuna karşı (apitest.nilvera.com swagger uçları): API anahtarıyla firma tanıma, seri seçimi,
/// e-Arşiv / e-SMM / e-Fatura JSON modeli, ETTN ile tekrar koruması (409 → durum), durum takibi, iptal, PDF ve hata sınıfları.
/// </summary>
public sealed class NilveraEDocumentProviderTests
{
    private const string ApiKey = "test-anahtar";
    private const string Reference = "3b241101-e2bb-4255-8caf-4136c566a962";

    private readonly FakeNilvera _server = new();

    private NilveraEDocumentProvider Provider()
    {
        var options = Options.Create(new NilveraOptions { ApiBaseUrl = "https://nilvera.test", Environment = EDocumentEnvironment.Test });
        var client = new NilveraClient(new StubHttpClientFactory(_server), options, TimeProvider.System, NullLogger<NilveraClient>.Instance);
        return new NilveraEDocumentProvider(client);
    }

    private static EDocumentProviderAccount Account() => new("1234567801", ApiKey);

    private static EDocumentSendRequest Request(EDocumentType type, decimal? withholding = null, string? recipient = null)
        => new(
            type,
            Guid.Parse(Reference),
            new DateTime(2026, 11, 12, 14, 35, 7),
            new EDocumentSendBuyer(EDocumentBuyerKind.Individual, "Ayşe Yılmaz", "10000000146", false, null, "İstanbul", "Kadıköy", null, "ayse@example.com"),
            [new EDocumentSendLine("Muayene", 1m, null, 1000m, 20m), new EDocumentSendLine("Aşı", 2m, "Adet", 250m, 10m)],
            withholding,
            "Hasta: Pamuk",
            recipient,
            null);

    private static EDocumentProviderRefs Refs(EDocumentType type) => new(type, Guid.Parse(Reference), Reference, null, Reference);

    private void SeriesExist(string api)
        => _server.On("GET", $"/{api}/Series", HttpStatusCode.OK,
            """{"Content":[{"Name":"ESK","IsActive":false,"IsDefault":true},{"Name":"VET","IsActive":true,"IsDefault":true},{"Name":"ABC","IsActive":true,"IsDefault":false}]}""");

    [Fact]
    public async Task Identify_Should_Read_Company_With_Bearer_Key()
    {
        _server.On("GET", "/general/Company", HttpStatusCode.OK, """{"Name":"Test Kurum 1","TaxNumber":"1234567801"}""");

        var result = await Provider().IdentifyAsync(ApiKey, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value!.AccountId.Should().Be("1234567801");
        result.Value.Name.Should().Be("Test Kurum 1");
        _server.Requests.Should().ContainSingle().Which.Authorization.Should().Be($"Bearer {ApiKey}");
    }

    [Fact]
    public async Task Identify_Invalid_Key_Should_Be_Authorization_Error()
    {
        _server.On("GET", "/general/Company", HttpStatusCode.Unauthorized, null);

        var result = await Provider().IdentifyAsync(ApiKey, CancellationToken.None);

        result.Error!.Kind.Should().Be(EDocumentErrorKind.Authorization);
    }

    [Fact]
    public async Task Verify_Should_Reject_Key_Of_Another_Company()
    {
        _server.On("GET", "/general/Company", HttpStatusCode.OK, """{"Name":"Başka","TaxNumber":"1234567802"}""");

        var result = await Provider().VerifyAsync(Account(), CancellationToken.None);

        result.Error!.Kind.Should().Be(EDocumentErrorKind.ProviderRejected);
    }

    [Fact]
    public async Task Issue_EArchive_Should_Send_Model_With_Reference_Default_Series_And_Totals()
    {
        SeriesExist("earchive");
        _server.On("POST", "/earchive/Send/Model", HttpStatusCode.OK, $$"""{"UUID":"{{Reference}}","InvoiceNumber":"VET2026000000001"}""");

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.EArchive), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value!.State.Should().Be(ProviderIssueState.Pending);
        result.Value.ProviderJobId.Should().Be(Reference);
        result.Value.ProviderInvoiceId.Should().Be(Reference);

        var invoice = _server.Body("POST", "/earchive/Send/Model")!["ArchiveInvoice"]!;
        var info = invoice["InvoiceInfo"]!;
        info["UUID"]!.ToString().Should().Be(Reference);
        info["InvoiceSerieOrNumber"]!.ToString().Should().Be("VET");
        info["InvoiceType"]!.ToString().Should().Be("SATIS");
        info["SendType"]!.ToString().Should().Be("ELEKTRONIK");
        info["CurrencyCode"]!.ToString().Should().Be("TRY");
        info["IssueDate"]!.ToString().Should().Be("2026-11-12T14:35:07");
        info["LineExtensionAmount"]!.GetValue<decimal>().Should().Be(1500m);
        info["KdvTotal"]!.GetValue<decimal>().Should().Be(250m);
        info["GeneralKDV20Total"]!.GetValue<decimal>().Should().Be(200m);
        info["GeneralKDV10Total"]!.GetValue<decimal>().Should().Be(50m);
        info["PayableAmount"]!.GetValue<decimal>().Should().Be(1750m);
        info["InvoiceProfile"].Should().BeNull();
        invoice["CompanyInfo"].Should().BeNull();

        var line = invoice["InvoiceLines"]![1]!;
        line["Quantity"]!.GetValue<decimal>().Should().Be(2m);
        line["Price"]!.GetValue<decimal>().Should().Be(250m);
        line["UnitType"]!.ToString().Should().Be("C62");
        line["KDVTotal"]!.GetValue<decimal>().Should().Be(50m);

        var customer = invoice["CustomerInfo"]!;
        customer["TaxNumber"]!.ToString().Should().Be("10000000146");
        customer["City"]!.ToString().Should().Be("İstanbul");
        customer["Country"]!.ToString().Should().Be("Türkiye");
        customer["Address"]!.ToString().Should().Be("Kadıköy / İstanbul");
        invoice["Notes"]![0]!.ToString().Should().Be("Hasta: Pamuk");
    }

    [Fact]
    public async Task Issue_EInvoice_Should_Use_Basic_Profile_And_Recipient_Alias()
    {
        SeriesExist("einvoice");
        _server.On("POST", "/einvoice/Send/Model", HttpStatusCode.OK, $$"""{"UUID":"{{Reference}}","InvoiceNumber":"VEF2026000000001"}""");

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.EInvoice, recipient: "urn:mail:defaultpk@nilvera.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        var body = _server.Body("POST", "/einvoice/Send/Model")!;
        body["CustomerAlias"]!.ToString().Should().Be("urn:mail:defaultpk@nilvera.com");
        body["EInvoice"]!["InvoiceInfo"]!["InvoiceProfile"]!.ToString().Should().Be("TEMELFATURA");
        body["EInvoice"]!["InvoiceInfo"]!["SendType"].Should().BeNull();
    }

    [Fact]
    public async Task Issue_ESmm_Should_Write_Gross_Wage_And_Income_Tax_Withholding()
    {
        SeriesExist("evoucher");
        _server.On("POST", "/evoucher/Send/Model", HttpStatusCode.OK, $$"""{"UUID":"{{Reference}}","VoucherNumber":"VET2026000000001"}""");

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.ESmm, withholding: 20m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        var voucher = _server.Body("POST", "/evoucher/Send/Model")!["Voucher"]!;
        voucher["VoucherInfo"]!["VoucherSerieOrNumber"]!.ToString().Should().Be("VET");
        var second = voucher["VoucherLines"]![1]!;
        second["Name"]!.ToString().Should().Be("Aşı (2 Adet)");
        second["GrossWage"]!.GetValue<decimal>().Should().Be(500m);
        second["GVWithholdingPercent"]!.GetValue<decimal>().Should().Be(20m);
        second["GVWithholdingTotal"]!.GetValue<decimal>().Should().Be(100m);
        second["KDVTotal"]!.GetValue<decimal>().Should().Be(50m);
    }

    [Fact]
    public async Task Duplicate_Reference_409_Should_Continue_From_Existing_Document()
    {
        SeriesExist("earchive");
        _server.On("POST", "/earchive/Send/Model", HttpStatusCode.Conflict,
            """{"Message":"HATALI ISTEK","Errors":[{"Code":1001,"Description":"ETTN Numarası Sistemde Kayıtlıdır."}]}""");
        _server.On("GET", $"/earchive/Invoices/{Reference}/Status", HttpStatusCode.OK, """{"StatusCode":"succeed","StatusDetail":"Başarılı","CancelStatus":false}""");
        _server.On("GET", $"/earchive/Invoices/{Reference}/Details", HttpStatusCode.OK, """{"InvoiceNumber":"VET2026000000001","InvoiceAmount":1750.0}""");

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.EArchive), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value!.State.Should().Be(ProviderIssueState.Issued);
        result.Value.ProviderDocumentId.Should().Be(Reference);
        result.Value.DocumentUuid.Should().Be(Reference);
        result.Value.DocumentNumber.Should().Be("VET2026000000001");
        result.Value.Total.Should().Be(1750m);
    }

    [Fact]
    public async Task Conflict_409_Without_Document_Should_Show_Provider_Detail()
    {
        SeriesExist("earchive");
        _server.On("POST", "/earchive/Send/Model", HttpStatusCode.Conflict,
            """{"Message":"HATALI ISTEK","Errors":[{"Code":1000,"Description":"VKN/TCKN ile firma uyuşmuyor."}]}""");

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.EArchive), CancellationToken.None);

        result.Error!.Kind.Should().Be(EDocumentErrorKind.InvalidData);
        result.Error.Message.Should().Contain("VKN/TCKN ile firma uyuşmuyor.");
        result.Error.ProviderCode.Should().Be("1000");
    }

    [Fact]
    public async Task Issue_Without_Active_Series_Should_Fail_With_Guidance()
    {
        _server.On("GET", "/earchive/Series", HttpStatusCode.OK, """{"Content":[{"Name":"ESK","IsActive":false,"IsDefault":true}]}""");

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.EArchive), CancellationToken.None);

        result.Error!.Kind.Should().Be(EDocumentErrorKind.InvalidData);
        result.Error.ProviderCode.Should().Be("no-series");
        _server.Requests.Should().NotContain(r => r.Method == "POST");
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2002)]
    [InlineData(2075)]
    [InlineData(2076)]
    [InlineData(2077)]
    [InlineData(2078)]
    public async Task Credit_Or_Package_Errors_Should_Be_Account_Error_That_Pauses_Automation(int code)
    {
        SeriesExist("earchive");
        _server.On("POST", "/earchive/Send/Model", HttpStatusCode.UnprocessableEntity,
            $$"""{"Message":"HATALI ISTEK","Errors":[{"Code":{{code}},"Description":"Kontör paketi."}]}""");

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.EArchive), CancellationToken.None);

        result.Error!.Kind.Should().Be(EDocumentErrorKind.AccountSuspended);
        EDocumentAutoIssueGuard.IsAccountWide(result.Error.Kind).Should().BeTrue();
    }

    [Fact]
    public async Task Key_Without_Scope_Should_Be_Authorization_Error()
    {
        SeriesExist("earchive");
        _server.On("POST", "/earchive/Send/Model", HttpStatusCode.Forbidden, null);

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.EArchive), CancellationToken.None);

        result.Error!.Kind.Should().Be(EDocumentErrorKind.Authorization);
    }

    [Fact]
    public async Task Per_Document_Rejection_Should_Not_Pause_Automation()
    {
        SeriesExist("earchive");
        _server.On("POST", "/earchive/Send/Model", HttpStatusCode.UnprocessableEntity,
            """{"Errors":[{"Code":2015,"Description":"Şema hatası."}]}""");

        var result = await Provider().IssueAsync(Account(), Request(EDocumentType.EArchive), CancellationToken.None);

        EDocumentAutoIssueGuard.IsAccountWide(result.Error!.Kind).Should().BeFalse();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("waiting")]
    public async Task Check_Numbered_ESmm_Should_Be_Issued_Before_Gib_Report(string status)
    {
        // Test ortamında görüldü: kesilen e-SMM / e-Arşiv rapor oluşana dek "unknown" / "Rapor Oluşturulmadı" kalır.
        _server.On("GET", $"/evoucher/Vouchers/{Reference}/Status", HttpStatusCode.OK,
            $$"""{"StatusDetail":"Rapor Oluşturulmadı.","StatusCode":"{{status}}","ReportStatus":"NotReported","CancelStatus":false}""");
        _server.On("GET", $"/evoucher/Vouchers/{Reference}/Details", HttpStatusCode.OK,
            """{"VoucherNumber":"ODO2026000000019","VoucherAmount":3000.00}""");

        var result = await Provider().CheckAsync(Account(), Refs(EDocumentType.ESmm), CancellationToken.None);

        result.Value!.State.Should().Be(ProviderIssueState.Issued);
        result.Value.DocumentNumber.Should().Be("ODO2026000000019");
        result.Value.Total.Should().Be(3000.00m);
    }

    [Fact]
    public async Task Check_EInvoice_Waiting_Should_Stay_Pending()
    {
        _server.On("GET", $"/einvoice/Sale/{Reference}/Status", HttpStatusCode.OK, """{"InvoiceStatus":{"Code":"waiting"}}""");

        var result = await Provider().CheckAsync(Account(), Refs(EDocumentType.EInvoice), CancellationToken.None);

        result.Value!.State.Should().Be(ProviderIssueState.Pending);
        result.Value.ProviderJobId.Should().Be(Reference);
        _server.Requests.Should().NotContain(r => r.Path.Contains("/Details"));
    }

    [Fact]
    public async Task Check_Error_Status_Should_Reject_With_Detail()
    {
        _server.On("GET", $"/earchive/Invoices/{Reference}/Status", HttpStatusCode.OK, """{"StatusCode":"error","StatusDetail":"Şema hatası"}""");

        var result = await Provider().CheckAsync(Account(), Refs(EDocumentType.EArchive), CancellationToken.None);

        result.Error!.Kind.Should().Be(EDocumentErrorKind.ProviderRejected);
        result.Error.Message.Should().Contain("Şema hatası");
    }

    [Fact]
    public async Task Check_EInvoice_Rejected_Answer_Should_Fail()
    {
        _server.On("GET", $"/einvoice/Sale/{Reference}/Status", HttpStatusCode.OK,
            """{"Answer":{"AnswerCode":"rejected","AnswerNote":"Hatalı tutar"},"InvoiceStatus":{"Code":"succeed"}}""");

        var result = await Provider().CheckAsync(Account(), Refs(EDocumentType.EInvoice), CancellationToken.None);

        result.Error!.Message.Should().Contain("reddedildi").And.Contain("Hatalı tutar");
    }

    [Fact]
    public async Task Cancel_Should_Treat_Already_Cancelled_As_Done_And_Retry_Queued()
    {
        _server.On("PUT", $"/earchive/Invoices/{Reference}/Cancel", HttpStatusCode.UnprocessableEntity,
            """{"Errors":[{"Code":2026,"Description":"Daha önce iptal edilmiş."}]}""");
        _server.On("PUT", $"/evoucher/Vouchers/{Reference}/Cancel", HttpStatusCode.UnprocessableEntity,
            """{"Errors":[{"Code":2036,"Description":"Kuyrukta."}]}""");

        var archive = await Provider().CancelAsync(Account(), Refs(EDocumentType.EArchive), CancellationToken.None);
        var voucher = await Provider().CancelAsync(Account(), Refs(EDocumentType.ESmm), CancellationToken.None);

        archive.Value!.State.Should().Be(ProviderCancelState.Cancelled);
        voucher.Value!.State.Should().Be(ProviderCancelState.Pending);
    }

    [Fact]
    public void CanCancel_Should_Exclude_EInvoice()
    {
        Provider().CanCancel(EDocumentType.EInvoice).Should().BeFalse();
        Provider().CanCancel(EDocumentType.EArchive).Should().BeTrue();
        Provider().CanCancel(EDocumentType.ESmm).Should().BeTrue();
    }

    [Fact]
    public async Task Cancel_EInvoice_Should_Be_Rejected_Without_Call()
    {
        var result = await Provider().CancelAsync(Account(), Refs(EDocumentType.EInvoice), CancellationToken.None);

        result.Error!.Kind.Should().Be(EDocumentErrorKind.ProviderRejected);
        _server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Pdf_Should_Accept_Raw_And_Base64_Content()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4 test");
        _server.OnBytes("GET", $"/earchive/Invoices/{Reference}/pdf", pdf, "application/pdf");
        _server.On("GET", $"/evoucher/Vouchers/{Reference}/pdf", HttpStatusCode.OK, $"\"{Convert.ToBase64String(pdf)}\"");

        var raw = await Provider().GetPdfAsync(Account(), Refs(EDocumentType.EArchive), CancellationToken.None);
        var encoded = await Provider().GetPdfAsync(Account(), Refs(EDocumentType.ESmm), CancellationToken.None);

        raw.Value!.Content.Should().Equal(pdf);
        encoded.Value!.Content.Should().Equal(pdf);
    }

    [Fact]
    public async Task Find_Recipient_Should_Skip_Deleted_Alias_And_Treat_404_As_Not_Taxpayer()
    {
        _server.On("GET", "/general/GlobalCompany/GetGlobalCustomerInfo/1234567802", HttpStatusCode.OK,
            """{"Aliases":[{"Name":"urn:mail:eski@nilvera.com","DeletionTime":"2025-01-01T00:00:00"},{"Name":"urn:mail:defaultpk@nilvera.com","DeletionTime":null}]}""");

        var found = await Provider().FindEInvoiceRecipientAsync(Account(), "1234567802", CancellationToken.None);
        var missing = await Provider().FindEInvoiceRecipientAsync(Account(), "10000000146", CancellationToken.None);

        found.Value.Should().Be("urn:mail:defaultpk@nilvera.com");
        missing.IsSuccess.Should().BeTrue();
        missing.Value.Should().BeNull();
    }

    [Fact]
    public async Task Discard_Should_Refuse_When_Document_Is_Issued()
    {
        _server.On("GET", $"/earchive/Invoices/{Reference}/Status", HttpStatusCode.OK, """{"StatusCode":"succeed"}""");

        var result = await Provider().DiscardDraftAsync(Account(), Refs(EDocumentType.EArchive), CancellationToken.None);

        result.Error!.Kind.Should().Be(EDocumentErrorKind.ProviderRejected);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeNilvera : HttpMessageHandler
    {
        private readonly List<(string Method, string Path, HttpStatusCode Status, byte[]? Body, string MediaType)> _routes = [];

        public List<(string Method, string Path, string? Authorization, string? Body)> Requests { get; } = [];

        public void On(string method, string path, HttpStatusCode status, string? body)
            => _routes.Add((method, path, status, body is null ? null : Encoding.UTF8.GetBytes(body), "application/json"));

        public void OnBytes(string method, string path, byte[] body, string mediaType)
            => _routes.Add((method, path, HttpStatusCode.OK, body, mediaType));

        public JsonNode? Body(string method, string path)
            => Requests.Where(r => r.Method == method && r.Path == path).Select(r => r.Body is null ? null : JsonNode.Parse(r.Body)).LastOrDefault();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method.Method, path, request.Headers.Authorization?.ToString(), body));

            var route = _routes.LastOrDefault(r => r.Method == request.Method.Method && r.Path == path);
            if (route.Path is null)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            var response = new HttpResponseMessage(route.Status);
            if (route.Body is not null)
            {
                response.Content = new ByteArrayContent(route.Body);
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(route.MediaType);
            }

            return response;
        }
    }
}
