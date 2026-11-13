using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.EDocuments;

namespace Backend.Veteriner.Infrastructure.EDocuments.Parasut;

/// <summary>
/// Paraşüt v4 e-belge adaptörü (devhub.parasut.com/openapi.json). Akış: alıcı cari → satış faturası (<c>external_reference_key</c>
/// = belgenin dış referansı, tekrarında mevcut fatura) → e-SMM (eşzamanlı) / e-Arşiv / e-Fatura (iş takibi) →
/// <c>active_e_document</c>. İptal satış faturası iptalidir. Paraşüt'e özgü her şey bu sınıfta kalır.
/// </summary>
public sealed class ParasutEDocumentProvider : IEDocumentProvider, IEDocumentOAuthProvider
{
    public const string Code = "parasut";

    private readonly ParasutClient _client;

    public ParasutEDocumentProvider(ParasutClient client) => _client = client;

    public string ProviderCode => Code;
    public string DisplayName => "Paraşüt";
    public bool IsConfigured => _client.Options.IsConfigured;
    public EDocumentEnvironment Environment => _client.Options.Environment;

    /// <summary>İptal satış faturası iptaliyle yapılır; üç tür için de geçerli.</summary>
    public bool CanCancel(EDocumentType type) => true;

    public string BuildAuthorizationUrl(string state, string codeChallenge) => _client.AuthorizationUrl(state, codeChallenge);

    public async Task<ProviderResult<string>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
    {
        try
        {
            var credentials = await _client.ExchangeCodeAsync(code, codeVerifier, ct);
            return ProviderResult<string>.Ok(credentials.Serialize());
        }
        catch (ParasutException ex)
        {
            return ProviderResult<string>.Fail(ex.Error);
        }
    }

    public Task<ProviderResult<IReadOnlyList<ProviderAccountInfo>>> ListAccountsAsync(string credentials, CancellationToken ct)
        => RunAsync<IReadOnlyList<ProviderAccountInfo>>(credentials, async session =>
        {
            var companies = await CompaniesAsync(session, ct);
            return companies.Select(c => new ProviderAccountInfo(c.Id, c.Name, c.TaxNumber)).ToList();
        });

    public Task<ProviderResult<ProviderAccountCheck>> VerifyAsync(EDocumentProviderAccount account, CancellationToken ct)
        => RunAsync(account.Credentials, async session =>
        {
            var company = (await CompaniesAsync(session, ct)).FirstOrDefault(c => c.Id == account.AccountId)
                ?? throw new ParasutException(new ProviderError(
                    EDocumentErrorKind.ProviderRejected, "Bağlı Paraşüt kullanıcısı bu firmaya artık erişemiyor; hesabı yeniden bağlayın."));
            return new ProviderAccountCheck(company.Name, company.EDocumentsEnabled);
        });

    public Task<ProviderResult<string?>> FindEInvoiceRecipientAsync(EDocumentProviderAccount account, string taxNumber, CancellationToken ct)
        => RunAsync(account.Credentials, async session =>
        {
            var response = await _client.SendAsync(
                session, HttpMethod.Get, $"/{account.AccountId}/e_invoice_inboxes?filter[vkn]={Uri.EscapeDataString(taxNumber)}",
                null, "/{company}/e_invoice_inboxes", ct);
            EnsureSuccess(response, "e-Fatura mükellef sorgusu");
            return response.Body?["data"] is JsonArray { Count: > 0 } inboxes
                ? inboxes[0]?["attributes"]?["e_invoice_address"]?.ToString()
                : null;
        });

