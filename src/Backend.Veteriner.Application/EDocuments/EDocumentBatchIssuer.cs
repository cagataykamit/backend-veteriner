using Backend.Veteriner.Domain.EDocuments;

namespace Backend.Veteriner.Application.EDocuments;

/// <summary>Toplu kesmede kesilemeyen ücret: neden kodu ve kullanıcı metni (belgesiz ücretler listesinde de görünür).</summary>
public sealed record EDocumentBatchSkip(Guid ChargeId, string Code, string Message);

public sealed record EDocumentBatchOutcome(IReadOnlyList<EDocument> Documents, IReadOnlyList<EDocumentBatchSkip> Skipped)
{
    public static EDocumentBatchOutcome Empty { get; } = new([], []);
}

/// <summary>
/// Kullanıcı seçimi olmadan kesme (ödeme ekranında "e-Belge kes" ve otomatik kesme): ücretler belge türüne göre gruplanır ve her
/// grup ayrı belge olur (e-SMM ile fatura birleşmez). Tekrar koruması anahtardan türetilen requestId ile: aynı ödeme veya aynı
/// otomatik tur iki kez çalışsa da, elle kesmeyle yarışsa da çift belge oluşmaz. Kesilemeyen ücret belgesiz kalır ve nedeni döner.
/// </summary>
public sealed class EDocumentBatchIssuer
{
    private readonly EDocumentDraftBuilder _drafts;
    private readonly EDocumentIssuer _issuer;

    public EDocumentBatchIssuer(EDocumentDraftBuilder drafts, EDocumentIssuer issuer)
    {
        _drafts = drafts;
        _issuer = issuer;
    }

    /// <param name="chargeIds">Aynı müşterinin ücretleri.</param>
    /// <param name="requestKey">Tetikleyici anahtarı (ör. <c>payment:{id}</c>, <c>auto</c>).</param>
    public async Task<EDocumentBatchOutcome> IssueAsync(
        Guid tenantId,
        Guid clinicId,
        Guid actorUserId,
        IReadOnlyList<Guid> chargeIds,
        EDocumentOrigin origin,
        string requestKey,
        EDocumentConnection connection,
        EDocumentProviderSession session,
        CancellationToken ct)
    {
        if (chargeIds.Count == 0)
            return EDocumentBatchOutcome.Empty;

        var draftResult = await _drafts.BuildAsync(tenantId, clinicId, new EDocumentSelection(chargeIds, null, null), trackCharges: true, ct);
        if (!draftResult.IsSuccess)
            return new EDocumentBatchOutcome([], chargeIds.Select(id => new EDocumentBatchSkip(id, draftResult.Error.Code, draftResult.Error.Message)).ToList());

        var draft = draftResult.Value!;
        var documents = new List<EDocument>();
        var skipped = new List<EDocumentBatchSkip>();
        foreach (var group in draft.Groups)
        {
            var requestId = EDocumentIssuer.DeterministicRequestId($"{requestKey}|{(group.IsSelfEmployedReceipt ? "smm" : "inv")}", group.ChargeIds);
            var existing = await _issuer.FindByRequestIdAsync(tenantId, requestId, ct);
            if (existing is not null)
            {
                documents.Add(existing);
                continue;
            }

            var hash = EDocumentIssuer.Fingerprint(clinicId, new EDocumentSelection(group.ChargeIds, null, null), null);
            var issued = await _issuer.IssueGroupAsync(
                tenantId, clinicId, actorUserId, draft, group, session, connection, requestId, hash, null, origin, ct);
            if (issued.IsSuccess)
                documents.Add(issued.Value!);
            else
                skipped.AddRange(group.ChargeIds.Select(id => new EDocumentBatchSkip(id, issued.Error.Code, issued.Error.Message)));
        }

        return new EDocumentBatchOutcome(documents, skipped);
    }
}
