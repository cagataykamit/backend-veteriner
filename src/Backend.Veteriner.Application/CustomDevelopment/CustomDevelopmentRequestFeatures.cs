using System.Text.Json.Serialization;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Users.Common;
using Backend.Veteriner.Domain.CustomDevelopment;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.CustomDevelopment;

/// <summary>Talep satırı; kiracı ve platform yöneticisi aynı alanları görür. Ad <c>DisplayName</c> yoksa e-postadan türetilir.</summary>
public sealed record CustomDevelopmentRequestDto(
    Guid Id,
    string RequestTitle,
    string RequestText,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CustomDevelopmentTargetKind TargetKind,
    string RequestTarget,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CustomDevelopmentPriority Priority,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CustomDevelopmentRequestStatus Status,
    bool IsOpen,
    Guid CreatedByUserId,
    string? CreatedByName,
    DateTime CreatedAtUtc,
    DateTime? StatusChangedAtUtc,
    string? DecisionReason,
    string? ResponseNote);

/// <summary>Kiracının talepleri (yeniden eskiye) ve açık talep sınırı; sınır doluysa yeni talep açılamaz.</summary>
public sealed record CustomDevelopmentRequestListDto(IReadOnlyList<CustomDevelopmentRequestDto> Items, int OpenCount, int MaxOpen);

/// <summary>GET /api/v1/custom-development-requests. Her pakette açık (paket düşünce talepler görünür kalır).</summary>
public sealed record GetCustomDevelopmentRequestsQuery : IRequest<Result<CustomDevelopmentRequestListDto>>;

/// <summary>
/// POST /api/v1/custom-development-requests. Kurumsal ister; açık talep sayısı <see cref="CustomDevelopmentRequest.MaxOpenPerTenant"/>
/// ise <c>CustomDevelopment.TooManyOpen</c>. Metinler denetim kaydında maskelenir.
/// </summary>
public sealed record CreateCustomDevelopmentRequestCommand(
    string? RequestTitle,
    string? RequestText,
    CustomDevelopmentTargetKind? TargetKind,
    string? RequestTarget,
    CustomDevelopmentPriority? Priority)
    : IRequest<Result<CustomDevelopmentRequestDto>>, IAuditableRequest
{
    public string AuditAction => "CustomDevelopmentRequest.Create";
    public string? AuditTarget => $"TargetKind={TargetKind};Priority={Priority}";
}

public sealed class CreateCustomDevelopmentRequestCommandValidator : AbstractValidator<CreateCustomDevelopmentRequestCommand>
{
    // Metin kuralları domain'de (CustomDevelopmentRequest.Create); burada yalnız zorunlu seçimler.
    public CreateCustomDevelopmentRequestCommandValidator()
    {
        RuleFor(x => x.TargetKind).NotNull().IsInEnum();
        RuleFor(x => x.Priority).NotNull().IsInEnum();
    }
}

public sealed class CustomDevelopmentRequestsByTenantSpec : Specification<CustomDevelopmentRequest>
{
    public CustomDevelopmentRequestsByTenantSpec(Guid tenantId)
    {
        Query.AsNoTracking();
        Query.Where(r => r.TenantId == tenantId).OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id);
    }
}

public sealed class OpenCustomDevelopmentRequestsByTenantSpec : Specification<CustomDevelopmentRequest>
{
    public OpenCustomDevelopmentRequestsByTenantSpec(Guid tenantId)
        => Query.Where(r => r.TenantId == tenantId && CustomDevelopmentRequest.OpenStatuses.Contains(r.Status));
}

public sealed record CustomDevelopmentUser(Guid Id, string? DisplayName, string Email);

public sealed class CustomDevelopmentUsersSpec : Specification<User, CustomDevelopmentUser>
{
    public CustomDevelopmentUsersSpec(IReadOnlyCollection<Guid> userIds)
    {
        Query.AsNoTracking();
        Query.Where(u => userIds.Contains(u.Id));
        Query.Select(u => new CustomDevelopmentUser(u.Id, u.DisplayName, u.Email));
    }
}

