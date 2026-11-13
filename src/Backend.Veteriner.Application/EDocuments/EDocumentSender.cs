using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.EDocuments;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.EDocuments;

/// <summary>
/// Belgeyi sağlayıcıya gönderme, asenkron sonucu sorma ve iptali tek yerde yürütür (kesme, yeniden deneme, elle yenileme ve
/// arka plan işi aynı yolu kullanır). Sağlayıcı sonucu durum makinesine (<see cref="EDocument"/>) çevrilir; belge ücretleri
/// serbest bırakınca bağ çözülür. Kayıt (SaveChanges) çağıranın işidir.
/// </summary>
public sealed class EDocumentSender
{
    /// <summary>Gönderim sürerken süreç düşerse arka plan işinin belgeye yeniden bakacağı süre (tekrar gönderim çift belge üretmez).</summary>
    public static readonly TimeSpan InFlightGrace = TimeSpan.FromMinutes(5);

    /// <summary>Belge, bağlantı başka sağlayıcıya / ortama geçtiği için gönderilmedi (sağlayıcı yanıt kodu yerine).</summary>
    public const string ProviderMismatchCode = "provider-mismatch";

    /// <summary>Ticari e-Faturada alıcı yanıtının sorulma aralığı (yanıt süresi boyunca).</summary>
    public static readonly TimeSpan BuyerAnswerCheckInterval = TimeSpan.FromHours(1);

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxCheckDelay = TimeSpan.FromMinutes(30);

    private readonly EDocumentProviderGateway _gateway;
    private readonly IRepository<Charge> _charges;
    private readonly IUserNotificationPublisher _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EDocumentSender> _logger;

