using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.EDocuments;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.EDocuments.Automation;

/// <summary>Bir klinik turunun sonucu; otomatik kesilen belgeler denetim kaydına yazılır.</summary>
public sealed record EDocumentAutomationRun(IReadOnlyList<EDocument> Documents, IReadOnlyList<EDocumentBatchSkip> Skipped, bool SummarySent);

/// <summary>
/// Otomatik e-belge ve gün sonu hatırlatması (ADR-036 AK-4, kullanıcı kararı 2026-11-11), klinik başına bir tur:
/// <list type="bullet">
/// <item>"Ödeme alınınca": her turda uygun ücretler (iptal edilmemiş, belgesiz, ödemesi ayar açıkken tamamlanmış) kesilir.</item>
/// <item>"Gün sonunda": ayar saatinde (İstanbul) günde bir kez aynı ücretler kesilir.</item>
/// <item>Gün sonu saatinde (otomatik kapalı olsa da) belge kesme izni olanlara özet bildirimi: otomatik kesilen, kesilemeyen ve
/// belgesiz ödenmiş ücret sayısı; hepsi sıfırsa bildirim yok.</item>
/// </list>
/// Uygun olmayan veya hata alan ücret kesilmez, belgesiz ücretler listesinde nedeniyle görünür. Hesap genelinde kalıcı hata
/// (yetki / anahtar, kontör, bağlantı) otomatik kesmeyi durdurur (<see cref="EDocumentAutoIssueGuard"/>); geçici hatalar arka plan
/// işinde artan aralıkla yeniden denenir. Ayar kapatılınca yeni belge kesilmez; gönderilmiş belgelerin sonucu yine alınır. Otomatik belgenin oluşturanı ayarı
/// açan kullanıcıdır; kaynağı <see cref="EDocumentOrigin.Automatic"/>. Gün sonu işaretlemesi işten önce kaydedilir (iki örnek aynı
/// günü ikinci kez çalıştırmaz; kaybeden eşzamanlılık hatası alır).
/// </summary>
public sealed class EDocumentAutomation
{
    /// <summary>Tek turda bakılan en çok ücret (kalan bir sonraki turda).</summary>
    public const int MaxChargesPerRun = 200;

    private readonly EDocumentProviderGateway _gateway;
    private readonly EDocumentBatchIssuer _batch;
    private readonly ITrialLimitPolicy _trial;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly IRepository<ClinicEDocumentSettings> _settings;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<EDocument> _documents;
    private readonly IUserNotificationPublisher _notifications;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;
    private readonly EDocumentAutoIssueGuard _guard;

