using Backend.Veteriner.Application.ClinicalTerms.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Ai.PatientSearch;

/// <summary>
/// Doğal dildeki arama cümlesini yapılandırılmış filtreye çevirir (AI-006; sözleşme §3). Model hasta/müşteri kaydı görmez, SQL
/// ya da sorgu yazmaz; yalnız <see cref="PetSearchFilter"/> alanlarını doldurur. Arama yapmaz: istemci filtreyi gösterir
/// ("şöyle anladım"), kullanıcı düzenleyebilir, sonra <see cref="SearchPetsQuery"/> ile arar. Cümle saklanmaz ve denetime yazılmaz.
/// </summary>
public sealed record InterpretPetSearchCommand(string Query)
    : IRequest<Result<PetSearchInterpretationDto>>, IAuditableRequest
{
    public string AuditAction => "Ai.PetSearch.Interpret";
    public string? AuditTarget => null;
}

public sealed class InterpretPetSearchCommandValidator : AbstractValidator<InterpretPetSearchCommand>
{
    public InterpretPetSearchCommandValidator()
        => RuleFor(x => x.Query).MaximumLength(PetSearchPrompt.MaxQueryLength)
            .WithMessage($"Arama en çok {PetSearchPrompt.MaxQueryLength} karakter olabilir.");
}

public sealed class InterpretPetSearchCommandHandler
    : IRequestHandler<InterpretPetSearchCommand, Result<PetSearchInterpretationDto>>
{
    private readonly PetSearchContext _context;
    private readonly AiUsageGuard _guard;
    private readonly AiInvoker _invoker;
    private readonly PetSearchRunner _runner;
    private readonly IReadRepository<ClinicalTerm> _terms;
    private readonly TimeProvider _timeProvider;

    public InterpretPetSearchCommandHandler(
        PetSearchContext context,
        AiUsageGuard guard,
        AiInvoker invoker,
        PetSearchRunner runner,
        IReadRepository<ClinicalTerm> terms,
        TimeProvider timeProvider)
    {
        _context = context;
        _guard = guard;
        _invoker = invoker;
        _runner = runner;
        _terms = terms;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PetSearchInterpretationDto>> Handle(InterpretPetSearchCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Result<PetSearchInterpretationDto>.Failure(AiErrors.SearchQueryRequired);

        var context = await _context.ResolveAsync(ct);
        if (!context.IsSuccess)
            return Result<PetSearchInterpretationDto>.Failure(context.Error);
        var (tenantId, clinicId, userId, _) = context.Value!;

        var grant = await _guard.CheckAsync(tenantId, clinicId, userId, ct);
        if (!grant.IsSuccess)
            return Result<PetSearchInterpretationDto>.Failure(grant.Error);

        var today = _runner.Today;
        var species = await _runner.ActiveSpeciesAsync(ct);
        var speciesCodes = species.Select(s => s.Code).ToList();
        var dictionary = (await _terms.ListAsync(
                new ClinicalTermsFilteredPagedSpec(tenantId, null, false, [], 1, PetSearchPrompt.MaxDictionaryTerms), ct))
            .Select(t => t.Name)
            .ToList();

        // Cümlede yazılmış olabilecek e-posta ve uzun numaralar (telefon, TCKN, çip) modele gitmez.
        var query = AiTextRedactor.Redact(request.Query.Trim(), [])!;

        var output = await _invoker.InvokeAsync(
            new AiInvocation(
                tenantId,
                clinicId,
                userId,
                AiFeature.PetSearch,
                null,
                [],
                grant.Value!,
                PetSearchPrompt.Version,
                PetSearchPrompt.System,
                PetSearchPrompt.BuildUserContent(query, today, species.Select(s => (s.Code, s.Name)).ToList(), dictionary),
                PetSearchPrompt.Schema(speciesCodes)),
            PetSearchPrompt.Parse,
            ct);
        if (!output.IsSuccess)
            return Result<PetSearchInterpretationDto>.Failure(output.Error);

        var (filter, dropped) = PetSearchFilterRules.Normalize(output.Value!.Filter, speciesCodes, today);
        return Result<PetSearchInterpretationDto>.Success(new PetSearchInterpretationDto(
            filter,
            filter.IsEmpty,
            output.Value.Unsupported,
            output.Value.Dropped.Concat(dropped).ToList(),
            grant.Value!.Model,
            _timeProvider.GetUtcNow().UtcDateTime));
    }
}

/// <summary>
/// Yapılandırılmış filtreyle hasta araması (AI-006; sözleşme §4). AI çağırmaz, kota harcamaz; kural <see cref="PetSearchRunner"/>'da.
/// </summary>
public sealed record SearchPetsQuery(PetSearchFilter Filter, int Page = 1, int PageSize = PetSearchRunner.DefaultPageSize)
    : IRequest<Result<PetSearchResultDto>>;

public sealed class SearchPetsQueryHandler : IRequestHandler<SearchPetsQuery, Result<PetSearchResultDto>>
{
    private readonly PetSearchContext _context;
    private readonly PetSearchRunner _runner;

    public SearchPetsQueryHandler(PetSearchContext context, PetSearchRunner runner)
    {
        _context = context;
        _runner = runner;
    }

    public async ValueTask<Result<PetSearchResultDto>> Handle(SearchPetsQuery request, CancellationToken ct)
    {
        var context = await _context.ResolveAsync(ct);
        if (!context.IsSuccess)
            return Result<PetSearchResultDto>.Failure(context.Error);

        var c = context.Value!;
        return await _runner.SearchAsync(c.TenantId, c.Scope, request.Filter ?? new PetSearchFilter(), request.Page, request.PageSize, ct);
    }
}

/// <summary>
/// İki ucun ortak bağlamı: kiracı, aktif klinik (AI kullanımı ve klinik kayıtlar kliniğe bağlıdır), kullanıcı ve klinik okuma
/// kapsamı (kullanıcının o kliniğe erişimi).
/// </summary>
public sealed class PetSearchContext
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;

    public PetSearchContext(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
    }

    public async Task<Result<PetSearchRequestContext>> ResolveAsync(CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<PetSearchRequestContext>.Failure(new Error("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir."));
        if (_clinicContext.ClinicId is not { } clinicId)
            return Result<PetSearchRequestContext>.Failure(AiErrors.ClinicSelectionRequired);
        if (_clientContext.UserId is not { } userId)
            return Result<PetSearchRequestContext>.Failure(new Error("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok."));

        var scope = await _scopeResolver.ResolveAsync(tenantId, clinicId, ct);
        return scope.IsSuccess
            ? Result<PetSearchRequestContext>.Success(new PetSearchRequestContext(tenantId, clinicId, userId, scope.Value!))
            : Result<PetSearchRequestContext>.Failure(scope.Error);
    }
}

public sealed record PetSearchRequestContext(Guid TenantId, Guid ClinicId, Guid UserId, ClinicReadScope Scope);
