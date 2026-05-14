using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.VaccineDefinitions.Contracts.Dtos;
using Backend.Veteriner.Application.VaccineDefinitions.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.VaccineDefinitions.Queries.GetById;

public sealed class GetVaccineDefinitionByIdQueryHandler
    : IRequestHandler<GetVaccineDefinitionByIdQuery, Result<VaccineDefinitionDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<VaccineDefinition> _definitions;

    public GetVaccineDefinitionByIdQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<VaccineDefinition> definitions)
    {
        _tenantContext = tenantContext;
        _definitions = definitions;
    }

    public async Task<Result<VaccineDefinitionDto>> Handle(GetVaccineDefinitionByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VaccineDefinitionDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var v = await _definitions.FirstOrDefaultAsync(new VaccineDefinitionByIdSpec(request.Id), ct);
        if (v is null)
            return Result<VaccineDefinitionDto>.Failure("VaccineDefinitions.NotFound", "Aşı tanımı bulunamadı.");

        if (v.TenantId is not null && v.TenantId != tenantId)
            return Result<VaccineDefinitionDto>.Failure("VaccineDefinitions.NotFound", "Aşı tanımı bulunamadı.");

        var dto = new VaccineDefinitionDto(
            v.Id,
            v.TenantId,
            v.SpeciesId,
            v.Name,
            v.Code,
            v.Description,
            v.DefaultNextDueDays,
            v.IsCore,
            v.IsActive);

        return Result<VaccineDefinitionDto>.Success(dto);
    }
}
