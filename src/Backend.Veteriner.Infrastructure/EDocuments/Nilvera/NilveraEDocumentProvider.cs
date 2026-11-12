using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.EDocuments;

namespace Backend.Veteriner.Infrastructure.EDocuments.Nilvera;

/// <summary>
/// Nilvera V1 e-belge adaptörü (developer.nilvera.com; şemalar <c>apitest.nilvera.com/{servis}/swagger/v1/swagger.json</c>).
/// Bağlantı API anahtarıyladır; hesap kimliği firmanın VKN/TCKN'sidir. Belge JSON modelle doğrudan gönderilir
/// (<c>/{servis}/Send/Model</c>); ETTN (UUID) belgenin dış referansıdır, tekrar gönderimde aynı ETTN 409 döner ve durum
/// sorgusuyla devam edilir (çift belge yok). Sonuç durum ucundan izlenir (Nilvera'da bildirim yok). Satıcı bilgisi
/// gönderilmez (Nilvera aktif firmayı doldurur); seri, hesaptaki varsayılan etkin seridir. Nilvera'ya özgü her şey bu sınıfta kalır.
/// </summary>
public sealed class NilveraEDocumentProvider : IEDocumentProvider, IEDocumentCredentialProvider
{
    public const string Code = "nilvera";

    /// <summary>422: belge zaten iptal edilmiş (e-Arşiv 2026, e-SMM 2034).</summary>
    private static readonly int[] AlreadyCancelled = [2026, 2034];

    /// <summary>422: kuyrukta / durum bilinmiyor veya iptal kaydı şimdilik oluşturulamadı; sonra yeniden denenir.</summary>
    private static readonly int[] CancelLater = [2028, 2029, 2036, 2037];

    private readonly NilveraClient _client;

    public NilveraEDocumentProvider(NilveraClient client) => _client = client;

    public string ProviderCode => Code;
    public string DisplayName => "Nilvera";
    public bool IsConfigured => _client.Options.IsConfigured;
    public EDocumentEnvironment Environment => _client.Options.Environment;

    public Task<ProviderResult<ProviderAccountInfo>> IdentifyAsync(string credentials, CancellationToken ct)
        => RunAsync(async () =>
        {
            var company = await CompanyAsync(credentials, ct);
            return new ProviderAccountInfo(company.TaxNumber, company.Name, company.TaxNumber);
        });

    public Task<ProviderResult<ProviderAccountCheck>> VerifyAsync(EDocumentProviderAccount account, CancellationToken ct)
        => RunAsync(async () =>
        {
            var company = await CompanyAsync(account.Credentials, ct);
            if (company.TaxNumber != account.AccountId)
            {
                throw new NilveraException(new ProviderError(
                    EDocumentErrorKind.ProviderRejected, "API anahtarı bağlı firmadan farklı bir Nilvera firmasına ait; doğru firmanın anahtarını girin."));
            }

            return new ProviderAccountCheck(company.Name, null);
        });

    public Task<ProviderResult<string?>> FindEInvoiceRecipientAsync(EDocumentProviderAccount account, string taxNumber, CancellationToken ct)
        => RunAsync(async () =>
        {
            var response = await _client.SendAsync(
                account.Credentials, HttpMethod.Get,
                $"/general/GlobalCompany/GetGlobalCustomerInfo/{Uri.EscapeDataString(taxNumber)}?globalUserType=Invoice",
                null, "/general/GlobalCompany/GetGlobalCustomerInfo/{vkn}", ct);
            if (response.Status == HttpStatusCode.NotFound)
                return null;
            EnsureSuccess(response, "e-Fatura mükellef sorgusu");

            // Kullanımdan kalkmış etiket (DeletionTime dolu) alıcı adresi sayılmaz.
            return (response.Body?["Aliases"] as JsonArray)?
                .Where(a => string.IsNullOrEmpty(a?["DeletionTime"]?.ToString()))
                .Select(a => a?["Name"]?.ToString())
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        });

