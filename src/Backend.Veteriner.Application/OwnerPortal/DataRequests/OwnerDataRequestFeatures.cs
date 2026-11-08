using System.Text.Json.Serialization;
using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.OwnerPortal.DataRequests;

/// <summary>Portal "veri taleplerim" satırı (docs/PORTAL_API_CONTRACT.md §11.1).</summary>
public sealed record PortalDataRequestDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerDataRequestType Type,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerDataRequestStatus Status,
    string Message,
    DateTime CreatedAtUtc,
    DateTime DueAtUtc,
    DateTime? RespondedAtUtc,
    string? ResponseNote,
    bool CanCancel);

/// <summary>Klinik gelen kutusu satırı (§11.2).</summary>
public sealed record StaffDataRequestDto(
    Guid Id,
    Guid ClinicId,
    string ClinicName,
    Guid ClientId,
    string ClientName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerDataRequestType Type,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerDataRequestStatus Status,
    string Message,
    DateTime CreatedAtUtc,
    DateTime DueAtUtc,
    bool IsOverdue,
    Guid? HandledByUserId,
    DateTime? HandledAtUtc,
    string? ResponseNote);

public sealed record PortalInboxSummaryDto(int OpenDataRequestCount, int OverdueDataRequestCount);

public sealed record GetPortalDataRequestsQuery(Guid OwnerAccountId, Guid LinkId)
    : IRequest<Result<IReadOnlyList<PortalDataRequestDto>>>, IAuditableRequest
{
    public string AuditAction => "Portal.Read.DataRequests";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

/// <summary>Hasta sahibi KVKK başvurusu (§11.1). Kiracı salt okunur olsa da başvuru kabul edilir (ilgili kişinin hakkı).</summary>
public sealed record CreatePortalDataRequestCommand(Guid OwnerAccountId, Guid LinkId, OwnerDataRequestType? Type, string? RequestText)
    : IRequest<Result<PortalDataRequestDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.DataRequests.Create";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

public sealed record CancelPortalDataRequestCommand(Guid OwnerAccountId, Guid LinkId, Guid Id)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.DataRequests.Cancel";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId};DataRequestId={Id}";
}

public sealed record GetPortalInboxDataRequestsQuery(Guid? ClinicId, OwnerDataRequestStatus? Status, int Page, int PageSize)
    : IRequest<Result<PagedResult<StaffDataRequestDto>>>;

public sealed record GetPortalInboxSummaryQuery(Guid? ClinicId) : IRequest<Result<PortalInboxSummaryDto>>;

/// <summary>Klinik talebin durumunu değiştirir; kapanışta sahibe işlemsel e-posta gider (§11.2). Abonelik salt okunurken de çalışır (KVKK yükümlülüğü).</summary>
public sealed record UpdatePortalDataRequestStatusCommand(Guid Id, OwnerDataRequestStatus? Status, string? ResponseNote)
    : IRequest<Result<StaffDataRequestDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "PortalInbox.DataRequest.UpdateStatus";
    public string? AuditTarget => $"DataRequestId={Id};Status={Status}";
}

public sealed class CreatePortalDataRequestCommandValidator : AbstractValidator<CreatePortalDataRequestCommand>
{
    public CreatePortalDataRequestCommandValidator()
    {
        RuleFor(x => x.Type).NotNull().IsInEnum();
        RuleFor(x => x.RequestText).NotEmpty().MaximumLength(OwnerDataRequest.MessageMaxLength).OverridePropertyName("message");
    }
}

public sealed class GetPortalInboxDataRequestsQueryValidator : AbstractValidator<GetPortalInboxDataRequestsQuery>
{
    public GetPortalInboxDataRequestsQueryValidator()
    {
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class UpdatePortalDataRequestStatusCommandValidator : AbstractValidator<UpdatePortalDataRequestStatusCommand>
{
    public UpdatePortalDataRequestStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Status).NotNull()
            .Must(s => s is OwnerDataRequestStatus.InProgress or OwnerDataRequestStatus.Completed or OwnerDataRequestStatus.Rejected)
            .WithMessage("Durum InProgress, Completed ya da Rejected olmalıdır.");
        RuleFor(x => x.ResponseNote).MaximumLength(OwnerDataRequest.ResponseNoteMaxLength);
    }
}

