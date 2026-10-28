using Backend.Veteriner.Application.CareInstructions.Contracts.Dtos;
using Backend.Veteriner.Application.CareInstructions.Queries.GetById;
using Backend.Veteriner.Application.CareInstructions.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.CareInstructions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.CareInstructions.Queries.GetList;

public sealed class GetCareInstructionsListQueryHandler
    : IRequestHandler<GetCareInstructionsListQuery, Result<PagedResult<CareInstructionDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<CareInstruction> _careInstructions;

    public GetCareInstructionsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<CareInstruction> careInstructions)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _careInstructions = careInstructions;
    }

    public async ValueTask<Result<PagedResult<CareInstructionDto>>> Handle(
        GetCareInstructionsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<CareInstructionDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
        {
            return Result<PagedResult<CareInstructionDto>>.Failure(ClinicContextRule.Mismatch("CareInstructions"));
        }

        // Yatış listesiyle aynı güvenlik kuralı: açık klinik kapsamı yoksa kiracının tüm talimatları döndürülmez.
        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (requestedClinicId is null)
        {
            return Result<PagedResult<CareInstructionDto>>.Failure(
                "CareInstructions.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi.");
        }

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<CareInstructionDto>>.Failure(scope.Error);

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var clinicId = scope.Value!.SingleClinicId;
        var accessibleClinicIds = scope.Value!.AccessibleClinicIds;

        var total = await _careInstructions.CountAsync(
            new CareInstructionsFilteredCountSpec(
                tenantId, clinicId, accessibleClinicIds, request.PetId, request.HospitalizationId, request.ExaminationId, request.VisitId),
            ct);

        var rows = await _careInstructions.ListAsync(
            new CareInstructionsFilteredPagedSpec(
                tenantId, clinicId, accessibleClinicIds, request.PetId, request.HospitalizationId, request.ExaminationId, request.VisitId,
                page, pageSize),
            ct);

        return Result<PagedResult<CareInstructionDto>>.Success(
            PagedResult<CareInstructionDto>.Create(
                rows.Select(CareInstructionMapping.ToDto).ToList(), total, page, pageSize));
    }
}
