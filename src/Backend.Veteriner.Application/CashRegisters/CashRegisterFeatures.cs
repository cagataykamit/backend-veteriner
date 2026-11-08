using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Finance;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.CashRegisters;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.CashRegisters;

public sealed record CashRegisterDto(Guid Id, Guid ClinicId, string Code, string Name, bool IsMain, bool IsActive);

public sealed record CashMovementCategoryDto(Guid Id, string Name, bool IsActive);

/// <summary>
/// GET /api/v1/cash-registers. Tek klinik; Ana kasa önce. Her pakette okunur (paket düşünce kasalar silinmez); Temel'de istemci
/// yalnız Ana kasayı kullanır, seçim göstermez.
/// </summary>
public sealed record GetCashRegistersQuery(Guid? ClinicId, bool IncludeInactive) : IRequest<Result<IReadOnlyList<CashRegisterDto>>>;

/// <summary>POST /api/v1/cash-registers. Kasa yönetimi paketi (Profesyonel, Kurumsal). Klinikte Ana kasa yoksa önce o açılır.</summary>
public sealed record CreateCashRegisterCommand(Guid? ClinicId, string? Code, string? Name)
    : IRequest<Result<CashRegisterDto>>, IAuditableRequest
{
    public string AuditAction => "CashRegister.Create";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

/// <summary>PUT /api/v1/cash-registers/{id}. Ad, kod ve durum; Ana kasa pasife alınamaz.</summary>
public sealed record UpdateCashRegisterCommand(Guid Id, string? Code, string? Name, bool IsActive)
    : IRequest<Result<CashRegisterDto>>, IAuditableRequest
{
    public string AuditAction => "CashRegister.Update";
    public string? AuditTarget => $"CashRegisterId={Id}";
}

/// <summary>GET /api/v1/cash-movements/categories. Kiracının gelir/gider türleri, ada göre.</summary>
public sealed record GetCashMovementCategoriesQuery(bool IncludeInactive) : IRequest<Result<IReadOnlyList<CashMovementCategoryDto>>>;

public sealed record CreateCashMovementCategoryCommand(string? Name) : IRequest<Result<CashMovementCategoryDto>>, IAuditableRequest
{
    public string AuditAction => "CashMovementCategory.Create";
    public string? AuditTarget => null;
}

public sealed record UpdateCashMovementCategoryCommand(Guid Id, string? Name, bool IsActive)
    : IRequest<Result<CashMovementCategoryDto>>, IAuditableRequest
{
    public string AuditAction => "CashMovementCategory.Update";
    public string? AuditTarget => $"CashMovementCategoryId={Id}";
}

public sealed class UpdateCashRegisterCommandValidator : AbstractValidator<UpdateCashRegisterCommand>
{
    public UpdateCashRegisterCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class UpdateCashMovementCategoryCommandValidator : AbstractValidator<UpdateCashMovementCategoryCommand>
{
    public UpdateCashMovementCategoryCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class CashMovementCategoriesSpec : Specification<CashMovementCategory>
{
    public CashMovementCategoriesSpec(Guid tenantId, bool includeInactive)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId);
        if (!includeInactive)
            Query.Where(c => c.IsActive);
        Query.OrderBy(c => c.Name);
    }
}

public sealed class CashMovementCategoryByIdSpec : Specification<CashMovementCategory>
{
    public CashMovementCategoryByIdSpec(Guid tenantId, Guid id) => Query.Where(c => c.TenantId == tenantId && c.Id == id);
}

public static class CashRegisterMapping
{
    public static CashRegisterDto ToDto(CashRegister r) => new(r.Id, r.ClinicId, r.Code, r.Name, r.IsMain, r.IsActive);

    public static CashMovementCategoryDto ToDto(CashMovementCategory c) => new(c.Id, c.Name, c.IsActive);

    /// <summary>Klinikte kod ve kiracıda tür adı benzersizdir (EF yapılandırması bu adları kullanır).</summary>
    public const string CodeIndexName = "UX_CashRegisters_TenantId_ClinicId_Code";

    public const string MainIndexName = "UX_CashRegisters_TenantId_ClinicId_Main";
    public const string CategoryNameIndexName = "UX_CashMovementCategories_TenantId_Name";

    public static bool Violates(DbUpdateException ex, string indexName)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static readonly Error DuplicateCode = new("CashRegisters.DuplicateCode", "Bu klinikte aynı kodlu bir kasa var.");
    public static readonly Error DuplicateCategory = new("CashMovementCategories.DuplicateName", "Aynı adlı bir tür var.");
}

public sealed class GetCashRegistersQueryHandler : IRequestHandler<GetCashRegistersQuery, Result<IReadOnlyList<CashRegisterDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<CashRegister> _registers;
    private readonly TimeProvider _timeProvider;

    public GetCashRegistersQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<CashRegister> registers,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _registers = registers;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<IReadOnlyList<CashRegisterDto>>> Handle(GetCashRegistersQuery request, CancellationToken ct)
    {
        var today = OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);
        var filter = await CashRegisterRules.ResolveClinicAsync(
            _tenantContext, _clinicContext, _scopeResolver, today, today, request.ClinicId, "CashRegisters", ct);
        if (!filter.IsSuccess)
            return Result<IReadOnlyList<CashRegisterDto>>.Failure(filter.Error);

        var rows = await _registers.ListAsync(
            new CashRegistersByClinicSpec(filter.Value!.TenantId, filter.Value.ClinicId!.Value, request.IncludeInactive), ct);
        return Result<IReadOnlyList<CashRegisterDto>>.Success(rows.Select(CashRegisterMapping.ToDto).ToList());
    }
}

