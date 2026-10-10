using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Visits.Queries.GetToday;

/// <param name="ClinicId">Klinik bağlamı yoksa zorunlu.</param>
/// <param name="LocalDate">İstanbul takvim günü; verilmezse bugün.</param>
public sealed record GetVisitsTodayQuery(Guid? ClinicId, DateOnly? LocalDate)
    : IRequest<Result<TodayDto>>;