    public Task<ProviderResult<ProviderIssueOutcome>> IssueAsync(EDocumentProviderAccount account, EDocumentSendRequest request, CancellationToken ct)
        => RunAsync(async () =>
        {
            var service = Service.For(request.Type);
            var series = await SeriesAsync(account.Credentials, service, ct);
            var response = await _client.SendAsync(
                account.Credentials, HttpMethod.Post, $"/{service.Api}/Send/Model", BuildModel(request, series),
                $"/{service.Api}/Send/Model", ct);

            if (response.Status == HttpStatusCode.Conflict)
            {
                // Aynı ETTN daha önce gönderilmiş (önceki deneme yanıtı kaybolmuş olabilir): belge Nilvera'da varsa durumundan
                // devam edilir. 409 başka bir çakışmaysa (ör. VKN uyuşmazlığı) belge yoktur ve hata gösterilir.
                var existing = await ReadOutcomeAsync(account.Credentials, request.Type, request.ExternalReference, ct);
                return existing ?? throw new NilveraException(NilveraClient.ToError(response, service.Label));
            }

            EnsureSuccess(response, service.Label);
            var uuid = Reference(request.ExternalReference);
            return ProviderIssueOutcome.Pending(uuid, uuid);
        });

    public Task<ProviderResult<ProviderIssueOutcome>> CheckAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
        => RunAsync(async () =>
        {
            var uuid = Reference(refs.ExternalReference);
            return await ReadOutcomeAsync(account.Credentials, refs.Type, refs.ExternalReference, ct)
                ?? ProviderIssueOutcome.Pending(uuid, uuid);
        });

    public Task<ProviderResult<ProviderCancelOutcome>> CancelAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
        => RunAsync(async () =>
        {
            if (refs.Type == EDocumentType.EInvoice)
            {
                throw new NilveraException(new ProviderError(
                    EDocumentErrorKind.ProviderRejected,
                    "e-Fatura Nilvera API'si üzerinden iptal edilemiyor; alıcıyla iade faturası veya GİB portalı üzerinden işlem yapın.",
                    "einvoice-cancel-unsupported"));
            }

            var service = Service.For(refs.Type);
            var uuid = Reference(refs.ExternalReference);
            var response = await _client.SendAsync(
                account.Credentials, HttpMethod.Put, $"/{service.Api}/{service.Collection}/{uuid}/Cancel", null,
                $"/{service.Api}/{service.Collection}/{{uuid}}/Cancel", ct);

            if (response.IsSuccess || response.ErrorCodes.Any(AlreadyCancelled.Contains))
                return new ProviderCancelOutcome(ProviderCancelState.Cancelled);
            if (response.Status == HttpStatusCode.UnprocessableEntity && response.ErrorCodes.Any(CancelLater.Contains))
                return new ProviderCancelOutcome(ProviderCancelState.Pending);

            EnsureSuccess(response, "iptal");
            return new ProviderCancelOutcome(ProviderCancelState.Cancelled);
        });

    /// <summary>Nilvera'ya taslak yazılmaz (doğrudan gönderim); kesilmiş belge silinemez, yalnız iptal edilir.</summary>
    public Task<ProviderResult<bool>> DiscardDraftAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
        => RunAsync(async () =>
        {
            var service = Service.For(refs.Type);
            var status = await StatusAsync(account.Credentials, service, Reference(refs.ExternalReference), ct);
            if (status is { Code: "succeed" })
                throw new NilveraException(new ProviderError(EDocumentErrorKind.ProviderRejected, "Belge Nilvera'da kesilmiş; vazgeçilemez, iptal edin."));
            return true;
        });

