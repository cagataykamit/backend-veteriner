using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.EDocuments;

/// <summary>
/// ADR-036 Dilim 3–4 uçtan uca (HTTP + LocalDB, sahte sağlayıcı): bağlantı (şifreli kimlik bilgisi yanıtta yok), önizleme
/// grupları, karma seçim reddi, e-SMM kesme, requestId tekrarı, belgeli ücret kilidi, e-Arşiv asenkron sonuç, iptal ve
/// ücretin serbest kalması, e-Fatura mükellef yönlendirmesi, PDF. Bkz. docs/E_DOCUMENTS_API_CONTRACT.md.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class EDocumentsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Tckn = "10000000146";
    private const string EInvoiceVkn = "1234567890";

    private static readonly string[] Permissions =
    [
        PermissionCatalog.Clients.Read, PermissionCatalog.Clients.Create, PermissionCatalog.Clients.Update,
        PermissionCatalog.Clinics.Read, PermissionCatalog.Clinics.Update,
        PermissionCatalog.Charges.Read, PermissionCatalog.Charges.Create, PermissionCatalog.Charges.Correct,
        PermissionCatalog.EDocuments.Read, PermissionCatalog.EDocuments.Issue, PermissionCatalog.EDocuments.Cancel,
        PermissionCatalog.Payments.Read, PermissionCatalog.Payments.Create, PermissionCatalog.Payments.Correct,
    ];

    private readonly FakeProvider _provider = new();
    private readonly KeyProvider _keyProvider = new();
    private readonly WebApplicationFactory<global::Program> _factory;

    public EDocumentsIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEDocumentProvider>();
            services.RemoveAll<IEDocumentCredentialProvider>();
            services.AddSingleton<IEDocumentProvider>(_provider);
            services.AddSingleton<IEDocumentProvider>(_keyProvider);
            services.AddSingleton<IEDocumentCredentialProvider>(_keyProvider);
        }));
    }

    [Fact]
    public async Task Api_Key_Connection_Should_Verify_Key_Store_It_Encrypted_And_Never_Return_It()
    {
        var ctx = await SeedAsync();
        var url = $"/api/v1/clinics/{ctx.ClinicId}/e-document-connection";

        var options = await ctx.GetAsync(url);
        options.GetProperty("availableProviders").EnumerateArray()
            .Single(p => p.GetProperty("provider").GetString() == KeyProvider.Code)
            .GetProperty("connectionMethod").GetString().Should().Be("Credentials");

        var wrong = await ctx.Http.PostAsJsonAsync($"{url}/credentials", new { provider = KeyProvider.Code, apiKey = "yanlis-anahtar" });
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("API anahtarı geçersiz").And.NotContain("yanlis-anahtar");
        (await ctx.GetAsync(url)).GetProperty("provider").GetString().Should().Be(FakeProvider.Code, "geçersiz anahtar mevcut bağlantıyı bozmaz");

        var oauthOnly = await ctx.Http.PostAsJsonAsync($"{url}/credentials", new { provider = FakeProvider.Code, apiKey = KeyProvider.ValidKey });
        oauthOnly.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var connected = await ctx.Http.PostAsJsonAsync($"{url}/credentials", new { provider = KeyProvider.Code, apiKey = $"  {KeyProvider.ValidKey}  " });
        var text = await connected.Content.ReadAsStringAsync();
        connected.StatusCode.Should().Be(HttpStatusCode.OK, text);
        text.Should().NotContain(KeyProvider.ValidKey);
        var dto = JsonDocument.Parse(text).RootElement;
        dto.GetProperty("status").GetString().Should().Be("Connected");
        dto.GetProperty("provider").GetString().Should().Be(KeyProvider.Code);
        dto.GetProperty("accountId").GetString().Should().Be("1234567801");
        dto.GetProperty("lastVerificationOk").GetBoolean().Should().BeTrue();

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Set<EDocumentConnection>().AsNoTracking().SingleAsync(c => c.ClinicId == ctx.ClinicId);
        stored.ProtectedCredentials.Should().NotBeNullOrEmpty().And.NotContain(KeyProvider.ValidKey);
        scope.ServiceProvider.GetRequiredService<ISecretProtector>()
            .Unprotect(EDocumentProviderGateway.CredentialsPurpose, stored.ProtectedCredentials!).Should().Be(KeyProvider.ValidKey);
        var payloads = await db.AuditLogs.AsNoTracking()
            .Where(a => a.ActorUserId == ctx.UserId && a.Action == "Clinic.EDocumentConnection.Connect")
            .Select(a => a.RequestPayload)
            .ToListAsync();
        payloads.Should().NotBeEmpty().And.AllSatisfy(p => p.Should().NotContain(KeyProvider.ValidKey).And.NotContain("yanlis-anahtar"));
    }

    [Fact]
    public async Task Issue_Flow_Should_Bind_Charges_Replay_RequestId_Lock_Charge_And_Release_On_Cancel()
    {
        var ctx = await SeedAsync();
        var clientId = await ctx.CreateClientAsync(new { personType = "Individual", nationalId = Tckn });
        var service = await ctx.ChargeAsync(clientId, 1200m, "Service");
        var product = await ctx.ChargeAsync(clientId, 300m, "Product");

        var connection = await ctx.Http.GetAsync($"/api/v1/clinics/{ctx.ClinicId}/e-document-connection");
        var connectionBody = await connection.Content.ReadAsStringAsync();
        connection.StatusCode.Should().Be(HttpStatusCode.OK, connectionBody);
        JsonDocument.Parse(connectionBody).RootElement.GetProperty("status").GetString().Should().Be("Connected");
        connectionBody.Should().NotContain("fake-credentials").And.NotContain("protectedCredentials");

        var preview = await ctx.PostAsync("/api/v1/e-documents/preview", new { clinicId = ctx.ClinicId, chargeIds = new[] { service, product } });
        var groups = preview.GetProperty("groups").EnumerateArray().ToList();
        groups.Select(g => g.GetProperty("documentType").GetString()).Should().Equal("ESmm", "EArchive");
        preview.GetProperty("buyer").GetProperty("identityMasked").GetString().Should().Be("100******46");

        var mixed = await ctx.Http.PostAsJsonAsync("/api/v1/e-documents", new { requestId = Guid.NewGuid(), clinicId = ctx.ClinicId, chargeIds = new[] { service, product } });
        (await IntegrationTestProblemDetails.ReadCodeAsync(mixed)).Should().Be("EDocuments.MixedDocumentTypes");

        var requestId = Guid.NewGuid();
        var issued = await ctx.PostAsync("/api/v1/e-documents", new { requestId, clinicId = ctx.ClinicId, chargeIds = new[] { service } });
        issued.GetProperty("documentType").GetString().Should().Be("ESmm");
        issued.GetProperty("status").GetString().Should().Be("Issued");
        issued.GetProperty("isTest").GetBoolean().Should().BeTrue();
        var documentId = issued.GetProperty("id").GetGuid();

        var replay = await ctx.PostAsync("/api/v1/e-documents", new { requestId, clinicId = ctx.ClinicId, chargeIds = new[] { service } });
        replay.GetProperty("id").GetGuid().Should().Be(documentId);
        _provider.IssueCount.Should().Be(1);

        var again = await ctx.Http.PostAsJsonAsync("/api/v1/e-documents", new { requestId = Guid.NewGuid(), clinicId = ctx.ClinicId, chargeIds = new[] { service } });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(again)).Should().Be("EDocuments.ChargeAlreadyDocumented");

        (await ctx.GetAsync($"/api/v1/charges/{service}")).GetProperty("eDocumentId").GetGuid().Should().Be(documentId);
        var voidAttempt = await ctx.Http.PostAsJsonAsync($"/api/v1/charges/{service}/void", new { reason = "Yanlış ücret" });
        voidAttempt.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(voidAttempt)).Should().Be("Charges.EDocumentAttached");

        var pdf = await ctx.Http.GetAsync($"/api/v1/e-documents/{documentId}/pdf");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var archive = await ctx.PostAsync("/api/v1/e-documents", new { requestId = Guid.NewGuid(), clinicId = ctx.ClinicId, chargeIds = new[] { product } });
        archive.GetProperty("documentType").GetString().Should().Be("EArchive");
        archive.GetProperty("status").GetString().Should().Be("Sending");
        var refreshed = await ctx.PostAsync($"/api/v1/e-documents/{archive.GetProperty("id").GetGuid()}/refresh", new { });
        refreshed.GetProperty("status").GetString().Should().Be("Issued");
        refreshed.GetProperty("documentNumber").GetString().Should().Be("ARC-1");

        var cancelled = await ctx.PostAsync($"/api/v1/e-documents/{documentId}/cancel", new { reason = "Yanlış müşteri" });
        cancelled.GetProperty("status").GetString().Should().Be("Cancelled");
        (await ctx.GetAsync($"/api/v1/charges/{service}")).GetProperty("eDocumentId").ValueKind.Should().Be(JsonValueKind.Null);

        var undocumented = await ctx.GetAsync($"/api/v1/e-documents/undocumented-charges?clinicId={ctx.ClinicId}&clientId={clientId}");
        undocumented.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("chargeId").GetGuid()).Should().Equal(service);

        var list = await ctx.GetAsync($"/api/v1/e-documents?clinicId={ctx.ClinicId}&clientId={clientId}");
        list.GetProperty("totalItems").GetInt32().Should().Be(2);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.AsNoTracking().Where(a => a.ActorUserId == ctx.UserId).Select(a => a.Action).ToListAsync();
        audit.Should().Contain(["EDocument.Issue", "EDocument.Cancel"]);
    }

    [Fact]
    public async Task Corporate_EInvoice_User_Should_Get_EInvoice_And_Missing_Tax_Number_Should_Be_Reported()
    {
        var ctx = await SeedAsync();
        var corporate = await ctx.CreateClientAsync(new { personType = "Corporate", taxNumber = EInvoiceVkn, taxOffice = "Kadıköy", legalName = "Örnek Ltd." });
        var charge = await ctx.ChargeAsync(corporate, 500m, "Product");

        var preview = await ctx.PostAsync("/api/v1/e-documents/preview", new { clinicId = ctx.ClinicId, chargeIds = new[] { charge } });
        preview.GetProperty("groups")[0].GetProperty("documentType").GetString().Should().Be("EInvoice");

        var issued = await ctx.PostAsync("/api/v1/e-documents", new { requestId = Guid.NewGuid(), clinicId = ctx.ClinicId, chargeIds = new[] { charge } });
        issued.GetProperty("documentType").GetString().Should().Be("EInvoice");
        _provider.LastRecipient.Should().Be("urn:mail:defaultpk@ornek.test");

        var noTax = await ctx.CreateClientAsync(new { personType = "Corporate", legalName = "Vergisiz Ltd." });
        var noTaxCharge = await ctx.ChargeAsync(noTax, 100m, "Product");
        var rejected = await ctx.Http.PostAsJsonAsync("/api/v1/e-documents", new { requestId = Guid.NewGuid(), clinicId = ctx.ClinicId, chargeIds = new[] { noTaxCharge } });
        var rejectedBody = await rejected.Content.ReadAsStringAsync();
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest, rejectedBody);
        JsonDocument.Parse(rejectedBody).RootElement.GetProperty("missingFields")[0].GetString().Should().Be("taxNumber");
    }

    [Fact]
    public async Task Without_Connection_Issue_Should_Be_Rejected_And_Disconnect_Should_Clear()
    {
        var ctx = await SeedAsync();
        var clientId = await ctx.CreateClientAsync(null);
        var charge = await ctx.ChargeAsync(clientId, 100m, "Service");

        var disconnect = await ctx.Http.DeleteAsync($"/api/v1/clinics/{ctx.ClinicId}/e-document-connection");
        disconnect.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var issue = await ctx.Http.PostAsJsonAsync("/api/v1/e-documents", new { requestId = Guid.NewGuid(), clinicId = ctx.ClinicId, chargeIds = new[] { charge } });
        issue.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(issue)).Should().Be("EDocuments.NotConnected");
    }

    [Fact]
    public async Task Payment_Should_Issue_Only_Fully_Paid_Charges_Once_And_Settings_Should_Report_Document_Kinds()
    {
        var ctx = await SeedAsync();
        var settings = await ctx.PutAsync($"/api/v1/clinics/{ctx.ClinicId}/e-document-settings", new { autoIssueMode = "Off", endOfDayTime = "22:30", issueOnPaymentByDefault = true });
        settings.GetProperty("issueOnPaymentByDefault").GetBoolean().Should().BeTrue();
        settings.GetProperty("endOfDayTime").GetString().Should().Be("22:30");
        settings.GetProperty("serviceDocument").GetString().Should().Be("ESmm");
        settings.GetProperty("productDocument").GetString().Should().Be("Invoice");

        var clientId = await ctx.CreateClientAsync(new { personType = "Individual", nationalId = Tckn });
        var full = await ctx.ChargeAsync(clientId, 1200m, "Service");
        var partial = await ctx.ChargeAsync(clientId, 500m, "Service");
        var paymentId = await ctx.PayAsync(clientId, full, 1200m);
        var partialPayment = await ctx.PayAsync(clientId, partial, 100m);

        var before = _provider.IssueCount;
        var result = await ctx.PostAsync("/api/v1/e-documents/from-payment", new { paymentId });
        result.GetProperty("documents").GetArrayLength().Should().Be(1, result.ToString());
        var doc = result.GetProperty("documents")[0];
        doc.GetProperty("origin").GetString().Should().Be("Payment");
        doc.GetProperty("documentType").GetString().Should().Be("ESmm");

        var again = await ctx.PostAsync("/api/v1/e-documents/from-payment", new { paymentId });
        again.GetProperty("documents")[0].GetProperty("id").GetGuid().Should().Be(doc.GetProperty("id").GetGuid());
        _provider.IssueCount.Should().Be(before + 1);

        var none = await ctx.PostAsync("/api/v1/e-documents/from-payment", new { paymentId = partialPayment });
        none.GetProperty("documents").GetArrayLength().Should().Be(0);
        var undocumented = await ctx.GetAsync($"/api/v1/e-documents/undocumented-charges?clinicId={ctx.ClinicId}&clientId={clientId}");
        var row = undocumented.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("chargeId").GetGuid().Should().Be(partial);
        row.GetProperty("reason").GetString().Should().Be("Ödeme bekliyor.");
    }

    [Fact]
    public async Task Automation_Should_Issue_Paid_Charges_Once_Report_Failures_And_Send_Daily_Summary()
    {
        var ctx = await SeedAsync();
        await ctx.PutAsync($"/api/v1/clinics/{ctx.ClinicId}/e-document-settings", new { autoIssueMode = "EndOfDay", endOfDayTime = "00:00", issueOnPaymentByDefault = false });

        var good = await ctx.CreateClientAsync(new { personType = "Individual", nationalId = Tckn });
        var goodCharge = await ctx.ChargeAsync(good, 400m, "Service");
        await ctx.PayAsync(good, goodCharge, 400m);
        var unpaid = await ctx.ChargeAsync(good, 50m, "Service");
        var bad = await ctx.CreateClientAsync(null);
        await RenameClientAsync(bad, "Hatalı Adres Müşteri");
        var badCharge = await ctx.ChargeAsync(bad, 250m, "Service");
        await ctx.PayAsync(bad, badCharge, 250m);

        var processor = _factory.Services.GetRequiredService<Backend.Veteriner.Infrastructure.EDocuments.EDocumentAutomationProcessor>();
        await processor.RunOnceAsync(CancellationToken.None);
        var issuedAfterFirst = _provider.IssueCount;
        await processor.RunOnceAsync(CancellationToken.None);
        _provider.IssueCount.Should().Be(issuedAfterFirst, "gün sonu günde bir kez çalışır; aynı ücret ikinci kez kesilmez");

        var goodDoc = (await ctx.GetAsync($"/api/v1/charges/{goodCharge}")).GetProperty("eDocumentId").GetGuid();
        var detail = await ctx.GetAsync($"/api/v1/e-documents/{goodDoc}");
        detail.GetProperty("origin").GetString().Should().Be("Automatic");
        detail.GetProperty("status").GetString().Should().Be("Issued");
        (await ctx.GetAsync($"/api/v1/charges/{unpaid}")).GetProperty("eDocumentId").ValueKind.Should().Be(JsonValueKind.Null);

        var manual = await ctx.Http.PostAsJsonAsync("/api/v1/e-documents", new { requestId = Guid.NewGuid(), clinicId = ctx.ClinicId, chargeIds = new[] { goodCharge } });
        (await IntegrationTestProblemDetails.ReadCodeAsync(manual)).Should().Be("EDocuments.ChargeAlreadyDocumented");

        var undocumented = await ctx.GetAsync($"/api/v1/e-documents/undocumented-charges?clinicId={ctx.ClinicId}&clientId={bad}");
        var failedRow = undocumented.GetProperty("items").EnumerateArray().Single();
        failedRow.GetProperty("reason").GetString().Should().Contain("Alıcı adresi zorunlu");
        failedRow.GetProperty("failedDocumentId").ValueKind.Should().Be(JsonValueKind.String);

        var reminders = await ctx.GetAsync($"/api/v1/e-documents/reminders?clinicId={ctx.ClinicId}");
        reminders.GetProperty("connected").GetBoolean().Should().BeTrue();
        reminders.GetProperty("failedCount").GetInt32().Should().Be(1);

        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var summaries = await db.UserNotifications.AsNoTracking()
            .Where(n => n.UserId == ctx.UserId && n.Type == Backend.Veteriner.Domain.Notifications.UserNotificationType.EDocumentsDailySummary)
            .ToListAsync();
        summaries.Should().ContainSingle().Which.Body.Should().Contain("1 belge otomatik kesildi").And.Contain("1 belge kesilemedi");
        (await db.AuditLogs.AsNoTracking().CountAsync(a => a.Action == "EDocument.AutoIssue" && a.TargetId == goodDoc.ToString())).Should().Be(1);
    }

    [Fact]
    public async Task Imported_Opening_Balance_Should_Stay_Out_Of_Every_EDocument_Flow()
    {
        var ctx = await SeedAsync();
        await ctx.PutAsync($"/api/v1/clinics/{ctx.ClinicId}/e-document-settings", new { autoIssueMode = "EndOfDay", endOfDayTime = "00:00", issueOnPaymentByDefault = true });
        var clientId = await ctx.CreateClientAsync(new { personType = "Individual", nationalId = Tckn });
        var devir = await AddOpeningBalanceAsync(ctx, clientId, 300m);
        var paymentId = await ctx.PayAsync(clientId, devir, 300m);

        var fromPayment = await ctx.PostAsync("/api/v1/e-documents/from-payment", new { paymentId });
        fromPayment.GetProperty("documents").GetArrayLength().Should().Be(0);

        var manual = await ctx.Http.PostAsJsonAsync("/api/v1/e-documents", new { requestId = Guid.NewGuid(), clinicId = ctx.ClinicId, chargeIds = new[] { devir } });
        manual.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await IntegrationTestProblemDetails.ReadCodeAsync(manual)).Should().Be("EDocuments.ImportedCharge");

        var before = _provider.IssueCount;
        await _factory.Services.GetRequiredService<Backend.Veteriner.Infrastructure.EDocuments.EDocumentAutomationProcessor>().RunOnceAsync(CancellationToken.None);
        _provider.IssueCount.Should().Be(before);
        (await ctx.GetAsync($"/api/v1/charges/{devir}")).GetProperty("eDocumentId").ValueKind.Should().Be(JsonValueKind.Null);

        var undocumented = await ctx.GetAsync($"/api/v1/e-documents/undocumented-charges?clinicId={ctx.ClinicId}&clientId={clientId}");
        undocumented.GetProperty("items").GetArrayLength().Should().Be(0, "devir bakiyesi belgesiz sayılmaz (ADR-048 T3)");
        var reminders = await ctx.GetAsync($"/api/v1/e-documents/reminders?clinicId={ctx.ClinicId}");
        reminders.GetProperty("undocumentedPaidCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Auto_Issue_Should_Pause_On_Account_Wide_Error_Notify_And_Resume_When_Settings_Saved()
    {
        var ctx = await SeedAsync();
        await ctx.PutAsync($"/api/v1/clinics/{ctx.ClinicId}/e-document-settings", new { autoIssueMode = "OnPayment", endOfDayTime = "23:59", issueOnPaymentByDefault = false });

        var broke = await ctx.CreateClientAsync(new { personType = "Individual", nationalId = Tckn });
        await RenameClientAsync(broke, "Kontörsüz Müşteri");
        var brokeCharge = await ctx.ChargeAsync(broke, 100m, "Service");
        await ctx.PayAsync(broke, brokeCharge, 100m);
        var waiting = await ctx.CreateClientAsync(new { personType = "Individual", nationalId = Tckn });
        var waitingCharge = await ctx.ChargeAsync(waiting, 200m, "Service");
        await ctx.PayAsync(waiting, waitingCharge, 200m);

        var processor = _factory.Services.GetRequiredService<Backend.Veteriner.Infrastructure.EDocuments.EDocumentAutomationProcessor>();
        await processor.RunOnceAsync(CancellationToken.None);

        var settings = await ctx.GetAsync($"/api/v1/clinics/{ctx.ClinicId}/e-document-settings");
        settings.GetProperty("autoIssuePausedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        settings.GetProperty("autoIssuePauseReason").GetString().Should().Contain("Kontör");
        (await ctx.GetAsync($"/api/v1/charges/{waitingCharge}")).GetProperty("eDocumentId").ValueKind
            .Should().Be(JsonValueKind.Null, "kalıcı hesap hatasından sonra tur durur, sıradaki ücret aynı hatayı almaz");

        var before = _provider.IssueCount;
        await processor.RunOnceAsync(CancellationToken.None);
        _provider.IssueCount.Should().Be(before, "durdurulmuş otomatik kesme yeni belge göndermez");

        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.UserNotifications.AsNoTracking().CountAsync(n =>
                    n.UserId == ctx.UserId && n.Type == Backend.Veteriner.Domain.Notifications.UserNotificationType.EDocumentsAutoIssuePaused))
                .Should().Be(1, "durdurma bir kez bildirilir");
        }

        var resumed = await ctx.PutAsync($"/api/v1/clinics/{ctx.ClinicId}/e-document-settings", new { autoIssueMode = "OnPayment", endOfDayTime = "23:59", issueOnPaymentByDefault = false });
        resumed.GetProperty("autoIssuePausedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
        await processor.RunOnceAsync(CancellationToken.None);
        (await ctx.GetAsync($"/api/v1/charges/{waitingCharge}")).GetProperty("eDocumentId").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Document_Should_Never_Go_To_Another_Provider_After_Connection_Switch()
    {
        var ctx = await SeedAsync();
        var clientId = await ctx.CreateClientAsync(new { personType = "Individual", nationalId = Tckn });
        var product = await ctx.ChargeAsync(clientId, 300m, "Product");
        var document = await ctx.PostAsync("/api/v1/e-documents", new { requestId = Guid.NewGuid(), clinicId = ctx.ClinicId, chargeIds = new[] { product } });
        document.GetProperty("status").GetString().Should().Be("Sending");
        var documentId = document.GetProperty("id").GetGuid();

        // Klinik, belge sonucu beklenirken başka sağlayıcıya geçti.
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            var connection = await db.EDocumentConnections.SingleAsync(c => c.ClinicId == ctx.ClinicId);
            connection.Authorize(
                KeyProvider.Code, EDocumentEnvironment.Test, protector.Protect(EDocumentProviderGateway.CredentialsPurpose, KeyProvider.ValidKey),
                "1234567801", "Test Kurum", ctx.UserId, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        var refreshed = await ctx.PostAsync($"/api/v1/e-documents/{documentId}/refresh", new { });
        refreshed.GetProperty("status").GetString().Should().Be("Failed");
        refreshed.GetProperty("lastError").GetProperty("message").GetString().Should().Contain("Sahte Sağlayıcı").And.Contain("başka bir entegratöre");

        // Yeniden deneme de yeni sağlayıcıya gitmez (KeyProvider.IssueAsync çağrılsa NotSupportedException olurdu).
        var retried = await ctx.PostAsync($"/api/v1/e-documents/{documentId}/retry", new { });
        retried.GetProperty("status").GetString().Should().Be("Failed");
    }

    [Fact]
    public async Task Voiding_Payment_Of_Documented_Charge_Should_Keep_Document_And_Notify()
    {
        var ctx = await SeedAsync();
        var clientId = await ctx.CreateClientAsync(new { personType = "Individual", nationalId = Tckn });
        var service = await ctx.ChargeAsync(clientId, 500m, "Service");
        var paymentId = await ctx.PayAsync(clientId, service, 500m);
        var issued = await ctx.PostAsync("/api/v1/e-documents", new { requestId = Guid.NewGuid(), clinicId = ctx.ClinicId, chargeIds = new[] { service } });
        issued.GetProperty("status").GetString().Should().Be("Issued");
        var documentId = issued.GetProperty("id").GetGuid();

        var voided = await ctx.Http.PostAsJsonAsync($"/api/v1/payments/{paymentId}/void", new { reason = "Yanlış kayıt" });
        voided.IsSuccessStatusCode.Should().BeTrue(await voided.Content.ReadAsStringAsync());

        (await ctx.GetAsync($"/api/v1/e-documents/{documentId}")).GetProperty("status").GetString()
            .Should().Be("Issued", "ödeme iptali belgeyi kendiliğinden iptal etmez");
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notice = await db.UserNotifications.AsNoTracking().SingleAsync(n =>
            n.UserId == ctx.UserId && n.Type == Backend.Veteriner.Domain.Notifications.UserNotificationType.EDocumentPaymentVoided);
        notice.TargetId.Should().Be(documentId);
        notice.Body.Should().Contain("SMM-1");
    }

    /// <summary>İçe aktarma partisiyle devir ücreti (ADR-048 T3); içe aktarma sihirbazının yazdığı kaydın aynısı.</summary>
    private async Task<Guid> AddOpeningBalanceAsync(TestContext ctx, Guid clientId, decimal amount)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = (await db.Clinics.AsNoTracking().SingleAsync(c => c.Id == ctx.ClinicId)).TenantId;
        var batch = Backend.Veteriner.Domain.Imports.ImportBatch.Create(tenantId, ctx.ClinicId, Guid.NewGuid(), "Devir", ctx.UserId, DateTime.UtcNow).Value!;
        db.ImportBatches.Add(batch);
        var charge = Backend.Veteriner.Domain.Charges.Charge.CreateOpeningBalance(
            tenantId, ctx.ClinicId, clientId, "Devir bakiyesi", amount, ctx.UserId, DateTime.UtcNow, Guid.NewGuid(), batch.Id).Value!;
        db.Charges.Add(charge);
        await db.SaveChangesAsync();
        return charge.Id;
    }

    private async Task RenameClientAsync(Guid clientId, string name)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Clients SET FullName = {name} WHERE Id = {clientId}");
    }

    private async Task<TestContext> SeedAsync()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid clinicId;
        Guid userId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            var tenant = await db.Tenants.SingleAsync(t => t.Name == DataSeeder.DefaultTenantName);
            var clinic = new Clinic(tenant.Id, $"Edoc-{Guid.NewGuid():N}"[..14], "Istanbul");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            email = $"edoc-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinic.Id, DateTime.UtcNow));

            db.ClinicTaxpayerSettings.Add(ClinicTaxpayerSettings.Create(
                tenant.Id, clinic.Id, ClinicTaxpayerType.Both, null, null, "Edoc Klinik", null, null, null, 20m, 20m, user.Id, DateTime.UtcNow).Value!);
            var connection = EDocumentConnection.Create(tenant.Id, clinic.Id, FakeProvider.Code, EDocumentEnvironment.Test, DateTime.UtcNow);
            connection.Authorize(
                FakeProvider.Code, EDocumentEnvironment.Test,
                protector.Protect(EDocumentProviderGateway.CredentialsPurpose, "fake-credentials"),
                "acc-1", "Sahte Hesap", user.Id, DateTime.UtcNow);
            db.EDocumentConnections.Add(connection);
            await db.SaveChangesAsync();

            clinicId = clinic.Id;
            userId = user.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, Permissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new TestContext(http, clinicId, userId);
    }

    private sealed record TestContext(HttpClient Http, Guid ClinicId, Guid UserId)
    {
        public async Task<Guid> CreateClientAsync(object? invoicingIdentity)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/clients", new { fullName = $"Belge Müşteri {Guid.NewGuid():N}"[..24], invoicingIdentity });
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Created, body);
            return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
        }

        public async Task<Guid> ChargeAsync(Guid clientId, decimal amount, string lineKind)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/charges", new
            {
                clinicId = ClinicId, clientId, description = lineKind == "Service" ? "Muayene" : "Mama", quantity = 1m, unitPrice = amount,
                tax = new { lineKind, vatRate = (decimal?)null },
            });
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Created, body);
            return JsonDocument.Parse(body).RootElement.GetProperty("charge").GetProperty("id").GetGuid();
        }

        public async Task<Guid> PayAsync(Guid clientId, Guid chargeId, decimal amount)
        {
            var response = await Http.PostAsJsonAsync("/api/v1/payments", new
            {
                clinicId = ClinicId, clientId, amount, currency = "TRY", method = 0, paidAtUtc = DateTime.UtcNow, requestId = Guid.NewGuid(),
                allocations = new[] { new { chargeId, amount } },
            });
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Created, body);
            var root = JsonDocument.Parse(body).RootElement;
            return root.ValueKind == JsonValueKind.String ? root.GetGuid() : root.GetProperty("id").GetGuid();
        }

        public async Task<JsonElement> PutAsync(string url, object body)
        {
            var response = await Http.PutAsJsonAsync(url, body);
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, text);
            return JsonDocument.Parse(text).RootElement;
        }

        public async Task<JsonElement> PostAsync(string url, object body)
        {
            var response = await Http.PostAsJsonAsync(url, body);
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, text);
            return JsonDocument.Parse(text).RootElement;
        }

        public async Task<JsonElement> GetAsync(string url)
        {
            var response = await Http.GetAsync(url);
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, text);
            return JsonDocument.Parse(text).RootElement;
        }
    }

    /// <summary>API anahtarıyla bağlanan sağlayıcı: yalnız <see cref="ValidKey"/> geçerli, hesabı VKN'dir.</summary>
    private sealed class KeyProvider : IEDocumentProvider, IEDocumentCredentialProvider
    {
        public const string Code = "anahtarli";
        public const string ValidKey = "gecerli-test-anahtari";

        public string ProviderCode => Code;
        public string DisplayName => "Anahtarlı Sağlayıcı";
        public bool IsConfigured => true;
        public EDocumentEnvironment Environment => EDocumentEnvironment.Test;

        public Task<ProviderResult<ProviderAccountInfo>> IdentifyAsync(string credentials, CancellationToken ct)
            => Task.FromResult(credentials == ValidKey
                ? ProviderResult<ProviderAccountInfo>.Ok(new ProviderAccountInfo("1234567801", "Test Kurum", "1234567801"))
                : ProviderResult<ProviderAccountInfo>.Fail(new ProviderError(EDocumentErrorKind.Authorization, "API anahtarı geçersiz.")));

        public Task<ProviderResult<ProviderAccountCheck>> VerifyAsync(EDocumentProviderAccount account, CancellationToken ct)
            => Task.FromResult(account.Credentials == ValidKey
                ? ProviderResult<ProviderAccountCheck>.Ok(new ProviderAccountCheck("Test Kurum", null))
                : ProviderResult<ProviderAccountCheck>.Fail(new ProviderError(EDocumentErrorKind.Authorization, "API anahtarı geçersiz.")));

        public Task<ProviderResult<string?>> FindEInvoiceRecipientAsync(EDocumentProviderAccount account, string taxNumber, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<ProviderResult<ProviderIssueOutcome>> IssueAsync(EDocumentProviderAccount account, EDocumentSendRequest request, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<ProviderResult<ProviderIssueOutcome>> CheckAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<ProviderResult<ProviderCancelOutcome>> CancelAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<ProviderResult<bool>> DiscardDraftAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<ProviderResult<ProviderPdf>> GetPdfAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
            => throw new NotSupportedException();
    }

    /// <summary>Sağlayıcı davranışını taklit eder: e-SMM eşzamanlı kesilir, e-Arşiv / e-Fatura sonucu sonradan gelir.</summary>
    private sealed class FakeProvider : IEDocumentProvider
    {
        public const string Code = "parasut";
        private int _issueCount;

        public int IssueCount => _issueCount;
        public string? LastRecipient { get; private set; }

        public string ProviderCode => Code;
        public string DisplayName => "Sahte Sağlayıcı";
        public bool IsConfigured => true;
        public EDocumentEnvironment Environment => EDocumentEnvironment.Test;

        public Task<ProviderResult<ProviderAccountCheck>> VerifyAsync(EDocumentProviderAccount account, CancellationToken ct)
            => Task.FromResult(ProviderResult<ProviderAccountCheck>.Ok(new ProviderAccountCheck("Sahte Hesap", true)));

        public Task<ProviderResult<string?>> FindEInvoiceRecipientAsync(EDocumentProviderAccount account, string taxNumber, CancellationToken ct)
            => Task.FromResult(ProviderResult<string?>.Ok(taxNumber == EInvoiceVkn ? "urn:mail:defaultpk@ornek.test" : null));

        public Task<ProviderResult<ProviderIssueOutcome>> IssueAsync(EDocumentProviderAccount account, EDocumentSendRequest request, CancellationToken ct)
        {
            account.Credentials.Should().Be("fake-credentials");
            Interlocked.Increment(ref _issueCount);
            LastRecipient = request.EInvoiceRecipient;
            if (request.Buyer.Name.Contains("Kontörsüz", StringComparison.Ordinal))
                return Task.FromResult(ProviderResult<ProviderIssueOutcome>.Fail(new ProviderError(EDocumentErrorKind.AccountSuspended, "Kontör bitti; kontör yükleyin.", "2000")));
            if (request.Buyer.Name.Contains("Hatalı", StringComparison.Ordinal))
                return Task.FromResult(ProviderResult<ProviderIssueOutcome>.Fail(new ProviderError(EDocumentErrorKind.InvalidData, "Alıcı adresi zorunlu.", "422")));
            var invoiceId = $"inv-{request.ExternalReference:N}";
            return Task.FromResult(ProviderResult<ProviderIssueOutcome>.Ok(request.Type == EDocumentType.ESmm
                ? new ProviderIssueOutcome(ProviderIssueState.Issued, invoiceId, "smm-1", null, "SMM-1", "uuid-1", 1200m)
                : ProviderIssueOutcome.Pending(invoiceId, "job-1")));
        }

        public Task<ProviderResult<ProviderIssueOutcome>> CheckAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
            => Task.FromResult(ProviderResult<ProviderIssueOutcome>.Ok(
                new ProviderIssueOutcome(ProviderIssueState.Issued, refs.ProviderInvoiceId, "arc-1", null, "ARC-1", "uuid-2", 300m)));

        public Task<ProviderResult<ProviderCancelOutcome>> CancelAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
            => Task.FromResult(ProviderResult<ProviderCancelOutcome>.Ok(new ProviderCancelOutcome(ProviderCancelState.Cancelled)));

        public Task<ProviderResult<bool>> DiscardDraftAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
            => Task.FromResult(ProviderResult<bool>.Ok(true));

        public Task<ProviderResult<ProviderPdf>> GetPdfAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
            => Task.FromResult(ProviderResult<ProviderPdf>.Ok(new ProviderPdf("%PDF-1.4"u8.ToArray(), "belge.pdf")));
    }
}
