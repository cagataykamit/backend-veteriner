using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Visits.Queries.GetToday;

/// <param name="ClinicId">Klinik bağlamı yoksa zorunlu.</param>
/// <param name="LocalDate">İstanbul takvim günü; verilmezse bugün.</param>
/// <param name="Voided">true ise yalnızca o günün yanlış geliş işaretli gelişleri döner.</param>
public sealed record GetVisitsTodayQuery(Guid? ClinicId, DateOnly? LocalDate, bool Voided = false)
    : IRequest<Result<TodayDto>>;