    public Task<ProviderResult<ProviderIssueOutcome>> IssueAsync(EDocumentProviderAccount account, EDocumentSendRequest request, CancellationToken ct)
        => RunAsync(account.Credentials, async session =>
        {
            var company = account.AccountId;
            var invoiceId = request.ExistingInvoiceId ?? await FindInvoiceByReferenceAsync(session, company, request.ExternalReference, ct);
            if (invoiceId is null)
            {
                var contactId = await EnsureContactAsync(session, company, request.Buyer, ct);
                invoiceId = await CreateSalesInvoiceAsync(session, company, request, contactId, ct);
            }

            // Önceki denemede e-belge oluşmuşsa yeniden oluşturulmaz (çift belge yok).
            var existing = await ReadActiveDocumentAsync(session, company, invoiceId, ct);
            if (existing is not null)
                return existing;

            return request.Type switch
            {
                EDocumentType.ESmm => await CreateESmmAsync(session, company, invoiceId, request, ct),
                EDocumentType.EArchive => await CreateAsyncDocumentAsync(
                    session, company, invoiceId, "e_archives",
                    new JsonObject { ["sales_invoice"] = Relation("sales_invoices", invoiceId) },
                    new JsonObject(), ct),
                EDocumentType.EInvoice => await CreateAsyncDocumentAsync(
                    session, company, invoiceId, "e_invoices",
                    new JsonObject { ["invoice"] = Relation("sales_invoices", invoiceId) },
                    new JsonObject { ["scenario"] = "basic", ["to"] = request.EInvoiceRecipient }, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Type, null),
            };
        });

    public Task<ProviderResult<ProviderIssueOutcome>> CheckAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
        => RunAsync(account.Credentials, async session =>
        {
            var company = account.AccountId;
            var invoiceId = refs.ProviderInvoiceId ?? await FindInvoiceByReferenceAsync(session, company, refs.ExternalReference, ct);
            if (invoiceId is null)
                return ProviderIssueOutcome.Pending(null, refs.ProviderJobId);

            var active = await ReadActiveDocumentAsync(session, company, invoiceId, ct);
            if (active is not null)
                return active;
            if (refs.ProviderJobId is null)
                return ProviderIssueOutcome.Pending(invoiceId, null);

            var job = await _client.SendAsync(session, HttpMethod.Get, $"/{company}/trackable_jobs/{refs.ProviderJobId}", null, "/{company}/trackable_jobs/{id}", ct);
            if (job.Status == HttpStatusCode.NotFound)
                return ProviderIssueOutcome.Pending(invoiceId, refs.ProviderJobId);
            EnsureSuccess(job, "iş durumu sorgusu");

            var attributes = job.Body?["data"]?["attributes"];
            var status = attributes?["status"]?.ToString();
            if (status == "error")
            {
                var errors = attributes?["errors"] is JsonArray list
                    ? string.Join("; ", list.Select(e => e?.ToString()).Where(e => !string.IsNullOrWhiteSpace(e)))
                    : null;
                throw new ParasutException(new ProviderError(
                    EDocumentErrorKind.ProviderRejected,
                    string.IsNullOrWhiteSpace(errors) ? "Paraşüt belgeyi oluşturamadı." : $"Paraşüt belgeyi oluşturamadı: {errors}",
                    "job-error"));
            }

            // "done" ile belge arasındaki kısa gecikmede bir sonraki kontrolde belge okunur.
            return ProviderIssueOutcome.Pending(invoiceId, refs.ProviderJobId);
        });

    public Task<ProviderResult<ProviderCancelOutcome>> CancelAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
        => RunAsync(account.Credentials, async session =>
        {
            var company = account.AccountId;
            var invoiceId = refs.ProviderInvoiceId ?? await FindInvoiceByReferenceAsync(session, company, refs.ExternalReference, ct)
                ?? throw new ParasutException(new ProviderError(EDocumentErrorKind.ProviderRejected, "Paraşüt'te fatura kaydı bulunamadı."));

            var response = await _client.SendAsync(session, HttpMethod.Delete, $"/{company}/sales_invoices/{invoiceId}/cancel", null, "/{company}/sales_invoices/{id}/cancel", ct);
            if (response.Status == HttpStatusCode.UnprocessableEntity && await IsCancelledAsync(session, company, invoiceId, ct))
                return new ProviderCancelOutcome(ProviderCancelState.Cancelled);
            EnsureSuccess(response, "iptal");
            return new ProviderCancelOutcome(ProviderCancelState.Cancelled);
        });