public sealed class OwnerDataRequestsByLinkSpec : Specification<OwnerDataRequest>
{
    public OwnerDataRequestsByLinkSpec(Guid tenantId, Guid linkId, int take = 50)
    {
        Query.AsNoTracking();
        Query.Where(r => r.TenantId == tenantId && r.LinkId == linkId);
        Query.OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id);
        Query.Take(take);
    }
}

public sealed class OpenOwnerDataRequestsByLinkSpec : Specification<OwnerDataRequest>
{
    public OpenOwnerDataRequestsByLinkSpec(Guid tenantId, Guid linkId)
        => Query.Where(r => r.TenantId == tenantId && r.LinkId == linkId
                            && (r.Status == OwnerDataRequestStatus.Open || r.Status == OwnerDataRequestStatus.InProgress));
}

public sealed class OwnerDataRequestByIdForUpdateSpec : Specification<OwnerDataRequest>
{
    public OwnerDataRequestByIdForUpdateSpec(Guid tenantId, Guid id) => Query.Where(r => r.TenantId == tenantId && r.Id == id);
}

/// <summary>Gelen kutusu: açık olanlar önce (vade yakın önce), sonra en yeni.</summary>
public sealed class PortalInboxDataRequestsSpec : Specification<OwnerDataRequest>
{
    public PortalInboxDataRequestsSpec(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        OwnerDataRequestStatus? status,
        bool openOnly = false,
        DateTime? dueBeforeUtc = null,
        int? page = null,
        int? pageSize = null)
    {
        Query.AsNoTracking();
        Query.Where(r => r.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (status is { } s)
            Query.Where(r => r.Status == s);
        if (openOnly)
            Query.Where(r => r.Status == OwnerDataRequestStatus.Open || r.Status == OwnerDataRequestStatus.InProgress);
        if (dueBeforeUtc is { } due)
            Query.Where(r => r.DueAtUtc < due);
        Query.OrderBy(r => r.Status == OwnerDataRequestStatus.Open || r.Status == OwnerDataRequestStatus.InProgress ? 0 : 1)
            .ThenBy(r => r.Status == OwnerDataRequestStatus.Open || r.Status == OwnerDataRequestStatus.InProgress ? r.DueAtUtc : DateTime.MaxValue)
            .ThenByDescending(r => r.CreatedAtUtc)
            .ThenBy(r => r.Id);
        if (page is { } p && pageSize is { } size)
            Query.Skip((p - 1) * size).Take(size);
    }
}

internal static class OwnerDataRequestMapping
{
    public static PortalDataRequestDto ToPortalDto(OwnerDataRequest r)
        => new(
            r.Id, r.Type, r.Status, r.Message, r.CreatedAtUtc, r.DueAtUtc,
            r.Status is OwnerDataRequestStatus.Completed or OwnerDataRequestStatus.Rejected ? r.HandledAtUtc : null,
            r.Status is OwnerDataRequestStatus.Completed or OwnerDataRequestStatus.Rejected ? r.ResponseNote : null,
            r.Status == OwnerDataRequestStatus.Open);

    public static StaffDataRequestDto ToStaffDto(OwnerDataRequest r, string clinicName, string clientName, DateTime utcNow)
        => new(
            r.Id, r.ClinicId, clinicName, r.ClientId, clientName, r.Type, r.Status, r.Message, r.CreatedAtUtc, r.DueAtUtc,
            r.IsOverdue(utcNow), r.HandledByUserId, r.HandledAtUtc, r.ResponseNote);
}

public sealed class GetPortalDataRequestsQueryHandler : IRequestHandler<GetPortalDataRequestsQuery, Result<IReadOnlyList<PortalDataRequestDto>>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<OwnerDataRequest> _requests;

    public GetPortalDataRequestsQueryHandler(OwnerPortalAccess access, IReadRepository<OwnerDataRequest> requests)
    {
        _access = access;
        _requests = requests;
    }

