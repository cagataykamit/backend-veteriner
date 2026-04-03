using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.LabResults.Queries.GetById;

public sealed class GetLabResultByIdQueryHandler
    : IRequestHandler<GetLabResultByIdQuery, Result<LabResultDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<LabResult> _labResults;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetLabResultByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<LabResult> labResults,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _labResults = labResults;
        _pets = pets;
        _clients = clients;
    }

    public async Task<Result<LabResultDetailDto>> Handle(GetLabResultByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<LabResultDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var row = await _labResults.FirstOrDefaultAsync(
            new LabResultByIdSpec(tenantId, request.Id), ct);
        if (row is null)
            return Result<LabResultDetailDto>.Failure("LabResults.NotFound", "Laboratuvar sonucu bulunamadı.");
        if (_clinicContext.ClinicId is { } clinicId && row.ClinicId != clinicId)
            return Result<LabResultDetailDto>.Failure("LabResults.NotFound", "Laboratuvar sonucu bulunamadı.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, row.PetId), ct);
        var petName = pet?.Name ?? string.Empty;
        var clientId = pet?.ClientId ?? Guid.Empty;

        var client = clientId != Guid.Empty
            ? await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, clientId), ct)
            : null;
        var clientName = client?.FullName ?? string.Empty;

        var dto = new LabResultDetailDto(
            row.Id,
            row.TenantId,
            row.ClinicId,
            row.PetId,
            petName,
            clientId,
            clientName,
            row.ExaminationId,
            row.ResultDateUtc,
            row.TestName,
            row.ResultText,
            row.Interpretation,
            row.Notes,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

        return Result<LabResultDetailDto>.Success(dto);
    }
}