    public Task<ProviderResult<bool>> DiscardDraftAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
        => RunAsync(account.Credentials, async session =>
        {
            var company = account.AccountId;
            var invoiceId = refs.ProviderInvoiceId ?? await FindInvoiceByReferenceAsync(session, company, refs.ExternalReference, ct);
            if (invoiceId is null)
                return true;
            if (await ReadActiveDocumentAsync(session, company, invoiceId, ct) is not null)
                throw new ParasutException(new ProviderError(EDocumentErrorKind.ProviderRejected, "Faturanın e-belgesi oluşmuş; silinmedi."));

            var response = await _client.SendAsync(session, HttpMethod.Delete, $"/{company}/sales_invoices/{invoiceId}", null, "/{company}/sales_invoices/{id}", ct);
            if (response.Status != HttpStatusCode.NotFound)
                EnsureSuccess(response, "taslak fatura silme");
            return true;
        });

    public Task<ProviderResult<ProviderPdf>> GetPdfAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
        => RunAsync(account.Credentials, async session =>
        {
            if (refs.ProviderDocumentId is null)
                return ProviderPdf.NotReady;
            var resource = ResourceName(refs.Type);
            var response = await _client.SendAsync(
                session, HttpMethod.Get, $"/{account.AccountId}/{resource}/{refs.ProviderDocumentId}/pdf", null, $"/{{company}}/{resource}/{{id}}/pdf", ct);
            if (response.Status == HttpStatusCode.NoContent)
                return ProviderPdf.NotReady;
            EnsureSuccess(response, "PDF");

            var url = response.Body?["data"]?["attributes"]?["url"]?.ToString();
            if (string.IsNullOrWhiteSpace(url))
                return ProviderPdf.NotReady;
            var bytes = await _client.DownloadAsync(url, ct);
            return new ProviderPdf(bytes, "belge.pdf");
        });

    private async Task<ProviderIssueOutcome> CreateESmmAsync(
        ParasutSession session, string company, string invoiceId, EDocumentSendRequest request, CancellationToken ct)
    {
        var attributes = new JsonObject();
        if (!string.IsNullOrWhiteSpace(request.Note))
            attributes["note"] = request.Note;
        var body = Document("e_smms", attributes, new JsonObject { ["sales_invoice"] = Relation("sales_invoices", invoiceId) });

        var response = await _client.SendAsync(session, HttpMethod.Post, $"/{company}/e_smms", body, "/{company}/e_smms", ct);
        EnsureSuccess(response, "e-SMM");
        var data = response.Body?["data"];
        var id = data?["id"]?.ToString();
        if (string.IsNullOrEmpty(id))
            return ProviderIssueOutcome.Pending(invoiceId, null);
        var total = await ReadInvoiceTotalAsync(session, company, invoiceId, ct);
        return new ProviderIssueOutcome(
            ProviderIssueState.Issued, invoiceId, id, null,
            data?["attributes"]?["invoice_number"]?.ToString(), data?["attributes"]?["uuid"]?.ToString(), total);
    }

    private async Task<ProviderIssueOutcome> CreateAsyncDocumentAsync(
        ParasutSession session, string company, string invoiceId, string resource, JsonObject relationships, JsonObject attributes, CancellationToken ct)
    {
        var response = await _client.SendAsync(
            session, HttpMethod.Post, $"/{company}/{resource}", Document(resource, attributes, relationships), $"/{{company}}/{resource}", ct);
        EnsureSuccess(response, resource == "e_invoices" ? "e-Fatura" : "e-Arşiv");
        var jobId = response.Body?["data"]?["type"]?.ToString() == "trackable_jobs" ? response.Body?["data"]?["id"]?.ToString() : null;
        return ProviderIssueOutcome.Pending(invoiceId, jobId);
    }