    public async ValueTask<Result<IReadOnlyList<PortalDataRequestDto>>> Handle(GetPortalDataRequestsQuery request, CancellationToken ct)
    {
        var scope = await _access.ResolveLinkForOwnerRightsAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scope.IsSuccess)
            return Result<IReadOnlyList<PortalDataRequestDto>>.Failure(scope.Error);

        var rows = await _requests.ListAsync(new OwnerDataRequestsByLinkSpec(scope.Value!.TenantId, scope.Value.LinkId), ct);
        return Result<IReadOnlyList<PortalDataRequestDto>>.Success(rows.Select(OwnerDataRequestMapping.ToPortalDto).ToList());
    }
}

public sealed class CreatePortalDataRequestCommandHandler : IRequestHandler<CreatePortalDataRequestCommand, Result<PortalDataRequestDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<OwnerAccountClientLink> _links;
    private readonly IReadRepository<Client> _clients;
    private readonly IRepository<OwnerDataRequest> _requests;
    private readonly IUserNotificationPublisher _notifications;
    private readonly TimeProvider _timeProvider;

    public CreatePortalDataRequestCommandHandler(
        OwnerPortalAccess access,
        IReadRepository<OwnerAccountClientLink> links,
        IReadRepository<Client> clients,
        IRepository<OwnerDataRequest> requests,
        IUserNotificationPublisher notifications,
        TimeProvider timeProvider)
    {
        _access = access;
        _links = links;
        _clients = clients;
        _requests = requests;
        _notifications = notifications;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PortalDataRequestDto>> Handle(CreatePortalDataRequestCommand request, CancellationToken ct)
    {
        var scope = await _access.ResolveLinkForOwnerRightsAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scope.IsSuccess)
            return Result<PortalDataRequestDto>.Failure(scope.Error);
        var s = scope.Value!;

        var link = await _links.FirstOrDefaultAsync(new OwnerAccountClientLinkByIdSpec(s.LinkId), ct);
        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(s.TenantId, s.ClientId), ct);
        if (link is null || client is null)
            return Result<PortalDataRequestDto>.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.");

        if (await _requests.CountAsync(new OpenOwnerDataRequestsByLinkSpec(s.TenantId, s.LinkId), ct) >= OwnerDataRequest.MaxOpenPerLink)
        {
            return Result<PortalDataRequestDto>.Failure(
                "Portal.DataRequests.TooManyOpen",
                $"Bu klinikte en fazla {OwnerDataRequest.MaxOpenPerLink} açık talebiniz olabilir; yanıt bekleyen talepleriniz sonuçlanınca yenisini gönderebilirsiniz.");
        }

        var created = OwnerDataRequest.Create(link, request.Type!.Value, request.RequestText, _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<PortalDataRequestDto>.Failure(created.Error);

        await _requests.AddAsync(created.Value!, ct);
        await _notifications.PortalDataRequestCreatedAsync(created.Value!, client.FullName, ct);
        await _requests.SaveChangesAsync(ct);
        return Result<PortalDataRequestDto>.Success(OwnerDataRequestMapping.ToPortalDto(created.Value!));
    }
}

public sealed class CancelPortalDataRequestCommandHandler : IRequestHandler<CancelPortalDataRequestCommand, Result>
{
    private readonly OwnerPortalAccess _access;
    private readonly IRepository<OwnerDataRequest> _requests;
    private readonly TimeProvider _timeProvider;

    public CancelPortalDataRequestCommandHandler(OwnerPortalAccess access, IRepository<OwnerDataRequest> requests, TimeProvider timeProvider)
    {
        _access = access;
        _requests = requests;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(CancelPortalDataRequestCommand request, CancellationToken ct)
    {
        var scope = await _access.ResolveLinkForOwnerRightsAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scope.IsSuccess)
            return Result.Failure(scope.Error);

        var row = await _requests.FirstOrDefaultAsync(new OwnerDataRequestByIdForUpdateSpec(scope.Value!.TenantId, request.Id), ct);
        if (row is null || row.LinkId != scope.Value.LinkId)
            return Result.Failure("Portal.DataRequests.NotFound", "Talep bulunamadı.");

        var cancelled = row.Cancel(_timeProvider.GetUtcNow().UtcDateTime);
        if (!cancelled.IsSuccess)
            return cancelled;

        try
        {
            await _requests.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure("Portal.DataRequests.NotOpen", "Talep bu sırada klinik tarafından ele alındı.");
        }

        return Result.Success();
    }
}

