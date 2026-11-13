using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.EDocuments;

/// <summary>
/// Bir belge grubunu keser (elle "Belge kes", ödeme ekranı ve otomatik kesme aynı yolu kullanır): belge türünü çözer (kurumsal
/// alıcıda mükellef sorgusu), belge kaydını ve ücret bağını gönderimden önce yazar, sağlayıcıya gönderir. Aynı <c>requestId</c>
/// ilk belgeyi döndürür; eşzamanlı iki kesme aynı ücreti alamaz (ücret eşzamanlılık belirteci).
/// </summary>
public sealed class EDocumentIssuer
{
    private readonly EDocumentProviderGateway _gateway;
    private readonly EDocumentSender _sender;
    private readonly IRepository<EDocument> _documents;
    private readonly IReadRepository<EDocument> _documentsRead;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public EDocumentIssuer(
        EDocumentProviderGateway gateway,
        EDocumentSender sender,
        IRepository<EDocument> documents,
        IReadRepository<EDocument> documentsRead,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _gateway = gateway;
        _sender = sender;
        _documents = documents;
        _documentsRead = documentsRead;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    /// <summary>Aynı <paramref name="requestId"/> ile önceden kesilmiş belge (tekrar istek); yoksa <c>null</c>.</summary>
    public Task<EDocument?> FindByRequestIdAsync(Guid tenantId, Guid requestId, CancellationToken ct)
        => _documentsRead.FirstOrDefaultAsync(new EDocumentByRequestIdSpec(tenantId, requestId), ct);

    /// <param name="createdByUserId">Elle ve ödemede işlemi yapan; otomatikte ayarı açan kullanıcı.</param>
    public async Task<Result<EDocument>> IssueGroupAsync(
        Guid tenantId,
        Guid clinicId,
        Guid createdByUserId,
        EDocumentDraftSet draft,
        EDocumentDraftGroup group,
        EDocumentProviderSession session,
        EDocumentConnection connection,
        Guid requestId,
        string requestHash,
        string? note,
        EDocumentOrigin origin,
        CancellationToken ct)
    {
        if (draft.Buyer is not { } buyer)
        {
            return Result<EDocument>.Failure(new Error(
                "EDocuments.BuyerIdentityMissing",
                EDocumentDraftBuilder.MissingBuyerMessage(draft.MissingBuyerFields),
                new Dictionary<string, object?> { ["missingFields"] = draft.MissingBuyerFields }));
        }

        var typeResult = await ResolveTypeAsync(draft, group, buyer, session, connection, ct);
        if (!typeResult.IsSuccess)
        {
            await _uow.SaveChangesAsync(ct);
            return Result<EDocument>.Failure(typeResult.Error);
        }

        var utcNow = Now();
        var charges = draft.Charges.Where(c => group.ChargeIds.Contains(c.Id)).ToList();
        var created = EDocument.Create(
            tenantId,
            clinicId,
            draft.Client.Id,
            draft.SaleId,
            draft.VisitId ?? (charges.Select(c => c.VisitId).Distinct().Count() == 1 ? charges[0].VisitId : null),
            typeResult.Value,
            session.Provider.Environment,
            session.Provider.ProviderCode,
            requestId,
            requestHash,
            note,
            buyer,
            group.Lines,
            draft.WithholdingRate,
            createdByUserId,
            utcNow,
            origin);
        if (!created.IsSuccess)
            return Result<EDocument>.Failure(created.Error);
        var document = created.Value!;

        foreach (var charge in charges)
        {
            var attached = charge.AttachEDocument(document.Id);
            if (!attached.IsSuccess)
                return Result<EDocument>.Failure(attached.Error);
        }

        document.BeginSending(utcNow);
        // Süreç gönderim sırasında düşerse arka plan işi belgeye bakar (aynı dış referansla, çift belge oluşmaz).
        document.AwaitProviderResult(null, utcNow.Add(EDocumentSender.InFlightGrace), utcNow);

        try
        {
            await _documents.AddAsync(document, ct);
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<EDocument>.Failure(
                "EDocuments.ConcurrencyConflict",
                "Seçilen ücretler aynı anda başka bir işlemle değişti; sayfayı yenileyip tekrar deneyin.");
        }
        catch (DbUpdateException)
        {
            // Aynı requestId ile eşzamanlı istek: benzersiz indeks kaybedene çarptı; kazanan belge döner.
            var winner = await FindByRequestIdAsync(tenantId, requestId, ct);
            if (winner is null)
                throw;
            return Result<EDocument>.Success(winner);
        }

        await _sender.SendAsync(document, connection, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<EDocument>.Success(document);
    }

    /// <summary>Fatura grubunda kurumsal alıcı için mükellef sorgusu: e-Fatura mükellefiyse e-Fatura, değilse e-Arşiv.</summary>
    private async Task<Result<EDocumentType>> ResolveTypeAsync(
        EDocumentDraftSet draft,
        EDocumentDraftGroup group,
        EDocumentBuyer buyer,
        EDocumentProviderSession session,
        EDocumentConnection connection,
        CancellationToken ct)
    {
        if (group.IsSelfEmployedReceipt)
            return Result<EDocumentType>.Success(EDocumentType.ESmm);
        if (buyer.Kind != EDocumentBuyerKind.Corporate)
            return Result<EDocumentType>.Success(EDocumentType.EArchive);

        var lookup = await session.Provider.FindEInvoiceRecipientAsync(session.Account, buyer.IdentityNumber, ct);
        _gateway.Absorb(connection, lookup);
        if (!lookup.IsSuccess)
            return Result<EDocumentType>.Failure(EDocumentErrors.FromProvider(lookup.Error!));

        return Result<EDocumentType>.Success(EDocumentTypeRule.Resolve(draft.TaxpayerType, group.Lines[0].LineKind, lookup.Value is not null));
    }

    /// <summary>Kaynak + not özeti; aynı requestId farklı içerikle gelirse ayırt eder.</summary>
    public static string Fingerprint(Guid clinicId, EDocumentSelection selection, string? note)
    {
        var builder = new StringBuilder();
        builder.Append(clinicId.ToString("N")).Append('|');
        foreach (var id in (selection.ChargeIds ?? []).Distinct().OrderBy(x => x))
            builder.Append(id.ToString("N")).Append(',');
        builder.Append('|').Append(selection.SaleId?.ToString("N")).Append('|').Append(selection.VisitId?.ToString("N"));
        builder.Append('|').Append(note?.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>
    /// Kullanıcı isteği olmadan kesilen belgenin (ödeme, otomatik) tekrar koruması: aynı anahtar ve ücretler her zaman aynı
    /// requestId'yi üretir; iki tetikleyici yarışırsa ikinci aynı belgeyi bulur.
    /// </summary>
    public static Guid DeterministicRequestId(string key, IEnumerable<Guid> chargeIds)
    {
        var text = key + "|" + string.Join(',', chargeIds.Distinct().OrderBy(x => x).Select(x => x.ToString("N")));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return new Guid(hash.AsSpan(0, 16));
    }

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;
}
