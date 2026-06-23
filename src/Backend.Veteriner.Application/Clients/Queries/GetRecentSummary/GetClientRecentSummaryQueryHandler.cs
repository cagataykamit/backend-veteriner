using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Queries.GetRecentSummary;

public sealed class GetClientRecentSummaryQueryHandler
    : IRequestHandler<GetClientRecentSummaryQuery, Result<ClientRecentSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Examination> _examinations;

    public GetClientRecentSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IReadRepository<Examination> examinations)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _clients = clients;
        _pets = pets;
        _appointments = appointments;
        _examinations = examinations;
    }

    public async Task<Result<ClientRecentSummaryDto>> Handle(GetClientRecentSummaryQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ClientRecentSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct);
        if (client is null)
            return Result<ClientRecentSummaryDto>.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, _clinicContext.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<ClientRecentSummaryDto>.Failure(scopeResult.Error);

        var singleClinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value.AccessibleClinicIds;

        if (accessibleClinicIds is { Count: 0 })
        {
            return Result<ClientRecentSummaryDto>.Success(
                new ClientRecentSummaryDto(request.ClientId, [], []));
        }

        var pets = await _pets.ListAsync(new PetsByTenantClientIdSpec(tenantId, request.ClientId), ct);
        if (pets.Count == 0)
        {
            return Result<ClientRecentSummaryDto>.Success(
                new ClientRecentSummaryDto(request.ClientId, [], []));
        }

        var petIds = pets.Select(p => p.Id).ToArray();
        var petNameById = pets.ToDictionary(p => p.Id, p => p.Name);

        var appts = await _appointments.ListAsync(
            new AppointmentsForClientPetsRecentSpec(
                tenantId,
                singleClinicId,
                petIds,
                ClientRecentSummaryConstants.RecentAppointmentsTake,
                accessibleClinicIds),
            ct);

        var exams = await _examinations.ListAsync(
            new ExaminationsForClientPetsRecentSpec(
                tenantId,
                singleClinicId,
                petIds,
                ClientRecentSummaryConstants.RecentExaminationsTake,
                accessibleClinicIds),
            ct);

        var recentAppts = appts
            .Select(a => new ClientRecentAppointmentSummaryItemDto(
                a.Id,
                a.ScheduledAtUtc,
                a.PetId,
                petNameById.GetValueOrDefault(a.PetId, string.Empty),
                a.Status,
                a.Notes))
            .ToList();

        var recentExams = exams
            .Select(e => new ClientRecentExaminationSummaryItemDto(
                e.Id,
                e.ExaminedAtUtc,
                e.PetId,
                petNameById.GetValueOrDefault(e.PetId, string.Empty),
                e.VisitReason))
            .ToList();

        return Result<ClientRecentSummaryDto>.Success(
            new ClientRecentSummaryDto(request.ClientId, recentAppts, recentExams));
    }
}