    public Task<ProviderResult<ProviderPdf>> GetPdfAsync(EDocumentProviderAccount account, EDocumentProviderRefs refs, CancellationToken ct)
        => RunAsync(async () =>
        {
            var service = Service.For(refs.Type);
            var uuid = Reference(refs.ExternalReference);
            var response = await _client.SendAsync(
                account.Credentials, HttpMethod.Get, $"/{service.Api}/{service.Collection}/{uuid}/pdf", null,
                $"/{service.Api}/{service.Collection}/{{uuid}}/pdf", ct);
            if (response.Status is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
                return ProviderPdf.NotReady;
            EnsureSuccess(response, "PDF");

            var bytes = PdfBytes(response);
            return bytes is null ? ProviderPdf.NotReady : new ProviderPdf(bytes, "belge.pdf");
        });

    /// <summary>PDF ham (<c>application/pdf</c>) ya da JSON içinde base64 metin gelebilir (doküman biçimi belirtmiyor).</summary>
    internal static byte[]? PdfBytes(NilveraResponse response)
    {
        if (response.Content.Length >= 4 && Encoding.ASCII.GetString(response.Content, 0, 4) == "%PDF")
            return response.Content;
        var text = response.Body is JsonValue value ? value.ToString() : null;
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Belgenin Nilvera'daki durumu: kesildiyse numara ve tutarla birlikte; yoksa / bekliyorsa <c>null</c> veya bekliyor.</summary>
    private async Task<ProviderIssueOutcome?> ReadOutcomeAsync(string apiKey, EDocumentType type, Guid reference, CancellationToken ct)
    {
        var service = Service.For(type);
        var uuid = Reference(reference);
        var status = await StatusAsync(apiKey, service, uuid, ct);
        if (status is null)
            return null;

        switch (status.Code)
        {
            case "succeed":
                var details = await _client.SendAsync(
                    apiKey, HttpMethod.Get, $"/{service.Api}/{service.Collection}/{uuid}/Details", null,
                    $"/{service.Api}/{service.Collection}/{{uuid}}/Details", ct);
                EnsureSuccess(details, "belge ayrıntısı");
                var number = details.Body?[service.NumberField]?.ToString();
                var total = Decimal(details.Body?[service.AmountField]);
                return new ProviderIssueOutcome(ProviderIssueState.Issued, uuid, uuid, null, number, uuid, total);
            case "error":
                throw new NilveraException(new ProviderError(
                    EDocumentErrorKind.ProviderRejected,
                    string.IsNullOrWhiteSpace(status.Detail) ? "Nilvera belgeyi oluşturamadı." : $"Nilvera belgeyi oluşturamadı: {status.Detail}",
                    "status-error"));
            case "rejected":
                throw new NilveraException(new ProviderError(
                    EDocumentErrorKind.ProviderRejected,
                    string.IsNullOrWhiteSpace(status.Detail) ? "e-Fatura alıcı tarafından reddedildi." : $"e-Fatura alıcı tarafından reddedildi: {status.Detail}",
                    "rejected"));
            default:
                return ProviderIssueOutcome.Pending(uuid, uuid);
        }
    }

    /// <summary>
    /// Durum ucu: e-Arşiv / e-SMM <c>{StatusCode, StatusDetail}</c>; e-Fatura <c>{InvoiceStatus{Code,…}, Answer{AnswerCode,…}}</c>.
    /// Belge Nilvera'da yoksa <c>null</c>. Kodlar: <c>unknown|waiting|succeed|error</c>; e-Fatura reddi <c>rejected</c> olarak döner.
    /// </summary>
    private async Task<NilveraStatus?> StatusAsync(string apiKey, Service service, string uuid, CancellationToken ct)
    {
        var response = await _client.SendAsync(
            apiKey, HttpMethod.Get, $"/{service.Api}/{service.Collection}/{uuid}/Status", null,
            $"/{service.Api}/{service.Collection}/{{uuid}}/Status", ct);
        if (response.Status == HttpStatusCode.NotFound)
            return null;
        EnsureSuccess(response, "durum sorgusu");

        var body = response.Body;
        if (service.Type != EDocumentType.EInvoice)
            return new NilveraStatus(body?["StatusCode"]?.ToString() ?? "unknown", body?["StatusDetail"]?.ToString());

        if (body?["Answer"]?["AnswerCode"]?.ToString() == "rejected")
            return new NilveraStatus("rejected", body["Answer"]?["AnswerNote"]?.ToString() ?? body["Answer"]?["Description"]?.ToString());
        var invoiceStatus = body?["InvoiceStatus"];
        return new NilveraStatus(
            invoiceStatus?["Code"]?.ToString() ?? "unknown",
            invoiceStatus?["DetailDescription"]?.ToString() ?? invoiceStatus?["Description"]?.ToString());
    }

    /// <summary>Hesaptaki seri: varsayılan ve etkin olan, yoksa ilk etkin seri (e-Arşiv'de seri zorunlu, 422 / 2033).</summary>
    private async Task<string> SeriesAsync(string apiKey, Service service, CancellationToken ct)
    {
        var response = await _client.SendAsync(apiKey, HttpMethod.Get, $"/{service.Api}/Series?PageSize=100", null, $"/{service.Api}/Series", ct);
        EnsureSuccess(response, "seri listesi");
        var active = ((response.Body?["Content"] as JsonArray) ?? [])
            .Where(s => s?["IsActive"]?.GetValue<bool>() == true && !string.IsNullOrWhiteSpace(s["Name"]?.ToString()))
            .ToList();
        var chosen = active.FirstOrDefault(s => s?["IsDefault"]?.GetValue<bool>() == true) ?? active.FirstOrDefault();
        return chosen?["Name"]?.ToString()
            ?? throw new NilveraException(new ProviderError(
                EDocumentErrorKind.InvalidData,
                $"Nilvera hesabında etkin {service.Label} serisi yok; Nilvera portalında seri tanımlayıp etkinleştirin.",
                "no-series"));
    }

    private async Task<NilveraCompany> CompanyAsync(string apiKey, CancellationToken ct)
    {
        var response = await _client.SendAsync(apiKey, HttpMethod.Get, "/general/Company", null, "/general/Company", ct);
        EnsureSuccess(response, "firma bilgisi");
        var taxNumber = response.Body?["TaxNumber"]?.ToString();
        if (string.IsNullOrWhiteSpace(taxNumber))
            throw new NilveraException(new ProviderError(EDocumentErrorKind.Unknown, "Nilvera firma bilgisi okunamadı."));
        var name = response.Body?["Name"]?.ToString();
        return new NilveraCompany(taxNumber, string.IsNullOrWhiteSpace(name) ? taxNumber : name);
    }

    /// <summary>
    /// JSON model gövdesi. Tutarlar KDV hariç gelir (<see cref="EDocumentSendLine.NetUnitPrice"/>); satır ve belge toplamları
    /// 2 ondalığa yuvarlanır. e-SMM'de satır miktarı yoktur: brüt ücret satır tutarıdır, gelir vergisi stopajı satıra yazılır.
    /// </summary>
    internal static JsonObject BuildModel(EDocumentSendRequest request, string series)
    {
        var lines = request.Lines.Select((line, index) =>
        {
            var net = Round(line.Quantity * line.NetUnitPrice);
            return (Line: line, Index: index + 1, Net: net, Vat: Round(net * line.VatRate / 100m));
        }).ToList();

        var notes = new JsonArray();
        if (!string.IsNullOrWhiteSpace(request.Note))
            notes.Add(request.Note);
        var issueDate = request.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00";

        if (request.Type == EDocumentType.ESmm)
        {
            var withholding = request.WithholdingRate ?? 0m;
            var voucherLines = new JsonArray();
            foreach (var l in lines)
            {
                voucherLines.Add(new JsonObject
                {
                    ["Name"] = SmmLineName(l.Line),
                    ["GrossWage"] = l.Net,
                    ["Price"] = l.Net,
                    ["KDVPercent"] = l.Line.VatRate,
                    ["KDVTotal"] = l.Vat,
                    ["GVWithholdingPercent"] = withholding,
                    ["GVWithholdingTotal"] = Round(l.Net * withholding / 100m),
                });
            }

            return new JsonObject
            {
                ["Voucher"] = new JsonObject
                {
                    ["VoucherInfo"] = new JsonObject
                    {
                        ["UUID"] = Reference(request.ExternalReference),
                        ["VoucherSerieOrNumber"] = series,
                        ["IssueDate"] = issueDate,
                        ["CurrencyCode"] = "TRY",
                        ["SendType"] = "ELEKTRONIK",
                    },
                    ["CustomerInfo"] = Customer(request.Buyer),
                    ["VoucherLines"] = voucherLines,
                    ["Notes"] = notes,
                },
            };
        }

        var invoiceLines = new JsonArray();
        foreach (var l in lines)
        {
            invoiceLines.Add(new JsonObject
            {
                ["Index"] = l.Index.ToString(CultureInfo.InvariantCulture),
                ["Name"] = l.Line.Description,
                ["Quantity"] = l.Line.Quantity,
                ["UnitType"] = UnitCode(l.Line.Unit),
                ["Price"] = l.Line.NetUnitPrice,
                ["AllowanceTotal"] = 0m,
                ["KDVPercent"] = l.Line.VatRate,
                ["KDVTotal"] = l.Vat,
            });
        }

        var lineTotal = lines.Sum(l => l.Net);
        var vatTotal = lines.Sum(l => l.Vat);
        var info = new JsonObject
        {
            ["UUID"] = Reference(request.ExternalReference),
            ["InvoiceType"] = "SATIS",
            ["InvoiceSerieOrNumber"] = series,
            ["IssueDate"] = issueDate,
            ["CurrencyCode"] = "TRY",
            ["LineExtensionAmount"] = lineTotal,
            ["GeneralAllowanceTotal"] = 0m,
            ["KdvTotal"] = vatTotal,
            ["PayableAmount"] = lineTotal + vatTotal,
        };
        foreach (var rate in lines.GroupBy(l => l.Line.VatRate).Where(g => g.Key > 0m))
        {
            if (VatTotalField(rate.Key) is { } field)
                info[field] = rate.Sum(l => l.Vat);
        }

        var invoice = new JsonObject
        {
            ["InvoiceInfo"] = info,
            ["CustomerInfo"] = Customer(request.Buyer),
            ["InvoiceLines"] = invoiceLines,
            ["Notes"] = notes,
        };

        if (request.Type == EDocumentType.EInvoice)
        {
            info["InvoiceProfile"] = "TEMELFATURA";
            return new JsonObject { ["EInvoice"] = invoice, ["CustomerAlias"] = request.EInvoiceRecipient };
        }

        info["SalesPlatform"] = "NORMAL";
        info["SendType"] = "ELEKTRONIK";
        return new JsonObject { ["ArchiveInvoice"] = invoice };
    }

    /// <summary>Alıcı. Adres, ilçe ve il Nilvera'da zorunludur; eksikse il / ilçe ile tamamlanır.</summary>
    private static JsonObject Customer(EDocumentSendBuyer buyer)
    {
        var city = Text(buyer.Province) ?? "Belirtilmedi";
        var district = Text(buyer.District) ?? city;
        var customer = new JsonObject
        {
            ["TaxNumber"] = buyer.IdentityNumber,
            ["Name"] = buyer.Name,
            ["Address"] = Text(buyer.AddressLine) ?? $"{district} / {city}",
            ["District"] = district,
            ["City"] = city,
            ["Country"] = "Türkiye",
        };
        if (Text(buyer.TaxOffice) is { } taxOffice)
            customer["TaxOffice"] = taxOffice;
        if (Text(buyer.Email) is { } email)
            customer["Mail"] = email;
        return customer;
    }

    private static string SmmLineName(EDocumentSendLine line)
        => line.Quantity == 1m
            ? line.Description
            : $"{line.Description} ({line.Quantity.ToString("0.###", CultureInfo.GetCultureInfo("tr-TR"))} {Text(line.Unit) ?? "adet"})";

    /// <summary>Birim adı → UN/ECE birim kodu (GİB kod listesi); bilinmeyen birim adet (<c>C62</c>) sayılır.</summary>
    internal static string UnitCode(string? unit)
        => Text(unit)?.ToLower(CultureInfo.GetCultureInfo("tr-TR")) switch
        {
            "kg" or "kilogram" => "KGM",
            "g" or "gr" or "gram" => "GRM",
            "mg" or "miligram" => "MGM",
            "l" or "lt" or "litre" => "LTR",
            "ml" or "mililitre" => "MLT",
            "kutu" => "BX",
            "paket" => "PA",
            "saat" => "HUR",
            "gün" => "DAY",
            _ => "C62",
        };

    private static string? VatTotalField(decimal rate) => rate switch
    {
        1m => "GeneralKDV1Total",
        8m => "GeneralKDV8Total",
        10m => "GeneralKDV10Total",
        18m => "GeneralKDV18Total",
        20m => "GeneralKDV20Total",
        _ => null,
    };

    private static string Reference(Guid reference) => reference.ToString("D");

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal? Decimal(JsonNode? node)
        => node is not null && decimal.TryParse(node.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static async Task<ProviderResult<T>> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return ProviderResult<T>.Ok(await action());
        }
        catch (NilveraException ex)
        {
            return ProviderResult<T>.Fail(ex.Error);
        }
    }

    private static void EnsureSuccess(NilveraResponse response, string action)
    {
        if (!response.IsSuccess)
            throw new NilveraException(NilveraClient.ToError(response, action));
    }

    private sealed record NilveraCompany(string TaxNumber, string Name);

    private sealed record NilveraStatus(string Code, string? Detail);

    /// <summary>Belge türünün Nilvera servisi: API adı, belge koleksiyonu ve ayrıntı yanıtındaki numara / tutar alanları.</summary>
    private sealed record Service(EDocumentType Type, string Api, string Collection, string Label, string NumberField, string AmountField)
    {
        private static readonly Service EArchive = new(EDocumentType.EArchive, "earchive", "Invoices", "e-Arşiv", "InvoiceNumber", "InvoiceAmount");
        private static readonly Service EInvoice = new(EDocumentType.EInvoice, "einvoice", "Sale", "e-Fatura", "InvoiceNumber", "InvoiceAmount");
        private static readonly Service ESmm = new(EDocumentType.ESmm, "evoucher", "Vouchers", "e-SMM", "VoucherNumber", "VoucherAmount");

        public static Service For(EDocumentType type) => type switch
        {
            EDocumentType.EArchive => EArchive,
            EDocumentType.EInvoice => EInvoice,
            EDocumentType.ESmm => ESmm,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
