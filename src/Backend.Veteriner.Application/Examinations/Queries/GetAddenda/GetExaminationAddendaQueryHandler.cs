using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Queries.GetAddenda;

public sealed class GetExaminationAddendaQueryHandler
    : IRequestHandler<GetExaminationAddendaQuery, Result<IReadOnlyList<ExaminationAddendumDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<ExaminationAddendum> _addenda;
    private readonly IClinicVeterinarianReader _userNames;

    public GetExaminationAddendaQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Examination> examinations,
        IReadRepository<ExaminationAddendum> addenda,
        IClinicVeterinarianReader userNames)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _examinations = examinations;
        _addenda = addenda;
        _userNames = userNames;
    }

    public async Task<Result<IReadOnlyList<ExaminationAddendumDto>>> Handle(
        GetExaminationAddendaQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<ExaminationAddendumDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var e = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, request.ExaminationId), ct);
        if (e is null || (_clinicContext.ClinicId is { } clinicId && e.ClinicId != clinicId))
        {
            return Result<IReadOnlyList<ExaminationAddendumDto>>.Failure(
                "Examinations.NotFound",
                "Muayene kaydı bulunamadı.");
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, e.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<IReadOnlyList<ExaminationAddendumDto>>.Failure(clinicAccess.Error);

        var rows = await _addenda.ListAsync(new ExaminationAddendaByExaminationIdSpec(tenantId, e.Id), ct);
        var names = rows.Count == 0
            ? new Dictionary<Guid, string?>()
            : await _userNames.GetNamesAsync(rows.Select(a => a.CreatedByUserId).Distinct().ToArray(), ct);

        IReadOnlyList<ExaminationAddendumDto> items = rows
            .Select(a => ExaminationAddendumDto.From(a, names.TryGetValue(a.CreatedByUserId, out var name) ? name : null))
            .ToList();
        return Result<IReadOnlyList<ExaminationAddendumDto>>.Success(items);
    }
}