    /// <summary>Faturanın etkin e-belgesi: varsa duruma göre kesildi / bekliyor; e-Fatura reddi ve e-Arşiv geri dönmesi hatadır.</summary>
    private async Task<ProviderIssueOutcome?> ReadActiveDocumentAsync(ParasutSession session, string company, string invoiceId, CancellationToken ct)
    {
        var response = await _client.SendAsync(
            session, HttpMethod.Get, $"/{company}/sales_invoices/{invoiceId}?include=active_e_document", null, "/{company}/sales_invoices/{id}", ct);
        EnsureSuccess(response, "fatura sorgusu");

        var invoice = response.Body?["data"];
        var reference = invoice?["relationships"]?["active_e_document"]?["data"];
        var documentId = reference?["id"]?.ToString();
        if (string.IsNullOrEmpty(documentId))
            return null;

        var type = reference?["type"]?.ToString();
        var included = (response.Body?["included"] as JsonArray)?
            .FirstOrDefault(n => n?["id"]?.ToString() == documentId && n?["type"]?.ToString() == type);
        var attributes = included?["attributes"];
        var status = attributes?["status"]?.ToString();
        var number = attributes?["invoice_number"]?.ToString() ?? invoice?["attributes"]?["invoice_no"]?.ToString();
        var uuid = attributes?["uuid"]?.ToString();
        var total = Decimal(invoice?["attributes"]?["net_total"]);

        if (type == "e_invoices" && status == "failed")
        {
            throw new ParasutException(new ProviderError(
                EDocumentErrorKind.ProviderRejected,
                $"e-Fatura GİB tarafından reddedildi: {attributes?["status_message"]?.ToString() ?? "ayrıntı yok"}",
                attributes?["status_code"]?.ToString()));
        }

        if (type == "e_archives" && status == "bounced")
            throw new ParasutException(new ProviderError(EDocumentErrorKind.ProviderRejected, "e-Arşiv belgesi geri döndü (bounced).", "bounced"));

        if (type == "e_invoices" && status == "waiting")
            return new ProviderIssueOutcome(ProviderIssueState.Pending, invoiceId, null, null, number, uuid, total);

        return new ProviderIssueOutcome(ProviderIssueState.Issued, invoiceId, documentId, null, number, uuid, total);
    }

    private async Task<decimal?> ReadInvoiceTotalAsync(ParasutSession session, string company, string invoiceId, CancellationToken ct)
    {
        var response = await _client.SendAsync(session, HttpMethod.Get, $"/{company}/sales_invoices/{invoiceId}", null, "/{company}/sales_invoices/{id}", ct);
        return response.Status == HttpStatusCode.OK ? Decimal(response.Body?["data"]?["attributes"]?["net_total"]) : null;
    }

    private async Task<bool> IsCancelledAsync(ParasutSession session, string company, string invoiceId, CancellationToken ct)
    {
        var response = await _client.SendAsync(session, HttpMethod.Get, $"/{company}/sales_invoices/{invoiceId}", null, "/{company}/sales_invoices/{id}", ct);
        return response.Status == HttpStatusCode.OK && response.Body?["data"]?["attributes"]?["item_type"]?.ToString() == "cancelled";
    }

    private async Task<string?> FindInvoiceByReferenceAsync(ParasutSession session, string company, Guid reference, CancellationToken ct)
    {
        var response = await _client.SendAsync(
            session, HttpMethod.Get, $"/{company}/sales_invoices/find_by_external_reference_key?external_reference_key={reference:D}",
            null, "/{company}/sales_invoices/find_by_external_reference_key", ct);
        if (response.Status == HttpStatusCode.NotFound)
            return null;
        EnsureSuccess(response, "fatura arama");
        return response.Body?["data"]?["id"]?.ToString();
    }

    /// <summary>Bilinen kimlikli alıcı vergi/TC numarasıyla aranır; bulunamazsa veya kimlik bilinmiyorsa yeni cari açılır.</summary>
    private async Task<string> EnsureContactAsync(ParasutSession session, string company, EDocumentSendBuyer buyer, CancellationToken ct)
    {
        if (!buyer.IsUnknownBuyer)
        {
            var found = await _client.SendAsync(
                session, HttpMethod.Get, $"/{company}/contacts?filter[tax_number]={Uri.EscapeDataString(buyer.IdentityNumber)}&page[size]=1",
                null, "/{company}/contacts", ct);
            EnsureSuccess(found, "cari arama");
            if (found.Body?["data"] is JsonArray { Count: > 0 } list && list[0]?["id"]?.ToString() is { Length: > 0 } existingId)
                return existingId;
        }

        var attributes = new JsonObject
        {
            ["name"] = buyer.Name,
            ["account_type"] = "customer",
            ["contact_type"] = buyer.Kind == EDocumentBuyerKind.Corporate ? "company" : "person",
            ["tax_number"] = buyer.IdentityNumber,
        };
        Set(attributes, "tax_office", buyer.TaxOffice);
        Set(attributes, "city", buyer.Province);
        Set(attributes, "district", buyer.District);
        Set(attributes, "address", buyer.AddressLine);
        Set(attributes, "email", buyer.Email);

        var created = await _client.SendAsync(session, HttpMethod.Post, $"/{company}/contacts", Document("contacts", attributes, null), "/{company}/contacts", ct);
        EnsureSuccess(created, "cari oluşturma");
        return created.Body?["data"]?["id"]?.ToString()
            ?? throw new ParasutException(new ProviderError(EDocumentErrorKind.Unknown, "Paraşüt cari kaydı oluşturulamadı."));
    }