public sealed class GetPortalInboxDataRequestsQueryHandler
    : IRequestHandler<GetPortalInboxDataRequestsQuery, Result<PagedResult<StaffDataRequestDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScope;
    private readonly IReadRepository<OwnerDataRequest> _requests;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Client> _clients;
    private readonly TimeProvider _timeProvider;

    public GetPortalInboxDataRequestsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScope,
        IReadRepository<OwnerDataRequest> requests,
        IReadRepository<Clinic> clinics,
        IReadRepository<Client> clients,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScope = clinicScope;
        _requests = requests;
        _clinics = clinics;
        _clients = clients;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PagedResult<StaffDataRequestDto>>> Handle(GetPortalInboxDataRequestsQuery request, CancellationToken ct)
    {
        var scope = await PortalInboxScope.ResolveAsync(_tenantContext, _clinicContext, _clinicScope, request.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<StaffDataRequestDto>>.Failure(scope.Error);
        var (tenantId, clinicId, accessible) = scope.Value;

        var total = await _requests.CountAsync(new PortalInboxDataRequestsSpec(tenantId, clinicId, accessible, request.Status), ct);
        var rows = total == 0
            ? []
            : await _requests.ListAsync(
                new PortalInboxDataRequestsSpec(tenantId, clinicId, accessible, request.Status, page: request.Page, pageSize: request.PageSize), ct);

        var clinicNames = rows.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _clinics.ListAsync(new ClinicsByTenantIdsSpec(tenantId, rows.Select(r => r.ClinicId).Distinct().ToArray()), ct))
                .ToDictionary(c => c.Id, c => c.Name);
        var clientNames = rows.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, rows.Select(r => r.ClientId).Distinct().ToArray()), ct))
                .ToDictionary(c => c.Id, c => c.FullName);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var items = rows
            .Select(r => OwnerDataRequestMapping.ToStaffDto(
                r, clinicNames.GetValueOrDefault(r.ClinicId, string.Empty), clientNames.GetValueOrDefault(r.ClientId, string.Empty), utcNow))
            .ToList();
        return Result<PagedResult<StaffDataRequestDto>>.Success(PagedResult<StaffDataRequestDto>.Create(items, total, request.Page, request.PageSize));
    }
}

public sealed class GetPortalInboxSummaryQueryHandler : IRequestHandler<GetPortalInboxSummaryQuery, Result<PortalInboxSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScope;
    private readonly IReadRepository<OwnerDataRequest> _requests;
    private readonly TimeProvider _timeProvider;

    public GetPortalInboxSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScope,
        IReadRepository<OwnerDataRequest> requests,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScope = clinicScope;
        _requests = requests;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PortalInboxSummaryDto>> Handle(GetPortalInboxSummaryQuery request, CancellationToken ct)
    {
        var scope = await PortalInboxScope.ResolveAsync(_tenantContext, _clinicContext, _clinicScope, request.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<PortalInboxSummaryDto>.Failure(scope.Error);
        var (tenantId, clinicId, accessible) = scope.Value;

        var open = await _requests.CountAsync(new PortalInboxDataRequestsSpec(tenantId, clinicId, accessible, null, openOnly: true), ct);
        var overdue = open == 0
            ? 0
            : await _requests.CountAsync(
                new PortalInboxDataRequestsSpec(tenantId, clinicId, accessible, null, openOnly: true, dueBeforeUtc: _timeProvider.GetUtcNow().UtcDateTime), ct);
        return Result<PortalInboxSummaryDto>.Success(new PortalInboxSummaryDto(open, overdue));
    }
}