public sealed class CreateCashRegisterCommandHandler : IRequestHandler<CreateCashRegisterCommand, Result<CashRegisterDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<CashRegister> _registers;
    private readonly TimeProvider _timeProvider;

    public CreateCashRegisterCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<Tenant> tenants,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<CashRegister> registers,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _tenants = tenants;
        _planFeatures = planFeatures;
        _registers = registers;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CashRegisterDto>> Handle(CreateCashRegisterCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<CashRegisterDto>.Failure(actor.Error);
        var tenantId = actor.Value.TenantId;

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.CashManagement, ct);
        if (!planAllows.IsSuccess)
            return Result<CashRegisterDto>.Failure(planAllows.Error);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var today = OperationDayBounds.ToLocalDate(nowUtc);
        var filter = await CashRegisterRules.ResolveClinicAsync(
            _tenantContext, _clinicContext, _scopeResolver, today, today, request.ClinicId, "CashRegisters", ct);
        if (!filter.IsSuccess)
            return Result<CashRegisterDto>.Failure(filter.Error);
        var clinicId = filter.Value!.ClinicId!.Value;

        var created = CashRegister.Create(tenantId, clinicId, request.Code, request.Name, nowUtc);
        if (!created.IsSuccess)
            return Result<CashRegisterDto>.Failure(created.Error);

        await CashRegisterRules.EnsureMainAsync(_registers, tenantId, clinicId, nowUtc, ct);
        await _registers.AddAsync(created.Value!, ct);
        try
        {
            await _registers.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (CashRegisterMapping.Violates(ex, CashRegisterMapping.CodeIndexName))
        {
            return Result<CashRegisterDto>.Failure(CashRegisterMapping.DuplicateCode);
        }

        return Result<CashRegisterDto>.Success(CashRegisterMapping.ToDto(created.Value!));
    }
}

public sealed class UpdateCashRegisterCommandHandler : IRequestHandler<UpdateCashRegisterCommand, Result<CashRegisterDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<CashRegister> _registers;
    private readonly TimeProvider _timeProvider;

    public UpdateCashRegisterCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<Tenant> tenants,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<CashRegister> registers,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _tenants = tenants;
        _planFeatures = planFeatures;
        _registers = registers;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CashRegisterDto>> Handle(UpdateCashRegisterCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<CashRegisterDto>.Failure(actor.Error);
        var tenantId = actor.Value.TenantId;

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.CashManagement, ct);
        if (!planAllows.IsSuccess)
            return Result<CashRegisterDto>.Failure(planAllows.Error);

        var register = await _registers.FirstOrDefaultAsync(new CashRegisterByIdSpec(tenantId, request.Id), ct);
        if (register is null || (_clinicContext.ClinicId is { } contextClinicId && register.ClinicId != contextClinicId))
            return Result<CashRegisterDto>.Failure(CashRegisterRules.NotFoundCode, "Kasa bulunamadı.");

        var access = await _scopeResolver.ResolveAsync(tenantId, register.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<CashRegisterDto>.Failure(access.Error);

        var updated = register.Update(request.Code, request.Name, request.IsActive, _timeProvider.GetUtcNow().UtcDateTime);
        if (!updated.IsSuccess)
            return Result<CashRegisterDto>.Failure(updated.Error);

        try
        {
            await _registers.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (CashRegisterMapping.Violates(ex, CashRegisterMapping.CodeIndexName))
        {
            return Result<CashRegisterDto>.Failure(CashRegisterMapping.DuplicateCode);
        }

        return Result<CashRegisterDto>.Success(CashRegisterMapping.ToDto(register));
    }
}