    private async Task<string> CreateSalesInvoiceAsync(
        ParasutSession session, string company, EDocumentSendRequest request, string contactId, CancellationToken ct)
    {
        var attributes = new JsonObject
        {
            ["item_type"] = "invoice",
            ["issue_date"] = request.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["currency"] = "TRL",
            ["description"] = "Vetinity",
            ["tax_number"] = request.Buyer.IdentityNumber,
        };
        Set(attributes, "tax_office", request.Buyer.TaxOffice);
        Set(attributes, "city", request.Buyer.Province);
        Set(attributes, "district", request.Buyer.District);
        Set(attributes, "billing_address", request.Buyer.AddressLine);
        Set(attributes, "invoice_note", request.Note);
        if (request.WithholdingRate is { } withholding)
            attributes["withholding_rate"] = withholding;

        // Paraşüt satırda ürün/hizmet kaydı ister ("Ürün/hizmet doldurulmalı"); satır adı ürün adı olur.
        var products = new Dictionary<string, string>(StringComparer.Ordinal);
        var details = new JsonArray();
        foreach (var line in request.Lines)
        {
            var productId = await EnsureProductAsync(session, company, line, products, ct);
            details.Add(new JsonObject
            {
                ["type"] = "sales_invoice_details",
                ["attributes"] = new JsonObject
                {
                    ["quantity"] = line.Quantity,
                    ["unit_price"] = line.NetUnitPrice,
                    ["vat_rate"] = line.VatRate,
                    ["description"] = line.Description,
                },
                ["relationships"] = new JsonObject { ["product"] = Relation("products", productId) },
            });
        }

        var relationships = new JsonObject
        {
            ["contact"] = Relation("contacts", contactId),
            ["details"] = new JsonObject { ["data"] = details },
            ["external_reference_key"] = new JsonObject
            {
                ["data"] = new JsonObject
                {
                    ["type"] = "external_reference_keys",
                    ["attributes"] = new JsonObject { ["key"] = request.ExternalReference.ToString("D") },
                },
            },
        };

        var response = await _client.SendAsync(
            session, HttpMethod.Post, $"/{company}/sales_invoices", Document("sales_invoices", attributes, relationships), "/{company}/sales_invoices", ct);

        // Aynı anahtarla önceden oluşmuş fatura: 422 + meta.sales_invoice_id (mesaj metnine değil bu alana güvenilir).
        if (response.Status == HttpStatusCode.UnprocessableEntity
            && response.Body?["errors"] is JsonArray errors
            && errors.Select(e => e?["meta"]?["sales_invoice_id"]?.ToString()).FirstOrDefault(id => !string.IsNullOrEmpty(id)) is { } existing)
        {
            return existing;
        }

        EnsureSuccess(response, "satış faturası");
        return response.Body?["data"]?["id"]?.ToString()
            ?? throw new ParasutException(new ProviderError(EDocumentErrorKind.Unknown, "Paraşüt fatura kaydı oluşturulamadı."));
    }