public sealed class UpdatePortalDataRequestStatusCommandHandler
    : IRequestHandler<UpdatePortalDataRequestStatusCommand, Result<StaffDataRequestDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScope;
    private readonly IRepository<OwnerDataRequest> _requests;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<OwnerAccount> _accounts;
    private readonly IOwnerNotificationDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public UpdatePortalDataRequestStatusCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScope,
        IRepository<OwnerDataRequest> requests,
        IReadRepository<Clinic> clinics,
        IReadRepository<Client> clients,
        IReadRepository<OwnerAccount> accounts,
        IOwnerNotificationDispatcher dispatcher,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScope = clinicScope;
        _requests = requests;
        _clinics = clinics;
        _clients = clients;
        _accounts = accounts;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<StaffDataRequestDto>> Handle(UpdatePortalDataRequestStatusCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<StaffDataRequestDto>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");
        if (_clientContext.UserId is not { } userId)
            return Result<StaffDataRequestDto>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var row = await _requests.FirstOrDefaultAsync(new OwnerDataRequestByIdForUpdateSpec(tenantId, request.Id), ct);
        if (row is null
            || (_clinicContext.ClinicId is { } contextClinicId && row.ClinicId != contextClinicId)
            || !(await _clinicScope.ResolveAsync(tenantId, row.ClinicId, ct)).IsSuccess)
        {
            // Atanmamış klinikteki talep varlığı sızdırmaz.
            return Result<StaffDataRequestDto>.Failure("PortalInbox.DataRequestNotFound", "Talep bulunamadı.");
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var handled = row.Handle(request.Status!.Value, request.ResponseNote, userId, utcNow);
        if (!handled.IsSuccess)
            return Result<StaffDataRequestDto>.Failure(handled.Error);

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, row.ClinicId), ct);
        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, row.ClientId), ct);
        var clinicName = clinic?.Name ?? string.Empty;

        if (row.Status is OwnerDataRequestStatus.Completed or OwnerDataRequestStatus.Rejected)
        {
            // Yanıt işlemsel iletidir (tercihle kapanmaz); e-posta talep güncellemesiyle aynı commit'te outbox'a yazılır.
            var account = await _accounts.FirstOrDefaultAsync(new OwnerAccountByIdSpec(row.OwnerAccountId), ct);
            var completed = row.Status == OwnerDataRequestStatus.Completed;
            var message = OwnerPortalEmails.DataRequestAnswered(clinicName, row.Type, completed, row.ResponseNote!);
            await _dispatcher.DispatchAsync(
                new OwnerNotificationMessage(
                    OwnerNotificationType.DataRequestAnswered,
                    row.OwnerAccountId,
                    account is { IsActive: true } ? account.Email : null,
                    account?.PushEnabled ?? false,
                    RecipientOptedIn: true,
                    message.Subject,
                    message.Body,
                    $"{clinicName}: veri talebiniz yanıtlandı",
                    "Ayrıntılar için portalı açın."),
                ct);
        }

        try
        {
            await _requests.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<StaffDataRequestDto>.Failure("PortalInbox.ConcurrencyConflict", "Talep bu sırada değişti; yeniden okuyup tekrar deneyin.");
        }

        return Result<StaffDataRequestDto>.Success(OwnerDataRequestMapping.ToStaffDto(row, clinicName, client?.FullName ?? string.Empty, utcNow));
    }
}

/// <summary>Gelen kutusu klinik kapsamı: istek <c>clinicId</c> aktif bağlamla çelişemez; yoksa erişilen klinikler.</summary>
internal static class PortalInboxScope
{
    public static async Task<Result<(Guid TenantId, Guid? ClinicId, IReadOnlyCollection<Guid>? Accessible)>> ResolveAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver resolver,
        Guid? requestClinicId,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
            return Result<(Guid, Guid?, IReadOnlyCollection<Guid>?)>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        if (ClinicContextRule.Conflicts(requestClinicId, clinicContext.ClinicId))
            return Result<(Guid, Guid?, IReadOnlyCollection<Guid>?)>.Failure(ClinicContextRule.Mismatch("PortalInbox"));

        var scope = await resolver.ResolveAsync(tenantId, clinicContext.ClinicId ?? requestClinicId, ct);
        return scope.IsSuccess
            ? Result<(Guid, Guid?, IReadOnlyCollection<Guid>?)>.Success((tenantId, scope.Value!.SingleClinicId, scope.Value.AccessibleClinicIds))
            : Result<(Guid, Guid?, IReadOnlyCollection<Guid>?)>.Failure(scope.Error);
    }
}
