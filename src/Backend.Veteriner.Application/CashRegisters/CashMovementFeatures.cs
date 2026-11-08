using System.Text.Json.Serialization;
using Ardalis.Specification;
using Backend.Veteriner.Application.CashCounts;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Finance;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Users.Common;
using Backend.Veteriner.Domain.CashRegisters;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.CashRegisters;

/// <summary>Kasa hareketi; aktarım bacaklarında <c>TransferId</c> ve karşı kasa dolu.</summary>
public sealed record CashMovementDto(
    Guid Id,
    Guid ClinicId,
    Guid CashRegisterId,
    string CashRegisterName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CashMovementKind Kind,
    DateOnly BusinessDate,
    decimal Amount,
    string Currency,
    Guid? CategoryId,
    string? CategoryName,
    string? Description,
    Guid? TransferId,
    Guid? CounterpartCashRegisterId,
    string? CounterpartCashRegisterName,
    Guid CreatedByUserId,
    string? CreatedByName,
    DateTime CreatedAtUtc,
    bool IsVoided,
    DateTime? VoidedAtUtc,
    string? VoidedByName,
    string? VoidReason);

/// <summary>Giriş/çıkış yönü (istek gövdesi); aktarım ayrı uçtadır.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CashMovementDirection
{
    In = 1,
    Out = 2,
}

/// <summary>GET /api/v1/cash-movements. Tek klinik; kasa verilmezse tüm kasalar. Gün, sonra kayıt zamanı azalan.</summary>
public sealed record GetCashMovementsQuery(
    DateOnly From, DateOnly To, Guid? ClinicId, Guid? CashRegisterId, bool IncludeVoided, int Page, int PageSize)
    : IRequest<Result<PagedResult<CashMovementDto>>>;

/// <summary>POST /api/v1/cash-movements. Kasaya giriş ya da kasadan çıkış (tahsilat ve iade dışı nakit).</summary>
public sealed record CreateCashMovementCommand(
    Guid? ClinicId,
    Guid CashRegisterId,
    CashMovementDirection Direction,
    DateOnly BusinessDate,
    decimal Amount,
    Guid? CategoryId,
    string? Description)
    : IRequest<Result<CashMovementDto>>, IAuditableRequest
{
    public string AuditAction => "CashMovement.Create";
    public string? AuditTarget => $"CashRegisterId={CashRegisterId}";
}

/// <summary>POST /api/v1/cash-movements/transfers. Tek işlemde kaynaktan çıkış ve hedefe giriş; dönen kayıt çıkış bacağıdır.</summary>
public sealed record CreateCashTransferCommand(
    Guid? ClinicId,
    Guid SourceCashRegisterId,
    Guid TargetCashRegisterId,
    DateOnly BusinessDate,
    decimal Amount,
    string? Description)
    : IRequest<Result<CashMovementDto>>, IAuditableRequest
{
    public string AuditAction => "CashTransfer.Create";
    public string? AuditTarget => $"SourceCashRegisterId={SourceCashRegisterId};TargetCashRegisterId={TargetCashRegisterId}";
}

/// <summary>POST /api/v1/cash-movements/{id}/void. Aktarımın bir bacağı verilirse iki bacak birlikte iptal edilir.</summary>
public sealed record VoidCashMovementCommand(Guid Id, string? Reason)
    : IRequest<Result>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "CashMovement.Void";
    public string? AuditTarget => $"CashMovementId={Id}";
}

public sealed class GetCashMovementsQueryValidator : AbstractValidator<GetCashMovementsQuery>
{
    public GetCashMovementsQueryValidator() => RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
}

public sealed class CreateCashMovementCommandValidator : AbstractValidator<CreateCashMovementCommand>
{
    public CreateCashMovementCommandValidator()
    {
        RuleFor(x => x.CashRegisterId).NotEmpty();
        RuleFor(x => x.Direction).IsInEnum();
    }
}

public sealed class CreateCashTransferCommandValidator : AbstractValidator<CreateCashTransferCommand>
{
    public CreateCashTransferCommandValidator()
    {
        RuleFor(x => x.SourceCashRegisterId).NotEmpty();
        RuleFor(x => x.TargetCashRegisterId).NotEmpty();
    }
}

