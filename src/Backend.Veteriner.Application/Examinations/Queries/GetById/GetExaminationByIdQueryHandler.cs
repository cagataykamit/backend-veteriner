using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Queries.GetById;

public sealed class GetExaminationByIdQueryHandler
    : IRequestHandler<GetExaminationByIdQuery, Result<ExaminationDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetExaminationByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Examination> examinations,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _examinations = examinations;
        _pets = pets;
        _clients = clients;
    }

    public async Task<Result<ExaminationDetailDto>> Handle(GetExaminationByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ExaminationDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var e = await _examinations.FirstOrDefaultAsync(
            new ExaminationByIdSpec(tenantId, request.Id), ct);
        if (e is null)
            return Result<ExaminationDetailDto>.Failure("Examinations.NotFound", "Muayene kaydı bulunamadı.");
        if (_clinicContext.ClinicId is { } clinicId && e.ClinicId != clinicId)
            return Result<ExaminationDetailDto>.Failure("Examinations.NotFound", "Muayene kaydi bulunamadi.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, e.PetId), ct);
        var petName = pet?.Name ?? string.Empty;
        var clientId = pet?.ClientId ?? Guid.Empty;
        var clientName = string.Empty;
        if (pet is not null)
        {
            var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, pet.ClientId), ct);
            clientName = client?.FullName ?? string.Empty;
        }

        var dto = new ExaminationDetailDto(
            e.Id,
            e.TenantId,
            e.ClinicId,
            e.PetId,
            petName,
            clientId,
            clientName,
            e.AppointmentId,
            e.ExaminedAtUtc,
            e.VisitReason,
            e.Findings,
            e.Assessment,
            e.Notes,
            e.CreatedAtUtc,
            e.UpdatedAtUtc);
        return Result<ExaminationDetailDto>.Success(dto);
    }
}
