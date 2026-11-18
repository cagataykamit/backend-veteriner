using System.Text.Json.Serialization;
using Ardalis.Specification;
using Backend.Veteriner.Application.Ai.Drafts;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.OwnerExplanations;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.OwnerExplanations;

// Hasta sahibine açıklama (AI-003; docs/AI_DRAFTS_API_CONTRACT.md §7.2): muayeneye bağlı ayrı kayıt. İzinler muayeneninkidir:
// okuma Examinations.Read, oluşturma Examinations.Update. Bugün HTTP oluşturma ucu yok; kayıt AI taslağının kabulüyle oluşur.

/// <param name="AiProvenance">"AI taslağından" izi (kabul eden, düzenlendi mi).</param>
public sealed record OwnerExplanationDto(
    Guid Id,
    Guid ExaminationId,
    Guid PetId,
    string Text,
    Guid? SourceAiDraftId,
    Guid CreatedByUserId,
    string? CreatedByName,
    DateTime CreatedAtUtc,
    IReadOnlyList<AiProvenanceDto> AiProvenance);

public sealed class OwnerExplanationsForExaminationSpec : Specification<OwnerExplanation>
{
    public OwnerExplanationsForExaminationSpec(Guid tenantId, Guid examinationId)
        => Query
            .Where(x => x.TenantId == tenantId && x.ExaminationId == examinationId)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .AsNoTracking();
}

// ----- Oluştur -----

/// <summary>Muayeneye açıklama ekler (kesinleşmiş muayeneye de). Metin klinik veridir, denetim yüküne yazılmaz.</summary>
/// <param name="SourceAiDraftId">Metnin başladığı AI taslağı.</param>
public sealed record CreateOwnerExplanationCommand(Guid ExaminationId, [property: JsonIgnore] string Text, Guid? SourceAiDraftId)
    : IRequest<Result<Guid>>, ITransactionalRequest, IAuditableRequest
{
    public string AuditAction => "OwnerExplanation.Create";
    public string? AuditTarget => $"ExaminationId={ExaminationId}";
}

public sealed class CreateOwnerExplanationCommandValidator : AbstractValidator<CreateOwnerExplanationCommand>
{
    public CreateOwnerExplanationCommandValidator()
    {
        RuleFor(x => x.ExaminationId).NotEmpty();
        RuleFor(x => x.Text).NotEmpty().MaximumLength(OwnerExplanation.MaxTextLength);
        RuleFor(x => x.SourceAiDraftId).Must(id => id != Guid.Empty).WithMessage("Kaynak taslak geçersiz.");
    }
}

public sealed class CreateOwnerExplanationCommandHandler : IRequestHandler<CreateOwnerExplanationCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IRepository<OwnerExplanation> _explanations;
    private readonly TimeProvider _timeProvider;

    public CreateOwnerExplanationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<Examination> examinations,
        IRepository<OwnerExplanation> explanations,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _examinations = examinations;
        _explanations = explanations;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateOwnerExplanationCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<Guid>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        if (_clientContext.UserId is not { } userId)
            return Result<Guid>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, request.ExaminationId), ct);
        if (exam is null || (_clinicContext.ClinicId is { } clinicId && exam.ClinicId != clinicId))
            return Result<Guid>.Failure("Examinations.NotFound", "Muayene kaydı bulunamadı.");

        var scope = await _scopeResolver.ResolveAsync(tenantId, exam.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<Guid>.Failure(scope.Error);

        var created = OwnerExplanation.Create(
            tenantId, exam.ClinicId, exam.PetId, exam.Id, request.Text, request.SourceAiDraftId, userId,
            _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<Guid>.Failure(created.Error);

        await _explanations.AddAsync(created.Value!, ct);
        await _explanations.SaveChangesAsync(ct);
        return Result<Guid>.Success(created.Value!.Id);
    }
}

// ----- Muayenenin açıklamaları -----

/// <summary>Muayenenin açıklamaları, yeniden eskiye.</summary>
public sealed record GetOwnerExplanationsQuery(Guid ExaminationId) : IRequest<Result<IReadOnlyList<OwnerExplanationDto>>>;

public sealed class GetOwnerExplanationsQueryHandler
    : IRequestHandler<GetOwnerExplanationsQuery, Result<IReadOnlyList<OwnerExplanationDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<OwnerExplanation> _explanations;
    private readonly IClinicVeterinarianReader _userNames;
    private readonly IAiProvenanceReader _provenance;

    public GetOwnerExplanationsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<Examination> examinations,
        IReadRepository<OwnerExplanation> explanations,
        IClinicVeterinarianReader userNames,
        IAiProvenanceReader provenance)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _examinations = examinations;
        _explanations = explanations;
        _userNames = userNames;
        _provenance = provenance;
    }

    public async ValueTask<Result<IReadOnlyList<OwnerExplanationDto>>> Handle(GetOwnerExplanationsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail(new Error("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir."));

        var exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, request.ExaminationId), ct);
        if (exam is null || (_clinicContext.ClinicId is { } clinicId && exam.ClinicId != clinicId))
            return Fail(new Error("Examinations.NotFound", "Muayene kaydı bulunamadı."));

        var scope = await _scopeResolver.ResolveAsync(tenantId, exam.ClinicId, ct);
        if (!scope.IsSuccess)
            return Fail(scope.Error);

        var rows = await _explanations.ListAsync(new OwnerExplanationsForExaminationSpec(tenantId, exam.Id), ct);
        var names = await _userNames.GetNamesAsync(rows.Select(r => r.CreatedByUserId).Distinct().ToList(), ct);
        var provenance = await _provenance.ForRecordsAsync(tenantId, rows.Select(r => r.Id).ToList(), ct);

        IReadOnlyList<OwnerExplanationDto> dtos = rows
            .Select(r => new OwnerExplanationDto(
                r.Id,
                r.ExaminationId,
                r.PetId,
                r.Text,
                r.SourceAiDraftId,
                r.CreatedByUserId,
                names.GetValueOrDefault(r.CreatedByUserId),
                r.CreatedAtUtc,
                provenance.GetValueOrDefault(r.Id) ?? []))
            .ToList();
        return Result<IReadOnlyList<OwnerExplanationDto>>.Success(dtos);
    }

    private static Result<IReadOnlyList<OwnerExplanationDto>> Fail(Error error)
        => Result<IReadOnlyList<OwnerExplanationDto>>.Failure(error);
}