public sealed class VoidCashMovementCommandValidator : AbstractValidator<VoidCashMovementCommand>
{
    public VoidCashMovementCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class CashMovementsPagedSpec : Specification<CashMovement>
{
    public CashMovementsPagedSpec(
        Guid tenantId, Guid clinicId, Guid? cashRegisterId, DateOnly from, DateOnly to, bool includeVoided, int? skip = null, int? take = null)
    {
        Query.AsNoTracking();
        Query.Where(m => m.TenantId == tenantId && m.ClinicId == clinicId && m.BusinessDate >= from && m.BusinessDate <= to);
        if (cashRegisterId is { } registerId)
            Query.Where(m => m.CashRegisterId == registerId);
        if (!includeVoided)
            Query.Where(m => m.VoidedAtUtc == null);
        Query.OrderByDescending(m => m.BusinessDate).ThenByDescending(m => m.CreatedAtUtc).ThenBy(m => m.Kind).ThenBy(m => m.Id);
        if (skip is { } s && take is { } t)
            Query.Skip(s).Take(t);
    }
}

public sealed class CashMovementForUpdateSpec : Specification<CashMovement>
{
    public CashMovementForUpdateSpec(Guid tenantId, Guid id) => Query.Where(m => m.TenantId == tenantId && m.Id == id);
}

public sealed class CashTransferLegsForUpdateSpec : Specification<CashMovement>
{
    public CashTransferLegsForUpdateSpec(Guid tenantId, Guid transferId) => Query.Where(m => m.TenantId == tenantId && m.TransferId == transferId);
}

public sealed class CashRegistersByIdsSpec : Specification<CashRegister>
{
    public CashRegistersByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking();
        Query.Where(r => r.TenantId == tenantId && ids.Contains(r.Id));
    }
}

public sealed class CashMovementCategoriesByIdsSpec : Specification<CashMovementCategory>
{
    public CashMovementCategoriesByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && ids.Contains(c.Id));
    }
}

/// <summary>Hareket satırı adları (kasa, tür, kullanıcı) toplu sorguyla; N+1 yok.</summary>
internal sealed class CashMovementNames
{
    private readonly IReadOnlyDictionary<Guid, string> _registers;
    private readonly IReadOnlyDictionary<Guid, string> _categories;
    private readonly IReadOnlyDictionary<Guid, string?> _users;

    private CashMovementNames(
        IReadOnlyDictionary<Guid, string> registers, IReadOnlyDictionary<Guid, string> categories, IReadOnlyDictionary<Guid, string?> users)
    {
        _registers = registers;
        _categories = categories;
        _users = users;
    }

    public static async Task<CashMovementNames> LoadAsync(
        IReadRepository<CashRegister> registers,
        IReadRepository<CashMovementCategory> categories,
        IReadRepository<User> users,
        Guid tenantId,
        IReadOnlyCollection<CashMovement> movements,
        CancellationToken ct)
    {
        var registerIds = movements.Select(m => m.CashRegisterId)
            .Concat(movements.Where(m => m.CounterpartCashRegisterId.HasValue).Select(m => m.CounterpartCashRegisterId!.Value))
            .Distinct().ToArray();
        var categoryIds = movements.Where(m => m.CategoryId.HasValue).Select(m => m.CategoryId!.Value).Distinct().ToArray();
        var userIds = movements.Select(m => m.CreatedByUserId)
            .Concat(movements.Where(m => m.VoidedByUserId.HasValue).Select(m => m.VoidedByUserId!.Value))
            .Distinct().ToArray();

        var registerNames = registerIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await registers.ListAsync(new CashRegistersByIdsSpec(tenantId, registerIds), ct)).ToDictionary(r => r.Id, r => r.Name);
        var categoryNames = categoryIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await categories.ListAsync(new CashMovementCategoriesByIdsSpec(tenantId, categoryIds), ct)).ToDictionary(c => c.Id, c => c.Name);
        var userNames = userIds.Length == 0
            ? new Dictionary<Guid, string?>()
            : (await users.ListAsync(new CashCountUsersSpec(userIds), ct)).ToDictionary(u => u.Id, u => UserDisplayName.Resolve(u.DisplayName, u.Email));

        return new CashMovementNames(registerNames, categoryNames, userNames);
    }

    public string Register(Guid id) => _registers.GetValueOrDefault(id, CashRegister.MainName);
    public string? Category(Guid? id) => id is { } v ? _categories.GetValueOrDefault(v) : null;
    public string? User(Guid? id) => id is { } v ? _users.GetValueOrDefault(v) : null;

    public CashMovementDto ToDto(CashMovement m)
        => new(
            m.Id, m.ClinicId, m.CashRegisterId, Register(m.CashRegisterId), m.Kind, m.BusinessDate, m.Amount, m.Currency,
            m.CategoryId, Category(m.CategoryId), m.Description,
            m.TransferId, m.CounterpartCashRegisterId, m.CounterpartCashRegisterId is { } c ? Register(c) : null,
            m.CreatedByUserId, User(m.CreatedByUserId), m.CreatedAtUtc,
            m.IsVoided, m.VoidedAtUtc, User(m.VoidedByUserId), m.VoidReason);
}

