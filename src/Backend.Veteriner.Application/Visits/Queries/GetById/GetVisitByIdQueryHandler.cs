using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Visits;
using MediatR;

namespace Backend.Veteriner.Application.Visits.Queries.GetById;

public sealed class GetVisitByIdQueryHandler : IRequestHandler<GetVisitByIdQuery, Result<VisitDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Visit> _visits;

    public GetVisitByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Visit> visits)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _visits = visits;
    }

    public async Task<Result<VisitDto>> Handle(GetVisitByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VisitDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var visit = await _visits.FirstOrDefaultAsync(
            new VisitByIdSpec(tenantId, request.Id, asNoTracking: true), ct);
        if (visit is null
            || (_clinicContext.ClinicId is { } contextClinicId && visit.ClinicId != contextClinicId))
        {
            return NotFound();
        }

        // Atanmamış klinikteki kayıt varlığı sızdırmaz: NotFound.
        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, visit.ClinicId, ct);
        if (!scope.IsSuccess)
            return NotFound();

        return Result<VisitDto>.Success(visit.ToDto());
    }

    private static Result<VisitDto> NotFound()
        => Result<VisitDto>.Failure("Visits.NotFound", "Geliş kaydı bulunamadı.");
}
