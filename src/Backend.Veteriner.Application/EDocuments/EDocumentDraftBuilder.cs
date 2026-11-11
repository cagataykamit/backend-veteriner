using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Application.Sales.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Sales;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.EDocuments;

/// <summary>Belge kaynağı: tam olarak biri — seçilen ücretler, satış veya ziyaretin belgesiz ücretleri.</summary>
public sealed record EDocumentSelection(IReadOnlyList<Guid>? ChargeIds, Guid? SaleId, Guid? VisitId);

public enum EDocumentSkipReason
{
    AlreadyDocumented = 0,
    Voided = 1
}

public sealed record EDocumentSkippedCharge(Guid ChargeId, EDocumentSkipReason Reason);

/// <summary>Aynı belgeye girebilecek satırlar: e-SMM grubu veya fatura grubu (e-Arşiv / e-Fatura alıcıya göre).</summary>
public sealed record EDocumentDraftGroup(bool IsSelfEmployedReceipt, IReadOnlyList<Guid> ChargeIds, IReadOnlyList<EDocumentLineDraft> Lines)
{
    public decimal TotalAmount => Lines.Sum(l => l.GrossAmount);
}

public sealed record EDocumentDraftSet(
    Client Client,
    EDocumentBuyer? Buyer,
    IReadOnlyList<string> MissingBuyerFields,
    ClinicTaxpayerType TaxpayerType,
    decimal? WithholdingRate,
    IReadOnlyList<Charge> Charges,
    IReadOnlyList<EDocumentDraftGroup> Groups,
    IReadOnlyList<EDocumentSkippedCharge> Skipped,
    Guid? SaleId,
    Guid? VisitId);

/// <summary>
/// Seçimden belge taslağı üretir (önizleme ve kesme aynı yolu kullanır): ücretleri ve satış satırlarını okur, alıcıyı
/// müşterinin fatura kimliğinden kurar, satırları belge türü kuralına göre gruplar (<see cref="EDocumentTypeRule"/>).
/// Satışa ait ücretin satırları satış satırlarından gelir ve ürün satırıdır. KDV oranı ücret oranı, yoksa klinik varsayılanı.
/// </summary>
public sealed class EDocumentDraftBuilder
{
    public const string MissingTaxNumberField = "taxNumber";

    private readonly IReadRepository<Charge> _chargesRead;
    private readonly IRepository<Charge> _chargesWrite;
    private readonly IReadRepository<Sale> _sales;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<ClinicTaxpayerSettings> _taxpayerSettings;

    public EDocumentDraftBuilder(
        IReadRepository<Charge> chargesRead,
        IRepository<Charge> chargesWrite,
        IReadRepository<Sale> sales,
        IReadRepository<Client> clients,
        IReadRepository<ClinicTaxpayerSettings> taxpayerSettings)
    {
        _chargesRead = chargesRead;
        _chargesWrite = chargesWrite;
        _sales = sales;
        _clients = clients;
        _taxpayerSettings = taxpayerSettings;
    }