    public EDocumentSender(
        EDocumentProviderGateway gateway,
        IRepository<Charge> charges,
        IUserNotificationPublisher notifications,
        TimeProvider timeProvider,
        ILogger<EDocumentSender> logger)
    {
        _gateway = gateway;
        _charges = charges;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// <c>Sending</c> durumundaki belgeyi gönderir. e-Fatura'da alıcı gelen kutusu her gönderimde sağlayıcıdan okunur.
    /// Bağlantı kullanılamıyorsa belge hatalı olur (kullanıcı yeniden bağlayıp yeniden dener).
    /// </summary>
    public async Task SendAsync(EDocument document, EDocumentConnection connection, CancellationToken ct)
    {
        var opened = _gateway.OpenFor(document, connection);
        if (!opened.IsSuccess)
        {
            var mismatch = opened.Error.Code == EDocumentProviderGateway.ProviderMismatchCode;
            document.MarkFailed(
                mismatch ? EDocumentErrorKind.ProviderRejected : EDocumentErrorKind.Authorization,
                opened.Error.Message,
                mismatch ? ProviderMismatchCode : null,
                Now());
            return;
        }

        var session = opened.Value!;
        string? recipient = null;
        if (document.Type == EDocumentType.EInvoice)
        {
            var lookup = await session.Provider.FindEInvoiceRecipientAsync(session.Account, document.Buyer.IdentityNumber, ct);
            _gateway.Absorb(connection, lookup);
            if (!lookup.IsSuccess)
            {
                ApplySendError(document, lookup.Error!);
                return;
            }

            recipient = lookup.Value;
            if (recipient is null)
            {
                document.MarkFailed(
                    EDocumentErrorKind.InvalidData,
                    "Alıcı artık e-Fatura mükellefi görünmüyor; belgeden vazgeçip yeniden kesin (e-Arşiv olur).",
                    null,
                    Now());
                return;
            }
        }

        var result = await session.Provider.IssueAsync(session.Account, BuildRequest(document, recipient), ct);
        _gateway.Absorb(connection, result);
        if (!result.IsSuccess)
        {
            ApplySendError(document, result.Error!);
            return;
        }

        ApplyIssueOutcome(document, result.Value!);
    }

    /// <summary>
    /// Bekleyen belgeye bakar: gönderiliyorsa sonucu sorar (iş yoksa ve belge oluşmadıysa aynı dış referansla yeniden gönderir),
    /// iptal isteniyorsa iptali yeniden dener. Deneme üst sınırında kalıcı hataya düşer. Ticari e-Fatura kesildiyse alıcı yanıtını
    /// sorar (<see cref="CheckBuyerAnswerAsync"/>; deneme sınırına sayılmaz).
    /// </summary>
    public async Task CheckAsync(EDocument document, EDocumentConnection? connection, int maxChecks, CancellationToken ct)
    {
        if (document.AwaitsBuyerAnswer)
        {
            await CheckBuyerAnswerAsync(document, connection, ct);
            return;
        }

        if (document.Status is not (EDocumentStatus.Sending or EDocumentStatus.CancelRequested))
            return;

        document.CountCheck();
        if (document.CheckCount > maxChecks)
        {
            if (document.Status == EDocumentStatus.Sending)
            {
                document.MarkFailed(
                    EDocumentErrorKind.Unknown,
                    "Sağlayıcıdan sonuç alınamadı. Belgeyi entegratör ekranından kontrol edin; kesilmediyse yeniden deneyin.",
                    null,
                    Now());
            }
            else
            {
                document.MarkCancelRejected("İptal sonucu alınamadı; entegratör ekranından kontrol edin.", null, Now());
            }

            return;
        }

        if (connection is null)
        {
            Postpone(document, "Entegratör bağlantısı yok.");
            return;
        }

        if (_gateway.OpenFor(document, connection) is { IsSuccess: false, Error.Code: EDocumentProviderGateway.ProviderMismatchCode } mismatch)
        {
            // Bağlantı başka sağlayıcıya / ortama geçti: belge eski sağlayıcıda kalır, yeniden gönderilmez ve sorulmaz.
            if (document.Status == EDocumentStatus.Sending)
                document.MarkFailed(EDocumentErrorKind.ProviderRejected, mismatch.Error.Message, ProviderMismatchCode, Now());
            else
                document.MarkCancelRejected(mismatch.Error.Message, ProviderMismatchCode, Now());
            return;
        }

        if (document.Status == EDocumentStatus.CancelRequested)
        {
            await CancelAsync(document, connection, ct);
            return;
        }

        if (document.ProviderJobId is null && document.ProviderDocumentId is null)
        {
            // Önceki gönderim geçici hatayla bitti veya süreç düştü: aynı dış referansla yeniden gönder.
            await SendAsync(document, connection, ct);
            return;
        }

        var opened = _gateway.OpenFor(document, connection);
        if (!opened.IsSuccess)
        {
            Postpone(document, opened.Error.Message);
            return;
        }

        var session = opened.Value!;
        var result = await session.Provider.CheckAsync(session.Account, Refs(document), ct);
        _gateway.Absorb(connection, result);
        if (!result.IsSuccess)
        {
            ApplySendError(document, result.Error!);
            return;
        }

        ApplyIssueOutcome(document, result.Value!);
    }

    /// <summary>
    /// Ticari e-Faturanın alıcı yanıtı: kabulde (veya yanıt süresi + pay dolunca) belge kesilmiş kalır; redde belge "alıcı
    /// reddetti" olur, ücretler serbest kalır (yeniden belgelenebilir), belge kesme yetkilisine bildirim gider. Yanıt yoksa ya da
    /// sorulamadıysa (bağlantı, geçici hata) bir sonraki aralıkta yeniden sorulur.
    /// </summary>
    public async Task CheckBuyerAnswerAsync(EDocument document, EDocumentConnection? connection, CancellationToken ct)
    {
        if (!document.AwaitsBuyerAnswer)
            return;

        var now = Now();
        var opened = _gateway.OpenFor(document, connection);
        if (opened.IsSuccess)
        {
            var session = opened.Value!;
            var result = await session.Provider.GetBuyerAnswerAsync(session.Account, Refs(document), ct);
            _gateway.Absorb(connection!, result);
            if (result is { IsSuccess: true, Value: { State: ProviderBuyerAnswerState.Accepted } })
            {
                document.CloseBuyerAnswer(now);
                return;
            }

            if (result is { IsSuccess: true, Value: { State: ProviderBuyerAnswerState.Rejected } rejected })
            {
                document.MarkRejectedByBuyer(rejected.Reason, now);
                await ReleaseChargesAsync(document, ct);
                await _notifications.EDocumentRejectedByBuyerAsync(
                    document.TenantId, document.ClinicId, document.Id, document.DocumentNumber, document.BuyerRejectionReason, ct);
                return;
            }

            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "e-Fatura alıcı yanıtı alınamadı. EDocumentId={EDocumentId}, Kind={Kind}, ProviderCode={ProviderCode}",
                    document.Id, result.Error!.Kind, result.Error.ProviderCode);
            }
        }

