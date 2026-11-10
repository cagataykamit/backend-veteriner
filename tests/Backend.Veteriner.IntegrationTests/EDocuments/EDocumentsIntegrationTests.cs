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
    ];

    private readonly FakeProvider _provider = new();
    private readonly WebApplicationFactory<global::Program> _factory;

    public EDocumentsIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEDocumentProvider>();
            services.AddSingleton<IEDocumentProvider>(_provider);
        }));
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
