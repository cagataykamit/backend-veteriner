using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.CustomDevelopment;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.CustomDevelopment;

/// <summary>Platform yöneticisi satırı: talep, sahibi kiracı ve geçerli sonraki durumlar (akış domain'de, kapanmış talepte boş).</summary>
public sealed record AdminCustomDevelopmentRequestDto(
    Guid TenantId,
    string TenantName,
    CustomDevelopmentRequestDto Request,
    IReadOnlyList<string> NextStatuses);

/// <summary>
/// GET /api/v1/admin/custom-development-requests (yalnız platform yöneticisi, kiracılar arası). Yeniden eskiye; <c>status</c> verilirse
/// yalnız o durum, <c>openOnly</c> ise yalnız sonuçlanmamışlar, <c>targetKind</c> verilirse yalnız o konu (ör. <c>Migration</c>, TIER-021).
/// </summary>
public sealed record GetAdminCustomDevelopmentRequestsQuery(
    CustomDevelopmentRequestStatus? Status, bool OpenOnly, int Page, int PageSize, CustomDevelopmentTargetKind? TargetKind = null)
    : IRequest<Result<PagedResult<AdminCustomDevelopmentRequestDto>>>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}

/// <summary>
/// POST /api/v1/admin/custom-development-requests/{id}/status (yalnız platform yöneticisi). Durum değişikliği denetim kaydına yazılır;
/// gerekçe ve not maskelenir. Kiracının abonelik yazma kapısına takılmaz (destek işlemi).
/// </summary>
public sealed record ChangeCustomDevelopmentRequestStatusCommand(
    Guid Id, CustomDevelopmentRequestStatus? Status, string? DecisionReason, string? ResponseNote)
    : IRequest<Result<AdminCustomDevelopmentRequestDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "CustomDevelopmentRequest.StatusChange";
    public string? AuditTarget => $"CustomDevelopmentRequestId={Id};Status={Status}";
}

public sealed class GetAdminCustomDevelopmentRequestsQueryValidator : AbstractValidator<GetAdminCustomDevelopmentRequestsQuery>
{
    public GetAdminCustomDevelopmentRequestsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.TargetKind).IsInEnum();
    }
}

public sealed class ChangeCustomDevelopmentRequestStatusCommandValidator : AbstractValidator<ChangeCustomDevelopmentRequestStatusCommand>
{
    public ChangeCustomDevelopmentRequestStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Status).NotNull().IsInEnum();
    }
}

public sealed class AdminCustomDevelopmentRequestsSpec : Specification<CustomDevelopmentRequest>
{
    public AdminCustomDevelopmentRequestsSpec(
        CustomDevelopmentRequestStatus? status, bool openOnly, CustomDevelopmentTargetKind? targetKind = null, int? skip = null, int? take = null)
    {
        Query.AsNoTracking();
        if (targetKind is { } kind)
            Query.Where(r => r.TargetKind == kind);
        if (status is { } s)
            Query.Where(r => r.Status == s);
        if (openOnly)
            Query.Where(r => CustomDevelopmentRequest.OpenStatuses.Contains(r.Status));
        Query.OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id);
        if (skip is { } k && take is { } t)
            Query.Skip(k).Take(t);
    }
}

public sealed class CustomDevelopmentRequestByIdForAdminSpec : Specification<CustomDevelopmentRequest>, ISingleResultSpecification<CustomDevelopmentRequest>
{
    public CustomDevelopmentRequestByIdForAdminSpec(Guid id) => Query.Where(r => r.Id == id);
}

public sealed record CustomDevelopmentTenantName(Guid Id, string Name);

public sealed class CustomDevelopmentTenantNamesSpec : Specification<Tenant, CustomDevelopmentTenantName>
{
    public CustomDevelopmentTenantNamesSpec(IReadOnlyCollection<Guid> tenantIds)
    {
        Query.AsNoTracking();
        Query.Where(t => tenantIds.Contains(t.Id));
        Query.Select(t => new CustomDevelopmentTenantName(t.Id, t.Name));
    }
}

internal static class CustomDevelopmentAdminRules
{
    public static async Task<IReadOnlyList<AdminCustomDevelopmentRequestDto>> ToAdminDtosAsync(
        IReadRepository<Tenant> tenants, IReadRepository<User> users, IReadOnlyList<CustomDevelopmentRequest> requests, CancellationToken ct)
    {
        var tenantIds = requests.Select(r => r.TenantId).Distinct().ToArray();
        var names = tenantIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await tenants.ListAsync(new CustomDevelopmentTenantNamesSpec(tenantIds), ct)).ToDictionary(t => t.Id, t => t.Name);
        var dtos = await CustomDevelopmentRequestRules.ToDtosAsync(users, requests, ct);

