using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Queries.GetById;

public sealed class GetExaminationByIdQueryHandler
    : IRequestHandler<GetExaminationByIdQuery, Result<ExaminationDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Examination> _examinations;

    public GetExaminationByIdQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Examination> examinations)
    {
        _tenantContext = tenantContext;
        _examinations = examinations;
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

        var dto = new ExaminationDetailDto(
            e.Id,
            e.TenantId,
            e.ClinicId,
            e.PetId,
            e.AppointmentId,
            e.ExaminedAtUtc,
            e.VisitReason,
            e.Findings,
            e.Assessment,
            e.Notes);
        return Result<ExaminationDetailDto>.Success(dto);
    }
}
