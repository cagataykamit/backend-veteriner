using System.Globalization;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Commands.Issue;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.EDocuments.Automation;

internal static class ClinicEDocumentSettingsMapping
{
    public static ClinicEDocumentSettingsDto ToDto(Guid clinicId, ClinicEDocumentSettings? s, ClinicTaxpayerType? taxpayerType)
        => new(
            clinicId,
            s?.AutoIssueMode ?? EDocumentAutoIssueMode.Off,
            (s?.EndOfDayTime ?? ClinicEDocumentSettings.DefaultEndOfDayTime).ToString("HH:mm", CultureInfo.InvariantCulture),
            s?.IssueOnPaymentByDefault ?? false,
            s?.AutoIssueEnabledAtUtc,
            taxpayerType,
            Kind(taxpayerType, ChargeLineKind.Service),
            Kind(taxpayerType, ChargeLineKind.Product));

    private static EDocumentKindForLine? Kind(ClinicTaxpayerType? type, ChargeLineKind line)
        => type is { } t
            ? EDocumentTypeRule.IsSelfEmployedReceipt(t, line) ? EDocumentKindForLine.ESmm : EDocumentKindForLine.Invoice
            : null;
}

public sealed class GetClinicEDocumentSettingsQueryHandler : IRequestHandler<GetClinicEDocumentSettingsQuery, Result<ClinicEDocumentSettingsDto>>
{
    private readonly EDocumentAccess _access;
    private readonly IReadRepository<ClinicEDocumentSettings> _settings;
    private readonly IReadRepository<ClinicTaxpayerSettings> _taxpayer;

    public GetClinicEDocumentSettingsQueryHandler(
        EDocumentAccess access, IReadRepository<ClinicEDocumentSettings> settings, IReadRepository<ClinicTaxpayerSettings> taxpayer)
    {
        _access = access;
        _settings = settings;
        _taxpayer = taxpayer;
    }