        return requests.Select((r, i) => new AdminCustomDevelopmentRequestDto(
                r.TenantId, names.GetValueOrDefault(r.TenantId) ?? "", dtos[i],
                CustomDevelopmentRequest.NextStatuses(r.Status).Select(s => s.ToString()).ToList()))
            .ToList();
    }
}

/// <remarks>Kiracı filtresi platform yönetimi denetleyicisinin sistem kapsamıyla kapalıdır; burada kiracı koşulu yoktur.</remarks>
public sealed class GetAdminCustomDevelopmentRequestsQueryHandler
    : IRequestHandler<GetAdminCustomDevelopmentRequestsQuery, Result<PagedResult<AdminCustomDevelopmentRequestDto>>>
{
    private readonly IReadRepository<CustomDevelopmentRequest> _requests;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<User> _users;

    public GetAdminCustomDevelopmentRequestsQueryHandler(
        IReadRepository<CustomDevelopmentRequest> requests, IReadRepository<Tenant> tenants, IReadRepository<User> users)
    {
        _requests = requests;
        _tenants = tenants;
        _users = users;
    }

    public async ValueTask<Result<PagedResult<AdminCustomDevelopmentRequestDto>>> Handle(
        GetAdminCustomDevelopmentRequestsQuery request, CancellationToken ct)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, GetAdminCustomDevelopmentRequestsQuery.MaxPageSize);
        var total = await _requests.CountAsync(new AdminCustomDevelopmentRequestsSpec(request.Status, request.OpenOnly, request.TargetKind), ct);
        var rows = await _requests.ListAsync(
            new AdminCustomDevelopmentRequestsSpec(request.Status, request.OpenOnly, request.TargetKind, (request.Page - 1) * pageSize, pageSize), ct);

        var items = await CustomDevelopmentAdminRules.ToAdminDtosAsync(_tenants, _users, rows, ct);
        return Result<PagedResult<AdminCustomDevelopmentRequestDto>>.Success(
            PagedResult<AdminCustomDevelopmentRequestDto>.Create(items, total, request.Page, pageSize));
    }
}

public sealed class ChangeCustomDevelopmentRequestStatusCommandHandler
    : IRequestHandler<ChangeCustomDevelopmentRequestStatusCommand, Result<AdminCustomDevelopmentRequestDto>>
{
    private readonly IClientContext _clientContext;
    private readonly IRepository<CustomDevelopmentRequest> _requests;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<User> _users;
    private readonly TimeProvider _timeProvider;

    public ChangeCustomDevelopmentRequestStatusCommandHandler(
        IClientContext clientContext,
        IRepository<CustomDevelopmentRequest> requests,
        IReadRepository<Tenant> tenants,
        IReadRepository<User> users,
        TimeProvider timeProvider)
    {
        _clientContext = clientContext;
        _requests = requests;
        _tenants = tenants;
        _users = users;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AdminCustomDevelopmentRequestDto>> Handle(ChangeCustomDevelopmentRequestStatusCommand request, CancellationToken ct)
    {
        if (_clientContext.UserId is not { } userId)
            return Result<AdminCustomDevelopmentRequestDto>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var row = await _requests.FirstOrDefaultAsync(new CustomDevelopmentRequestByIdForAdminSpec(request.Id), ct);
        if (row is null)
            return Result<AdminCustomDevelopmentRequestDto>.Failure("CustomDevelopment.NotFound", "Talep bulunamadı.");

        var changed = row.ChangeStatus(request.Status!.Value, request.DecisionReason, request.ResponseNote, userId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!changed.IsSuccess)
            return Result<AdminCustomDevelopmentRequestDto>.Failure(changed.Error);

        try
        {
            await _requests.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<AdminCustomDevelopmentRequestDto>.Failure(
                "CustomDevelopment.ConcurrencyConflict", "Talep eşzamanlı olarak değişti; yeniden okunup işlem tekrarlanmalı.");
        }

        return Result<AdminCustomDevelopmentRequestDto>.Success(
            (await CustomDevelopmentAdminRules.ToAdminDtosAsync(_tenants, _users, [row], ct))[0]);
    }
}