    /// <summary>
    /// Satırın ürün/hizmet kaydı: açıklamadan türetilen sabit kodla (<c>VTN-…</c>) aranır, yoksa oluşturulur. Aynı açıklama her
    /// belgede aynı kayda düşer; kliniğin Paraşüt'teki kendi ürünlerine dokunulmaz.
    /// </summary>
    private async Task<string> EnsureProductAsync(
        ParasutSession session, string company, EDocumentSendLine line, Dictionary<string, string> cache, CancellationToken ct)
    {
        var name = line.Description.Trim();
        var code = ProductCode(name);
        if (cache.TryGetValue(code, out var cached))
            return cached;

        var found = await _client.SendAsync(
            session, HttpMethod.Get, $"/{company}/products?filter[code]={Uri.EscapeDataString(code)}&page[size]=1", null, "/{company}/products", ct);
        EnsureSuccess(found, "ürün arama");
        if (found.Body?["data"] is JsonArray { Count: > 0 } list && list[0]?["id"]?.ToString() is { Length: > 0 } existingId)
            return cache[code] = existingId;

        var attributes = new JsonObject
        {
            ["name"] = name.Length > 255 ? name[..255] : name,
            ["code"] = code,
            ["vat_rate"] = line.VatRate,
            ["currency"] = "TRL",
        };
        Set(attributes, "unit", line.Unit);
        var created = await _client.SendAsync(session, HttpMethod.Post, $"/{company}/products", Document("products", attributes, null), "/{company}/products", ct);
        EnsureSuccess(created, "ürün oluşturma");
        var id = created.Body?["data"]?["id"]?.ToString()
            ?? throw new ParasutException(new ProviderError(EDocumentErrorKind.Unknown, "Paraşüt ürün kaydı oluşturulamadı."));
        return cache[code] = id;
    }

    /// <summary>Açıklamadan türetilen sabit ürün kodu (aynı açıklama → aynı Paraşüt ürünü).</summary>
    public static string ProductCode(string name)
        => "VTN-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant())))[..12];

    private async Task<IReadOnlyList<ParasutCompany>> CompaniesAsync(ParasutSession session, CancellationToken ct)
    {
        var response = await _client.SendAsync(session, HttpMethod.Get, "/me?include=companies", null, "/me", ct);
        EnsureSuccess(response, "kullanıcı bilgisi");
        return ((response.Body?["included"] as JsonArray) ?? [])
            .Where(n => n?["type"]?.ToString() == "companies")
            .Select(n =>
            {
                var a = n!["attributes"];
                var name = a?["name"]?.ToString();
                var legal = a?["legal_name"]?.ToString();
                return new ParasutCompany(
                    n["id"]!.ToString(),
                    string.IsNullOrWhiteSpace(legal) ? name ?? n["id"]!.ToString() : legal,
                    a?["tax_number"]?.ToString(),
                    a?["e_invoicing_activated_at"] is null ? null : !string.IsNullOrEmpty(a["e_invoicing_activated_at"]?.ToString()));
            })
            .ToList();
    }

    private async Task<ProviderResult<T>> RunAsync<T>(string credentials, Func<ParasutSession, Task<T>> action)
    {
        var parsed = ParasutCredentials.Parse(credentials);
        if (parsed is null)
            return ProviderResult<T>.Fail(new ProviderError(EDocumentErrorKind.Authorization, "Paraşüt bağlantı bilgisi okunamadı; hesabı yeniden bağlayın."));

        var session = new ParasutSession(parsed);
        try
        {
            var value = await action(session);
            return ProviderResult<T>.Ok(value, Refreshed(session));
        }
        catch (ParasutException ex)
        {
            return ProviderResult<T>.Fail(ex.Error, Refreshed(session));
        }
    }

    private static string? Refreshed(ParasutSession session) => session.Refreshed ? session.Credentials.Serialize() : null;

    private static void EnsureSuccess(ParasutResponse response, string action)
    {
        if ((int)response.Status is >= 200 and < 300)
            return;
        throw new ParasutException(ParasutClient.ToError(response, action));
    }

    private static string ResourceName(EDocumentType type) => type switch
    {
        EDocumentType.ESmm => "e_smms",
        EDocumentType.EArchive => "e_archives",
        EDocumentType.EInvoice => "e_invoices",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static JsonObject Document(string type, JsonObject? attributes, JsonObject? relationships)
    {
        var data = new JsonObject { ["type"] = type };
        if (attributes is not null)
            data["attributes"] = attributes;
        if (relationships is not null)
            data["relationships"] = relationships;
        return new JsonObject { ["data"] = data };
    }

    private static JsonObject Relation(string type, string id)
        => new() { ["data"] = new JsonObject { ["type"] = type, ["id"] = id } };

    private static void Set(JsonObject target, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            target[name] = value;
    }

    private static decimal? Decimal(JsonNode? node)
    {
        if (node is null)
            return null;
        return decimal.TryParse(node.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private sealed record ParasutCompany(string Id, string Name, string? TaxNumber, bool? EDocumentsEnabled);
}