    public EDocumentAutomation(
        EDocumentProviderGateway gateway,
        EDocumentAutoIssueGuard guard,
        EDocumentBatchIssuer batch,
        ITrialLimitPolicy trial,
        IRepository<EDocumentConnection> connections,
        IRepository<ClinicEDocumentSettings> settings,
        IReadRepository<Charge> charges,
        IReadRepository<EDocument> documents,
        IUserNotificationPublisher notifications,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _gateway = gateway;
        _guard = guard;
        _batch = batch;
        _trial = trial;
        _connections = connections;
        _settings = settings;
        _charges = charges;
        _documents = documents;
        _notifications = notifications;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async Task<EDocumentAutomationRun> RunClinicAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
    {
        var connection = await _connections.FirstOrDefaultAsync(new EDocumentConnectionByClinicSpec(tenantId, clinicId, tracked: true), ct);
        if (connection is { Status: EDocumentConnectionStatus.ReauthorizationRequired })
        {
            // Anahtar / yetki sonradan geçersizleşti: otomatik kesme açıksa durur ve yöneticiye bildirilir (bir kez).
            if (await _guard.PauseAsync(tenantId, clinicId, connection.LastVerificationMessage ?? EDocumentProviderGateway.NotConnected(connection).Message, ct))
                await _uow.SaveChangesAsync(ct);
            return new EDocumentAutomationRun([], [], false);
        }

        if (connection is not { Status: EDocumentConnectionStatus.Connected })
            return new EDocumentAutomationRun([], [], false);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var local = OperationDayBounds.ToLocal(utcNow);
        var localDate = DateOnly.FromDateTime(local);

        var settings = await _settings.FirstOrDefaultAsync(new ClinicEDocumentSettingsByClinicSpec(tenantId, clinicId), ct);
        var endOfDayDue = (settings?.IsEndOfDayDue(localDate, TimeOnly.FromDateTime(local)))
                          ?? TimeOnly.FromDateTime(local) >= ClinicEDocumentSettings.DefaultEndOfDayTime;
        var mode = settings?.AutoIssueMode ?? EDocumentAutoIssueMode.Off;
        var issueNow = mode == EDocumentAutoIssueMode.OnPayment || (mode == EDocumentAutoIssueMode.EndOfDay && endOfDayDue);
        if (!issueNow && !endOfDayDue)
            return new EDocumentAutomationRun([], [], false);

        if (endOfDayDue && !await ClaimEndOfDayAsync(tenantId, clinicId, connection, settings, localDate, utcNow, ct))
            return new EDocumentAutomationRun([], [], false);
        settings ??= await _settings.FirstOrDefaultAsync(new ClinicEDocumentSettingsByClinicSpec(tenantId, clinicId), ct);

        var documents = new List<EDocument>();
        var skipped = new List<EDocumentBatchSkip>();
        if (issueNow && settings is { AutoIssueEnabledAtUtc: { } since, IsAutoIssuePaused: false })
        {
            var opened = _gateway.Open(connection);
            var trialBlocked = opened.IsSuccess
                               && opened.Value!.Provider.Environment == EDocumentEnvironment.Live
                               && await _trial.IsTrialAsync(tenantId, ct);
            if (!opened.IsSuccess)
            {
                if (await _guard.PauseAsync(tenantId, clinicId, opened.Error.Message, ct))
                    await _uow.SaveChangesAsync(ct);
            }
            else if (!trialBlocked)
            {
                await IssueEligibleAsync(tenantId, clinicId, settings, since, connection, opened.Value!, documents, skipped, ct);
            }
        }

        var summarySent = false;
        if (endOfDayDue)
            summarySent = await SendSummaryAsync(tenantId, clinicId, connection, localDate, ct);

        return new EDocumentAutomationRun(documents, skipped, summarySent);
    }

    /// <summary>
    /// Uygun ücretleri müşteri başına keser. Hesap genelinde kalıcı hata (yetki / anahtar, kontör, hesap askıda) çıkınca tur durur
    /// ve otomatik kesme durdurulur; kalan ücretler belgesiz kalır (sorun giderilip sürdürülünce kesilir).
    /// </summary>
    private async Task IssueEligibleAsync(
        Guid tenantId,
        Guid clinicId,
        ClinicEDocumentSettings settings,
        DateTime since,
        EDocumentConnection connection,
        EDocumentProviderSession session,
        List<EDocument> documents,
        List<EDocumentBatchSkip> skipped,
        CancellationToken ct)
    {
        var eligible = await _charges.ListAsync(new AutoIssueEligibleChargesSpec(tenantId, clinicId, since, MaxChargesPerRun), ct);
        foreach (var byClient in eligible.GroupBy(c => c.ClientId))
        {
            foreach (var chunk in byClient.Select(c => c.Id).Chunk(EDocumentRules.MaxChargesPerDocument))
            {
                var outcome = await _batch.IssueAsync(
                    tenantId, clinicId, settings.UpdatedByUserId, chunk, EDocumentOrigin.Automatic, "auto", connection, session, ct);
                documents.AddRange(outcome.Documents);
                skipped.AddRange(outcome.Skipped);

                var blocking = outcome.Documents.FirstOrDefault(d =>
                    d.Status == EDocumentStatus.Failed && EDocumentAutoIssueGuard.IsAccountWide(d.LastErrorKind));
                var reason = blocking?.LastErrorMessage
                             ?? (connection.Status != EDocumentConnectionStatus.Connected
                                 ? connection.LastVerificationMessage ?? EDocumentProviderGateway.NotConnected(connection).Message
                                 : null);
                if (reason is null)
                    continue;

                if (await _guard.PauseAsync(tenantId, clinicId, reason, ct))
                    await _uow.SaveChangesAsync(ct);
                return;
            }
        }
    }

    /// <summary>Günün işaretini işten önce kaydeder; eşzamanlı ikinci tur kaybeder ve çalışmaz.</summary>
    private async Task<bool> ClaimEndOfDayAsync(
        Guid tenantId, Guid clinicId, EDocumentConnection connection, ClinicEDocumentSettings? settings, DateOnly localDate, DateTime utcNow, CancellationToken ct)
    {
        var isNew = settings is null;
        settings ??= ClinicEDocumentSettings.Create(tenantId, clinicId, connection.ConnectedByUserId ?? Guid.Empty, utcNow);
        settings.MarkEndOfDayRun(localDate);
        try
        {
            if (isNew)
                await _settings.AddAsync(settings, ct);
            await _uow.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task<bool> SendSummaryAsync(Guid tenantId, Guid clinicId, EDocumentConnection connection, DateOnly localDate, CancellationToken ct)
    {
        var (dayStart, dayEnd) = OperationDayBounds.ForLocalDate(localDate);
        var automatic = await _documents.ListAsync(new AutomaticEDocumentsInRangeSpec(tenantId, clinicId, dayStart, dayEnd), ct);
        var issued = automatic.Count(d => d.Status is EDocumentStatus.Issued or EDocumentStatus.Sending);
        var failed = automatic.Count(d => d.Status == EDocumentStatus.Failed);
        var undocumented = connection.ConnectedAtUtc is { } since
            ? await _charges.CountAsync(new UndocumentedPaidChargesSpec(tenantId, clinicId, since), ct)
            : 0;
        if (issued == 0 && failed == 0 && undocumented == 0)
            return false;

        await _notifications.EDocumentsDailySummaryAsync(tenantId, clinicId, issued, failed, undocumented, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