        // Yanıt yok: süre (+ pay) dolduysa kabul edilmiş sayılır, değilse bir sonraki aralıkta yeniden sorulur.
        if (now > document.BuyerAnswerDueUtc!.Value.AddDays(EDocumentRules.BuyerAnswerGraceDays))
            document.CloseBuyerAnswer(now);
        else
            document.PostponeBuyerAnswerCheck(now.Add(BuyerAnswerCheckInterval), now);
    }

    /// <summary><c>CancelRequested</c> belgeyi sağlayıcıda iptal eder; başarıda ücretler serbest kalır.</summary>
    public async Task CancelAsync(EDocument document, EDocumentConnection connection, CancellationToken ct)
    {
        var opened = _gateway.OpenFor(document, connection);
        if (!opened.IsSuccess)
        {
            document.MarkCancelRejected(opened.Error.Message, null, Now());
            return;
        }

        var session = opened.Value!;
        var result = await session.Provider.CancelAsync(session.Account, Refs(document), ct);
        _gateway.Absorb(connection, result);
        if (!result.IsSuccess)
        {
            var error = result.Error!;
            if (error.IsTransient)
                document.RecordTransientFailure(error.Message, error.ProviderCode, NextCheck(document), Now());
            else
                document.MarkCancelRejected(error.Message, error.ProviderCode, Now());
            return;
        }

        if (result.Value!.State == ProviderCancelState.Pending)
        {
            document.RecordTransientFailure("İptal sağlayıcıda sürüyor.", null, NextCheck(document), Now());
            return;
        }

        document.MarkCancelled(Now());
        await ReleaseChargesAsync(document, ct);
    }

    /// <summary>Hatalı belgeden vazgeçmede sağlayıcıdaki taslak kayıt geri alınır (en iyi çaba; hata vazgeçmeyi durdurmaz).</summary>
    public async Task DiscardDraftAsync(EDocument document, EDocumentConnection? connection, CancellationToken ct)
    {
        var opened = _gateway.OpenFor(document, connection);
        if (!opened.IsSuccess)
            return;

        var session = opened.Value!;
        var result = await session.Provider.DiscardDraftAsync(session.Account, Refs(document), ct);
        _gateway.Absorb(connection!, result);
        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "e-belge taslağı sağlayıcıda geri alınamadı. EDocumentId={EDocumentId}, Kind={Kind}, ProviderCode={ProviderCode}",
                document.Id,
                result.Error!.Kind,
                result.Error.ProviderCode);
        }
    }

    public async Task ReleaseChargesAsync(EDocument document, CancellationToken ct)
    {
        if (document.HoldsCharges)
            return;
        var charges = await _charges.ListAsync(new ChargesByEDocumentForUpdateSpec(document.TenantId, document.Id), ct);
        foreach (var charge in charges)
            charge.ReleaseEDocument(document.Id);
    }

    public static EDocumentProviderRefs Refs(EDocument document)
        => new(document.Type, document.ExternalReference, document.ProviderInvoiceId, document.ProviderDocumentId, document.ProviderJobId);

    /// <summary>Satırları KDV (ve uygulanıyorsa stopaj) hariç tutara çevirir (<see cref="EDocumentAmounts"/>).</summary>
    public static EDocumentSendRequest BuildRequest(EDocument document, string? eInvoiceRecipient)
    {
        var lines = document.Lines
            .OrderBy(l => l.Position)
            .Select(l =>
            {
                var net = EDocumentAmounts.NetLineAmount(l.GrossAmount, l.VatRate, document.WithholdingRate);
                return new EDocumentSendLine(l.Description, l.Quantity, l.Unit, EDocumentAmounts.NetUnitPrice(net, l.Quantity), l.VatRate);
            })
            .ToList();

        var buyer = document.Buyer;
        return new EDocumentSendRequest(
            document.Type,
            document.ExternalReference,
            OperationDayBounds.ToLocal(document.CreatedAtUtc),
            new EDocumentSendBuyer(
                buyer.Kind, buyer.Name, buyer.IdentityNumber, buyer.IsUnknownBuyer, buyer.TaxOffice,
                buyer.Province, buyer.District, buyer.AddressLine, buyer.Email),
            lines,
            document.WithholdingRate,
            document.Note,
            eInvoiceRecipient,
            document.ProviderInvoiceId);
    }

    private void ApplyIssueOutcome(EDocument document, ProviderIssueOutcome outcome)
    {
        if (outcome.ProviderInvoiceId is { } invoiceId)
            document.RecordProviderInvoice(invoiceId);

        if (outcome.State == ProviderIssueState.Issued && outcome.ProviderDocumentId is { } documentId)
        {
            var now = Now();
            document.MarkIssued(documentId, outcome.DocumentNumber, outcome.DocumentUuid, outcome.Total, now);
            if (outcome.AwaitsBuyerAnswer)
                document.AwaitBuyerAnswer(now.AddDays(EDocumentRules.BuyerAnswerWindowDays), now.Add(BuyerAnswerCheckInterval), now);
            return;
        }

        document.AwaitProviderResult(outcome.ProviderJobId, NextCheck(document), Now());
    }

    private void ApplySendError(EDocument document, ProviderError error)
    {
        if (error.IsTransient)
            document.RecordTransientFailure(error.Message, error.ProviderCode, NextCheck(document), Now());
        else
            document.MarkFailed(error.Kind, error.Message, error.ProviderCode, Now());
    }

    private void Postpone(EDocument document, string message)
        => document.RecordTransientFailure(message, null, NextCheck(document), Now());

    /// <summary>Artan aralık: 10 sn, 20 sn, 40 sn … en çok 30 dk.</summary>
    private DateTime NextCheck(EDocument document)
    {
        var factor = Math.Pow(2, Math.Min(document.CheckCount, 12));
        var delay = TimeSpan.FromSeconds(Math.Min(FirstCheckDelay.TotalSeconds * factor, MaxCheckDelay.TotalSeconds));
        return Now().Add(delay);
    }

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;
}