internal static class CustomDevelopmentRequestRules
{
    public static async Task<IReadOnlyList<CustomDevelopmentRequestDto>> ToDtosAsync(
        IReadRepository<User> users, IReadOnlyList<CustomDevelopmentRequest> requests, CancellationToken ct)
    {
        var ids = requests.Select(r => r.CreatedByUserId).Distinct().ToArray();
        var names = ids.Length == 0
            ? new Dictionary<Guid, string?>()
            : (await users.ListAsync(new CustomDevelopmentUsersSpec(ids), ct)).ToDictionary(u => u.Id, u => UserDisplayName.Resolve(u.DisplayName, u.Email));

        return requests.Select(r => new CustomDevelopmentRequestDto(
                r.Id, r.RequestTitle, r.RequestText, r.TargetKind, r.RequestTarget, r.Priority, r.Status, r.IsOpen,
                r.CreatedByUserId, names.GetValueOrDefault(r.CreatedByUserId), r.CreatedAtUtc, r.StatusChangedAtUtc,
                r.DecisionReason, r.ResponseNote))
            .ToList();
    }
}

public sealed class GetCustomDevelopmentRequestsQueryHandler
    : IRequestHandler<GetCustomDevelopmentRequestsQuery, Result<CustomDevelopmentRequestListDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<CustomDevelopmentRequest> _requests;
    private readonly IReadRepository<User> _users;

    public GetCustomDevelopmentRequestsQueryHandler(
        ITenantContext tenantContext, IReadRepository<CustomDevelopmentRequest> requests, IReadRepository<User> users)
    {
        _tenantContext = tenantContext;
        _requests = requests;
        _users = users;
    }

    public async ValueTask<Result<CustomDevelopmentRequestListDto>> Handle(GetCustomDevelopmentRequestsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<CustomDevelopmentRequestListDto>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var rows = await _requests.ListAsync(new CustomDevelopmentRequestsByTenantSpec(tenantId), ct);
        var items = await CustomDevelopmentRequestRules.ToDtosAsync(_users, rows, ct);
        return Result<CustomDevelopmentRequestListDto>.Success(
            new CustomDevelopmentRequestListDto(items, items.Count(i => i.IsOpen), CustomDevelopmentRequest.MaxOpenPerTenant));
    }
}

public sealed class CreateCustomDevelopmentRequestCommandHandler
    : IRequestHandler<CreateCustomDevelopmentRequestCommand, Result<CustomDevelopmentRequestDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<CustomDevelopmentRequest> _requests;
    private readonly IReadRepository<User> _users;
    private readonly TimeProvider _timeProvider;

    public CreateCustomDevelopmentRequestCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<CustomDevelopmentRequest> requests,
        IReadRepository<User> users,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _planFeatures = planFeatures;
        _requests = requests;
        _users = users;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CustomDevelopmentRequestDto>> Handle(CreateCustomDevelopmentRequestCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<CustomDevelopmentRequestDto>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");
        if (_clientContext.UserId is not { } userId)
            return Result<CustomDevelopmentRequestDto>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.CustomDevelopment, ct);
        if (!planAllows.IsSuccess)
            return Result<CustomDevelopmentRequestDto>.Failure(planAllows.Error);

        if (await _requests.CountAsync(new OpenCustomDevelopmentRequestsByTenantSpec(tenantId), ct) >= CustomDevelopmentRequest.MaxOpenPerTenant)
        {
            return Result<CustomDevelopmentRequestDto>.Failure(
                "CustomDevelopment.TooManyOpen",
                $"Aynı anda en fazla {CustomDevelopmentRequest.MaxOpenPerTenant} açık talebiniz olabilir; açık talepler tamamlanınca ya da kapsam dışı kararı verilince yenisini açabilirsiniz.");
        }

        var created = CustomDevelopmentRequest.Create(
            tenantId, request.RequestTitle, request.RequestText, request.TargetKind!.Value, request.RequestTarget, request.Priority!.Value,
            userId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<CustomDevelopmentRequestDto>.Failure(created.Error);

        await _requests.AddAsync(created.Value!, ct);
        await _requests.SaveChangesAsync(ct);

        return Result<CustomDevelopmentRequestDto>.Success((await CustomDevelopmentRequestRules.ToDtosAsync(_users, [created.Value!], ct))[0]);
    }
}
