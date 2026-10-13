using Backend.Veteriner.Application.CareInstructions.Contracts.Dtos;
using Backend.Veteriner.Application.CareInstructions.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.CareInstructions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.CareInstructions.Queries.GetById;

public sealed class GetCareInstructionByIdQueryHandler
    : IRequestHandler<GetCareInstructionByIdQuery, Result<CareInstructionDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<CareInstruction> _careInstructions;

    public GetCareInstructionByIdQueryHandler(
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

    public async Task<Result<CareInstructionDto>> Handle(GetCareInstructionByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<CareInstructionDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var row = await _careInstructions.FirstOrDefaultAsync(new CareInstructionByIdSpec(tenantId, request.Id), ct);
        if (row is null || (_clinicContext.ClinicId is { } clinicId && row.ClinicId != clinicId))
            return NotFound();

        // Atanmamış klinikteki kaydın varlığı sızdırılmaz.
        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, row.ClinicId, ct);
        if (!scope.IsSuccess)
            return NotFound();

        return Result<CareInstructionDto>.Success(CareInstructionMapping.ToDto(row));
    }

    private static Result<CareInstructionDto> NotFound()
        => Result<CareInstructionDto>.Failure("CareInstructions.NotFound", "Bakım talimatı bulunamadı.");
}

internal static class CareInstructionMapping
{
    public static CareInstructionDto ToDto(CareInstruction x)
        => new(
            x.Id,
            x.TenantId,
            x.ClinicId,
            x.PetId,
            x.HospitalizationId,
            x.ExaminationId,
            x.VisitId,
            x.Text,
            x.CreatedByUserId,
            x.CreatedAtUtc,
            x.UpdatedByUserId,
            x.UpdatedAtUtc,
            ExaminationRowVersion.Encode(x.RowVersion));
}