public sealed class GetCashMovementsQueryHandler : IRequestHandler<GetCashMovementsQuery, Result<PagedResult<CashMovementDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<CashMovement> _movements;
    private readonly IReadRepository<CashRegister> _registers;
    private readonly IReadRepository<CashMovementCategory> _categories;
    private readonly IReadRepository<User> _users;

    public GetCashMovementsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<CashMovement> movements,
        IReadRepository<CashRegister> registers,
        IReadRepository<CashMovementCategory> categories,
        IReadRepository<User> users)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _movements = movements;
        _registers = registers;
        _categories = categories;
        _users = users;
    }

    public async ValueTask<Result<PagedResult<CashMovementDto>>> Handle(GetCashMovementsQuery request, CancellationToken ct)
    {
        var filter = await CashRegisterRules.ResolveClinicAsync(
            _tenantContext, _clinicContext, _scopeResolver, request.From, request.To, request.ClinicId, "CashMovements", ct);
        if (!filter.IsSuccess)
            return Result<PagedResult<CashMovementDto>>.Failure(filter.Error);
        var tenantId = filter.Value!.TenantId;
        var clinicId = filter.Value.ClinicId!.Value;

        if (request.CashRegisterId is { } registerId)
        {
            var register = await CashRegisterRules.ResolveAsync(_registers, tenantId, clinicId, registerId, ct);
            if (!register.IsSuccess)
                return Result<PagedResult<CashMovementDto>>.Failure(register.Error);
        }

        var pageSize = Math.Clamp(request.PageSize, 1, RegisterReportConstants.MaxPageSize);
        var total = await _movements.CountAsync(
            new CashMovementsPagedSpec(tenantId, clinicId, request.CashRegisterId, request.From, request.To, request.IncludeVoided), ct);
        var rows = await _movements.ListAsync(
            new CashMovementsPagedSpec(
                tenantId, clinicId, request.CashRegisterId, request.From, request.To, request.IncludeVoided, (request.Page - 1) * pageSize, pageSize),
            ct);

        var names = await CashMovementNames.LoadAsync(_registers, _categories, _users, tenantId, rows, ct);
        return Result<PagedResult<CashMovementDto>>.Success(
            PagedResult<CashMovementDto>.Create(rows.Select(names.ToDto).ToList(), total, request.Page, pageSize));
    }
}

public sealed class CreateCashMovementCommandHandler : IRequestHandler<CreateCashMovementCommand, Result<CashMovementDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<CashMovement> _movements;
    private readonly IReadRepository<CashRegister> _registers;
    private readonly IReadRepository<CashMovementCategory> _categories;
    private readonly IReadRepository<User> _users;
    private readonly TimeProvider _timeProvider;

    public CreateCashMovementCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<Tenant> tenants,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<CashMovement> movements,
        IReadRepository<CashRegister> registers,
        IReadRepository<CashMovementCategory> categories,
        IReadRepository<User> users,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _tenants = tenants;
        _planFeatures = planFeatures;
        _movements = movements;
        _registers = registers;
        _categories = categories;
        _users = users;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CashMovementDto>> Handle(CreateCashMovementCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<CashMovementDto>.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.CashManagement, ct);
        if (!planAllows.IsSuccess)
            return Result<CashMovementDto>.Failure(planAllows.Error);

        var filter = await CashRegisterRules.ResolveClinicAsync(
            _tenantContext, _clinicContext, _scopeResolver, request.BusinessDate, request.BusinessDate, request.ClinicId, "CashMovements", ct);
        if (!filter.IsSuccess)
            return Result<CashMovementDto>.Failure(filter.Error);

        var register = await CashRegisterRules.ResolveAsync(_registers, tenantId, filter.Value!.ClinicId!.Value, request.CashRegisterId, ct);
        if (!register.IsSuccess)
            return Result<CashMovementDto>.Failure(register.Error);

        if (request.CategoryId is { } categoryId)
        {
            var category = await _categories.FirstOrDefaultAsync(new CashMovementCategoryByIdSpec(tenantId, categoryId), ct);
            if (category is null)
                return Result<CashMovementDto>.Failure("CashMovementCategories.NotFound", "Gelir/gider türü bulunamadı.");
            if (!category.IsActive)
                return Result<CashMovementDto>.Failure(CashMovement.ValidationCode, "Pasif gelir/gider türü seçilemez.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var kind = request.Direction == CashMovementDirection.In ? CashMovementKind.In : CashMovementKind.Out;
        var created = CashMovement.Create(
            register.Value!.Register!, kind, request.BusinessDate, OperationDayBounds.ToLocalDate(nowUtc),
            request.Amount, request.CategoryId, request.Description, userId, nowUtc);
        if (!created.IsSuccess)
            return Result<CashMovementDto>.Failure(created.Error);

        await _movements.AddAsync(created.Value!, ct);
        await _movements.SaveChangesAsync(ct);

        var names = await CashMovementNames.LoadAsync(_registers, _categories, _users, tenantId, [created.Value!], ct);
        return Result<CashMovementDto>.Success(names.ToDto(created.Value!));
    }
}