public sealed class GetCashMovementCategoriesQueryHandler
    : IRequestHandler<GetCashMovementCategoriesQuery, Result<IReadOnlyList<CashMovementCategoryDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<CashMovementCategory> _categories;

    public GetCashMovementCategoriesQueryHandler(ITenantContext tenantContext, IReadRepository<CashMovementCategory> categories)
    {
        _tenantContext = tenantContext;
        _categories = categories;
    }

    public async ValueTask<Result<IReadOnlyList<CashMovementCategoryDto>>> Handle(GetCashMovementCategoriesQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<IReadOnlyList<CashMovementCategoryDto>>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var rows = await _categories.ListAsync(new CashMovementCategoriesSpec(tenantId, request.IncludeInactive), ct);
        return Result<IReadOnlyList<CashMovementCategoryDto>>.Success(rows.Select(CashRegisterMapping.ToDto).ToList());
    }
}

public sealed class CreateCashMovementCategoryCommandHandler : IRequestHandler<CreateCashMovementCategoryCommand, Result<CashMovementCategoryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<CashMovementCategory> _categories;
    private readonly TimeProvider _timeProvider;

    public CreateCashMovementCategoryCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<Tenant> tenants,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<CashMovementCategory> categories,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _tenants = tenants;
        _planFeatures = planFeatures;
        _categories = categories;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CashMovementCategoryDto>> Handle(CreateCashMovementCategoryCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<CashMovementCategoryDto>.Failure(actor.Error);

        var planAllows = await _planFeatures.EnsureAllowedAsync(actor.Value.TenantId, PlanFeature.CashManagement, ct);
        if (!planAllows.IsSuccess)
            return Result<CashMovementCategoryDto>.Failure(planAllows.Error);

        var created = CashMovementCategory.Create(actor.Value.TenantId, request.Name, _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<CashMovementCategoryDto>.Failure(created.Error);

        await _categories.AddAsync(created.Value!, ct);
        try
        {
            await _categories.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (CashRegisterMapping.Violates(ex, CashRegisterMapping.CategoryNameIndexName))
        {
            return Result<CashMovementCategoryDto>.Failure(CashRegisterMapping.DuplicateCategory);
        }

        return Result<CashMovementCategoryDto>.Success(CashRegisterMapping.ToDto(created.Value!));
    }
}

public sealed class UpdateCashMovementCategoryCommandHandler : IRequestHandler<UpdateCashMovementCategoryCommand, Result<CashMovementCategoryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<CashMovementCategory> _categories;
    private readonly TimeProvider _timeProvider;

    public UpdateCashMovementCategoryCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<Tenant> tenants,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<CashMovementCategory> categories,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _tenants = tenants;
        _planFeatures = planFeatures;
        _categories = categories;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CashMovementCategoryDto>> Handle(UpdateCashMovementCategoryCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<CashMovementCategoryDto>.Failure(actor.Error);

        var planAllows = await _planFeatures.EnsureAllowedAsync(actor.Value.TenantId, PlanFeature.CashManagement, ct);
        if (!planAllows.IsSuccess)
            return Result<CashMovementCategoryDto>.Failure(planAllows.Error);

        var category = await _categories.FirstOrDefaultAsync(new CashMovementCategoryByIdSpec(actor.Value.TenantId, request.Id), ct);
        if (category is null)
            return Result<CashMovementCategoryDto>.Failure("CashMovementCategories.NotFound", "Tür bulunamadı.");

        var updated = category.Update(request.Name, request.IsActive, _timeProvider.GetUtcNow().UtcDateTime);
        if (!updated.IsSuccess)
            return Result<CashMovementCategoryDto>.Failure(updated.Error);

        try
        {
            await _categories.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (CashRegisterMapping.Violates(ex, CashRegisterMapping.CategoryNameIndexName))
        {
            return Result<CashMovementCategoryDto>.Failure(CashRegisterMapping.DuplicateCategory);
        }

        return Result<CashMovementCategoryDto>.Success(CashRegisterMapping.ToDto(category));
    }
}