    public async ValueTask<Result<ClinicEDocumentSettingsDto>> Handle(GetClinicEDocumentSettingsQuery request, CancellationToken ct)
    {
        var access = await _access.ResolveClinicSettingsAsync(PermissionCatalog.Clinics.Read, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicEDocumentSettingsDto>.Failure(access.Error);

        var tenantId = access.Value!.TenantId;
        var row = await _settings.FirstOrDefaultAsync(new ClinicEDocumentSettingsByClinicSpec(tenantId, request.ClinicId), ct);
        var taxpayer = await _taxpayer.FirstOrDefaultAsync(new ClinicTaxpayerSettingsByClinicSpec(tenantId, request.ClinicId), ct);
        return Result<ClinicEDocumentSettingsDto>.Success(ClinicEDocumentSettingsMapping.ToDto(request.ClinicId, row, taxpayer?.TaxpayerType));
    }
}

public sealed class UpdateClinicEDocumentSettingsCommandHandler
    : IRequestHandler<UpdateClinicEDocumentSettingsCommand, Result<ClinicEDocumentSettingsDto>>
{
    private readonly EDocumentAccess _access;
    private readonly IRepository<ClinicEDocumentSettings> _settings;
    private readonly IReadRepository<ClinicTaxpayerSettings> _taxpayer;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public UpdateClinicEDocumentSettingsCommandHandler(
        EDocumentAccess access,
        IRepository<ClinicEDocumentSettings> settings,
        IReadRepository<ClinicTaxpayerSettings> taxpayer,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _access = access;
        _settings = settings;
        _taxpayer = taxpayer;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicEDocumentSettingsDto>> Handle(UpdateClinicEDocumentSettingsCommand request, CancellationToken ct)
    {
        var access = await _access.ResolveClinicSettingsAsync(PermissionCatalog.Clinics.Update, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicEDocumentSettingsDto>.Failure(access.Error);
        var caller = access.Value!;

        if (request.AutoIssueMode is not { } mode)
            return Result<ClinicEDocumentSettingsDto>.Failure("EDocuments.Validation", "Otomatik e-belge ayarı zorunludur.");
        var time = ClinicEDocumentSettings.DefaultEndOfDayTime;
        if (!string.IsNullOrWhiteSpace(request.EndOfDayTime)
            && !TimeOnly.TryParseExact(request.EndOfDayTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            return Result<ClinicEDocumentSettingsDto>.Failure("EDocuments.Validation", "Gün sonu saati SS:dd biçiminde olmalıdır.");
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var row = await _settings.FirstOrDefaultAsync(new ClinicEDocumentSettingsByClinicSpec(caller.TenantId, request.ClinicId), ct);
        var isNew = row is null;
        row ??= ClinicEDocumentSettings.Create(caller.TenantId, request.ClinicId, caller.UserId, utcNow);
        var updated = row.Update(mode, time, request.IssueOnPaymentByDefault, caller.UserId, utcNow);
        if (!updated.IsSuccess)
            return Result<ClinicEDocumentSettingsDto>.Failure(updated.Error);

        try
        {
            if (isNew)
                await _settings.AddAsync(row, ct);
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<ClinicEDocumentSettingsDto>.Failure(EDocumentErrors.ConcurrencyConflict());
        }

        var taxpayer = await _taxpayer.FirstOrDefaultAsync(new ClinicTaxpayerSettingsByClinicSpec(caller.TenantId, request.ClinicId), ct);
        return Result<ClinicEDocumentSettingsDto>.Success(ClinicEDocumentSettingsMapping.ToDto(request.ClinicId, row, taxpayer?.TaxpayerType));
    }
}

public sealed class GetEDocumentRemindersQueryHandler : IRequestHandler<GetEDocumentRemindersQuery, Result<EDocumentRemindersDto>>
{
    private readonly EDocumentAccess _access;
    private readonly IReadRepository<EDocumentConnection> _connections;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<EDocument> _documents;

    public GetEDocumentRemindersQueryHandler(
        EDocumentAccess access, IReadRepository<EDocumentConnection> connections, IReadRepository<Charge> charges, IReadRepository<EDocument> documents)
    {
        _access = access;
        _connections = connections;
        _charges = charges;
        _documents = documents;
    }

    public async ValueTask<Result<EDocumentRemindersDto>> Handle(GetEDocumentRemindersQuery request, CancellationToken ct)
    {
        var access = await _access.ResolveAsync(PermissionCatalog.EDocuments.Read, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentRemindersDto>.Failure(access.Error);

        var caller = access.Value!;
        var connection = await _connections.FirstOrDefaultAsync(new EDocumentConnectionByClinicSpec(caller.TenantId, caller.ClinicId), ct);
        if (connection is not { Status: EDocumentConnectionStatus.Connected, ConnectedAtUtc: { } since })
            return Result<EDocumentRemindersDto>.Success(new EDocumentRemindersDto(false, 0, 0));

        var paid = await _charges.CountAsync(new UndocumentedPaidChargesSpec(caller.TenantId, caller.ClinicId, since), ct);
        var failed = await _documents.CountAsync(
            new EDocumentsFilteredSpec(caller.TenantId, caller.ClinicId, null, EDocumentStatus.Failed, null, null, null), ct);
        return Result<EDocumentRemindersDto>.Success(new EDocumentRemindersDto(true, paid, failed));
    }
}

public sealed class IssueEDocumentsFromPaymentCommandHandler
    : IRequestHandler<IssueEDocumentsFromPaymentCommand, Result<EDocumentBatchResultDto>>
{
    private readonly EDocumentAccess _access;
    private readonly EDocumentProviderGateway _gateway;
    private readonly EDocumentBatchIssuer _batch;
    private readonly IEDocumentProviderResolver _providers;
    private readonly ITrialLimitPolicy _trial;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<PaymentAllocation> _allocations;
    private readonly IReadRepository<Charge> _charges;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<EDocument> _documents;
    private readonly TimeProvider _timeProvider;

    public IssueEDocumentsFromPaymentCommandHandler(
        EDocumentAccess access,
        EDocumentProviderGateway gateway,
        EDocumentBatchIssuer batch,
        IEDocumentProviderResolver providers,
        ITrialLimitPolicy trial,
        IReadRepository<Payment> payments,
        IReadRepository<PaymentAllocation> allocations,
        IReadRepository<Charge> charges,
        IRepository<EDocumentConnection> connections,
        IReadRepository<Client> clients,
        IReadRepository<EDocument> documents,
        TimeProvider timeProvider)
    {
        _documents = documents;
        _access = access;
        _gateway = gateway;
        _batch = batch;
        _providers = providers;
        _trial = trial;
        _payments = payments;
        _allocations = allocations;
        _charges = charges;
        _connections = connections;
        _clients = clients;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<EDocumentBatchResultDto>> Handle(IssueEDocumentsFromPaymentCommand request, CancellationToken ct)
    {
        var basics = _access.Basics(PermissionCatalog.EDocuments.Issue);
        if (!basics.IsSuccess)
            return Result<EDocumentBatchResultDto>.Failure(basics.Error);

        var payment = await _payments.FirstOrDefaultAsync(new PaymentByIdSpec(basics.Value!.TenantId, request.PaymentId), ct);
        if (payment is null)
            return Result<EDocumentBatchResultDto>.Failure("Payments.NotFound", "Ödeme bulunamadı.");

        var access = await _access.ResolveForClinicAsync(PermissionCatalog.EDocuments.Issue, payment.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentBatchResultDto>.Failure(access.Error);
        var caller = access.Value!;

        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(caller.TenantId, caller.ClinicId, tracked: true), ct);
        var opened = _gateway.Open(connection);
        if (!opened.IsSuccess)
            return Result<EDocumentBatchResultDto>.Failure(opened.Error);
        var session = opened.Value!;

        var trialBlock = await IssueEDocumentCommandHandler.EnsureNotTrialLiveAsync(_trial, session, caller.TenantId, ct);
        if (!trialBlock.IsSuccess)
            return Result<EDocumentBatchResultDto>.Failure(trialBlock.Error);

        // Ödemenin karşıladığı ücretlerden yalnız tamamen ödenmiş ve belgesiz olanlar; kısmi ödenen ücret ödeme tamamlanınca kesilir.
        var allocations = await _allocations.ListAsync(new ActiveAllocationsOfPaymentSpec(caller.TenantId, payment.Id), ct);
        var chargeIds = allocations.Select(a => a.ChargeId).Distinct().ToList();
        var charges = chargeIds.Count == 0
            ? []
            : await _charges.ListAsync(new ChargesForEDocumentSpec(caller.TenantId, chargeIds, tracked: false), ct);
        var eligible = charges.Where(EDocumentRules.IsDocumentable)
            .Where(c => c.EDocumentId is null && c.OutstandingAmount == 0m).Select(c => c.Id).ToList();

        var outcome = await _batch.IssueAsync(
            caller.TenantId, caller.ClinicId, caller.UserId, eligible, EDocumentOrigin.Payment, $"payment:{payment.Id:N}",
            connection!, session, ct);

        // Tekrar çağrı veya önceden belgelenmiş ücret: ödemenin ücretlerine bağlı mevcut belgeler de döner.
        var documents = outcome.Documents.ToList();
        foreach (var existingId in charges.Where(c => c.EDocumentId is not null).Select(c => c.EDocumentId!.Value).Distinct())
        {
            if (documents.All(d => d.Id != existingId)
                && await _documents.FirstOrDefaultAsync(new EDocumentByIdSpec(caller.TenantId, existingId), ct) is { } existing)
                documents.Add(existing);
        }

        var names = await Queries.GetEDocumentsListQueryHandler.ClientNamesAsync(_clients, caller.TenantId, documents.Select(d => d.ClientId), ct);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return Result<EDocumentBatchResultDto>.Success(new EDocumentBatchResultDto(
            documents.Select(d => d.ToDto(names.GetValueOrDefault(d.ClientId, string.Empty), _providers, now)).ToList(),
            outcome.Skipped.Select(s => new EDocumentBatchSkipDto(s.ChargeId, s.Code, s.Message)).ToList()));
    }
}