    /// <param name="trackCharges">Kesmede ücretler izlenir (belge bağı eşzamanlılık belirteciyle yazılır).</param>
    public async Task<Result<EDocumentDraftSet>> BuildAsync(
        Guid tenantId, Guid clinicId, EDocumentSelection selection, bool trackCharges, CancellationToken ct)
    {
        var sources = (selection.ChargeIds is { Count: > 0 } ? 1 : 0) + (selection.SaleId.HasValue ? 1 : 0) + (selection.VisitId.HasValue ? 1 : 0);
        if (sources != 1)
            return Fail("EDocuments.Validation", "Kaynak olarak ücretler, satış veya ziyaretten tam olarak biri verilmelidir.");

        var settings = await _taxpayerSettings.FirstOrDefaultAsync(new ClinicTaxpayerSettingsByClinicSpec(tenantId, clinicId), ct);
        if (settings?.TaxpayerType is not { } taxpayerType)
        {
            return Fail(
                "EDocuments.TaxpayerTypeMissing",
                "Klinik mükellef türü seçilmemiş; Ayarlar > Klinik > Mükellef bilgileri bölümünü doldurun.");
        }

        var defaultVat = settings.DefaultVatRate;
        var chargesResult = await LoadChargesAsync(tenantId, clinicId, selection, trackCharges, ct);
        if (!chargesResult.IsSuccess)
            return Result<EDocumentDraftSet>.Failure(chargesResult.Error);

        var (allCharges, saleId) = chargesResult.Value;
        if (allCharges.Select(c => c.ClientId).Distinct().Count() > 1)
            return Fail("EDocuments.MixedClients", "Seçilen ücretler aynı müşteriye ait olmalıdır.");

        var skipped = new List<EDocumentSkippedCharge>();
        var included = new List<Charge>();
        foreach (var charge in allCharges)
        {
            if (charge.IsVoided)
                skipped.Add(new EDocumentSkippedCharge(charge.Id, EDocumentSkipReason.Voided));
            else if (charge.EDocumentId.HasValue)
                skipped.Add(new EDocumentSkippedCharge(charge.Id, EDocumentSkipReason.AlreadyDocumented));
            else
                included.Add(charge);
        }

        var clientId = allCharges.Count > 0 ? allCharges[0].ClientId : (Guid?)null;
        if (clientId is null)
            return Fail("EDocuments.NothingToDocument", "Belgesi kesilecek ücret yok.");

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, clientId.Value), ct);
        if (client is null)
            return Fail("Clients.NotFound", "Müşteri bulunamadı.");

        var saleLines = await LoadSaleLinesAsync(tenantId, included, ct);
        var groups = BuildGroups(included, saleLines, taxpayerType, defaultVat);
        var (buyer, missing) = BuildBuyer(client);

        return Result<EDocumentDraftSet>.Success(new EDocumentDraftSet(
            client,
            buyer,
            missing,
            taxpayerType,
            settings.WithholdingRate,
            included,
            groups,
            skipped,
            saleId,
            selection.VisitId));
    }

    /// <summary>Müşteriden alıcı: peşin müşteri bilinmeyen alıcı; kurumsalda vergi numarası zorunlu; bireyselde TCKN yoksa bilinmeyen alıcı kimliği.</summary>
    public static (EDocumentBuyer? Buyer, IReadOnlyList<string> Missing) BuildBuyer(Client client)
    {
        if (client.IsWalkIn)
            return (EDocumentBuyer.UnknownIndividual(), []);

        if (client.PersonType == ClientPersonType.Corporate)
        {
            var corporate = EDocumentBuyer.Corporate(
                client.LegalName ?? client.FullName,
                client.TaxNumber,
                client.TaxOffice,
                client.BillingProvince,
                client.BillingDistrict,
                client.BillingAddressLine,
                client.Email);
            return corporate.IsSuccess ? (corporate.Value, []) : (null, [MissingTaxNumberField]);
        }

        var individual = EDocumentBuyer.Individual(
            client.FullName,
            client.NationalId,
            client.BillingProvince,
            client.BillingDistrict,
            client.BillingAddressLine,
            client.Email);
        return (individual.Value, []);
    }

    private async Task<Result<(IReadOnlyList<Charge> Charges, Guid? SaleId)>> LoadChargesAsync(
        Guid tenantId, Guid clinicId, EDocumentSelection selection, bool track, CancellationToken ct)
    {
        if (selection.VisitId is { } visitId)
        {
            var spec = new UndocumentedChargesOfVisitSpec(tenantId, clinicId, visitId, track);
            var visitCharges = track ? await _chargesWrite.ListAsync(spec, ct) : await _chargesRead.ListAsync(spec, ct);
            return Result<(IReadOnlyList<Charge>, Guid?)>.Success((visitCharges, null));
        }

        IReadOnlyList<Guid> ids;
        Guid? saleId = null;
        if (selection.SaleId is { } requestedSaleId)
        {
            var sale = await _sales.FirstOrDefaultAsync(new SaleByIdSpec(tenantId, requestedSaleId), ct);
            if (sale is null || sale.ClinicId != clinicId)
                return Result<(IReadOnlyList<Charge>, Guid?)>.Failure("EDocuments.SaleNotFound", "Satış bulunamadı.");
            if (sale.IsVoided || sale.ChargeId is null)
                return Result<(IReadOnlyList<Charge>, Guid?)>.Failure("EDocuments.ChargeVoided", "İptal edilmiş satışa belge kesilemez.");
            ids = [sale.ChargeId.Value];
            saleId = sale.Id;
        }
        else
        {
            ids = selection.ChargeIds!.Distinct().ToList();
            if (ids.Count > EDocumentRules.MaxChargesPerDocument)
            {
                return Result<(IReadOnlyList<Charge>, Guid?)>.Failure(
                    "EDocuments.Validation", $"Bir belgeye en çok {EDocumentRules.MaxChargesPerDocument} ücret bağlanabilir.");
            }
        }

        var chargeSpec = new ChargesForEDocumentSpec(tenantId, ids, track);
        var charges = track ? await _chargesWrite.ListAsync(chargeSpec, ct) : await _chargesRead.ListAsync(chargeSpec, ct);
        if (charges.Count != ids.Count || charges.Any(c => c.ClinicId != clinicId))
            return Result<(IReadOnlyList<Charge>, Guid?)>.Failure("EDocuments.ChargeNotFound", "Seçilen ücretlerden biri bulunamadı.");
        if (!charges.All(EDocumentRules.IsDocumentable))
        {
            return Result<(IReadOnlyList<Charge>, Guid?)>.Failure(
                "EDocuments.ImportedCharge", "Eski programdan devreden bakiyeye e-belge kesilmez.");
        }

        var ordered = ids.Select(id => charges.First(c => c.Id == id)).ToList();
        return Result<(IReadOnlyList<Charge>, Guid?)>.Success((ordered, saleId));
    }

    private async Task<IReadOnlyDictionary<Guid, Sale>> LoadSaleLinesAsync(Guid tenantId, IReadOnlyList<Charge> charges, CancellationToken ct)
    {
        if (charges.Count == 0)
            return new Dictionary<Guid, Sale>();
        var sales = await _sales.ListAsync(new SalesByChargeIdsSpec(tenantId, charges.Select(c => c.Id).ToList()), ct);
        return sales.Where(s => !s.IsVoided).ToDictionary(s => s.ChargeId!.Value);
    }

    private static IReadOnlyList<EDocumentDraftGroup> BuildGroups(
        IReadOnlyList<Charge> charges,
        IReadOnlyDictionary<Guid, Sale> salesByCharge,
        ClinicTaxpayerType taxpayerType,
        decimal defaultVat)
    {
        var receipt = (Charges: new List<Guid>(), Lines: new List<EDocumentLineDraft>());
        var invoice = (Charges: new List<Guid>(), Lines: new List<EDocumentLineDraft>());

        foreach (var charge in charges)
        {
            var vat = charge.VatRate ?? defaultVat;
            IReadOnlyList<EDocumentLineDraft> lines = salesByCharge.TryGetValue(charge.Id, out var sale)
                ? sale.Lines
                    .Select(l => new EDocumentLineDraft(charge.Id, l.ProductName, l.Quantity, l.Unit, l.LineAmount, vat, ChargeLineKind.Product))
                    .ToList()
                : [new EDocumentLineDraft(charge.Id, charge.Description, charge.Quantity, null, charge.Amount, vat, charge.LineKind)];

            // Satış satırları her zaman ürün; bir ücretin satırları tek gruba düşer.
            var target = EDocumentTypeRule.IsSelfEmployedReceipt(taxpayerType, lines[0].LineKind) ? receipt : invoice;
            target.Charges.Add(charge.Id);
            target.Lines.AddRange(lines);
        }

        var groups = new List<EDocumentDraftGroup>();
        if (receipt.Lines.Count > 0)
            groups.Add(new EDocumentDraftGroup(true, receipt.Charges, receipt.Lines));
        if (invoice.Lines.Count > 0)
            groups.Add(new EDocumentDraftGroup(false, invoice.Charges, invoice.Lines));
        return groups;
    }

    private static Result<EDocumentDraftSet> Fail(string code, string message) => Result<EDocumentDraftSet>.Failure(code, message);
}