public sealed class CreateCashTransferCommandHandler : IRequestHandler<CreateCashTransferCommand, Result<CashMovementDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<CashMovement> _movements;
    private readonly IReadRepository<CashRegister> _registers;
    private readonly IReadRepository<CashMovementCategory> _categories;
    private readonly IReadRepository<User> _users;
    private readonly TimeProvider _timeProvider;

    public CreateCashTransferCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<Tenant> tenants,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<CashMovement> movements,
        IReadRepository<CashRegister> registers,
        IReadRepository<CashMovementCategory> categories,
        IReadRepository<User> users,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _tenants = tenants;
        _planFeatures = planFeatures;
        _movements = movements;
        _registers = registers;
        _categories = categories;
        _users = users;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CashMovementDto>> Handle(CreateCashTransferCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<CashMovementDto>.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.CashManagement, ct);
        if (!planAllows.IsSuccess)
            return Result<CashMovementDto>.Failure(planAllows.Error);

        var filter = await CashRegisterRules.ResolveClinicAsync(
            _tenantContext, _clinicContext, _scopeResolver, request.BusinessDate, request.BusinessDate, request.ClinicId, "CashMovements", ct);
        if (!filter.IsSuccess)
            return Result<CashMovementDto>.Failure(filter.Error);
        var clinicId = filter.Value!.ClinicId!.Value;

        var source = await CashRegisterRules.ResolveAsync(_registers, tenantId, clinicId, request.SourceCashRegisterId, ct);
        if (!source.IsSuccess)
            return Result<CashMovementDto>.Failure(source.Error);
        var target = await CashRegisterRules.ResolveAsync(_registers, tenantId, clinicId, request.TargetCashRegisterId, ct);
        if (!target.IsSuccess)
            return Result<CashMovementDto>.Failure(target.Error);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var legs = CashMovement.CreateTransfer(
            source.Value!.Register!, target.Value!.Register!, request.BusinessDate, OperationDayBounds.ToLocalDate(nowUtc),
            request.Amount, request.Description, userId, nowUtc);
        if (!legs.IsSuccess)
            return Result<CashMovementDto>.Failure(legs.Error);

        // İki bacak tek SaveChanges'te: biri yazılamazsa hiçbiri kalmaz.
        var (outLeg, inLeg) = legs.Value;
        await _movements.AddRangeAsync([outLeg, inLeg], ct);
        await _movements.SaveChangesAsync(ct);

        var names = await CashMovementNames.LoadAsync(_registers, _categories, _users, tenantId, [outLeg], ct);
        return Result<CashMovementDto>.Success(names.ToDto(outLeg));
    }
}

public sealed class VoidCashMovementCommandHandler : IRequestHandler<VoidCashMovementCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<CashMovement> _movements;
    private readonly TimeProvider _timeProvider;

    public VoidCashMovementCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<Tenant> tenants,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<CashMovement> movements,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _tenants = tenants;
        _planFeatures = planFeatures;
        _movements = movements;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(VoidCashMovementCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.CashManagement, ct);
        if (!planAllows.IsSuccess)
            return planAllows;

        var movement = await _movements.FirstOrDefaultAsync(new CashMovementForUpdateSpec(tenantId, request.Id), ct);
        if (movement is null || (_clinicContext.ClinicId is { } contextClinicId && movement.ClinicId != contextClinicId))
            return Result.Failure(CashRegisterRules.MovementNotFound());

        var access = await _scopeResolver.ResolveAsync(tenantId, movement.ClinicId, ct);
        if (!access.IsSuccess)
            return Result.Failure(access.Error);

        var legs = movement.TransferId is { } transferId
            ? await _movements.ListAsync(new CashTransferLegsForUpdateSpec(tenantId, transferId), ct)
            : [movement];

        // Bacaklar birlikte iptal edilir; biri iptalliyse hiçbirine dokunulmaz.
        if (legs.Any(l => l.IsVoided))
            return Result.Failure("CashMovements.AlreadyVoided", "Kasa hareketi zaten iptal edilmiş.");

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var leg in legs)
        {
            var voided = leg.Void(userId, request.Reason, nowUtc);
            if (!voided.IsSuccess)
                return voided;
        }

        try
        {
            await _movements.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure("CashMovements.ConcurrencyConflict", "Kasa hareketi eşzamanlı olarak değişti; yeniden okunup işlem tekrarlanmalı.");
        }

        return Result.Success();
    }
}
